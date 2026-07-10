// ─────────────────────────────────────────────────────────────
// 룰렛 파티 클라이언트 (Day3: 하이트맵 지형 + 지형 위 이동)
//  - Three.js 씬 + 산·강 하이트맵 지형 (shared 의 terrainHeight 공유)
//  - 로컬 캐릭터: 클라 권위 키네매틱 이동, 지형 높이에 붙어 걷기 + 점프
//  - Socket.IO 위치 송신(15Hz) / 스냅샷 수신 → 원격 플레이어 보간 렌더
//  - 닉네임 중복 시 서버가 거부 → 로비에서 재입력
// ─────────────────────────────────────────────────────────────
import './style.css';
import * as THREE from 'three';
import { io, type Socket } from 'socket.io-client';
import {
  NET,
  WORLD,
  EV,
  ANIM,
  clamp,
  terrainHeight,
  type C2S_Join,
  type C2S_Input,
  type S2C_Welcome,
  type S2C_JoinRejected,
  type S2C_Snapshot,
  type S2C_PlayerJoined,
  type S2C_PlayerLeft,
  type PlayerState,
} from '@roulette/shared';

const SERVER_URL =
  (import.meta as any).env?.VITE_SERVER_URL ||
  `${location.protocol}//${location.hostname}:${NET.PORT}`;

// ── DOM ───────────────────────────────────────────────────────
const overlay = document.getElementById('overlay')!;
const nameInput = document.getElementById('name') as HTMLInputElement;
const joinBtn = document.getElementById('join') as HTMLButtonElement;
const joinError = document.getElementById('joinError')!;
const hud = document.getElementById('hud')!;
const statusEl = document.getElementById('status')!;
const listEl = document.getElementById('list')!;

// ── Three.js 씬 ───────────────────────────────────────────────
const scene = new THREE.Scene();
scene.background = new THREE.Color(0x87ceeb);
scene.fog = new THREE.Fog(0x87ceeb, 90, 200);

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

buildTerrain();

// ── 하이트맵 지형 (shared terrainHeight 로 정점 변위, 서버와 완전 동일) ──
function buildTerrain() {
  const geo = new THREE.PlaneGeometry(WORLD.SIZE_X, WORLD.SIZE_Z, WORLD.SEG_X, WORLD.SEG_Z);
  geo.rotateX(-Math.PI / 2); // XY 평면 → XZ 평면(Y 위)
  const pos = geo.attributes.position as THREE.BufferAttribute;

  const cSand = new THREE.Color(0xcbb681);
  const cGrass = new THREE.Color(0x5a8a3a);
  const cRock = new THREE.Color(0x8a7a5a);
  const cSnow = new THREE.Color(0xe8eef2);
  const colors = new Float32Array(pos.count * 3);
  const tmp = new THREE.Color();

  for (let i = 0; i < pos.count; i++) {
    const x = pos.getX(i);
    const z = pos.getZ(i);
    const y = terrainHeight(x, z);
    pos.setY(i, y);
    if (y < WORLD.WATER_Y + 0.6) tmp.copy(cSand);
    else if (y > 7.5) tmp.copy(cSnow);
    else if (y > 3.5) tmp.copy(cRock);
    else tmp.copy(cGrass);
    colors[i * 3] = tmp.r;
    colors[i * 3 + 1] = tmp.g;
    colors[i * 3 + 2] = tmp.b;
  }
  geo.setAttribute('color', new THREE.BufferAttribute(colors, 3));
  geo.computeVertexNormals();
  scene.add(new THREE.Mesh(geo, new THREE.MeshStandardMaterial({ vertexColors: true })));

  // 강물 수면
  const water = new THREE.Mesh(
    new THREE.PlaneGeometry(WORLD.SIZE_X, WORLD.SIZE_Z),
    new THREE.MeshStandardMaterial({ color: 0x2a6fb0, transparent: true, opacity: 0.62 }),
  );
  water.rotation.x = -Math.PI / 2;
  water.position.y = WORLD.WATER_Y;
  scene.add(water);
}

// ── 아바타 ────────────────────────────────────────────────────
function makeAvatar(color: number, name: string): THREE.Group {
  const g = new THREE.Group();
  const body = new THREE.Mesh(
    new THREE.CapsuleGeometry(0.4, 0.9, 4, 10),
    new THREE.MeshStandardMaterial({ color }),
  );
  body.position.y = 0.85;
  g.add(body);
  const nose = new THREE.Mesh(
    new THREE.BoxGeometry(0.18, 0.18, 0.3),
    new THREE.MeshStandardMaterial({ color: 0x1a1a1a }),
  );
  nose.position.set(0, 0.95, -0.42); // 로컬 -Z = 전방
  g.add(nose);
  g.add(makeLabel(name));
  return g;
}

function makeLabel(text: string): THREE.Sprite {
  const canvas = document.createElement('canvas');
  canvas.width = 256;
  canvas.height = 64;
  const ctx = canvas.getContext('2d')!;
  ctx.fillStyle = 'rgba(0,0,0,0.55)';
  roundRect(ctx, 4, 8, 248, 48, 12);
  ctx.fill();
  ctx.font = 'bold 30px system-ui, sans-serif';
  ctx.fillStyle = '#fff';
  ctx.textAlign = 'center';
  ctx.textBaseline = 'middle';
  ctx.fillText(text, 128, 34);
  const tex = new THREE.CanvasTexture(canvas);
  tex.anisotropy = 4;
  const spr = new THREE.Sprite(new THREE.SpriteMaterial({ map: tex, depthTest: false, transparent: true }));
  spr.scale.set(2.4, 0.6, 1);
  spr.position.y = 2.1;
  spr.renderOrder = 999;
  return spr;
}

function roundRect(ctx: CanvasRenderingContext2D, x: number, y: number, w: number, h: number, r: number) {
  ctx.beginPath();
  ctx.moveTo(x + r, y);
  ctx.arcTo(x + w, y, x + w, y + h, r);
  ctx.arcTo(x + w, y + h, x, y + h, r);
  ctx.arcTo(x, y + h, x, y, r);
  ctx.arcTo(x, y, x + w, y, r);
  ctx.closePath();
}

// ── 상태 ──────────────────────────────────────────────────────
let socket: Socket | null = null;
let myId: string | null = null;
let myColor = 0xffffff;
let pendingName = '';

const local = { x: 0, y: 0, z: 0, yaw: 0, vy: 0, grounded: true, anim: ANIM.IDLE as number };
let localAvatar: THREE.Group | null = null;

interface Remote {
  group: THREE.Group;
  tx: number;
  ty: number;
  tz: number;
  tyaw: number;
  name: string;
  color: number;
}
const remotes = new Map<string, Remote>();

// ── 입력 ──────────────────────────────────────────────────────
const keys: Record<string, boolean> = {};
addEventListener('keydown', (e) => {
  keys[e.code] = true;
  if (e.code === 'Space') e.preventDefault();
});
addEventListener('keyup', (e) => {
  keys[e.code] = false;
});
renderer.domElement.addEventListener('click', () => {
  if (myId) renderer.domElement.requestPointerLock();
});
addEventListener('mousemove', (e) => {
  if (document.pointerLockElement === renderer.domElement) {
    local.yaw -= e.movementX * 0.0025;
  }
});

// ── 이동 (클라 권위 키네매틱, 지형 위를 걷는다) ──────────────
const SPEED = 7;
const GRAVITY = -25;
const JUMP_V = 9;
const STEP_DOWN = 0.7; // 이보다 낮은 턱은 붙어서 내려감, 그보다 크면 낙하

function updateLocal(dt: number) {
  const fx = -Math.sin(local.yaw);
  const fz = -Math.cos(local.yaw);
  const rx = Math.cos(local.yaw);
  const rz = -Math.sin(local.yaw);

  let fwd = 0;
  let strafe = 0;
  if (keys['KeyW']) fwd += 1;
  if (keys['KeyS']) fwd -= 1;
  if (keys['KeyD']) strafe += 1;
  if (keys['KeyA']) strafe -= 1;

  let dx = fx * fwd + rx * strafe;
  let dz = fz * fwd + rz * strafe;
  const len = Math.hypot(dx, dz);
  const moving = len > 0.001;
  if (moving) {
    dx /= len;
    dz /= len;
    local.x = clamp(local.x + dx * SPEED * dt, -WORLD.SIZE_X / 2, WORLD.SIZE_X / 2);
    local.z = clamp(local.z + dz * SPEED * dt, -WORLD.SIZE_Z / 2, WORLD.SIZE_Z / 2);
  }

  const groundY = terrainHeight(local.x, local.z);

  if (local.grounded) {
    if (keys['Space']) {
      local.vy = JUMP_V;
      local.grounded = false;
      local.y += local.vy * dt;
    } else if (groundY < local.y - STEP_DOWN) {
      // 절벽에서 걸어 나감 → 낙하 시작
      local.grounded = false;
      local.vy = 0;
    } else {
      local.y = groundY; // 완만한 경사는 지형에 붙어 오르내림
    }
  } else {
    local.vy += GRAVITY * dt;
    local.y += local.vy * dt;
    if (local.y <= groundY) {
      local.y = groundY;
      local.vy = 0;
      local.grounded = true;
    }
  }

  local.anim = !local.grounded ? ANIM.JUMP : moving ? ANIM.RUN : ANIM.IDLE;

  if (localAvatar) {
    localAvatar.position.set(local.x, local.y, local.z);
    localAvatar.rotation.y = local.yaw;
  }
}

function updateCamera() {
  const fx = -Math.sin(local.yaw);
  const fz = -Math.cos(local.yaw);
  const dist = 8;
  const height = 4.5;
  const cx = local.x - fx * dist;
  const cz = local.z - fz * dist;
  let cy = local.y + height;
  // 카메라가 지형을 뚫지 않도록 지면 위로 클램프
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
    let dyaw = r.tyaw - r.group.rotation.y;
    while (dyaw > Math.PI) dyaw -= Math.PI * 2;
    while (dyaw < -Math.PI) dyaw += Math.PI * 2;
    r.group.rotation.y += dyaw * a;
  }
}

// ── 네트워킹 ──────────────────────────────────────────────────
let sendSeq = 0;
let lastPingSent = 0;
let rttMs = 0;

function connect() {
  socket = io(SERVER_URL, { transports: ['websocket'] });

  socket.on('connect', () => {
    statusEl.textContent = '연결됨 · 입장 중…';
    socket!.emit(EV.JOIN, { name: pendingName } satisfies C2S_Join);
  });

  socket.on('connect_error', (err) => {
    showError(`연결 실패: ${err.message} (서버가 켜져 있나요?)`);
    joinBtn.disabled = false;
  });

  socket.on(EV.JOIN_REJECTED, (m: S2C_JoinRejected) => {
    showError(m.reason);
    joinBtn.disabled = false;
    nameInput.focus();
    nameInput.select();
  });

  socket.on(EV.WELCOME, (w: S2C_Welcome) => {
    myId = w.id;
    myColor = w.color;
    local.x = w.spawn.x;
    local.y = w.spawn.y;
    local.z = w.spawn.z;
    local.grounded = true;
    local.vy = 0;
    localAvatar = makeAvatar(w.color, pendingName);
    scene.add(localAvatar);
    for (const p of w.players) if (p.id !== myId) addRemote(p);
    overlay.hidden = true;
    hud.hidden = false;
    refreshList();
  });

  socket.on(EV.PLAYER_JOINED, (m: S2C_PlayerJoined) => {
    if (m.player.id !== myId) addRemote(m.player);
    refreshList();
  });

  socket.on(EV.PLAYER_LEFT, (m: S2C_PlayerLeft) => {
    const r = remotes.get(m.id);
    if (r) {
      scene.remove(r.group);
      remotes.delete(m.id);
    }
    refreshList();
  });

  socket.on(EV.SNAPSHOT, (snap: S2C_Snapshot) => {
    if (!myId) return; // 입장 전에는 무시
    for (const p of snap.players) {
      if (p.id === myId) continue;
      const r = remotes.get(p.id);
      if (r) {
        r.tx = p.x;
        r.ty = p.y;
        r.tz = p.z;
        r.tyaw = p.yaw;
      } else {
        addRemote(p);
      }
    }
  });

  socket.on(EV.PONG, (clientTime: number) => {
    rttMs = Date.now() - clientTime;
  });

  socket.on('disconnect', () => {
    if (myId) statusEl.textContent = '연결 끊김 · 재접속 시도 중…';
  });
}

function addRemote(p: PlayerState) {
  if (remotes.has(p.id)) return;
  const group = makeAvatar(p.color, p.name);
  group.position.set(p.x, p.y, p.z);
  group.rotation.y = p.yaw;
  scene.add(group);
  remotes.set(p.id, { group, tx: p.x, ty: p.y, tz: p.z, tyaw: p.yaw, name: p.name, color: p.color });
}

// ── HUD ───────────────────────────────────────────────────────
function refreshList() {
  const rows = [rowHtml(hex(myColor), `${pendingName} (나)`, true)];
  for (const r of remotes.values()) rows.push(rowHtml(hex(r.color), r.name, false));
  listEl.innerHTML = rows.join('');
}
function rowHtml(colorHex: string, label: string, me: boolean) {
  return `<div class="row"><span class="dot" style="background:${colorHex}"></span><span class="${
    me ? 'me' : ''
  }">${escapeHtml(label)}</span></div>`;
}
function hex(c: number) {
  return '#' + c.toString(16).padStart(6, '0');
}
function escapeHtml(s: string) {
  return s.replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]!));
}
function showError(msg: string) {
  joinError.textContent = msg;
  joinError.hidden = false;
}
function hideError() {
  joinError.hidden = true;
}

// ── 메인 루프 ─────────────────────────────────────────────────
let lastT = performance.now();
let sendAccum = 0;
const SEND_INTERVAL = 1 / NET.CLIENT_SEND_HZ;

function frame(now: number) {
  const dt = Math.min((now - lastT) / 1000, 0.05);
  lastT = now;

  if (myId) {
    updateLocal(dt);
    updateCamera();
  }
  updateRemotes(dt);

  if (myId && socket) {
    sendAccum += dt;
    if (sendAccum >= SEND_INTERVAL) {
      sendAccum = 0;
      socket.emit(EV.INPUT, {
        x: local.x,
        y: local.y,
        z: local.z,
        yaw: local.yaw,
        anim: local.anim as C2S_Input['anim'],
        seq: sendSeq++,
      } satisfies C2S_Input);
    }
    if (now - lastPingSent > NET.HEARTBEAT_MS) {
      lastPingSent = now;
      socket.emit(EV.PING, Date.now());
    }
    statusEl.textContent = `연결됨 · ${remotes.size + 1}명 접속 · RTT ${rttMs}ms`;
  }

  renderer.render(scene, camera);
  requestAnimationFrame(frame);
}
requestAnimationFrame(frame);

// ── 입장 처리 (닉네임 거부 시 재입력 지원) ───────────────────
function doJoin() {
  const name = nameInput.value.trim();
  if (!name) {
    showError('닉네임을 입력하세요.');
    nameInput.focus();
    return;
  }
  pendingName = name;
  joinBtn.disabled = true;
  hideError();
  if (socket && socket.connected) {
    // 이미 연결돼 있음(거부 후 재시도) → 새 닉네임으로 다시 JOIN
    socket.emit(EV.JOIN, { name: pendingName } satisfies C2S_Join);
  } else if (!socket) {
    connect(); // 최초: 연결되면 'connect' 핸들러가 JOIN 전송
  }
}
joinBtn.addEventListener('click', doJoin);
nameInput.addEventListener('keydown', (e) => {
  if (e.key === 'Enter') doJoin();
});
nameInput.focus();
