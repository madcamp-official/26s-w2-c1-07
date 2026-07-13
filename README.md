# 26s-w2-c1-07

## 공통과제 II : 협업형 실전 산출물 제작 (2인 1팀)

**목적:** 실시간 인터랙션, LLM Wrapper, Cross-Platform 중 하나의 옵션을 선택해 구현하며, 선택한 기술을 실제로 동작하는 형태의 산출물로 완성한다.

**선택 옵션:**

| 옵션 | 설명 |
|---|---|
| 실시간 인터랙션 | 사용자 간 상태 변화, 실시간 데이터 흐름, 스트리밍 응답 등 실시간성이 드러나는 기능을 구현 |
| LLM Wrapper | LLM API를 활용하여 AI 기능이 포함된 산출물을 구현 |
| Cross-Platform | 하나의 산출물을 여러 실행 환경에서 사용할 수 있도록 구현* |

> *데스크톱 앱 ↔ 모바일 앱; 혹은 다른 폼팩터에서의 앱; 웹만/웹 기반 프레임워크(Electron, Tauri 등) 대신 다른 프레임워크를 시도해보는 것을 적극 권장

**결과물:** 선택한 옵션이 적용된 작동 가능한 산출물, 실행 가능한 코드, 시연 자료 및 관련 문서

---

## 팀원

| 이름 | 학교 | GitHub | 역할 |
|---|---|---|---|
| 이유담 | 포항공과대학교 | https://github.com/omok00 |  |
| 권순호 | 한양대학교 | https://github.com/rth934 |  |

---

## 선택 옵션

- 실시간 인터랙션

---

## 기획안

> 📄 상세 기획서: [docs/기획서.md](docs/기획서.md)

- **산출물 주제:** 다인원 수직 클라이밍 파티 게임 (Only Up! 모티브), 매 라운드 전 준비 페이즈에 플레이어들이 구조물(보이는/보이지 않는)을 설치하고, 그 구조물이 3라운드 내내 누적되는 랜덤 타워에서 더 높이 오르기를 경쟁하는 실시간 게임. 상세: [docs/클라이밍_전환_명세서.md](docs/클라이밍_전환_명세서.md)
- **제작 목적:** MECCHA CHAMELEON, Fall Guys에서 착안한 "다수의 사람을 한 맵에 넣자"는 아이디어를 실시간 인터랙션 기술로 구현. 우스꽝스러운 장면(숏폼 적합)과 유저 제작 맵 기반의 반복 플레이 가치를 추구
- **선택 옵션:** 실시간 인터랙션
- **핵심 구현 요소:**
  - 다수 유저의 위치·상태 실시간 동기화 (랜덤 생성 수직 타워에서 동시 등반)
  - 전 페이즈 3인칭 마우스룩 조준 시점: 마우스=조준(화면 중앙 조준점), 커서 잠금 기반 조작
  - 매 라운드 전 준비 페이즈: 자유 비행 카메라로 구조물 설치. 보이는 구조물(전원 상시 공개)과 보이지 않는 구조물(준비 중 설치자에게만, 충돌 시 전원에게 일시 공개) 이원화, **설치물은 3라운드 누적**
  - 은닉 체력(10)과 낙하 데미지(낙하高 5 이상): 체력은 비공개, 소진 시 그 라운드 탈락 + 생존자 관전
  - 점수 = 라운드 종료 시점 높이 비례 + 순위 보너스, 3라운드 누적. 라운드마다 하이라이트 카드
- **사용 / 시연 시나리오:** 유저들이 입장 → 준비(구조물 설치) → 라운드 1 등반 → 하이라이트 → 준비(추가 설치, 이전 설치물 유지) → 라운드 2 → ... → 라운드 3 → 누적 점수로 최종 순위 발표. 라운드가 갈수록 보이지 않는 함정이 늘어나 심리전이 깊어진다
- **팀원별 역할:**

### 개발 일정

> **재편 1차(Day 2 종료 시점):** 코어 게임 루프 구현이 계획을 크게 앞질러, 당초 Day 2~5로 잡았던
> 네트워킹·지형·매치·준비 페이즈·점수를 조기 완료 → 남은 일정을 아트·완성도에 집중하도록 재구성했다.
>
> **재편 2차(2026-07-11, 클라이밍 전환):** 게임 컨셉을 "주제 룰렛 + 수평 코스 레이스"에서
> **Only Up 모티브 수직 클라이밍**(구조물 설치·3라운드 누적)으로 전면 전환([docs/클라이밍_전환_명세서.md](docs/클라이밍_전환_명세서.md)).
> 룰렛·주제·모드 3종·캔디 코스는 폐기하고, 랜덤 타워·구조물 2종(보임/안보임)·은닉 체력·낙하 데미지·
> 관전·높이 점수로 재구현했다. 이하 표는 전환을 반영해 갱신한 일정이다(Day 1~5는 완료 이력).

| 날짜 | 목표 | 상태 |
|---|---|---|
| Day 1 | 기획·셋업·웹 프로토로 설계 검증·Unity 전환 | ✅ 완료 |
| Day 2~3 | (구 컨셉) **코어 전부**: NGO 접속·이동 동기화 + 지형 + 매치 FSM + 게임 모드 3종 + 사망/부활 + 준비 페이즈(장애물·투표·가중 룰렛) + 상세 점수 | ✅ 완료 |
| Day 4~5 | (구 컨셉) **게임 UI 전체**(HUD·스코어보드·하이라이트 카드·최종 결과) + 이름표·플레이어 색 + 마우스룩 조준 시점 → **골든 패스 완주 가능한 플레이어블** | ✅ 완료 |
| Day 6 | **클라이밍 전환 구현**: 랜덤 타워 맵(시드 결정론) + 구조물 설치(보임/안보임, 자유 비행 카메라) + 은닉 체력·낙하 데미지·관전 + 높이 점수 + 지면 24×24 확장 + 프로젝트 구조 실무 정합(Structure 리네임·폴더 재편·템플릿 잔재 제거) | ▶ 진행 |
| Day 6 잔여 | 다인원 플레이테스트·밸런싱(페이즈 시간·낙하 데미지·구조물 지급량)·투명 구조물 심리전 검증 | ⬜ |
| Day 7 | Windows 빌드·시연 영상·회고(KPT)·(여유 시 Unity Relay/Facepunch Steam 트랜스포트 연동) | ⬜ |

---

## 구현 명세서

이 문서는 **Steam 출시를 목표로 Unity 엔진(Netcode for GameObjects)** 기준으로 작성한 구현 명세서다. 우선순위는 **필수**(출시 MVP 코어) / **권장**(완성도) / **선택**(확장)으로 표기한다.

> **컨셉 전환 노트(2026-07-11):** 게임 설계가 "주제 룰렛 파티"에서 **Only Up 모티브 수직 클라이밍**(구조물 설치·누적)으로 전면 전환됐다. 이 명세서는 전환 후 기준이며, 게임 규칙의 단일 상세 문서는 [docs/클라이밍_전환_명세서.md](docs/클라이밍_전환_명세서.md)다. [docs/기획서.md](docs/기획서.md)의 룰렛·주제·모드 부분은 무효.

> **엔진 전환 노트:** `packages/` 의 웹(Three.js + Node/Socket.IO) 코드는 설계·룰을 빠르게 검증한 1차 개념 검증 프로토타입이며, 프로덕션 빌드는 Unity 로 진행한다. 당시 검증한 규칙 중 룰렛·주제·수평 코스 관련 규칙은 클라이밍 전환으로 폐기됐고, **3라운드 누적·준비 페이즈 설치·호스트 권위** 같은 구조적 골격만 승계됐다.

### 1. 네트워킹 & 상태 동기화 (Netcode for GameObjects)

| 구현 요소 | 설명 | 우선순위 |
|---|---|---|
| NGO 호스트 권위 모델 | Netcode for GameObjects 채택. 한 명이 **호스트(서버+클라 겸용)**가 되어 게임 원장(페이즈·점수·체력·사망 판정·구조물 설치 검증)의 단일 권위를 갖는다. 웹 프로토타입의 '서버 권위'를 호스트가 대신한다. | 필수 |
| 전송 계층 | **Unity Relay(참가 코드) 기본**: 호스트가 방을 만들면 6자리 참가 코드가 발급되고, 참가자는 코드만으로 접속한다. 양쪽 모두 아웃바운드 연결이라 서로 다른 네트워크(캠퍼스 망 격리·NAT·방화벽)에서도 연결된다. UGS 익명 로그인 자동 처리. `ConnectionService._useRelay` 를 끄면 기존 LAN 직접 IP:포트 접속으로 폴백(포트 자동 탐색 유지). Steam(Facepunch) 릴레이는 출시 단계 작업. | 필수(구현됨) / Steam 은 선택(출시) |
| 이동 동기화 (ClientNetworkTransform) | 각 플레이어 캐릭터는 소유 클라가 로컬 이동하고 `ClientNetworkTransform`(클라 권위 + 내장 보간)으로 위치를 복제. 호스트는 재시뮬 없이 중계·검증. 웹의 '클라 권위 릴레이'와 동일 철학. | 필수 |
| 상태 = NetworkVariable / RPC | 페이즈·라운드·타이머·승자·맵 시드는 `NetworkVariable<T>`(변경 시 자동 동기화), 일회성 이벤트(텔레포트·탈락 통보)는 대상 지정 `Rpc`, 클라 요청(구조물 설치·낙하 보고·충돌 공개)은 `ServerRpc`, 라운드 결과 목록은 `NetworkList<T>`. | 필수 |
| 접속·로비 | 타이틀(닉네임) → 방 만들기(참가 코드 발급)/방 참가(코드 입력) → 대기방(참가자 목록·준비·호스트 시작) 흐름(LobbyUI + LobbyManager + ConnectionService). 접속 승인으로 진행 중 참가·정원 초과를 거절. Steam 로비는 출시 단계. | 필수(구현됨) |
| 규모 & 부하 검증 | Multiplayer Play Mode(가상 플레이어)와 빌드 다중 실행으로 다인원 테스트, 호스트 대역폭·지연 실측. | 필수 |
| 끊김 처리·재접속 | 클라 이탈 시 관전·재접속 유예. 호스트 이탈 시 매치 종료(호스트 마이그레이션은 선택). | 권장 |
| 전용 서버(DGS) 확장 | 대규모 시 헤드리스 Unity 전용 서버(KCLOUD VM 또는 Unity Multiplay)로 전환. NGO 코드 대부분 재사용. | 선택 |

### 2. 준비 페이즈 (구조물 설치, 매 라운드 전 반복)

| 구현 요소 | 설명 | 우선순위 |
|---|---|---|
| 구조물 설치 (ServerRpc + NetworkObject 스폰) | PREP 동안 자유 비행 고스트 카메라(WASD+마우스룩+Space/Ctrl, 본체는 바닥 잠금)로 맵 볼륨을 날며 조준점에 좌클릭 설치. `PlaceStructureServerRpc(pos,yaw,type,invisible)` → 호스트 검증(PREP 한정·종류별 잔여 개수·볼륨 경계·겹침) 후 스폰. | 필수 |
| 구조물 2종(보임/안보임) | 보이는 구조물(벽·원기둥): 생성 후 모든 페이즈에서 전원에게 보임. 보이지 않는 구조물: PREP 에는 설치자에게만(반투명), 그 외 페이즈엔 아무에게도 안 보임, 플레이어 충돌 시 `revealDuration`(2초) 동안 전원 공개. 콜라이더는 항상 전원 동일(공정). | 필수 |
| 라운드별 지급 개수 | R1 전: 보임 3 + 안보임 1, R2 전: 보임 2 + 안보임 2, R3 전: 보임 1 + 안보임 3 (시리얼라이즈 배열, 이월 없음). | 필수 |
| 설치물 누적 | 설치한 구조물은 라운드 사이에 지우지 않고 3라운드 내내 누적(핵심 룰). 매치 종료 시에만 전체 despawn. | 필수 |
| 배치 프리뷰 | 조준점에 반투명 고스트 + 로컬 예비검증으로 초록/빨강 표시, R 키 90도 회전. | 권장 |

### 3. 라운드 시스템 (클라이밍 단일 모드)

| 구현 요소 | 설명 | 우선순위 |
|---|---|---|
| 매치 상태머신 (NetworkBehaviour) | 호스트의 `MatchManager : NetworkBehaviour` 가 FSM(LOBBY → [PREP → PLAY → HIGHLIGHT → INTERMISSION] ×3, 마지막 라운드는 INTERMISSION 대신 RESULT) 구동. 룰렛/주제 없음. `NetworkVariable<Phase>` + 종료 타임스탬프로 전 클라 동기화. | 필수 |
| 랜덤 타워 맵 | 6×6×50 볼륨을 높이 75등분, 슬라이스마다 보이는 구조물 2개를 겹치지 않게 랜덤 배치(총 150). 호스트 시드 1개를 `NetworkVariable` 복제 → 전 피어 결정론적 로컬 생성. 바닥 6×6 + 투명 경계벽. | 필수 |
| 은닉 체력·낙하 데미지 | 라운드 시작 체력 10(서버 전용, UI 비공개). 낙하高 5 이상 착지 시 소유자가 `ReportFallServerRpc` 보고 → 서버가 차감(기준 2 + 초과 m 당 1.5). | 필수 |
| 사망/관전 | 체력 0 이하 → 그 라운드 행동 불가(입력 잠금, 본체 렌더·콜라이더 off), 카메라는 생존자 추적 관전(좌클릭 대상 순환). 부활 없음, 라운드 종료 시 일괄 복귀. | 필수 |
| 라운드 종료 조건 | 타이머 만료(기본 180초) / 전원 사망 / 첫 정상(y≥50) 도달 후 유예(15초). | 필수 |

### 4. 점수 시스템 (높이 비례 + 순위 보너스)

| 구현 요소 | 설명 | 우선순위 |
|---|---|---|
| 높이 점수 | 라운드 **종료 시점** 플레이어 높이 y / 50 × 700 (0~700). peak 이 아니라 종료 시점이라 마지막까지 서 있는 위치가 중요. 탈락자는 사망 지점 높이. 정상 도달자는 만점 고정. 호스트 계산. | 필수 |
| 순위 보너스 | 종료 높이 내림차순 1/2/3위에 +300/200/100 (동률은 먼저 도달한 쪽 우선). | 필수 |
| 3라운드 누적·순위 | `NetworkList<RoundResult>`로 동기화, RESULT 에서 누적 총점 최종 순위. | 필수 |
| 실시간 리더보드 | 라운드 중 Top-N 표시. | 권장 |

### 5. 하이라이트

| 구현 요소 | 설명 | 우선순위 |
|---|---|---|
| 사망 이벤트 로깅 | 호스트가 라운드 사망(위치·시각·낙하高)을 기록(하이라이트·통계의 원천). | 필수 |
| 라운드 결과 카드 | 각 라운드 하이라이트 페이즈에 라운드 우승자·Top3·최고 도달 높이 카드 표시(복제된 `Results` 기반, 클라 로컬 렌더). | 필수 |
| 임팩트 스탯 카드 | 최다 낙사 유발 구조물(보이지 않는 구조물 낚시 횟수 등) 산정 → 카드 표시. | 권장 |
| 킬캠 리플레이 | Unity Timeline/스냅샷 녹화 기반 3D 리플레이. | 선택 |
| 클립 캡처·공유 | 숏폼용 녹화 버튼. | 선택 |

### 6. 클라이언트 · 맵 · 캐릭터 · 카메라

| 구현 요소 | 설명 | 우선순위 |
|---|---|---|
| 맵 (랜덤 타워) | 6×6×50 볼륨(구조물 생성 범위) 랜덤 생성 클라이밍 타워(`ClimbMapGenerator`): 시드 기반 결정론 생성으로 전 피어 동일, 슬라이스(75등분)당 보이는 구조물 2개. 지면 24×24 + 투명 경계벽(플레이어 이동 가능 범위). 지오메트리는 `Ground` 레이어(카메라 오클루전 호환), 캔디 파스텔 머티리얼 재사용. 점프력 Δy=1 기준 밸런싱. 발판 풀 = 기본 2종(Wall/Cylinder) + 가구 발판 8종(테이블 3·눕힌 서랍장·옷장·냉장고·욕조·쿠션, `Assets/Prefabs/Platforms/`): 가구는 네스티드 프리팹 래퍼에 렌더 바운즈 맞춤 BoxCollider + `PlatformSegment`(가중치 0.3~0.7)를 붙여 충돌·체인 앵커가 AABB 윗면으로 정합된다. | 필수 |
| 캐릭터 컨트롤러 | `CharacterController`(또는 Rigidbody) 기반 이동·점프, 지면·장애물 콜라이더. 물리는 Unity 내장(웹의 커스텀 키네매틱 대체). | 필수 |
| 카메라 (마우스룩 조준) | 전 페이즈 배그식 3인칭 오버숄더 마우스룩: 마우스=조준(화면 중앙 조준점), WASD 는 카메라 기준 이동, 몸은 카메라 방향. Ground 레이어 SphereCast 오클루전, 커서 잠금(Esc 토글). 후방추적 시점은 `useAimView=false` 로 보존. | 필수 |
| UI (uGUI 코드 생성) | 프리팹 없는 코드 생성 HUD: 페이즈 배너·타이머·누적 점수판·현재 높이·탈락/관전 배너·하이라이트 카드·최종 결과·조준점·머리 위 이름표. PREP 은 구조물 종류/잔여 개수 패널. | 필수 |
| 캐릭터 (스틱맨) | 휴머노이드 스틱맨 모델(PolyOne Free Stickman)을 Player 프리팹에 네스티드 프리팹으로 부착. 1D 블렌드트리(Speed: Idle 0 / Walk 2 / Run 6) + Airborne(Grounded) 전이, `PlayerAnimDriver` 가 transform 프레임 차분으로 속도를 추정해 원격 플레이어도 팔다리가 움직인다. `PlayerTint` 로 플레이어별 색 적용. | 필수(구현됨) |
| 맵 장식 에셋 (가구) | Furniture Mega Pack 을 등반 맵("거대한 방" 테마)용으로 선별 도입: 테이블 8·의자 6·소파 5·침대 8·옷장 6·서랍장 6·쿠션 8·주방 20(냉장고·캐비닛 등)·욕조 7 = 프리팹 74종. 원본 1.7GB 를 노멀/메탈릭맵 제거 + 알베도 1K 축소로 40MB 로 경량화(스타일라이즈드 룩 기준 시각 손실 없음, URP/Lit). 이 중 8종은 발판 래퍼로 맵 생성 풀에 사용 중(위 '맵' 행), 나머지는 장식·구조물 후보. | 권장(사용 중) |
| 아트·이펙트 | 이모트, 낙사 파티클 등(숏폼용 '웃긴 장면'). | 권장 |

### 7. 기술 스택 · 빌드 · Steam 출시

| 구현 요소 | 설명 | 우선순위 |
|---|---|---|
| Unity 6 LTS + NGO | Unity 6 LTS(6000.x) + `com.unity.netcode.gameobjects`. 언어 C#. | 필수 |
| Steamworks 연동 | Facepunch.Steamworks(App ID·로비·친구·업적) + Facepunch Transport. 초기엔 개발용 App ID 480(Spacewar)로 테스트. | 선택(출시 단계) |
| 프로젝트 구조 | 단일 Unity 프로젝트. `Assets/Scripts/{Map, Match, Net, UI}` (네임스페이스 `RouletteParty.{Map, Match, Net, UI}` 로 통일). asmdef 어셈블리 분리는 규모 증가 시 도입. | 필수 |
| 버전관리 | Git, Unity `.gitignore`, `.meta` 커밋 규칙. 대용량 에셋 팩은 LFS 대신 **선별 + 텍스처 축소 후 커밋**(가구 팩 1.7GB→40MB 사례). | 필수 |
| 빌드 & 출시 | Windows 빌드 → Steamworks 파트너 depot 업로드. Steam Direct(앱 수수료 $100·30일 대기·심사), 상점 페이지·Coming Soon. | 필수 |
| 전용 서버 빌드 | 헤드리스 리눅스 서버 빌드(KCLOUD/Multiplay), 대규모 확장 시. | 선택 |

### 구현 규모 및 현실성

NGO 호스트 권위 + Steam P2P/릴레이 기준 **MVP 동시 접속은 8~16명**을 현실선으로 본다(파티 게임 표준 규모). 호스트 업로드 대역폭이 병목이라 상태 압축·관심영역(AOI)은 스트레치다. **수십~100명은 헤드리스 전용 서버(DGS) 전환**으로만 가능하며 MVP 범위 밖으로 둔다. NGO 는 호스트/서버 코드가 처음부터 분리돼 있어 나중에 전용 서버로 옮기기 쉽다.

### 일정 · 컷라인

밀릴 경우 **골든 패스**(접속 → 준비(구조물 설치) → 등반 → 높이 점수 → 3라운드 누적 → 최종 순위)를 최우선 사수. 컷 순서: 배치 프리뷰 → 임팩트 스탯 카드 → 실시간 리더보드 → 관전 대상 순환(고정 1인 관전으로 축소).

### 권장 기술 스택

| 분류 | 선택 | 근거 |
|---|---|---|
| 엔진 | Unity 6 LTS (C#) | 스팀 배포 검증된 파이프라인, 에셋·물리·에디터 |
| 네트워킹 | Netcode for GameObjects (호스트 권위) | Unity 네이티브, 서버 권위 파티 게임에 적합, 우리 설계와 동형 |
| 전송 | UnityTransport(현행) → Unity Relay(데모) → Facepunch/Steam(출시) | 단계적 전환, `ApplyConnectionData()` 한 곳만 교체 |
| 물리·이동 | CharacterController + Unity Physics | 플랫포머 이동에 충분, 콜라이더 공유 |
| 카메라·UI | 커스텀 마우스룩 카메라 + uGUI(코드 생성 HUD) | 조준 시점 요구사항 직접 구현·프리팹 없는 HUD |
| 버전관리 | Git (대용량 에셋은 선별·축소 후 커밋) | 무료 플랜 LFS 한도 회피, 클론만으로 실행 가능 유지 |
| 배포 | Steamworks (Steam Direct) | 스팀 출시 |

---

## 아키텍처

**Unity + Netcode for GameObjects 호스트 권위** 구조. 한 명이 호스트(서버 겸용)로 게임 원장을 소유하고, 나머지가 접속한다. 전송 계층은 현재 UnityTransport(IP 직접)이며, 데모용 Unity Relay → 출시용 Steam 릴레이로 단계 전환한다(`ApplyConnectionData()` 한 곳만 교체).

```
 [Unity 클라이언트 x N]                          [호스트 (서버+클라 겸용)]
  로컬 이동·렌더·입력       --ServerRpc-->         MatchManager (NetworkBehaviour)
  (맵·구조물·캐릭터)        <-NetworkVariable/RPC-  페이즈·점수·체력·사망 판정 (권위)
  UI (준비/HUD/결과)        <-NetworkTransform---   NetworkObject 복제 (플레이어·구조물)
        |                                                  |
        +--- UnityTransport (IP 직접, 현행) ---------------+
             향후: Unity Relay(데모) → Steam 릴레이(출시)
```

- 호스트가 단일 권위. `NetworkVariable`(자동 동기화 상태) + `ServerRpc`/`ClientRpc`(요청·이벤트) + `ClientNetworkTransform`(이동)으로 구성.
- 대규모 확장 시 호스트 대신 헤드리스 Unity 전용 서버(DGS)로 교체(NGO 코드 재사용).

**프로젝트 구조** (단일 Unity 프로젝트)

- `Assets/Scripts/Net`: NGO 부트스트랩·트랜스포트·플레이어 컨트롤러(이동/비행/관전)·색상
- `Assets/Scripts/Match`: 매치 FSM(`MatchManager`)·구조물(`Structure`)·준비 페이즈 설치 UI
- `Assets/Scripts/Map`: 랜덤 타워 생성(`ClimbMapGenerator`, 시드 결정론)
- `Assets/Scripts/UI`: 코드 생성 HUD·플레이어 팔레트
- (참고) `packages/`: 초기 웹 개념검증 프로토타입 (설계 검증용, 프로덕션 아님)

---

## 설계 문서

> 프로젝트 성격에 따라 필요한 항목만 작성

### 화면 / 인터페이스 설계

페이즈 구동 단일 화면(3D 씬 위 UI 오버레이, 전 페이즈 마우스룩 조준 시점·커서 잠금). 로비(접속) → 준비(자유 비행 카메라, 숫자키 1/2 보이는 구조물·3 보이지 않는 구조물 선택, 조준점 좌클릭 설치, R 회전, 종류별 잔여 개수 표시) → 플레이(상단 라운드·타이머 배너, 현재 높이 표시, 조준점, 탈락 시 "관전 중" 배너) → 하이라이트(라운드 우승자·Top3 카드) → 결과(전체 화면 누적 최종 순위). 우상단에 누적 점수판 상시 표시, Esc 로 커서 잠금 해제. 체력은 어디에도 표시하지 않음(비공개 정보).

### 데이터 구조

라이브 상태는 호스트가 소유하고 NGO 로 동기화한다.

- 플레이어: `NetworkObject` + `ClientNetworkTransform`(위치). 체력(HP)은 서버 전용(복제·표시 안 함)
- 구조물: `NetworkObject` 프리팹 `{ type(WALL|CYLINDER), ownerId, isInvisible, revealUntil }` 3라운드 누적
- 맵: `NetworkVariable<int> MapSeed` 1개(전 피어 결정론적 로컬 생성, 6×6×50 / 75슬라이스 / 슬라이스당 2개)
- 매치: `NetworkVariable<Phase>`(LOBBY|PREP|PLAY|HIGHLIGHT|RESULT), `round(1~3)`
- 점수: 라운드당 종료 시점 높이/50 × 700 + 순위 보너스(300/200/100), 3라운드 누적. `NetworkList<RoundResult>`

### 네트워크 계약 (NGO)

외부 API 는 사용하지 않는다. 통신은 NGO 의 RPC·NetworkVariable 로 이뤄진다.

| 종류 | 이름 | 방향 | 설명 |
|---|---|---|---|
| ServerRpc | `PlaceStructureServerRpc(pos,yaw,type,invisible)` | C→H | 구조물 설치 요청(호스트가 검증 후 스폰) |
| ServerRpc | `ReportFallServerRpc(fallHeight)` | C→H | 착지 낙하高 보고(서버가 은닉 체력 차감·사망 판정) |
| ServerRpc | `RevealStructureServerRpc(ref)` | C→H | 보이지 않는 구조물과의 충돌 보고(전원 일시 공개) |
| Rpc(Single) | `TeleportPlayerRpc(pos)` | H→소유 클라 | 라운드 시작 바닥 배치(소유자가 스스로 텔레포트) |
| Rpc(Single) | `EliminatedRpc()` | H→소유 클라 | 탈락 통보(입력 잠금 + 관전 전환) |
| NetworkVariable | `Phase / Round / PhaseEndTime / AliveCount / RoundWinner / MatchWinner / MapSeed` | H→C | 페이즈·타이머·승자·맵 시드 동기화 |
| NetworkList | `Results` (라운드별 순위·점수 행) | H→C | 점수판·하이라이트·결과 UI 데이터 |
| NetworkVariable | 구조물 `Type / OwnerId / IsInvisible / RevealUntil` | H→C | 구조물 상태(가시성 규칙의 근거) |
| NetworkTransform | 플레이어 위치 | 소유 클라→전체 | ClientNetworkTransform(소유자 권위) 보간 |

(C=클라이언트, H=호스트)

체력은 서버 전용 상태로 복제하지 않는다(플레이어 비공개 정보). 구조물 가시성은 복제된 `IsInvisible/RevealUntil` 과 현재 페이즈로 각 클라가 로컬 판정하며, 콜라이더는 항상 전원 동일하다(물리 공정성). 상세 규칙·시리얼라이즈 필드 목록은 [docs/클라이밍_전환_명세서.md](docs/클라이밍_전환_명세서.md).

---

## 산출물 및 실행 방법

- **산출물 설명:** Unity 기반 실시간 멀티플레이어 수직 클라이밍 파티 게임(Steam 출시 목표, 게임명은 컨셉 전환에 따라 재선정 예정). 초기 웹 프로토타입으로 설계·룰 검증 후 Unity 로 프로덕션, 이후 클라이밍 컨셉으로 전환.
- **실행 환경:** Unity 6 LTS(6000.x), Windows 빌드. 멀티플레이는 IP 직접 접속(UnityTransport, 같은 네트워크 기준). 원격 데모용 Unity Relay 는 완성도 강화 계획 5절 참조.
- **실행 방법:** Unity Hub 로 프로젝트 열기 → NGO·Facepunch 패키지 설치 → 에디터 Play 로 호스트 시작, 친구 초대 또는 빌드 다중 실행으로 접속.
- **시연 영상 / 이미지:** (선택)

### 실행 방법

```text
# Unity 프로젝트 (프로덕션)
1. Unity Hub 에서 Unity 6 LTS(6000.x) 설치 후 프로젝트 열기 (NGO 등 패키지는 manifest 로 자동 설치)
2. Play → 좌상단 [Host] 버튼으로 호스트 시작
   (기본 포트 7777 이 사용 중이면 자동으로 다음 빈 포트를 찾아 호스팅하고 화면에 "호스팅 포트"로 표시)
3. 다른 인스턴스(Multiplayer Play Mode 가상 플레이어 또는 빌드 실행)에서 호스트 IP·포트 입력 후 [Client]
   (같은 PC 는 127.0.0.1, 같은 네트워크는 호스트 사설 IP. 포트는 호스트 화면에 표시된 값)
4. 빌드: File > Build Profiles (Windows) → 빌드 2개 실행으로 멀티 테스트
5. (출시 단계) Facepunch.Steamworks + Facepunch Transport 전환, Steamworks depot 업로드

# 초기 웹 개념검증 프로토타입 (참고용, packages/)
npm install -g pnpm && pnpm install && pnpm dev   # http://localhost:5173
```

### 기술 구성

| 분류 | 사용 기술 |
|---|---|
| 엔진 / 언어 | Unity 6 LTS, C# |
| 네트워킹 | Netcode for GameObjects (호스트 권위) |
| 전송 | UnityTransport(IP 직접, 현행) → Unity Relay(데모, 검토 중) → Facepunch/Steam(출시 단계) |
| 물리 / 카메라 / UI | CharacterController, 커스텀 마우스룩 카메라, uGUI(코드 생성 HUD) |
| 버전관리 / 배포 | Git + Git LFS, Steamworks (Steam Direct) |
| 초기 검증 (웹) | TypeScript, Socket.IO, Three.js (packages/, 참고용) |

---

## 회고 문서

> [KPT 방법론 참고](https://velog.io/@habwa/%EB%8B%A8%EA%B8%B0-%ED%94%84%EB%A1%9C%EC%A0%9D%ED%8A%B8-%ED%9A%8C%EA%B3%A0-KPT-%EB%B0%A9%EB%B2%95%EB%A1%A0)

### Keep: 잘 된 점, 다음에도 유지할 것

-
-
-

### Problem: 아쉬웠던 점, 개선이 필요한 것

-
-
-

### Try: 다음번에 시도해볼 것

-
-
-

### 팀원별 소감

**권순호:**

> 

**이유담:**

> 

---

## 참고 자료

### 실시간 인터랙션

**WebSocket**
- https://developer.mozilla.org/en-US/docs/Web/API/WebSockets_API
- https://techblog.woowahan.com/5268/
- https://tech.kakao.com/posts/391
- https://daleseo.com/websocket/
- https://kakaoentertainment-tech.tistory.com/110

**Socket.IO**
- https://socket.io/docs/v4/
- https://inpa.tistory.com/entry/SOCKET-%F0%9F%93%9A-Namespace-Room-%EA%B8%B0%EB%8A%A5
- https://adjh54.tistory.com/549
- https://fred16157.github.io/node.js/nodejs-socketio-communication-room-and-namespace/

**SSE (Server-Sent Events)**
- https://developer.mozilla.org/en-US/docs/Web/API/Server-sent_events
- https://developer.mozilla.org/ko/docs/Web/API/Server-sent_events/Using_server-sent_events
- https://api7.ai/ko/blog/what-is-sse

**TCP / UDP Socket**
- https://docs.python.org/3/library/socket.html
- https://inpa.tistory.com/entry/NW-%F0%9F%8C%90-%EC%95%84%EC%A7%81%EB%8F%84-%EB%AA%A8%ED%98%B8%ED%95%9C-TCP-UDP-%EA%B0%9C%EB%85%90-%E2%9D%93-%EC%89%BD%EA%B2%8C-%EC%9D%B4%ED%95%B4%ED%95%98%EC%9E%90

**gRPC Streaming**
- https://grpc.io/docs/what-is-grpc/core-concepts/
- https://tech.ktcloud.com/entry/gRPC%EC%9D%98-%EB%82%B4%EB%B6%80-%EA%B5%AC%EC%A1%B0-%ED%8C%8C%ED%97%A4%EC%B9%98%EA%B8%B0-HTTP2-Protobuf-%EA%B7%B8%EB%A6%AC%EA%B3%A0-%EC%8A%A4%ED%8A%B8%EB%A6%AC%EB%B0%8D
- https://tech.ktcloud.com/entry/gRPC%EC%9D%98-%EB%82%B4%EB%B6%80-%EA%B5%AC%EC%A1%B0-%ED%8C%8C%ED%97%A4%EC%B9%98%EA%B8%B02-Channel-Stub
- https://inspirit941.tistory.com/371
- https://devocean.sk.com/blog/techBoardDetail.do?ID=167433

**WebRTC**
- https://developer.mozilla.org/en-US/docs/Web/API/WebRTC_API
- https://webrtc.org/getting-started/overview
- https://web.dev/articles/webrtc-basics?hl=ko
- https://devocean.sk.com/blog/techBoardDetail.do?ID=164885
- https://beomkey-nkb.github.io/%EA%B0%9C%EB%85%90%EC%A0%95%EB%A6%AC/webRTC%EC%A0%95%EB%A6%AC/
- https://gh402.tistory.com/45
- https://on.com2us.com/tech/webrtc-coturn-turn-stun-server-setup-guide/

**QUIC / WebTransport**
- https://developer.mozilla.org/en-US/docs/Web/API/WebTransport_API
- https://datatracker.ietf.org/doc/html/rfc9000
- https://news.hada.io/topic?id=13888

#### KCLOUD VM / Cloudflare Tunnel 환경별 주의사항

| 환경 | 사용 가능(권장) 기술 | 포트/조건 | 주의할 기술 |
|---|---|---|---|
| **로컬 / 일반 VM** | HTTP/REST, WebSocket, Socket.IO, SSE, TCP Socket, gRPC Streaming, WebRTC, QUIC/WebTransport 등 대부분 가능 | 직접 포트 개방 가능. 예: 3000, 5000, 8000, 8080, 9000 등. 외부 공개 시 방화벽/보안그룹/공인 IP 설정 필요 | WebRTC는 STUN/TURN 필요 가능. QUIC/WebTransport는 HTTP/3 · UDP 지원 필요 |
| **KCLOUD VM (VPN 내부)** | HTTP/REST, WebSocket, Socket.IO, SSE, WebRTC 시그널링 | 접속 기기 VPN 필요. 기본 허용 포트: **22, 80, 443**. 개발 포트(3000, 8000, 8080 등)는 직접 접근 제한 가능 | TCP Socket은 포트 제한 있음. gRPC는 HTTP/2 설정 필요. WebRTC 미디어·UDP·QUIC/WebTransport 비권장 |
| **KCLOUD VM + Tunnel** | HTTP/REST, WebSocket, Socket.IO, SSE, WebRTC 시그널링 | VM의 `localhost:<port>`를 도메인에 연결. `localPort`는 **1024~65535**. 예: 3000, 8000, 8080 가능 | 순수 TCP Socket, UDP, WebRTC 미디어/DataChannel, QUIC/WebTransport 불가. gRPC 보장 어려움 |
| **외부 서비스 + 우리 도메인** | HTTP/REST, WebSocket, Socket.IO, SSE, WebRTC 시그널링 | Vercel/Netlify/Railway/Render/AWS/GCP 등에 배포 후 CNAME/A 레코드 연결. 보통 외부는 **443** 사용 | WebSocket/gRPC/TCP/UDP는 플랫폼 지원 여부 확인 필요. 서버리스 플랫폼은 장시간 연결 제한 가능 |
| **서버 없이 외부 SaaS 사용** | Supabase Realtime, Firebase, Pusher/Ably, LLM API Streaming | 직접 포트 관리 불필요. 각 서비스 SDK/API 사용 | 커스텀 TCP/UDP 서버 구현 불가. WebRTC는 STUN/TURN 필요할 수 있음 |

### LLM Wrapper

- https://github.com/teddylee777/openai-api-kr
- https://github.com/teddylee777/langchain-kr
- https://devocean.sk.com/blog/techBoardDetail.do?ID=167407
- https://mastra.ai/docs

### Cross-Platform

- https://flutter.dev/
- https://reactnative.dev/
- https://docs.expo.dev/
- https://kotlinlang.org/multiplatform/

### Unity / Steam (엔진 전환)

- Netcode for GameObjects: https://docs-multiplayer.unity3d.com/
- Facepunch.Steamworks: https://wiki.facepunch.com/steamworks/
- Facepunch Transport (community contributions): https://github.com/Unity-Technologies/multiplayer-community-contributions
- Steamworks 파트너 / Steam Direct: https://partner.steamgames.com/
- Steam 온보딩(수수료·30일 대기): https://partner.steamgames.com/doc/gettingstarted/onboarding
