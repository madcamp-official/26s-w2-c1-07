// ─────────────────────────────────────────────────────────────
// 룰렛 파티 — 권위 서버 (Day2 프로토타입)
//
// 넷코드 모델 (명세서 §1): 클라 권위 이동 릴레이 + 서버 권위 원장.
//  - 각 클라가 자기 캐릭터 물리를 로컬 시뮬 → 위치/yaw 를 서버로 송신
//  - 서버는 이동을 재시뮬하지 않고, 경량 경계 클램프만 한 뒤 릴레이
//  - 서버 게임 루프는 SNAPSHOT_HZ 로 전원 상태를 1 배치 메시지로 브로드캐스트
// ─────────────────────────────────────────────────────────────
import { createServer } from 'node:http';
import { Server } from 'socket.io';
import {
  NET,
  WORLD,
  EV,
  PALETTE,
  clamp,
  type PlayerState,
  type C2S_Join,
  type C2S_Input,
  type S2C_Welcome,
  type S2C_PlayerJoined,
  type S2C_PlayerLeft,
  type S2C_Snapshot,
} from '@roulette/shared';

interface Entry {
  state: PlayerState;
  lastSeen: number;
}

/** 단일 룸 레지스트리 (RoomManager 는 Day4+ 에서 다중 룸으로 추상화) */
const players = new Map<string, Entry>();
let colorCursor = 0;

const http = createServer((req, res) => {
  if (req.url === '/health') {
    res.writeHead(200, { 'content-type': 'application/json' });
    res.end(JSON.stringify({ ok: true, players: players.size }));
    return;
  }
  res.writeHead(200, { 'content-type': 'text/plain; charset=utf-8' });
  res.end('roulette-party server is running');
});

const io = new Server(http, {
  // Day2 개발용: Vite dev(5173) ↔ 서버(8080) 교차 출처 허용.
  // 배포(단일 포트)에서는 동일 출처라 CORS 불필요.
  cors: { origin: '*' },
});

function spawnPoint() {
  const x = (Math.random() - 0.5) * WORLD.SIZE_X * 0.5;
  const z = (Math.random() - 0.5) * WORLD.SIZE_Z * 0.5;
  return { x, y: WORLD.SPAWN_Y, z };
}

io.on('connection', (socket) => {
  let id: string | null = null;

  socket.on(EV.JOIN, (msg: C2S_Join) => {
    if (id) return; // 중복 join 무시
    id = socket.id;
    const name =
      (msg?.name ?? '').toString().slice(0, 16).trim() || `Player-${id.slice(0, 4)}`;
    const color = PALETTE[colorCursor++ % PALETTE.length];
    const sp = spawnPoint();
    const state: PlayerState = {
      id,
      name,
      color,
      x: sp.x,
      y: sp.y,
      z: sp.z,
      yaw: 0,
      anim: 0,
    };
    players.set(id, { state, lastSeen: Date.now() });

    const welcome: S2C_Welcome = {
      id,
      color,
      spawn: sp,
      players: [...players.values()].map((e) => e.state),
      serverTime: Date.now(),
    };
    socket.emit(EV.WELCOME, welcome);
    socket.broadcast.emit(EV.PLAYER_JOINED, { player: state } satisfies S2C_PlayerJoined);
    console.log(`[join]  ${name} (${id})  ·  현재 ${players.size}명`);
  });

  socket.on(EV.INPUT, (msg: C2S_Input) => {
    if (!id) return;
    const e = players.get(id);
    if (!e) return;
    e.lastSeen = Date.now();
    // 클라 권위 릴레이 + 경량 방어 (경계 클램프). 이동은 재계산하지 않는다.
    const hx = WORLD.SIZE_X / 2;
    const hz = WORLD.SIZE_Z / 2;
    e.state.x = clamp(Number(msg.x) || 0, -hx, hx);
    e.state.z = clamp(Number(msg.z) || 0, -hz, hz);
    e.state.y = clamp(Number(msg.y) || 0, -5, 50);
    e.state.yaw = Number(msg.yaw) || 0;
    e.state.anim = (msg.anim | 0) as PlayerState['anim'];
  });

  // 앱레벨 하트비트: 살아있음 신호 + 시각 왕복(RTT) 측정용
  socket.on(EV.PING, (clientTime: number) => {
    const e = id ? players.get(id) : undefined;
    if (e) e.lastSeen = Date.now();
    socket.emit(EV.PONG, clientTime);
  });

  socket.on('disconnect', (reason) => {
    if (id && players.delete(id)) {
      io.emit(EV.PLAYER_LEFT, { id } satisfies S2C_PlayerLeft);
      console.log(`[left]  ${id} (${reason})  ·  현재 ${players.size}명`);
    }
  });
});

// ── 스냅샷 브로드캐스트 루프 (배치: 1 메시지에 전원 상태) ──────────
setInterval(() => {
  if (players.size === 0) return;
  const snap: S2C_Snapshot = {
    t: Date.now(),
    players: [...players.values()].map((e) => e.state),
  };
  io.emit(EV.SNAPSHOT, snap);
}, 1000 / NET.SNAPSHOT_HZ);

// ── 하트비트 스윕: 유령 소켓 제거 ─────────────────────────────
setInterval(() => {
  const now = Date.now();
  for (const [pid, e] of players) {
    if (now - e.lastSeen > NET.HEARTBEAT_TIMEOUT_MS) {
      players.delete(pid);
      io.emit(EV.PLAYER_LEFT, { id: pid } satisfies S2C_PlayerLeft);
      io.sockets.sockets.get(pid)?.disconnect(true);
      console.log(`[timeout]  ${pid}  ·  현재 ${players.size}명`);
    }
  }
}, 2000);

http.listen(NET.PORT, () => {
  console.log(`\n  🎡  roulette-party server  ·  http://localhost:${NET.PORT}`);
  console.log(`      snapshot ${NET.SNAPSHOT_HZ}Hz  ·  기다리는 중...\n`);
});
