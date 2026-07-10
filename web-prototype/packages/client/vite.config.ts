import { defineConfig } from 'vite';

// Day2 개발 서버. 클라(5173) 는 게임 서버(8080) 에 Socket.IO 로 접속한다.
// 서버 주소는 VITE_SERVER_URL 로 덮어쓸 수 있고, 기본값은 현재 host:8080.
export default defineConfig({
  server: {
    port: 5173,
    host: true, // LAN/터널에서 다른 기기 접속 허용
  },
});
