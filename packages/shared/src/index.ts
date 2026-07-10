// ─────────────────────────────────────────────────────────────
// 공유 프로토콜 계약 (single source of truth)
// server / client / bot 이 전부 이 타입·상수·지형/장애물 유틸을 import 한다.
// ─────────────────────────────────────────────────────────────

/** 네트워크 파라미터 */
export const NET = {
  PORT: 8080,
  SNAPSHOT_HZ: 15,
  CLIENT_SEND_HZ: 15,
  HEARTBEAT_MS: 2500,
  HEARTBEAT_TIMEOUT_MS: 9000,
  INTERP_DELAY_MS: 110,
} as const;

/** 월드 파라미터 (산·강 하이트맵 롱맵) */
export const WORLD = {
  SIZE_X: 80,
  SIZE_Z: 160,
  SEG_X: 80,
  SEG_Z: 160,
  WATER_Y: -1.2,
} as const;

/** 레이스 시작/결승 라인 (Z 축) 및 사망 수위 */
export const COURSE = {
  /** 스폰(출발) 영역 중심 Z (+끝) */
  START_Z: WORLD.SIZE_Z / 2 - 12,
  /** 결승선 Z (-끝). 이보다 작은 z 에 도달하면 완주 */
  GOAL_Z: -WORLD.SIZE_Z / 2 + 10,
  /** 이 높이 아래로 떨어지면 사망(강물에 빠짐) */
  DEATH_Y: WORLD.WATER_Y - 2.5,
} as const;

// ── 결정론적 하이트맵 ─────────────────────────────────────────
function smoothstep(t: number): number {
  t = t < 0 ? 0 : t > 1 ? 1 : t;
  return t * t * (3 - 2 * t);
}

export function terrainHeight(x: number, z: number): number {
  let h =
    3.2 * Math.sin(x * 0.06) * Math.cos(z * 0.045) +
    1.8 * Math.sin(x * 0.11 + z * 0.07) +
    1.0 * Math.sin(z * 0.19);
  const edge = Math.abs(x) / (WORLD.SIZE_X / 2);
  h += Math.pow(edge, 2.2) * 10;
  const riverCenter = 12 * Math.sin(z * 0.028) + 6 * Math.sin(z * 0.011);
  const bank = 9;
  const d = Math.abs(x - riverCenter);
  if (d < bank) h -= smoothstep(1 - d / bank) * 6.5;
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
  /** 이번 라운드 사망(관전) 여부 */
  dead: boolean;
}

// ── 매치 페이즈 / 주제(게임 모드) / 장애물 ────────────────────
export const PHASE = {
  LOBBY: 'LOBBY',
  PREP: 'PREP',
  ROULETTE: 'ROULETTE',
  PLAY: 'PLAY',
  HIGHLIGHT: 'HIGHLIGHT',
  RESULT: 'RESULT',
} as const;
export type Phase = (typeof PHASE)[keyof typeof PHASE];

export type TopicId = 'race' | 'height' | 'survive';
export interface TopicDef {
  id: TopicId;
  name: string;
  desc: string;
  /** 사망 시 부활 여부 (survive 는 관전) */
  respawn: boolean;
}
export const TOPICS: readonly TopicDef[] = [
  { id: 'race', name: '목표 지점 도달', desc: '결승선에 먼저 도달하세요', respawn: true },
  { id: 'height', name: '가장 높은 곳', desc: '라운드 종료 시 가장 높은 곳에 있으세요', respawn: true },
  { id: 'survive', name: '오래 버티기', desc: '강물에 빠지지 말고 오래 생존하세요', respawn: false },
];
export function topicDef(id: TopicId): TopicDef {
  return TOPICS.find((t) => t.id === id)!;
}

export const OBSTACLE = { WALL: 'WALL', CYLINDER: 'CYLINDER', GHOST: 'GHOST' } as const;
export type ObstacleType = (typeof OBSTACLE)[keyof typeof OBSTACLE];

export interface Obstacle {
  id: number;
  type: ObstacleType;
  x: number;
  z: number;
  yaw: number;
  ownerId: PlayerId;
}

/** 장애물 크기 (클라 렌더 + 서버/클라 충돌 공유) */
export const OBSTACLE_SIZE = {
  WALL: { w: 6, h: 3, d: 0.7 },
  GHOST: { w: 6, h: 3, d: 0.7 },
  CYLINDER: { r: 1.3, h: 3 },
} as const;

/** 매치 규칙 상수 */
export const RULES = {
  MAX_OBSTACLES_PER_PLAYER: 3,
  ROUNDS: 3,
  PHASE_SEC: { LOBBY: 3, PREP: 30, ROULETTE: 5, PLAY: 45, HIGHLIGHT: 8, RESULT: 14 } as Record<Phase, number>,
  /** 사망 귀속 반경 (이 반경 내 소유 장애물이 있으면 킬 크레딧) */
  KILL_ATTRIB_RADIUS: 4,
  /** 클리어:장애물 점수 비율 */
  CLEAR_WEIGHT: 0.7,
  OBSTACLE_KILL_POINTS: 100,
  OBSTACLE_SCORE_CAP: 300,
  HEIGHT_MAX: 12,
} as const;

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
export interface C2S_Place {
  type: ObstacleType;
  x: number;
  z: number;
  yaw: number;
}
export interface C2S_Vote {
  topicId: TopicId;
}
export interface C2S_Died {
  x: number;
  y: number;
  z: number;
}

// ── Server → Client ───────────────────────────────────────────
export interface S2C_Welcome {
  id: PlayerId;
  color: number;
  spawn: { x: number; y: number; z: number };
  players: PlayerState[];
  obstacles: Obstacle[];
  phase: Phase;
  round: number;
  endsAt: number;
  topicId: TopicId | null;
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
export interface S2C_Phase {
  phase: Phase;
  round: number;
  endsAt: number;
  topicId: TopicId | null;
}
export interface S2C_ObstacleAdded {
  obstacle: Obstacle;
}
export interface S2C_ObstacleRejected {
  reason: string;
}
export interface S2C_ObstacleSnapshot {
  obstacles: Obstacle[];
}
export interface S2C_VoteUpdate {
  counts: Partial<Record<TopicId, number>>;
  myBudget: number;
}
export interface S2C_Roulette {
  topicId: TopicId;
  order: TopicId[];
  spinMs: number;
  startAt: number;
}
export interface S2C_Respawn {
  x: number;
  y: number;
  z: number;
}
export interface ScoreRow {
  id: PlayerId;
  name: string;
  color: number;
  clear: number;
  obstacle: number;
  total: number;
  cumulative: number;
  rank: number;
}
export interface S2C_RoundResult {
  round: number;
  topicId: TopicId;
  rows: ScoreRow[];
}
export interface S2C_Highlight {
  round: number;
  deaths: number;
  topScorerName: string | null;
  deadliestOwnerName: string | null;
  deadliestKills: number;
  text: string;
}
export interface S2C_MatchResult {
  rows: ScoreRow[];
}

export const EV = {
  JOIN: 'join',
  INPUT: 'input',
  PLACE: 'obstacle:place',
  VOTE: 'vote',
  DIED: 'died',
  WELCOME: 'welcome',
  JOIN_REJECTED: 'join_rejected',
  PLAYER_JOINED: 'player_joined',
  PLAYER_LEFT: 'player_left',
  SNAPSHOT: 'snapshot',
  PHASE: 'phase',
  OBSTACLE_ADDED: 'obstacle:added',
  OBSTACLE_REJECTED: 'obstacle:rejected',
  OBSTACLE_SNAPSHOT: 'obstacle:snapshot',
  VOTE_UPDATE: 'vote:update',
  ROULETTE: 'roulette',
  RESPAWN: 'respawn',
  ROUND_RESULT: 'round:result',
  HIGHLIGHT: 'highlight',
  MATCH_RESULT: 'match:result',
  PING: 'hb_ping',
  PONG: 'hb_pong',
} as const;

export const PALETTE = [
  0xef4444, 0xf59e0b, 0x10b981, 0x3b82f6, 0x8b5cf6, 0xec4899, 0x14b8a6, 0xf97316,
  0x84cc16, 0x06b6d4, 0xa855f7, 0xf43f5e,
] as const;

export function clamp(v: number, min: number, max: number): number {
  return v < min ? min : v > max ? max : v;
}
export function nameKey(name: string): string {
  return name.trim().toLowerCase();
}

/**
 * 캡슐(반경 r) 대 장애물 수평 충돌 밀어내기.
 * (px,pz) 를 장애물 밖으로 민 새 좌표를 반환. 충돌 없으면 그대로.
 * 클라이언트 이동과 서버 검증이 동일 결과를 내도록 shared 에 둔다.
 */
export function resolveObstacle(px: number, pz: number, r: number, o: Obstacle): [number, number] {
  if (o.type === 'CYLINDER') {
    const dx = px - o.x;
    const dz = pz - o.z;
    const dist = Math.hypot(dx, dz);
    const min = OBSTACLE_SIZE.CYLINDER.r + r;
    if (dist < min && dist > 1e-4) {
      const push = min - dist;
      return [px + (dx / dist) * push, pz + (dz / dist) * push];
    }
    return [px, pz];
  }
  // WALL / GHOST: 회전 AABB. 장애물 로컬 좌표로 변환해 판정.
  const size = OBSTACLE_SIZE.WALL;
  const cos = Math.cos(-o.yaw);
  const sin = Math.sin(-o.yaw);
  const lx = (px - o.x) * cos - (pz - o.z) * sin;
  const lz = (px - o.x) * sin + (pz - o.z) * cos;
  const hw = size.w / 2 + r;
  const hd = size.d / 2 + r;
  if (lx > -hw && lx < hw && lz > -hd && lz < hd) {
    // 가장 가까운 면으로 밀어냄
    const penX = hw - Math.abs(lx);
    const penZ = hd - Math.abs(lz);
    let nlx = lx;
    let nlz = lz;
    if (penX < penZ) nlx = lx < 0 ? -hw : hw;
    else nlz = lz < 0 ? -hd : hd;
    // 로컬 → 월드 복원
    const wcos = Math.cos(o.yaw);
    const wsin = Math.sin(o.yaw);
    return [o.x + nlx * wcos - nlz * wsin, o.z + nlx * wsin + nlz * wcos];
  }
  return [px, pz];
}
