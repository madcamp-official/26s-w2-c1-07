// ─────────────────────────────────────────────────────────────
// 공유 프로토콜 계약 (single source of truth)
// server / client / bot 이 전부 이 타입·상수·지형함수를 import 한다.
// ─────────────────────────────────────────────────────────────

/** 네트워크 파라미터 (명세서 §7 권장 기술 스택 기준) */
export const NET = {
  PORT: 8080,
  SNAPSHOT_HZ: 15,
  CLIENT_SEND_HZ: 15,
  HEARTBEAT_MS: 2500,
  HEARTBEAT_TIMEOUT_MS: 9000,
  INTERP_DELAY_MS: 110,
} as const;

/** 월드 파라미터 (Day3: 산·강 하이트맵 롱맵) */
export const WORLD = {
  /** 폭 (X 축) */
  SIZE_X: 80,
  /** 길이 (Z 축, 진행 방향) */
  SIZE_Z: 160,
  /** 지형 메시 세그먼트 수 */
  SEG_X: 80,
  SEG_Z: 160,
  /** 강물 수면 높이 */
  WATER_Y: -1.2,
} as const;

// ── 결정론적 하이트맵 ─────────────────────────────────────────
// 클라(렌더 + 지면 충돌)와 서버(스폰 높이 + 검증)가 이 함수 하나를 공유해
// 모두가 완전히 동일한 지형을 본다. 난수를 쓰지 않는다.

function smoothstep(t: number): number {
  t = t < 0 ? 0 : t > 1 ? 1 : t;
  return t * t * (3 - 2 * t);
}

/** (x, z) 지점의 지형 높이(y) */
export function terrainHeight(x: number, z: number): number {
  // 완만한 구릉
  let h =
    3.2 * Math.sin(x * 0.06) * Math.cos(z * 0.045) +
    1.8 * Math.sin(x * 0.11 + z * 0.07) +
    1.0 * Math.sin(z * 0.19);

  // 좌우 가장자리로 갈수록 솟는 산 능선
  const edge = Math.abs(x) / (WORLD.SIZE_X / 2); // 0..1
  h += Math.pow(edge, 2.2) * 10;

  // 길이 방향으로 굽이치는 강을 파냄
  const riverCenter = 12 * Math.sin(z * 0.028) + 6 * Math.sin(z * 0.011);
  const bank = 9; // 강폭 반경
  const d = Math.abs(x - riverCenter);
  if (d < bank) {
    h -= smoothstep(1 - d / bank) * 6.5; // 중심으로 갈수록 깊게
  }

  return h;
}

export type PlayerId = string;

export const ANIM = { IDLE: 0, RUN: 1, JUMP: 2 } as const;
export type AnimState = (typeof ANIM)[keyof typeof ANIM];

export interface PlayerState {
  id: PlayerId;
  name: string;
  color: number;
  x: number;
  y: number;
  z: number;
  yaw: number;
  anim: AnimState;
}

// ── Client → Server ───────────────────────────────────────────
export interface C2S_Join {
  name: string;
}
export interface C2S_Input {
  x: number;
  y: number;
  z: number;
  yaw: number;
  anim: AnimState;
  seq: number;
}

// ── Server → Client ───────────────────────────────────────────
export interface S2C_Welcome {
  id: PlayerId;
  color: number;
  spawn: { x: number; y: number; z: number };
  players: PlayerState[];
  serverTime: number;
}
export interface S2C_JoinRejected {
  reason: string;
}
export interface S2C_PlayerJoined {
  player: PlayerState;
}
export interface S2C_PlayerLeft {
  id: PlayerId;
}
export interface S2C_Snapshot {
  t: number;
  players: PlayerState[];
}

/** Socket.IO 이벤트 이름 상수 */
export const EV = {
  JOIN: 'join',
  INPUT: 'input',
  WELCOME: 'welcome',
  JOIN_REJECTED: 'join_rejected',
  PLAYER_JOINED: 'player_joined',
  PLAYER_LEFT: 'player_left',
  SNAPSHOT: 'snapshot',
  PING: 'hb_ping',
  PONG: 'hb_pong',
} as const;

/** 아바타 색 팔레트 (서버가 입장 순으로 배정) */
export const PALETTE = [
  0xef4444, 0xf59e0b, 0x10b981, 0x3b82f6, 0x8b5cf6, 0xec4899, 0x14b8a6, 0xf97316,
  0x84cc16, 0x06b6d4, 0xa855f7, 0xf43f5e,
] as const;

export function clamp(v: number, min: number, max: number): number {
  return v < min ? min : v > max ? max : v;
}

/** 닉네임 정규화 + 대소문자 무시 비교용 키 */
export function nameKey(name: string): string {
  return name.trim().toLowerCase();
}
