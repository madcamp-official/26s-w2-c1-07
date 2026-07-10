// ─────────────────────────────────────────────────────────────
// 룰렛 파티 클라이언트 (Day2 프로토타입)
//  - Three.js 씬 + 로컬 캐릭터(클라 권위 키네매틱 이동)
//  - Socket.IO 로 위치 송신(15Hz) / 스냅샷 수신 → 원격 플레이어 보간 렌더
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
  type C2S_Join,
  type C2S_Input,
  type S2C_Welcome,
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
const hud = document.getElementById('hud')!;
const statusEl = document.getElementById('status')!;
const listEl = document.getElementById('list')!;

// ── Three.js 씬 ───────────────────────────────────────────────
const scene = new THREE.Scene();
scene.background = new THREE.Color(0x87ceeb);
scene.fog = new THREE.Fog(0x87ceeb, 70, 150);

const camera = new THREE.PerspectiveCamera(70, innerWidth / innerHeight, 0.1, 500);
const renderer = new THREE.WebGLRenderer({ antialias: true });
renderer.setSize(innerWidth, innerHeight);
renderer.setPixelRatio(Math.min(devicePixelRatio, 2));
document.body.appendChild(renderer.domElement);
addEventListener('resize', () => {
  camera.aspect = innerWidth / innerHeight;
  camera.updateProjectionMatrix();
  renderer.setSize(innerWidth, innerHeight);
});

scene.add(new THREE.HemisphereLight(0xffffff, 0x557733, 0.95));
const sun = new THREE.DirectionalLight(0xffffff, 1.0);
sun.position.set(30, 60, 20);
scene.add(sun);

// 바닥 + 그리드 + 경계 벽 (평지 아레나)
const ground = new THREE.Mesh(
  new THREE.PlaneGeometry(WORLD.SIZE_X, WORLD.SIZE_Z),
  new THREE.MeshStandardMaterial({ color: 0x6b8e23 }),
);
ground.rotation.x = -Math.PI / 2;
scene.add(ground);

const grid = new THREE.GridHelper(WORLD.SIZE_X, WORLD.SIZE_X / 2, 0x2f4f1f, 0x3f5f2f);
(grid.material as THREE.Material).transparent = true;
(grid.material as THREE.Material).opacity = 0.4;
scene.add(grid);

{
  const hx = WORLD.SIZE_X / 2;
  const hz = WORLD.SIZE_Z / 2;
  const wallMat = new THREE.MeshStandardMaterial({ color: 0x8899aa, transparent: true, opacity: 0.25 });
  const mk = (w: number, d: number, x: number, z: number) => {
    const m = new THREE.Mesh(new THREE.BoxGeometry(w, 2, d), wallMat);
    m.position.set(x, 1, z);
    scene.add(m);
  };
  mk(WORLD.SIZE_X, 0.5, 0, -hz);
  mk(WORLD.SIZE_X, 0.5, 0, hz);
  mk(0.5, WORLD.SIZE_Z, -hx, 0);
  mk(0.5, WORLD.SIZE_Z, hx, 0);
}

// ── 아바타 팩토리 ─────────────────────────────────────────────
function makeAvatar(color: number, name: string): THREE.Group {
  const g = new THREE.Group();
  const body = new THREE.Mesh(
    new THREE.CapsuleGeometry(0.4, 0.9, 4, 10),
    new THREE.MeshStandardMaterial({ color }),
  );
  body.position.y = 0.85;
  g.add(body);
  // 정면 표시용 코 (로컬 -Z = 전방)
  const nose = new THREE.Mesh(
    new THREE.BoxGeometry(0.18, 0.18, 0.3),
    new THREE.MeshStandardMaterial({ color: 0x1a1a1a }),
  );
  nose.position.set(0, 0.95, -0.42);
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

const local = { x: 0, y: 0, z: 0, yaw: 0, vy: 0, anim: ANIM.IDLE as number };
let localAvatar: THREE.Group | null = null;

interface Remote {
  group: THREE.Group;
  // 보간 목표
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

// 마우스 시점 (pointer lock)
renderer.domElement.addEventListener('click', () => {
  if (myId) renderer.domElement.requestPointerLock();
});
addEventListener('mousemove', (e) => {
  if (document.pointerLockElement === renderer.domElement) {
    local.yaw -= e.movementX * 0.0025;
  }
});

// ── 이동 (클라 권위 키네매틱) ────────────────────────────────
const SPEED = 7;
const GRAVITY = -25;
const JUMP_V = 9;

function updateLocal(dt: number) {
  // yaw=0 → 전방 -Z. 전방/우측 벡터
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
    local.x += dx * SPEED * dt;
    local.z += dz * SPEED * dt;
  }

  // 중력 + 점프
  const onGround = local.y <= WORLD.GROUND_Y + 0.0001;
  if (keys['Space'] && onGround) local.vy = JUMP_V;
  local.vy += GRAVITY * dt;
  local.y += local.vy * dt;
  if (local.y < WORLD.GROUND_Y) {
    local.y = WORLD.GROUND_Y;
    local.vy = 0;
  }

  // 경계 클램프 (서버도 동일하게 방어)
  local.x = clamp(local.x, -WORLD.SIZE_X / 2, WORLD.SIZE_X / 2);
  local.z = clamp(local.z, -WORLD.SIZE_Z / 2, WORLD.SIZE_Z / 2);

  local.anim = !onGround ? ANIM.JUMP : moving ? ANIM.RUN : ANIM.IDLE;

  if (localAvatar) {
    localAvatar.position.set(local.x, local.y, local.z);
    localAvatar.rotation.y = local.yaw;
  }
}

function updateCamera() {
  const fx = -Math.sin(local.yaw);
  const fz = -Math.cos(local.yaw);
  const dist = 8;
  const height = 5;
  camera.position.set(local.x - fx * dist, local.y + height, local.z - fz * dist);
  camera.lookAt(local.x, local.y + 1.2, local.z);
}

function updateRemotes(dt: number) {
  // 스냅샷 보간(간이): 목표를 향해 지수 감쇠 lerp. TCP 지터를 부드럽게 흡수.
  const a = 1 - Math.exp(-dt * 12);
  for (const r of remotes.values()) {
    r.group.position.x += (r.tx - r.group.position.x) * a;
    r.group.position.y += (r.ty - r.group.position.y) * a;
    r.group.position.z += (r.tz - r.group.position.z) * a;
    // yaw 최단경로 보간
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

function connect(name: string) {
  socket = io(SERVER_URL, { transports: ['websocket'] });

  socket.on('connect', () => {
    statusEl.textContent = '연결됨 · 입장 중…';
    socket!.emit(EV.JOIN, { name } satisfies C2S_Join);
  });

  socket.on('connect_error', (err) => {
    statusEl.textContent = `연결 실패: ${err.message} (서버 켜져 있나요?)`;
  });

  socket.on(EV.WELCOME, (w: S2C_Welcome) => {
    myId = w.id;
    _myColor = w.color;
    local.x = w.spawn.x;
    local.y = w.spawn.y;
    local.z = w.spawn.z;
    localAvatar = makeAvatar(w.color, name);
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
    statusEl.textContent = '연결 끊김 · 재접속 시도 중…';
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

function refreshList() {
  const rows: string[] = [];
  rows.push(rowHtml(myColorHex(), `${nameInput.value || '나'} (나)`, true));
  for (const r of remotes.values()) rows.push(rowHtml(hex(r.color), r.name, false));
  listEl.innerHTML = rows.join('');
}
function rowHtml(colorHex: string, label: string, me: boolean) {
  return `<div class="row"><span class="dot" style="background:${colorHex}"></span><span class="${me ? 'me' : ''}">${escapeHtml(
    label,
  )}</span></div>`;
}
function hex(c: number) {
  return '#' + c.toString(16).padStart(6, '0');
}
let _myColor = 0xffffff;
function myColorHex() {
  return hex(_myColor);
}
function escapeHtml(s: string) {
  return s.replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]!));
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

  // 입력 송신 (15Hz)
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
    // 하트비트
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

// ── 입장 처리 ─────────────────────────────────────────────────
function doJoin() {
  const name = nameInput.value.trim();
  if (!name) {
    nameInput.focus();
    return;
  }
  joinBtn.disabled = true;
  statusEl.textContent = '연결 중…';
  connect(name);
}
joinBtn.addEventListener('click', doJoin);
nameInput.addEventListener('keydown', (e) => {
  if (e.key === 'Enter') doJoin();
});
nameInput.focus();
