// ─────────────────────────────────────────────────────────────
// 룰렛 파티 클라이언트 (Day4~6: 준비·룰렛·플레이·하이라이트·결과 전체 루프)
// ─────────────────────────────────────────────────────────────
import './style.css';
import * as THREE from 'three';
import { io, type Socket } from 'socket.io-client';
import {
  NET, WORLD, COURSE, RULES, EV, PHASE, ANIM, OBSTACLE_SIZE,
  clamp, terrainHeight, resolveObstacle, topicDef,
  type Phase, type TopicId, type ObstacleType, type Obstacle,
  type C2S_Join, type C2S_Input, type C2S_Place, type C2S_Vote, type C2S_Died,
  type S2C_Welcome, type S2C_JoinRejected, type S2C_Snapshot,
  type S2C_PlayerJoined, type S2C_PlayerLeft, type S2C_Phase,
  type S2C_ObstacleAdded, type S2C_ObstacleRejected, type S2C_ObstacleSnapshot,
  type S2C_VoteUpdate, type S2C_Roulette, type S2C_Respawn,
  type S2C_RoundResult, type S2C_Highlight, type S2C_MatchResult,
  type PlayerState,
} from '@roulette/shared';

const SERVER_URL =
  (import.meta as any).env?.VITE_SERVER_URL || `${location.protocol}//${location.hostname}:${NET.PORT}`;

// ── DOM ───────────────────────────────────────────────────────
const $ = (id: string) => document.getElementById(id)!;
const overlay = $('overlay');
const nameInput = $('name') as HTMLInputElement;
const joinBtn = $('join') as HTMLButtonElement;
const joinError = $('joinError');
const hud = $('hud');
const statusEl = $('status');
const listEl = $('list');
const phaseBar = $('phaseBar');
const phaseName = $('phaseName');
const timerEl = $('timer');
const objectiveEl = $('objective');
const prepEl = $('prep');
const obsLeftEl = $('obsLeft');
const rouletteEl = $('roulette');
const highlightEl = $('highlight');
const resultEl = $('result');
const resultRows = $('resultRows');

// ── Three.js ──────────────────────────────────────────────────
const scene = new THREE.Scene();
scene.background = new THREE.Color(0x87ceeb);
scene.fog = new THREE.Fog(0x87ceeb, 100, 220);
const camera = new THREE.PerspectiveCamera(70, innerWidth / innerHeight, 0.1, 600);
const renderer = new THREE.WebGLRenderer({ antialias: true });
renderer.setSize(innerWidth, innerHeight);
renderer.setPixelRatio(Math.min(devicePixelRatio, 2));
document.body.appendChild(renderer.domElement);
addEventListener('resize', () => {
  camera.aspect = innerWidth / innerHeight;
  camera.updateProjectionMatrix();
  renderer.setSize(innerWidth, innerHeight);
});
scene.add(new THREE.HemisphereLight(0xffffff, 0x557733, 0.9));
const sun = new THREE.DirectionalLight(0xffffff, 1.0);
sun.position.set(40, 80, 30);
scene.add(sun);

let terrainMesh: THREE.Mesh;
buildTerrain();
buildGoalLine();

function buildTerrain() {
  const geo = new THREE.PlaneGeometry(WORLD.SIZE_X, WORLD.SIZE_Z, WORLD.SEG_X, WORLD.SEG_Z);
  geo.rotateX(-Math.PI / 2);
  const pos = geo.attributes.position as THREE.BufferAttribute;
  const cSand = new THREE.Color(0xcbb681), cGrass = new THREE.Color(0x5a8a3a);
  const cRock = new THREE.Color(0x8a7a5a), cSnow = new THREE.Color(0xe8eef2);
  const colors = new Float32Array(pos.count * 3);
  const t = new THREE.Color();
  for (let i = 0; i < pos.count; i++) {
    const x = pos.getX(i), z = pos.getZ(i);
    const y = terrainHeight(x, z);
    pos.setY(i, y);
    if (y < WORLD.WATER_Y + 0.6) t.copy(cSand);
    else if (y > 7.5) t.copy(cSnow);
    else if (y > 3.5) t.copy(cRock);
    else t.copy(cGrass);
    colors[i * 3] = t.r; colors[i * 3 + 1] = t.g; colors[i * 3 + 2] = t.b;
  }
  geo.setAttribute('color', new THREE.BufferAttribute(colors, 3));
  geo.computeVertexNormals();
  terrainMesh = new THREE.Mesh(geo, new THREE.MeshStandardMaterial({ vertexColors: true }));
  scene.add(terrainMesh);
  const water = new THREE.Mesh(
    new THREE.PlaneGeometry(WORLD.SIZE_X, WORLD.SIZE_Z),
    new THREE.MeshStandardMaterial({ color: 0x2a6fb0, transparent: true, opacity: 0.62 }),
  );
  water.rotation.x = -Math.PI / 2;
  water.position.y = WORLD.WATER_Y;
  scene.add(water);
}
function buildGoalLine() {
  const mk = (z: number, color: number) => {
    const m = new THREE.Mesh(
      new THREE.BoxGeometry(WORLD.SIZE_X, 0.4, 1.2),
      new THREE.MeshStandardMaterial({ color, transparent: true, opacity: 0.5 }),
    );
    m.position.set(0, terrainHeight(0, z) + 4, z);
    scene.add(m);
  };
  mk(COURSE.GOAL_Z, 0x22c55e); // 결승선
  mk(COURSE.START_Z, 0xf59e0b); // 출발선
}

// ── 아바타 ────────────────────────────────────────────────────
function makeAvatar(color: number, name: string): THREE.Group {
  const g = new THREE.Group();
  const body = new THREE.Mesh(new THREE.CapsuleGeometry(0.4, 0.9, 4, 10), new THREE.MeshStandardMaterial({ color }));
  body.position.y = 0.85;
  g.add(body);
  const nose = new THREE.Mesh(new THREE.BoxGeometry(0.18, 0.18, 0.3), new THREE.MeshStandardMaterial({ color: 0x1a1a1a }));
  nose.position.set(0, 0.95, -0.42);
  g.add(nose);
  g.add(makeLabel(name));
  return g;
}
function makeLabel(text: string): THREE.Sprite {
  const c = document.createElement('canvas');
  c.width = 256; c.height = 64;
  const x = c.getContext('2d')!;
  x.fillStyle = 'rgba(0,0,0,0.55)';
  x.beginPath();
  (x as any).roundRect ? (x as any).roundRect(4, 8, 248, 48, 12) : x.rect(4, 8, 248, 48);
  x.fill();
  x.font = 'bold 30px system-ui, sans-serif';
  x.fillStyle = '#fff'; x.textAlign = 'center'; x.textBaseline = 'middle';
  x.fillText(text, 128, 34);
  const tex = new THREE.CanvasTexture(c);
  tex.anisotropy = 4;
  const spr = new THREE.Sprite(new THREE.SpriteMaterial({ map: tex, depthTest: false, transparent: true }));
  spr.scale.set(2.4, 0.6, 1);
  spr.position.y = 2.1;
  spr.renderOrder = 999;
  return spr;
}

// ── 장애물 ────────────────────────────────────────────────────
const obstacleData = new Map<number, Obstacle>();
const obstacleMeshes = new Map<number, THREE.Object3D>();

function makeObstacleMesh(o: Obstacle): THREE.Object3D {
  let mesh: THREE.Mesh;
  if (o.type === 'CYLINDER') {
    const s = OBSTACLE_SIZE.CYLINDER;
    mesh = new THREE.Mesh(new THREE.CylinderGeometry(s.r, s.r, s.h, 16), new THREE.MeshStandardMaterial({ color: 0x9ca3af }));
    mesh.position.set(o.x, terrainHeight(o.x, o.z) + s.h / 2, o.z);
  } else {
    const s = OBSTACLE_SIZE.WALL;
    const ghost = o.type === 'GHOST';
    mesh = new THREE.Mesh(
      new THREE.BoxGeometry(s.w, s.h, s.d),
      new THREE.MeshStandardMaterial({ color: ghost ? 0xbfe3ff : 0x94a3b8, transparent: ghost, opacity: ghost ? 0.14 : 1 }),
    );
    mesh.position.set(o.x, terrainHeight(o.x, o.z) + s.h / 2, o.z);
    mesh.rotation.y = o.yaw;
  }
  return mesh;
}
function addObstacle(o: Obstacle) {
  obstacleData.set(o.id, o);
  const m = makeObstacleMesh(o);
  obstacleMeshes.set(o.id, m);
  scene.add(m);
  updateObsLeft();
}
function clearObstacles() {
  for (const m of obstacleMeshes.values()) scene.remove(m);
  obstacleMeshes.clear();
  obstacleData.clear();
  updateObsLeft();
}
function updateObsLeft() {
  let mine = 0;
  for (const o of obstacleData.values()) if (o.ownerId === myId) mine++;
  obsLeftEl.textContent = `남은 배치 ${RULES.MAX_OBSTACLES_PER_PLAYER - mine}개`;
}

// ── 상태 ──────────────────────────────────────────────────────
let socket: Socket | null = null;
let myId: string | null = null;
let myColor = 0xffffff;
let pendingName = '';
let phase: Phase = PHASE.LOBBY;
let roundNo = 0;
let endsAt = 0;
let topicId: TopicId | null = null;
let selectedType: ObstacleType = 'WALL';
let placeYaw = 0;
let myVote: TopicId | null = null;
let localDead = false;
const cumulativeMap = new Map<string, number>();

const local = { x: 0, y: 0, z: 0, yaw: 0, vy: 0, grounded: true, anim: ANIM.IDLE as number };
let localAvatar: THREE.Group | null = null;

interface Remote { group: THREE.Group; tx: number; ty: number; tz: number; tyaw: number; name: string; color: number }
const remotes = new Map<string, Remote>();

// ── 입력 ──────────────────────────────────────────────────────
const keys: Record<string, boolean> = {};
addEventListener('keydown', (e) => {
  keys[e.code] = true;
  if (e.code === 'Space') e.preventDefault();
  if (e.code === 'KeyR' && phase === PHASE.PREP) placeYaw += Math.PI / 6;
});
addEventListener('keyup', (e) => { keys[e.code] = false; });

const ndc = new THREE.Vector2();
const raycaster = new THREE.Raycaster();
renderer.domElement.addEventListener('click', (e) => {
  if (!myId) return;
  if (phase === PHASE.PREP) {
    // 바닥 레이캐스트 → 장애물 배치
    ndc.set((e.clientX / innerWidth) * 2 - 1, -(e.clientY / innerHeight) * 2 + 1);
    raycaster.setFromCamera(ndc, camera);
    const hit = raycaster.intersectObject(terrainMesh)[0];
    if (hit && socket) {
      socket.emit(EV.PLACE, { type: selectedType, x: hit.point.x, z: hit.point.z, yaw: placeYaw } satisfies C2S_Place);
    }
  } else if (phase === PHASE.PLAY && !localDead) {
    renderer.domElement.requestPointerLock();
  }
});
addEventListener('mousemove', (e) => {
  if (document.pointerLockElement === renderer.domElement) local.yaw -= e.movementX * 0.0025;
});

// 팔레트 · 투표 버튼
prepEl.querySelectorAll('.obs').forEach((b) =>
  b.addEventListener('click', () => {
    selectedType = (b as HTMLElement).dataset.type as ObstacleType;
    prepEl.querySelectorAll('.obs').forEach((x) => x.classList.remove('sel'));
    b.classList.add('sel');
  }),
);
prepEl.querySelectorAll('.vote').forEach((b) =>
  b.addEventListener('click', () => {
    const t = (b as HTMLElement).dataset.topic as TopicId;
    myVote = t;
    if (socket) socket.emit(EV.VOTE, { topicId: t } satisfies C2S_Vote);
    prepEl.querySelectorAll('.vote').forEach((x) => x.classList.remove('myvote'));
    b.classList.add('myvote');
  }),
);

// ── 이동 (지형 + 장애물 충돌) ─────────────────────────────────
const SPEED = 7, GRAVITY = -25, JUMP_V = 9, STEP_DOWN = 0.7, CAP_R = 0.45;

function updateLocal(dt: number) {
  if (localDead) { // 관전(부활 대기/탈락) — 조작 정지
    if (localAvatar) localAvatar.visible = false;
    return;
  }
  if (localAvatar) localAvatar.visible = true;

  const fx = -Math.sin(local.yaw), fz = -Math.cos(local.yaw);
  const rx = Math.cos(local.yaw), rz = -Math.sin(local.yaw);
  let fwd = 0, strafe = 0;
  if (keys['KeyW']) fwd += 1;
  if (keys['KeyS']) fwd -= 1;
  if (keys['KeyD']) strafe += 1;
  if (keys['KeyA']) strafe -= 1;
  let dx = fx * fwd + rx * strafe, dz = fz * fwd + rz * strafe;
  const len = Math.hypot(dx, dz);
  const moving = len > 0.001;
  if (moving) {
    dx /= len; dz /= len;
    local.x += dx * SPEED * dt;
    local.z += dz * SPEED * dt;
  }
  // 장애물 밀어내기
  for (const o of obstacleData.values()) [local.x, local.z] = resolveObstacle(local.x, local.z, CAP_R, o);
  local.x = clamp(local.x, -WORLD.SIZE_X / 2, WORLD.SIZE_X / 2);
  local.z = clamp(local.z, -WORLD.SIZE_Z / 2, WORLD.SIZE_Z / 2);

  const groundY = terrainHeight(local.x, local.z);
  if (local.grounded) {
    if (keys['Space']) { local.vy = JUMP_V; local.grounded = false; local.y += local.vy * dt; }
    else if (groundY < local.y - STEP_DOWN) { local.grounded = false; local.vy = 0; }
    else local.y = groundY;
  } else {
    local.vy += GRAVITY * dt;
    local.y += local.vy * dt;
    if (local.y <= groundY) { local.y = groundY; local.vy = 0; local.grounded = true; }
  }
  local.anim = !local.grounded ? ANIM.JUMP : moving ? ANIM.RUN : ANIM.IDLE;

  // 사망 판정 (플레이 중 강물)
  if (phase === PHASE.PLAY && local.y < COURSE.DEATH_Y && socket) {
    localDead = true;
    socket.emit(EV.DIED, { x: local.x, y: local.y, z: local.z } satisfies C2S_Died);
    setObjectiveDead();
  }

  if (localAvatar) {
    localAvatar.position.set(local.x, local.y, local.z);
    localAvatar.rotation.y = local.yaw;
  }
}
function updateCamera() {
  const fx = -Math.sin(local.yaw), fz = -Math.cos(local.yaw);
  const cx = local.x - fx * 8, cz = local.z - fz * 8;
  let cy = local.y + 4.5;
  const camGround = terrainHeight(cx, cz) + 2;
  if (cy < camGround) cy = camGround;
  camera.position.set(cx, cy, cz);
  camera.lookAt(local.x, local.y + 1.2, local.z);
}
function updateRemotes(dt: number) {
  const a = 1 - Math.exp(-dt * 12);
  for (const r of remotes.values()) {
    r.group.position.x += (r.tx - r.group.position.x) * a;
    r.group.position.y += (r.ty - r.group.position.y) * a;
    r.group.position.z += (r.tz - r.group.position.z) * a;
    let d = r.tyaw - r.group.rotation.y;
    while (d > Math.PI) d -= Math.PI * 2;
    while (d < -Math.PI) d += Math.PI * 2;
    r.group.rotation.y += d * a;
  }
}

// ── 페이즈 UI ─────────────────────────────────────────────────
const PHASE_KO: Record<Phase, string> = {
  LOBBY: '대기 중', PREP: '준비', ROULETTE: '룰렛', PLAY: '플레이', HIGHLIGHT: '하이라이트', RESULT: '결과',
};
function applyPhase(p: S2C_Phase) {
  phase = p.phase; roundNo = p.round; endsAt = p.endsAt; topicId = p.topicId;
  prepEl.hidden = phase !== PHASE.PREP;
  if (phase !== PHASE.ROULETTE) { rouletteEl.hidden = true; }
  if (phase !== PHASE.HIGHLIGHT) highlightEl.hidden = true;
  resultEl.hidden = phase !== PHASE.RESULT;
  objectiveEl.hidden = !(phase === PHASE.PLAY || phase === PHASE.LOBBY);

  // PLAY 가 아니면 포인터 잠금 해제 (준비 페이즈에서 바닥 클릭 배치 가능하게)
  if (phase !== PHASE.PLAY && document.pointerLockElement) document.exitPointerLock();

  phaseBar.hidden = !myId;
  if (phase === PHASE.LOBBY) objectiveEl.innerHTML = '🎮 곧 새 매치가 시작됩니다…';
  if (phase === PHASE.PLAY && topicId) {
    const t = topicDef(topicId);
    objectiveEl.innerHTML = `<b>${roundNo}라운드 · ${t.name}</b> — ${t.desc}`;
  }
  if (phase === PHASE.PREP) { myVote = null; localDead = false; prepEl.querySelectorAll('.vote').forEach((x) => x.classList.remove('myvote')); }
}

let rouletteTimer: ReturnType<typeof setInterval> | null = null;
function playRoulette(ev: S2C_Roulette) {
  rouletteEl.hidden = false;
  const slotOf = (id: TopicId) => rouletteEl.querySelector(`.r-slot[data-topic="${id}"]`)!;
  rouletteEl.querySelectorAll('.r-slot').forEach((s) => s.classList.remove('active', 'win'));
  if (rouletteTimer) clearInterval(rouletteTimer);
  let i = 0;
  const start = performance.now();
  rouletteTimer = setInterval(() => {
    rouletteEl.querySelectorAll('.r-slot').forEach((s) => s.classList.remove('active'));
    slotOf(ev.order[i % ev.order.length]).classList.add('active');
    i++;
    if (performance.now() - start > ev.spinMs) {
      if (rouletteTimer) clearInterval(rouletteTimer);
      rouletteEl.querySelectorAll('.r-slot').forEach((s) => s.classList.remove('active'));
      slotOf(ev.topicId).classList.add('win');
    }
  }, 90);
}
function setObjectiveDead() {
  const t = topicId ? topicDef(topicId) : null;
  objectiveEl.innerHTML = t && !t.respawn ? `💀 탈락! 다음 라운드까지 관전` : `💧 낙사! 잠시 후 부활…`;
}

// ── 네트워킹 ──────────────────────────────────────────────────
let sendSeq = 0, lastPingSent = 0, rttMs = 0;

function connect() {
  socket = io(SERVER_URL, { transports: ['websocket'] });
  socket.on('connect', () => { statusEl.textContent = '연결됨 · 입장 중…'; socket!.emit(EV.JOIN, { name: pendingName } satisfies C2S_Join); });
  socket.on('connect_error', (err) => { showError(`연결 실패: ${err.message}`); joinBtn.disabled = false; });
  socket.on(EV.JOIN_REJECTED, (m: S2C_JoinRejected) => { showError(m.reason); joinBtn.disabled = false; nameInput.focus(); nameInput.select(); });

  socket.on(EV.WELCOME, (w: S2C_Welcome) => {
    myId = w.id; myColor = w.color;
    local.x = w.spawn.x; local.y = w.spawn.y; local.z = w.spawn.z; local.grounded = true; local.vy = 0;
    localAvatar = makeAvatar(w.color, pendingName);
    scene.add(localAvatar);
    for (const p of w.players) if (p.id !== myId) addRemote(p);
    clearObstacles();
    for (const o of w.obstacles) addObstacle(o);
    overlay.hidden = true; hud.hidden = false;
    applyPhase({ phase: w.phase, round: w.round, endsAt: w.endsAt, topicId: w.topicId });
    refreshList();
  });

  socket.on(EV.PLAYER_JOINED, (m: S2C_PlayerJoined) => { if (m.player.id !== myId) addRemote(m.player); refreshList(); });
  socket.on(EV.PLAYER_LEFT, (m: S2C_PlayerLeft) => { const r = remotes.get(m.id); if (r) { scene.remove(r.group); remotes.delete(m.id); } refreshList(); });

  socket.on(EV.SNAPSHOT, (snap: S2C_Snapshot) => {
    if (!myId) return;
    for (const p of snap.players) {
      if (p.id === myId) continue;
      const r = remotes.get(p.id);
      if (r) { r.tx = p.x; r.ty = p.y; r.tz = p.z; r.tyaw = p.yaw; r.group.visible = !p.dead; }
      else addRemote(p);
    }
  });

  socket.on(EV.PHASE, (p: S2C_Phase) => applyPhase(p));
  socket.on(EV.OBSTACLE_ADDED, (m: S2C_ObstacleAdded) => addObstacle(m.obstacle));
  socket.on(EV.OBSTACLE_SNAPSHOT, (m: S2C_ObstacleSnapshot) => { clearObstacles(); for (const o of m.obstacles) addObstacle(o); });
  socket.on(EV.OBSTACLE_REJECTED, (m: S2C_ObstacleRejected) => { statusEl.textContent = `배치 불가: ${m.reason}`; });
  socket.on(EV.VOTE_UPDATE, (m: S2C_VoteUpdate) => {
    for (const t of ['race', 'height', 'survive'] as TopicId[]) {
      const el = prepEl.querySelector(`b[data-c="${t}"]`);
      if (el) el.textContent = String(m.counts[t] ?? 0);
    }
  });
  socket.on(EV.ROULETTE, (ev: S2C_Roulette) => playRoulette(ev));
  socket.on(EV.RESPAWN, (r: S2C_Respawn) => { local.x = r.x; local.y = r.y; local.z = r.z; local.vy = 0; local.grounded = true; localDead = false; if (topicId && phase === PHASE.PLAY) { const t = topicDef(topicId); objectiveEl.innerHTML = `<b>${roundNo}라운드 · ${t.name}</b> — ${t.desc}`; } });
  socket.on(EV.ROUND_RESULT, (m: S2C_RoundResult) => { for (const r of m.rows) cumulativeMap.set(r.id, r.cumulative); refreshList(); });
  socket.on(EV.HIGHLIGHT, (m: S2C_Highlight) => { highlightEl.textContent = `✨ ${m.text}`; highlightEl.hidden = false; });
  socket.on(EV.MATCH_RESULT, (m: S2C_MatchResult) => {
    resultRows.innerHTML = m.rows
      .map((r) => `<div class="rrow ${r.rank === 1 ? 'top' : ''}"><span class="rk">${r.rank}위</span><span class="dot" style="background:${hex(r.color)}"></span><span>${escapeHtml(r.name)}</span><span class="sc">${r.cumulative}점</span></div>`)
      .join('');
  });

  socket.on(EV.PONG, (clientTime: number) => { rttMs = Date.now() - clientTime; });
  socket.on('disconnect', () => { if (myId) statusEl.textContent = '연결 끊김 · 재접속 중…'; });
}

function addRemote(p: PlayerState) {
  if (remotes.has(p.id)) return;
  const group = makeAvatar(p.color, p.name);
  group.position.set(p.x, p.y, p.z);
  group.rotation.y = p.yaw;
  group.visible = !p.dead;
  scene.add(group);
  remotes.set(p.id, { group, tx: p.x, ty: p.y, tz: p.z, tyaw: p.yaw, name: p.name, color: p.color });
}

// ── HUD ───────────────────────────────────────────────────────
function refreshList() {
  const rows = [rowHtml(hex(myColor), `${pendingName} (나)`, true, cumulativeMap.get(myId ?? ''))];
  for (const r of remotes.values()) rows.push(rowHtml(hex(r.color), r.name, false, cumulativeMap.get(idOf(r))));
  listEl.innerHTML = rows.join('');
}
function idOf(r: Remote): string { for (const [id, v] of remotes) if (v === r) return id; return ''; }
function rowHtml(colorHex: string, label: string, me: boolean, score?: number) {
  const s = score !== undefined ? `<span class="score">${score}</span>` : '';
  return `<div class="row"><span class="dot" style="background:${colorHex}"></span><span class="${me ? 'me' : ''}">${escapeHtml(label)}</span>${s}</div>`;
}
function hex(c: number) { return '#' + c.toString(16).padStart(6, '0'); }
function escapeHtml(s: string) { return s.replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]!)); }
function showError(msg: string) { joinError.textContent = msg; joinError.hidden = false; }
function hideError() { joinError.hidden = true; }

// ── 메인 루프 ─────────────────────────────────────────────────
let lastT = performance.now(), sendAccum = 0;
const SEND_INTERVAL = 1 / NET.CLIENT_SEND_HZ;
function frame(now: number) {
  const dt = Math.min((now - lastT) / 1000, 0.05);
  lastT = now;
  if (myId) { updateLocal(dt); updateCamera(); }
  updateRemotes(dt);

  if (myId && socket) {
    sendAccum += dt;
    if (sendAccum >= SEND_INTERVAL) {
      sendAccum = 0;
      socket.emit(EV.INPUT, { x: local.x, y: local.y, z: local.z, yaw: local.yaw, anim: local.anim as C2S_Input['anim'], seq: sendSeq++ } satisfies C2S_Input);
    }
    if (now - lastPingSent > NET.HEARTBEAT_MS) { lastPingSent = now; socket.emit(EV.PING, Date.now()); }

    // 페이즈 바 + 타이머
    phaseName.textContent = phase === PHASE.PLAY ? `${roundNo}라운드` : PHASE_KO[phase];
    const remain = Math.max(0, Math.ceil((endsAt - Date.now()) / 1000));
    timerEl.textContent = phase === PHASE.LOBBY ? '' : `${remain}s`;
    statusEl.textContent = `${remotes.size + 1}명 · RTT ${rttMs}ms`;

    // PLAY 실시간 목표 지표
    if (phase === PHASE.PLAY && !localDead && topicId) {
      const t = topicDef(topicId);
      let live = '';
      if (topicId === 'race') live = ` · 남은 거리 ${Math.max(0, Math.round(local.z - COURSE.GOAL_Z))}m`;
      else if (topicId === 'height') live = ` · 현재 높이 ${local.y.toFixed(1)}`;
      else live = ` · 생존 ${remain}s`;
      objectiveEl.innerHTML = `<b>${roundNo}라운드 · ${t.name}</b>${live}`;
    }
  }
  renderer.render(scene, camera);
  requestAnimationFrame(frame);
}
requestAnimationFrame(frame);

// ── 입장 ──────────────────────────────────────────────────────
function doJoin() {
  const name = nameInput.value.trim();
  if (!name) { showError('닉네임을 입력하세요.'); nameInput.focus(); return; }
  pendingName = name; joinBtn.disabled = true; hideError();
  if (socket && socket.connected) socket.emit(EV.JOIN, { name: pendingName } satisfies C2S_Join);
  else if (!socket) connect();
}
joinBtn.addEventListener('click', doJoin);
nameInput.addEventListener('keydown', (e) => { if (e.key === 'Enter') doJoin(); });
nameInput.focus();
