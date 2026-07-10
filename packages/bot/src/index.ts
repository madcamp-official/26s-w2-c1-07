// ─────────────────────────────────────────────────────────────
// 봇 하니스 (명세서 §1) — 헤드리스 클라이언트로 다인원 규모/지연/손실 실측.
// 각 봇이 접속 → JOIN → 15Hz 로 랜덤워크 위치 송신. 스냅샷 수신량을 집계.
//
// 사용법:
//   pnpm bots                      # 기본 10봇, localhost:8080
//   BOTS=30 pnpm bots              # 30봇
//   SERVER=http://호스트:8080 BOTS=20 pnpm bots
// ─────────────────────────────────────────────────────────────
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
} from '@roulette/shared';

const SERVER = process.env.SERVER || `http://localhost:${NET.PORT}`;
const COUNT = Number(process.env.BOTS || 10);

let totalSnapshots = 0;
let connected = 0;

interface Bot {
  socket: Socket;
  x: number;
  z: number;
  yaw: number;
  vx: number;
  vz: number;
  seq: number;
}

function makeBot(i: number): Bot {
  const bot: Bot = {
    socket: io(SERVER, { transports: ['websocket'], reconnection: true }),
    x: (Math.random() - 0.5) * WORLD.SIZE_X * 0.4,
    z: (Math.random() - 0.5) * WORLD.SIZE_Z * 0.4,
    yaw: Math.random() * Math.PI * 2,
    vx: 0,
    vz: 0,
    seq: 0,
  };

  bot.socket.on('connect', () => {
    connected++;
    bot.socket.emit(EV.JOIN, { name: `bot-${i}` } satisfies C2S_Join);
  });
  bot.socket.on(EV.WELCOME, (w: S2C_Welcome) => {
    bot.x = w.spawn.x;
    bot.z = w.spawn.z;
  });
  bot.socket.on(EV.SNAPSHOT, (_s: S2C_Snapshot) => {
    totalSnapshots++;
  });
  bot.socket.on('disconnect', () => {
    connected = Math.max(0, connected - 1);
  });
  bot.socket.on('connect_error', (err) => {
    if (i === 0) console.error(`connect_error: ${err.message}`);
  });

  return bot;
}

const bots: Bot[] = Array.from({ length: COUNT }, (_, i) => makeBot(i));

// 랜덤워크 + 15Hz 송신
const SPEED = 5;
setInterval(() => {
  const dt = 1 / NET.CLIENT_SEND_HZ;
  for (const b of bots) {
    if (!b.socket.connected) continue;
    // 가끔 방향 전환
    if (Math.random() < 0.03) b.yaw = Math.random() * Math.PI * 2;
    b.vx = -Math.sin(b.yaw) * SPEED;
    b.vz = -Math.cos(b.yaw) * SPEED;
    b.x = clamp(b.x + b.vx * dt, -WORLD.SIZE_X / 2, WORLD.SIZE_X / 2);
    b.z = clamp(b.z + b.vz * dt, -WORLD.SIZE_Z / 2, WORLD.SIZE_Z / 2);
    b.socket.emit(EV.INPUT, {
      x: b.x,
      y: WORLD.GROUND_Y,
      z: b.z,
      yaw: b.yaw,
      anim: ANIM.RUN,
      seq: b.seq++,
    } satisfies C2S_Input);
  }
}, 1000 / NET.CLIENT_SEND_HZ);

// 하트비트
setInterval(() => {
  for (const b of bots) if (b.socket.connected) b.socket.emit(EV.PING, Date.now());
}, NET.HEARTBEAT_MS);

// 1초마다 수신 통계 출력
let lastSnap = 0;
setInterval(() => {
  const perSec = totalSnapshots - lastSnap;
  lastSnap = totalSnapshots;
  const expected = connected * NET.SNAPSHOT_HZ;
  console.log(
    `접속 ${connected}/${COUNT}봇 · 스냅샷 수신 ${perSec}/s (기대 ~${expected}) · 누적 ${totalSnapshots}`,
  );
}, 1000);

console.log(`\n🤖  봇 ${COUNT}기를 ${SERVER} 에 접속시킵니다...\n`);

process.on('SIGINT', () => {
  console.log('\n봇 종료.');
  for (const b of bots) b.socket.close();
  process.exit(0);
});
