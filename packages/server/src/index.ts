// ─────────────────────────────────────────────────────────────
// 룰렛 파티 — 권위 서버 (Day4~6: 매치 FSM · 장애물 · 투표 · 룰렛 · 모드 · 점수 · 하이라이트)
//
// 넷코드: 클라 권위 이동 릴레이 + 서버 권위 게임 원장.
// 매치 흐름: PREP(장애물·투표) → [ROULETTE → PLAY → HIGHLIGHT] ×3라운드 → RESULT → 반복
// ─────────────────────────────────────────────────────────────
import { createServer } from 'node:http';
import { Server } from 'socket.io';
import {
  NET, WORLD, COURSE, RULES, EV, PHASE, PALETTE, TOPICS, OBSTACLE_SIZE,
  clamp, terrainHeight, nameKey, topicDef, resolveObstacle,
  type Phase, type TopicId, type Obstacle, type ObstacleType,
  type PlayerState, type ScoreRow,
  type C2S_Join, type C2S_Input, type C2S_Place, type C2S_Vote, type C2S_Died,
  type S2C_Welcome, type S2C_JoinRejected, type S2C_PlayerJoined, type S2C_PlayerLeft,
  type S2C_Snapshot, type S2C_Phase, type S2C_ObstacleAdded, type S2C_ObstacleRejected,
  type S2C_ObstacleSnapshot, type S2C_VoteUpdate, type S2C_Roulette, type S2C_Respawn,
  type S2C_RoundResult, type S2C_Highlight, type S2C_MatchResult,
} from '@roulette/shared';

// 개발/시연용 빠른 페이즈: FAST=1 로 실행하면 매치가 ~1분으로 짧아진다
const FAST = process.env.FAST === '1';
const SEC: Record<Phase, number> = FAST
  ? { LOBBY: 1, PREP: 6, ROULETTE: 3, PLAY: 8, HIGHLIGHT: 3, RESULT: 5 }
  : RULES.PHASE_SEC;

interface Entry {
  state: PlayerState;
  lastSeen: number;
}

const players = new Map<string, Entry>();
const obstacles = new Map<number, Obstacle>();
const votes = new Map<string, TopicId>();
const cumulative = new Map<string, number>();
let colorCursor = 0;
let obstacleSeq = 1;

interface DeathEvent { id: string; ownerId: string | null; tMs: number }

const match = {
  phase: PHASE.LOBBY as Phase,
  round: 0,
  endsAt: 0,
  topicId: null as TopicId | null,
  prevTopicId: null as TopicId | null,
  roundStart: 0,
  // 라운드별 추적
  peak: new Map<string, number>(),
  finishMs: new Map<string, number>(),
  eliminatedMs: new Map<string, number>(),
  kills: new Map<string, number>(),
  deaths: [] as DeathEvent[],
  lastHighlight: null as S2C_Highlight | null,
};

// ── HTTP + Socket.IO ──────────────────────────────────────────
const http = createServer((req, res) => {
  if (req.url === '/health') {
    res.writeHead(200, { 'content-type': 'application/json' });
    res.end(JSON.stringify({ ok: true, players: players.size, phase: match.phase, round: match.round }));
    return;
  }
  res.writeHead(200, { 'content-type': 'text/plain; charset=utf-8' });
  res.end('roulette-party server');
});
const io = new Server(http, { cors: { origin: '*' } });

// ── 유틸 ──────────────────────────────────────────────────────
function startSpawn() {
  // 강물(수면 아래)·산 가장자리를 피해 걷기 좋은 출발 지점을 고른다
  for (let i = 0; i < 40; i++) {
    const x = (Math.random() - 0.5) * WORLD.SIZE_X * 0.5;
    const z = COURSE.START_Z + (Math.random() - 0.5) * 10;
    const y = terrainHeight(x, z);
    if (y > WORLD.WATER_Y + 1.0) return { x, y, z };
  }
  const z = COURSE.START_Z;
  const x = -18;
  return { x, y: terrainHeight(x, z), z };
}
function isNameTaken(name: string): boolean {
  const key = nameKey(name);
  for (const e of players.values()) if (nameKey(e.state.name) === key) return true;
  return false;
}
function ownedCount(id: string): number {
  let n = 0;
  for (const o of obstacles.values()) if (o.ownerId === id) n++;
  return n;
}
function broadcastPhase() {
  io.emit(EV.PHASE, {
    phase: match.phase, round: match.round, endsAt: match.endsAt, topicId: match.topicId,
  } satisfies S2C_Phase);
}
function voteCounts(): Partial<Record<TopicId, number>> {
  const c: Partial<Record<TopicId, number>> = {};
  for (const t of votes.values()) c[t] = (c[t] ?? 0) + 1;
  return c;
}
function broadcastVotes() {
  io.emit(EV.VOTE_UPDATE, { counts: voteCounts(), myBudget: RULES.MAX_OBSTACLES_PER_PLAYER } satisfies S2C_VoteUpdate);
}

// ── 페이즈 전이 ───────────────────────────────────────────────
function setPhase(phase: Phase, sec: number) {
  match.phase = phase;
  match.endsAt = Date.now() + sec * 1000;
}

function enterLobby() {
  setPhase(PHASE.LOBBY, SEC.LOBBY);
  match.round = 0;
  match.topicId = null;
  broadcastPhase();
}
function enterPrep() {
  obstacles.clear();
  votes.clear();
  cumulative.clear();
  match.round = 0;
  match.topicId = null;
  match.prevTopicId = null;
  for (const e of players.values()) e.state.dead = false;
  io.emit(EV.OBSTACLE_SNAPSHOT, { obstacles: [] } satisfies S2C_ObstacleSnapshot);
  broadcastVotes();
  setPhase(PHASE.PREP, SEC.PREP);
  broadcastPhase();
}
function pickTopic(): { winner: TopicId; order: TopicId[] } {
  const counts = voteCounts();
  const candidates = TOPICS.filter((t) => t.id !== match.prevTopicId);
  const weights = candidates.map((t) => (counts[t.id] ?? 0) + 1);
  const total = weights.reduce((a, b) => a + b, 0);
  let r = Math.random() * total;
  let winner = candidates[0].id;
  for (let i = 0; i < candidates.length; i++) {
    r -= weights[i];
    if (r <= 0) { winner = candidates[i].id; break; }
  }
  return { winner, order: TOPICS.map((t) => t.id) };
}
function enterRoulette(round: number) {
  match.round = round;
  const { winner, order } = pickTopic();
  match.topicId = winner;
  match.prevTopicId = winner;
  setPhase(PHASE.ROULETTE, SEC.ROULETTE);
  io.emit(EV.ROULETTE, {
    topicId: winner, order, spinMs: (SEC.ROULETTE - 1) * 1000, startAt: Date.now(),
  } satisfies S2C_Roulette);
  broadcastPhase();
}
function enterPlay() {
  match.peak.clear();
  match.finishMs.clear();
  match.eliminatedMs.clear();
  match.kills.clear();
  match.deaths = [];
  match.roundStart = Date.now();
  // 전원 출발선으로 텔레포트
  for (const [id, e] of players) {
    const sp = startSpawn();
    e.state.x = sp.x; e.state.y = sp.y; e.state.z = sp.z; e.state.dead = false;
    match.peak.set(id, sp.y);
    io.to(id).emit(EV.RESPAWN, { x: sp.x, y: sp.y, z: sp.z } satisfies S2C_Respawn);
  }
  setPhase(PHASE.PLAY, SEC.PLAY);
  broadcastPhase();
}
function computeRound() {
  const topic = match.topicId!;
  const M = Math.max(players.size, 1);
  const roundMs = SEC.PLAY * 1000;

  // 완주 순위 (race)
  const finishers = [...match.finishMs.entries()].sort((a, b) => a[1] - b[1]);
  const finishRank = new Map<string, number>();
  finishers.forEach(([id], i) => finishRank.set(id, i + 1));

  const rows: ScoreRow[] = [];
  for (const [id, e] of players) {
    let clear = 0;
    if (topic === 'race') {
      if (finishRank.has(id)) {
        const rank = finishRank.get(id)!;
        clear = 500 + (500 * (M - rank)) / Math.max(M - 1, 1);
      } else {
        const prog = clamp((COURSE.START_Z - e.state.z) / (COURSE.START_Z - COURSE.GOAL_Z), 0, 1);
        clear = 500 * prog;
      }
    } else if (topic === 'height') {
      clear = 1000 * clamp((match.peak.get(id) ?? e.state.y) / RULES.HEIGHT_MAX, 0, 1);
    } else {
      const aliveMs = match.eliminatedMs.has(id) ? match.eliminatedMs.get(id)! : roundMs;
      clear = 1000 * clamp(aliveMs / roundMs, 0, 1);
    }
    const kills = match.kills.get(id) ?? 0;
    const obstacle = Math.min(RULES.OBSTACLE_SCORE_CAP, kills * RULES.OBSTACLE_KILL_POINTS);
    const total = Math.round(clear * RULES.CLEAR_WEIGHT) + obstacle;
    const cum = (cumulative.get(id) ?? 0) + total;
    cumulative.set(id, cum);
    rows.push({ id, name: e.state.name, color: e.state.color, clear: Math.round(clear), obstacle, total, cumulative: cum, rank: 0 });
  }
  rows.sort((a, b) => b.cumulative - a.cumulative);
  rows.forEach((r, i) => (r.rank = i + 1));
  io.emit(EV.ROUND_RESULT, { round: match.round, topicId: topic, rows } satisfies S2C_RoundResult);

  // 하이라이트 산정
  const deaths = match.deaths.length;
  const killTally = new Map<string, { name: string; n: number }>();
  for (const d of match.deaths) {
    if (!d.ownerId) continue;
    const owner = players.get(d.ownerId);
    if (!owner) continue;
    const cur = killTally.get(d.ownerId) ?? { name: owner.state.name, n: 0 };
    cur.n++;
    killTally.set(d.ownerId, cur);
  }
  let deadliestOwnerName: string | null = null;
  let deadliestKills = 0;
  for (const v of killTally.values()) if (v.n > deadliestKills) { deadliestKills = v.n; deadliestOwnerName = v.name; }
  const topScorerName = rows.length ? rows.sort((a, b) => b.total - a.total)[0].name : null;
  let text = `${match.round}라운드 · ${topicDef(topic).name}`;
  if (deaths > 0) text += ` · ${deaths}명 낙사`;
  if (deadliestOwnerName) text += ` · 최다 유발 ${deadliestOwnerName}(${deadliestKills}킬)`;
  match.lastHighlight = { round: match.round, deaths, topScorerName, deadliestOwnerName, deadliestKills, text };
}
function enterHighlight() {
  computeRound();
  setPhase(PHASE.HIGHLIGHT, SEC.HIGHLIGHT);
  if (match.lastHighlight) io.emit(EV.HIGHLIGHT, match.lastHighlight satisfies S2C_Highlight);
  broadcastPhase();
}
function enterResult() {
  const rows: ScoreRow[] = [...players.values()].map((e) => ({
    id: e.state.id, name: e.state.name, color: e.state.color,
    clear: 0, obstacle: 0, total: 0, cumulative: cumulative.get(e.state.id) ?? 0, rank: 0,
  }));
  rows.sort((a, b) => b.cumulative - a.cumulative);
  rows.forEach((r, i) => (r.rank = i + 1));
  io.emit(EV.MATCH_RESULT, { rows } satisfies S2C_MatchResult);
  setPhase(PHASE.RESULT, SEC.RESULT);
  broadcastPhase();
}

// ── 매치 컨트롤러 (150ms) ────────────────────────────────────
setInterval(() => {
  const now = Date.now();
  if (match.phase === PHASE.LOBBY) {
    if (players.size > 0 && now >= match.endsAt) enterPrep();
    return;
  }
  if (players.size === 0) { enterLobby(); return; }
  if (match.phase === PHASE.PLAY) updatePlayTracking(now);
  if (now < match.endsAt) return;

  switch (match.phase) {
    case PHASE.PREP: enterRoulette(1); break;
    case PHASE.ROULETTE: enterPlay(); break;
    case PHASE.PLAY: enterHighlight(); break;
    case PHASE.HIGHLIGHT:
      if (match.round < RULES.ROUNDS) enterRoulette(match.round + 1);
      else enterResult();
      break;
    case PHASE.RESULT: enterPrep(); break;
  }
}, 150);

function updatePlayTracking(now: number) {
  for (const [id, e] of players) {
    if (e.state.dead) continue;
    // 최고 높이
    const p = match.peak.get(id);
    if (p === undefined || e.state.y > p) match.peak.set(id, e.state.y);
    // 레이스 완주
    if (match.topicId === 'race' && !match.finishMs.has(id) && e.state.z <= COURSE.GOAL_Z) {
      match.finishMs.set(id, now - match.roundStart);
    }
  }
}

// ── 사망 처리 ─────────────────────────────────────────────────
function handleDeath(id: string, x: number, y: number, z: number) {
  if (match.phase !== PHASE.PLAY) return;
  const e = players.get(id);
  if (!e || e.state.dead) return;
  e.state.dead = true;

  // 귀속: 사망 지점 반경 내 소유 장애물 → 소유자에게 킬
  let ownerId: string | null = null;
  let best: number = RULES.KILL_ATTRIB_RADIUS;
  for (const o of obstacles.values()) {
    if (o.ownerId === id) continue;
    const d = Math.hypot(x - o.x, z - o.z);
    if (d < best) { best = d; ownerId = o.ownerId; }
  }
  if (ownerId) match.kills.set(ownerId, (match.kills.get(ownerId) ?? 0) + 1);
  match.deaths.push({ id, ownerId, tMs: Date.now() - match.roundStart });

  const topic = topicDef(match.topicId!);
  if (topic.respawn) {
    setTimeout(() => {
      const cur = players.get(id);
      if (!cur || match.phase !== PHASE.PLAY) return;
      const sp = startSpawn();
      cur.state.x = sp.x; cur.state.y = sp.y; cur.state.z = sp.z; cur.state.dead = false;
      io.to(id).emit(EV.RESPAWN, { x: sp.x, y: sp.y, z: sp.z } satisfies S2C_Respawn);
    }, 2000);
  } else {
    match.eliminatedMs.set(id, Date.now() - match.roundStart);
  }
}

// ── 연결 ──────────────────────────────────────────────────────
io.on('connection', (socket) => {
  let id: string | null = null;

  socket.on(EV.JOIN, (msg: C2S_Join) => {
    if (id) return;
    const name = (msg?.name ?? '').toString().slice(0, 16).trim();
    if (!name) { socket.emit(EV.JOIN_REJECTED, { reason: '닉네임을 입력하세요.' } satisfies S2C_JoinRejected); return; }
    if (isNameTaken(name)) {
      socket.emit(EV.JOIN_REJECTED, { reason: `'${name}' 은(는) 이미 사용 중입니다. 다른 닉네임을 써주세요.` } satisfies S2C_JoinRejected);
      return;
    }
    id = socket.id;
    const color = PALETTE[colorCursor++ % PALETTE.length];
    const sp = startSpawn();
    const state: PlayerState = { id, name, color, x: sp.x, y: sp.y, z: sp.z, yaw: 0, anim: 0, dead: false };
    players.set(id, { state, lastSeen: Date.now() });

    socket.emit(EV.WELCOME, {
      id, color, spawn: sp,
      players: [...players.values()].map((e) => e.state),
      obstacles: [...obstacles.values()],
      phase: match.phase, round: match.round, endsAt: match.endsAt, topicId: match.topicId,
      serverTime: Date.now(),
    } satisfies S2C_Welcome);
    socket.broadcast.emit(EV.PLAYER_JOINED, { player: state } satisfies S2C_PlayerJoined);
    console.log(`[join]  ${name} (${id})  ·  현재 ${players.size}명`);
  });

  socket.on(EV.INPUT, (msg: C2S_Input) => {
    if (!id) return;
    const e = players.get(id);
    if (!e) return;
    e.lastSeen = Date.now();
    const hx = WORLD.SIZE_X / 2, hz = WORLD.SIZE_Z / 2;
    e.state.x = clamp(Number(msg.x) || 0, -hx, hx);
    e.state.z = clamp(Number(msg.z) || 0, -hz, hz);
    const gy = terrainHeight(e.state.x, e.state.z);
    e.state.y = clamp(Number(msg.y) || 0, gy - 3, gy + 60);
    e.state.yaw = Number(msg.yaw) || 0;
    e.state.anim = (msg.anim | 0) as PlayerState['anim'];
  });

  socket.on(EV.PLACE, (msg: C2S_Place) => {
    if (!id) return;
    if (match.phase !== PHASE.PREP) { socket.emit(EV.OBSTACLE_REJECTED, { reason: '준비 페이즈에만 배치할 수 있어요.' } satisfies S2C_ObstacleRejected); return; }
    if (ownedCount(id) >= RULES.MAX_OBSTACLES_PER_PLAYER) { socket.emit(EV.OBSTACLE_REJECTED, { reason: `1인당 최대 ${RULES.MAX_OBSTACLES_PER_PLAYER}개까지예요.` } satisfies S2C_ObstacleRejected); return; }
    const type = msg.type as ObstacleType;
    if (!['WALL', 'CYLINDER', 'GHOST'].includes(type)) return;
    const x = clamp(Number(msg.x) || 0, -WORLD.SIZE_X / 2 + 2, WORLD.SIZE_X / 2 - 2);
    const z = clamp(Number(msg.z) || 0, COURSE.GOAL_Z + 3, COURSE.START_Z - 3);
    const yaw = Number(msg.yaw) || 0;
    // 겹침 방지
    for (const o of obstacles.values()) {
      if (Math.hypot(o.x - x, o.z - z) < 2.5) { socket.emit(EV.OBSTACLE_REJECTED, { reason: '너무 가까이 겹쳐요.' } satisfies S2C_ObstacleRejected); return; }
    }
    const obstacle: Obstacle = { id: obstacleSeq++, type, x, z, yaw, ownerId: id };
    obstacles.set(obstacle.id, obstacle);
    io.emit(EV.OBSTACLE_ADDED, { obstacle } satisfies S2C_ObstacleAdded);
  });

  socket.on(EV.VOTE, (msg: C2S_Vote) => {
    if (!id || match.phase !== PHASE.PREP) return;
    if (!TOPICS.some((t) => t.id === msg.topicId)) return;
    votes.set(id, msg.topicId);
    broadcastVotes();
  });

  socket.on(EV.DIED, (msg: C2S_Died) => {
    if (!id) return;
    handleDeath(id, Number(msg.x) || 0, Number(msg.y) || 0, Number(msg.z) || 0);
  });

  socket.on(EV.PING, (clientTime: number) => {
    const e = id ? players.get(id) : undefined;
    if (e) e.lastSeen = Date.now();
    socket.emit(EV.PONG, clientTime);
  });

  socket.on('disconnect', (reason) => {
    if (id && players.delete(id)) {
      votes.delete(id);
      io.emit(EV.PLAYER_LEFT, { id } satisfies S2C_PlayerLeft);
      console.log(`[left]  ${id} (${reason})  ·  현재 ${players.size}명`);
    }
  });
});

// ── 스냅샷 브로드캐스트 ───────────────────────────────────────
setInterval(() => {
  if (players.size === 0) return;
  io.emit(EV.SNAPSHOT, { t: Date.now(), players: [...players.values()].map((e) => e.state) } satisfies S2C_Snapshot);
}, 1000 / NET.SNAPSHOT_HZ);

// ── 하트비트 스윕 ─────────────────────────────────────────────
setInterval(() => {
  const now = Date.now();
  for (const [pid, e] of players) {
    if (now - e.lastSeen > NET.HEARTBEAT_TIMEOUT_MS) {
      players.delete(pid);
      votes.delete(pid);
      io.emit(EV.PLAYER_LEFT, { id: pid } satisfies S2C_PlayerLeft);
      io.sockets.sockets.get(pid)?.disconnect(true);
      console.log(`[timeout]  ${pid}`);
    }
  }
}, 2000);

// void 참조 (미사용 import 방지)
void OBSTACLE_SIZE;
void resolveObstacle;

enterLobby();
http.listen(NET.PORT, () => {
  console.log(`\n  🎡  roulette-party server  ·  http://localhost:${NET.PORT}`);
  console.log(`      매치 FSM 가동 · snapshot ${NET.SNAPSHOT_HZ}Hz\n`);
});
