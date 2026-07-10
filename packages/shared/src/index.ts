// ─────────────────────────────────────────────────────────────
// 공유 프로토콜 계약 (single source of truth)
// server / client / bot 이 전부 이 타입·상수를 import 한다.
// 여기를 바꾸면 양쪽이 컴파일 단계에서 어긋남을 감지한다.
// ─────────────────────────────────────────────────────────────

/** 네트워크 파라미터 (명세서 §7 권장 기술 스택 기준) */
export const NET = {
  /** 서버 단일 포트 (HTTP + Socket.IO) */
  PORT: 8080,
  /** 서버 → 클라 스냅샷 브로드캐스트 주기 (Hz) */
  SNAPSHOT_HZ: 15,
  /** 클라 → 서버 입력 송신 주기 (Hz) */
  CLIENT_SEND_HZ: 15,
  /** 앱레벨 하트비트 ping 주기 (ms) — Cloudflare Tunnel 유휴 종료 회피 */
  HEARTBEAT_MS: 2500,
  /** 서버가 이 시간 동안 신호 없으면 유령 판정 (ms) */
  HEARTBEAT_TIMEOUT_MS: 9000,
  /** 클라 원격 플레이어 렌더 지연 버퍼 (ms) — 보간용 */
  INTERP_DELAY_MS: 110,
} as const;

/** 월드 파라미터 (Day2 는 평지 아레나, Day3 에서 하이트맵 지형으로 확장) */
export const WORLD = {
  SIZE_X: 60,
  SIZE_Z: 60,
  GROUND_Y: 0,
  SPAWN_Y: 0,
} as const;

export type PlayerId = string;

/** 애니메이션 상태 (Day2 는 표시용 힌트) */
export const ANIM = { IDLE: 0, RUN: 1, JUMP: 2 } as const;
export type AnimState = (typeof ANIM)[keyof typeof ANIM];

/** 서버 권위 플레이어 스냅샷 1인분 */
export interface PlayerState {
  id: PlayerId;
  name: string;
  /** 0xRRGGBB */
  color: number;
  x: number;
  y: number;
  z: number;
  /** Y축 heading (rad) */
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
  /** 클라 송신 시퀀스 (디버깅·흐름 제어용) */
  seq: number;
}

// ── Server → Client ───────────────────────────────────────────
export interface S2C_Welcome {
  id: PlayerId;
  color: number;
  spawn: { x: number; y: number; z: number };
  /** 접속 시점의 전체 월드 스냅샷 (본인 포함) */
  players: PlayerState[];
  /** 서버 시각 (클라 시계 오프셋 추정용) */
  serverTime: number;
}
export interface S2C_PlayerJoined {
  player: PlayerState;
}
export interface S2C_PlayerLeft {
  id: PlayerId;
}
/** 배치 스냅샷: 1 메시지에 전원 상태를 묶어 보낸다 (클라당 O(1)/틱) */
export interface S2C_Snapshot {
  t: number;
  players: PlayerState[];
}

/** Socket.IO 이벤트 이름 상수 — 오타 방지 */
export const EV = {
  JOIN: 'join',
  INPUT: 'input',
  WELCOME: 'welcome',
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
