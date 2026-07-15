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

> 📄 초기 기획서(전환 전): [docs/archive/기획서.md](docs/archive/기획서.md) ·
> 컨셉 전환 결정 기록(2026-07-11): [docs/archive/클라이밍_전환_명세서.md](docs/archive/클라이밍_전환_명세서.md)
> - 현행 게임 규칙의 단일 출처는 아래 **구현 명세서**다.

- **산출물 주제:** 「얼렁뚱탑」: 다인원 수직 클라이밍 파티 게임 (Only Up! 모티브), 매 라운드 전 준비 페이즈에 플레이어들이 구조물(보이는/보이지 않는)을 설치하고, 그 구조물이 3라운드 내내 누적되는 랜덤 타워에서 더 높이 오르기를 경쟁하는 실시간 게임
- **제작 목적:** MECCHA CHAMELEON, Fall Guys에서 착안한 "다수의 사람을 한 맵에 넣자"는 아이디어를 실시간 인터랙션 기술로 구현. 우스꽝스러운 장면(숏폼 적합)과 유저 제작 맵 기반의 반복 플레이 가치를 추구
- **선택 옵션:** 실시간 인터랙션
- **핵심 구현 요소:**
  - 다수 유저의 위치·상태 실시간 동기화 (랜덤 생성 수직 타워에서 동시 등반)
  - 전 페이즈 3인칭 마우스룩 시점: 마우스=시선, 커서 잠금 기반 조작(화면 중앙 조준점 없음)
  - 매 라운드 전 준비 페이즈: 자유 비행 카메라로 구조물 설치. 보이는 구조물(전원 상시 공개)과 보이지 않는 구조물(준비 중 설치자에게만, 충돌 시 전원에게 일시 공개) 이원화, **설치물은 3라운드 누적**
  - 낙하 탈락 2규칙(공중 낙하·착지 낙하, 서버 판정): 탈락 시 잠깐 관전 후 시작 섬에서 자동 부활
  - 점수 = 7항목 합산(진행도·정상 도달 시간·청크 선착순·순위·안정성·투명 구조물 영향·반복 탈락 감점), 3라운드 누적. 라운드마다 하이라이트 카드
- **사용 / 시연 시나리오:** 유저들이 입장 → 준비(구조물 설치) → 라운드 1 등반 → 하이라이트 → 준비(추가 설치, 이전 설치물 유지) → 라운드 2 → ... → 라운드 3 → 누적 점수로 최종 순위 발표. 라운드가 갈수록 보이지 않는 함정이 늘어나 심리전이 깊어진다

### 개발 일정

> **재편 1차(Day 2 종료 시점):** 코어 게임 루프 구현이 계획을 크게 앞질러, 당초 Day 2~5로 잡았던
> 네트워킹·지형·매치·준비 페이즈·점수를 조기 완료 → 남은 일정을 아트·완성도에 집중하도록 재구성했다.
>
> **재편 2차(2026-07-11, 클라이밍 전환):** 게임 컨셉을 "주제 룰렛 + 수평 코스 레이스"에서
> **Only Up 모티브 수직 클라이밍**(구조물 설치·3라운드 누적)으로 전면 전환([docs/archive/클라이밍_전환_명세서.md](docs/archive/클라이밍_전환_명세서.md)).
> 룰렛·주제·모드 3종·캔디 코스는 폐기하고, 랜덤 타워·구조물 2종(보임/안보임)·은닉 체력·낙하 데미지·
> 관전·높이 점수로 재구현했다(은닉 체력·단순 높이 점수는 이후 낙하 탈락 2규칙·7항목 점수로 재개편).
> 이하 표는 전환을 반영해 갱신한 일정이다(Day 1~5는 완료 이력).

| 날짜 | 목표 | 상태 |
|---|---|---|
| Day 1 | 기획·셋업·웹 프로토로 설계 검증·Unity 전환 | ✅ 완료 |
| Day 2~3 | (구 컨셉) **코어 전부**: NGO 접속·이동 동기화 + 지형 + 매치 FSM + 게임 모드 3종 + 사망/부활 + 준비 페이즈(장애물·투표·가중 룰렛) + 상세 점수 | ✅ 완료 |
| Day 4~5 | (구 컨셉) **게임 UI 전체**(HUD·스코어보드·하이라이트 카드·최종 결과) + 이름표·플레이어 색 + 마우스룩 조준 시점 → **골든 패스 완주 가능한 플레이어블** | ✅ 완료 |
| Day 6 | **클라이밍 전환 구현**: 랜덤 타워 맵(시드 결정론) + 구조물 설치(보임/안보임, 자유 비행 카메라) + 낙하 탈락·관전·자동 부활 + 점수 개편(7항목) + 씬 분리(로비/게임) + Unity Relay 참가 코드 + UCH 스타일 UI 재스킨 + 프로젝트 구조 실무 정합(Structure 리네임·폴더 재편) | ✅ 완료 |
| Day 7 | **마감 반영**: 게임명 「얼렁뚱탑」 확정(로비 배너·부제·productName) + 구조물 전면 재편(벽/원기둥/투명벽 폐기 → 실제 가구·자연물 8종, 크기 소/중/대는 에셋 고유 크기, **투명은 형태가 아니라 플래그** → 투명 나무·투명 옷장 가능) + 라운드별 매치 설정(준비/등반 시간·구조물 개수 3x3 그리드 + [표준 모드] 일괄 복원, 구조물 중 투명은 라운드당 정확히 1개) + 방 정원 10명·최소 시작 1명(릴레이 할당·색 팔레트 10색 동조) + 조준점 UI 제거 + F1 도움말 키캡 2단 개편 + 호버 시 글씨 사라짐 수정 + 닉네임 한글 입력(IME) + 이름표 머리 바로 위 + 배포 상태 정리(fastMode 해제·디버그 GUI 꺼짐) + **macOS(Universal, Mono) 빌드 산출**(`Builds/얼렁뚱탑_mac/얼렁뚱탑.app`, 전송용 zip 포함) | ✅ 완료 |
| 잔여(마감 후) | 다인원 실기 플레이테스트(크로스 네트워크 Relay·Mac 실기 실행 확인)·정원 10인 동시 접속 검증·시연 영상·(출시 단계) Facepunch Steam 트랜스포트 | ⬜ |

---

## 문서 안내

현행 문서는 이 README(구현 명세서·아키텍처·실행 방법)와 운영 가이드 하나로 단일화했다.
과정에서 만든 기획·제안·전환 문서는 삭제하지 않고 `docs/archive/` 에 결정 기록으로 보존한다
(각 문서 상단에 상태와 현행 문서 위치를 표기).

| 문서 | 상태 | 내용 |
|---|---|---|
| README.md (이 문서) | **현행** | 게임 규칙·구현 명세·아키텍처·실행 방법의 단일 출처 |
| [docs/LAN_테스트_가이드.md](docs/LAN_테스트_가이드.md) | **현행** | LAN 직접 IP 접속 폴백의 두 PC 수동 테스트 절차 |
| [docs/archive/기획서.md](docs/archive/기획서.md) | 아카이브 | 최초 기획(주제 룰렛 파티) - 전환으로 핵심 루프 폐기 |
| [docs/archive/클라이밍_전환_명세서.md](docs/archive/클라이밍_전환_명세서.md) | 아카이브 | 2026-07-11 클라이밍 컨셉 전환의 결정 기록(폐기/승계 목록) |
| [docs/archive/점수_시스템_개편안.md](docs/archive/점수_시스템_개편안.md) | 아카이브 | 7항목 점수 개편 요구사항 - 구현 완료, 현행 수치는 4절 |
| [docs/archive/조준_시점_명세서.md](docs/archive/조준_시점_명세서.md) | 아카이브 | 마우스룩 조준 시점 기능 명세 - 구현 완료 |
| [docs/archive/완성도_강화_계획.md](docs/archive/완성도_강화_계획.md) | 아카이브 | 구 컨셉 기준 완성도 계획 - 유효 항목은 반영 완료 |
| [docs/archive/개발가이드.md](docs/archive/개발가이드.md) | 아카이브 | Day 6 시점(구 컨셉) 작업 가이드 |

---

## 구현 명세서

이 문서는 **Steam 출시를 목표로 Unity 엔진(Netcode for GameObjects)** 기준으로 작성한 구현 명세서다. 우선순위는 **필수**(출시 MVP 코어) / **권장**(완성도) / **선택**(확장)으로 표기한다.

> **컨셉 전환 노트(2026-07-11):** 게임 설계가 "주제 룰렛 파티"에서 **Only Up 모티브 수직 클라이밍**(구조물 설치·누적)으로 전면 전환됐다. **이 구현 명세서가 전환 이후 갱신을 거친 현행 게임 규칙의 단일 출처**이며, 전환 당시의 결정 기록과 폐기/승계 목록은 [docs/archive/클라이밍_전환_명세서.md](docs/archive/클라이밍_전환_명세서.md), 전환 전 기획은 [docs/archive/기획서.md](docs/archive/기획서.md)에 보존돼 있다.

> **엔진 전환 노트:** `web-prototype/` 의 웹(Three.js + Node/Socket.IO) 코드는 설계·룰을 빠르게 검증한 1차 개념 검증 프로토타입이며, 프로덕션 빌드는 Unity 로 진행한다. 당시 검증한 규칙 중 룰렛·주제·수평 코스 관련 규칙은 클라이밍 전환으로 폐기됐고, **3라운드 누적·준비 페이즈 설치·호스트 권위** 같은 구조적 골격만 승계됐다.

### 1. 네트워킹 & 상태 동기화 (Netcode for GameObjects)

| 구현 요소 | 설명 | 우선순위 |
|---|---|---|
| NGO 호스트 권위 모델 | Netcode for GameObjects 채택. 한 명이 **호스트(서버+클라 겸용)**가 되어 게임 원장(페이즈·점수·탈락 판정·구조물 설치 검증)의 단일 권위를 갖는다. 웹 프로토타입의 '서버 권위'를 호스트가 대신한다. | 필수 |
| 전송 계층 | **Unity Relay(참가 코드) 기본**: 호스트가 방을 만들면 6자리 참가 코드가 발급되고, 참가자는 코드만으로 접속한다. 양쪽 모두 아웃바운드 연결이라 서로 다른 네트워크(캠퍼스 망 격리·NAT·방화벽)에서도 연결된다. UGS 익명 로그인 자동 처리. `ConnectionService._useRelay` 를 끄면 기존 LAN 직접 IP:포트 접속으로 폴백(포트 자동 탐색 유지). Steam(Facepunch) 릴레이는 출시 단계 작업. | 필수(구현됨) / Steam 은 선택(출시) |
| 이동 동기화 (ClientNetworkTransform) | 각 플레이어 캐릭터는 소유 클라가 로컬 이동하고 `ClientNetworkTransform`(클라 권위 + 내장 보간)으로 위치를 복제. 호스트는 재시뮬 없이 중계·검증. 웹의 '클라 권위 릴레이'와 동일 철학. | 필수 |
| 상태 = NetworkVariable / RPC | 페이즈·라운드·타이머·승자·맵 시드는 `NetworkVariable<T>`(변경 시 자동 동기화), 일회성 이벤트(텔레포트·탈락 통보)는 대상 지정 `Rpc`, 클라 요청(구조물 설치·낙하 보고·충돌 공개)은 `ServerRpc`, 라운드 결과 목록은 `NetworkList<T>`. | 필수 |
| 접속·로비 | 타이틀(닉네임) → 방 만들기(참가 코드 발급)/방 참가(코드 입력) → 대기방(참가자 목록·준비·호스트 시작) 흐름(LobbyUI + LobbyManager + ConnectionService). **정원 10명 / 최소 시작 인원 1명**(혼자서도 시작 가능 - 솔로 연습/시연). 정원은 세 군데가 함께 맞아야 한다: `LobbyManager._maxPlayers`(접속 승인 거절 기준) = 10, `ConnectionService._relayMaxConnections`(릴레이 할당 크기, 호스트 제외) = 9, 플레이어 색 팔레트(PlayerPalette) = 10색(만석에서도 색 안 겹침). 접속 승인으로 진행 중 참가·정원 초과를 거절. Steam 로비는 출시 단계. | 필수(구현됨) |
| 매치 설정 (라운드별) | **호스트가 대기방에서 라운드마다 준비 시간·등반 시간·구조물 개수를 프리셋 버튼으로 조절**(3라운드 x 3항목 = 9개 값. 프리셋: 준비 30초/1분/90초, 등반 2/3/4분, 구조물 3/4/5개 - `LobbyManager` 인스펙터 배열로 변경 가능). **[표준 모드] 버튼**은 9개 값을 미리 정해 둔 기본값(라운드별 60초/3분/4개)으로 한 번에 되돌리고, 현재 설정이 표준과 같으면 버튼이 청록으로 켜진다(`IsStandard`). 표준값은 서버 인스펙터에만 있어 클라가 값을 지어내 보낼 여지가 없다(`ApplyStandardSetupServerRpc` 는 인자 없음). 9개 값은 **한 덩어리(`MatchSetup`)로 복제**한다 - 표준 모드의 일괄 변경이 원자적이어야 하므로 값별 NetworkVariable 로 쪼개지 않았다. 참가자에게는 라운드별 요약을 읽기 전용으로 표시. 선택은 PlayerPrefs 한 문자열(`"prep,play,count" x3`)로 영속(키 9개보다 원자적 - 부분 저장 상태가 안 생긴다), 서버 소비는 정적 `MatchSettings` 로 게임 씬 MatchManager 에 전달(씬 분리 대응, 서버 권위라 서버 값만 유효). 서버는 호스트 여부·대기방 여부·값 범위(`_prepMin/Max` 등)를 전부 재검증한다(실측: 준비 99999/등반 -50/구조물 999 요청 → 600/30/12 로 클램프). 연출 페이즈(로비/하이라이트/대기/결과) 시간은 MatchManager 인스펙터에서 페이즈별 조절. `EnterPhase` 가 페이즈/지속시간을 로그로 남긴다. **설정 그리드는 GUILayout 이 아니라 절대 좌표로 그린다** - GUILayout 은 요소마다 스타일 마진을 끼워 넣어 폭을 지정해도 열 제목과 버튼이 어긋난다(실측). | 필수(구현됨) |
| 씬 분리 (LobbyScene / MainScene) | **대기방은 전용 `LobbyScene`**(스테이지에 아바타 줄 세우기 + LobbyUI + LobbyManager 씬 NetworkObject), **게임은 `MainScene`**(MatchManager + 맵 + HUD). 게임 시작 시 `NetworkSceneManager.LoadScene` 으로 전 클라 동시 전환, 결과(RESULT) 후 대기방 씬 자동 복귀(구조물 정리·준비 초기화·닉네임 스냅샷 유지, 매치마다 새 맵 시드). 전역 시스템(NetworkManager/ConnectionService/오디오/설정/SceneFlow)은 씬에 두지 않고 **`Resources/NetworkRig` 프리팹을 부트스트랩이 앱 시작 시 1회 생성**(DontDestroyOnLoad) - 어느 씬에서 플레이를 시작해도 동작하고 씬 재로드 중복이 없다. 세션 종료(호스트 이탈 등) 시 SceneFlow 가 대기방 씬으로 복귀. 닉네임은 게임 씬에서 LobbyManager 부재 시 정적 스냅샷 폴백(PlayerPalette). | 필수(구현됨) |
| 규모 & 부하 검증 | Multiplayer Play Mode(가상 플레이어)와 빌드 다중 실행으로 다인원 테스트, 호스트 대역폭·지연 실측. | 필수 |
| 끊김 처리·재접속 | 클라 이탈 시 관전·재접속 유예. 호스트 이탈 시 매치 종료(호스트 마이그레이션은 선택). | 권장 |
| 전용 서버(DGS) 확장 | 대규모 시 헤드리스 Unity 전용 서버(KCLOUD VM 또는 Unity Multiplay)로 전환. NGO 코드 대부분 재사용. | 선택 |

### 2. 준비 페이즈 (구조물 설치, 매 라운드 전 반복)

| 구현 요소 | 설명 | 우선순위 |
|---|---|---|
| 구조물 설치 (ServerRpc + NetworkObject 스폰) | PREP 동안 자유 비행 고스트 카메라로 맵 볼륨을 날며 화면 중앙 조준 지점에 좌클릭 설치. **배치 큐 = 카트라이더식 아이템 슬롯 바(좌상단)**: PREP 시작 시 이번 라운드 지급량 전체(보이는 N + 투명 M)를 미리 굴려 **지금 설치할 것 = 큰 노란 슬롯, 이어질 순서 = 작은 크림 슬롯**으로 실물 썸네일(런타임 렌더) 표시. **[Alt]** 전환, **[1]/[2]** 보이는/투명 점프. 화면에는 슬롯 바와 "[Alt] 전환 · [F1] 도움말" 힌트 한 줄만 남기고, **조작 상세 설명은 전부 F1 도움말(설정 - 설명 탭)로 이동**. 회전은 3축 90도 스텝: **[R]** Y축, **[T]** X축, **[G]** Z축(블루프린트·실물 모두 회전된 바운즈로 바닥 정렬). `PlaceStructureServerRpc(pos,yaw,pitch,roll,type,invisible)` → 호스트 검증(PREP 한정·종류별 잔여 개수·볼륨 경계·겹침) 후 스폰. | 필수 |
| 구조물 (형태 8종 x 투명 플래그) | **투명은 형태가 아니라 속성이다**: 예전의 "투명 벽" 전용 타입은 폐지하고 `Structure.Hidden`(NetworkVariable) 플래그로 분리 - 어떤 형태든 투명일 수 있다(**투명 나무·투명 옷장**). 벽/원기둥/투명벽 타입과 프리팹은 삭제. **형태 8종은 실제 가구·자연물 에셋이고 크기 등급이 형태에 내장**(스케일 배율 폐지 - 옷장을 2배로 늘리면 비례가 깨진다): 소 = 쿠션(0.26m)·바위(0.53m)·그루터기(0.81m), 중 = 테이블(1.59m)·바위(1.95m)·서랍장(2.36m), 대 = 나무(4.22m)·옷장(5.60m). 형태 -> 등급/프리팹 매핑은 `MatchManager._structureDefs` 인스펙터 테이블(새 형태 = StructureType 값 하나 + 테이블 한 줄 + NetworkPrefabs 등록, 코드 수정 불필요). **배치 큐가 등급을 먼저 굴리고**(중 `_mediumChance` 15% / 대 `_largeChance` 7% / 나머지 소, 실측 3000회 77.4/16.3/6.4%) **그 등급의 형태 중 하나를 뽑는다** - 슬롯에 중/대 배지 표시. 투명 구조물의 등급 추첨은 `_invisibleCanBeBig` 로 on/off(끄면 투명은 항상 소). **형태는 PREP 시작 시 지급량 전체를 미리 굴려 배치 큐로 확정**(PrepClientUI 가 클라 로컬로 굴림 - 결과물은 서버 스폰·복제라 전 피어 일치 불필요, 블루프린트 프리뷰는 큐의 선택 형태와 1:1). 투명 구조물: PREP 에는 설치자에게만(반투명), 그 외 페이즈엔 아무에게도 안 보임, 플레이어 충돌 시 `revealDuration`(2초) 동안 전원 공개. 콜라이더는 항상 전원 동일(공정). | 필수 |
| 구조물 가시성의 두 함정 | ① **스폰 직후 누출**: Type/Hidden 은 Spawn 이후에야 서버가 쓰므로(스폰 전 쓰기는 리셋돼 유실) 클라에는 몇 프레임 뒤 도착한다. 그동안 렌더러를 켜 두면 투명 구조물이 잠깐 보여 함정 위치가 샌다 → `Type` 기본값을 `TypeUnknown`(255) 센티널로 두고 **값이 도착하기 전에는 전부 숨긴다**(보이는 구조물이 1~2프레임 늦게 나타나는 건 체감되지 않는다). ② **LODGroup 이 숨김을 되돌린다**: 나무·그루터기 에셋에는 LODGroup 이 있고 LODGroup 은 거리마다 자기 LOD 의 `renderer.enabled` 를 직접 켠다 - `enabled` 로 숨기면 다음 갱신에 도로 켜져 함정이 저절로 드러난다(실측: 숨김 상태의 투명 나무 LOD 렌더러가 `enabled=True` 로 복구됨) → 숨김은 **`renderer.forceRenderingOff`** 로 한다(`enabled` 를 건드리지 않아 LODGroup 과 싸우지 않음). 머티리얼 캐시는 렌더러당 **슬롯 배열**로 보관(`sharedMaterial` 단수로 다루면 서브메시가 1개로 뭉개진다). | 필수 |
| 라운드별 지급 개수 | **지급분 중 정확히 1개가 투명(함정), 나머지는 전부 보이는 구조물**(`MatchManager._invisiblePerRound` = 1). 총 개수는 호스트가 대기방에서 라운드별로 고르고(3/4/5개 프리셋), 미설정 시 인스펙터 기본값 `_structureAllowance` = {4,4,4}. 이월 없음(PREP 진입마다 사용량 리셋). 실측: R1 3개 → 보임 2 + 투명 1, R2 4개 → 보임 3 + 투명 1, R3 5개 → 보임 4 + 투명 1. | 필수 |
| 설치물 누적 | 설치한 구조물은 라운드 사이에 지우지 않고 3라운드 내내 누적(핵심 룰). 매치 종료 시에만 전체 despawn. | 필수 |
| 배치 프리뷰 | 화면 중앙 조준 지점에 반투명 블루프린트(실물 프리팹 복제) + 로컬 예비검증으로 초록/빨강 표시, [R]/[T]/[G] 3축 90도 회전 반영. **조준점(크로스헤어) UI 는 없다** - 설치 위치 피드백은 블루프린트가 실물 형태·색으로 더 정확히 전달하고, 등반 중에는 겨눌 대상 자체가 없다. | 권장(구현됨) |

### 3. 라운드 시스템 (클라이밍 단일 모드)

| 구현 요소 | 설명 | 우선순위 |
|---|---|---|
| 매치 상태머신 (NetworkBehaviour) | 호스트의 `MatchManager : NetworkBehaviour` 가 FSM(LOBBY → [PREP → PLAY → HIGHLIGHT → INTERMISSION] ×3, 마지막 라운드는 INTERMISSION 대신 RESULT) 구동. 룰렛/주제 없음. `NetworkVariable<Phase>` + 종료 타임스탬프로 전 클라 동기화. | 필수 |
| 랜덤 타워 맵 | 호스트 시드 1개를 `NetworkVariable` 복제 → 전 피어 결정론적 로컬 생성(NetworkObject 스폰 없음, late-join 자동 대응). 시작 섬 → 구간 기반 굽이 레인(청크 5개) → 도착 청크 합류 구조 - 상세는 6절 "맵" 행. 초기 설계(6×6×50 볼륨 슬라이스 살포)는 "길이 보이는 Only Up 스타일"을 위해 레인 방식으로 개편. | 필수 |
| 낙하 탈락 2규칙 (체력 시스템 폐지) | ① 공중 낙하: 낙하 거리(최고점 - 현재 y)가 `_lethalAirFall`(15) 이상이면 즉시 탈락 - 서버가 위치 샘플링으로 직접 추적. ② 착지 낙하: `_lethalLandFall`(7) 이상 낙하한 채 착지하면 탈락 - 접지는 소유자만 정확히 알므로 소유자가 `ReportFallServerRpc` 보고 후 서버 판정. 초기 설계의 은닉 체력(10)·낙하 데미지는 시작 섬 밖 전체가 낭떠러지가 되면서 폐지. | 필수 |
| 낙하 탈락/자동 부활 | 낙하 탈락(공중 낙하·착지 낙하 규칙) 시 잠깐 관전(입력 잠금, 본체 렌더·콜라이더 off, 좌클릭 대상 순환) 후 `_respawnDelay`(1.5초) 뒤 시작 섬에서 자동 부활한다. 페널티 = 등반 진행 손실(점수는 종료 시점 높이). 부활 텔레포트는 서버 낙하 추적 기준점을 리셋해 재탈락으로 오인되지 않는다. 전멸 조기 종료 규칙은 폐지(탈락이 일시 상태이므로). | 필수 |
| 라운드 종료 조건 | 타이머 만료(기본 180초, 대기방에서 호스트 조절) / 첫 정상(도착 청크) 도달 후 잔여 타이머를 `finishGrace`(15초)로 단축. 전원 사망 조기 종료는 자동 부활 도입으로 폐지. | 필수 |

### 4. 점수 시스템 (개편: 7항목 합산, [docs/archive/점수_시스템_개편안.md](docs/archive/점수_시스템_개편안.md) 반영)

라운드 점수 = **진행도 + 정상 도달 시간 + 청크 선착순 + 순위 + 안정성 + 투명 구조물 영향 - 반복 탈락 감점** (하한 0). 실시간 계산/동기화 없음: 라운드 중 호스트가 사실만 수집(`PlayerRuntime`/`MatchStatsTracker`)하고 `PLAY → HIGHLIGHT` 전환에서 순수 함수 `MatchScoring.RoundScore()` 1회로 계산(설정·공식은 `ScoringConfig`/`MatchScoring.cs` 단일 지점, 전부 인스펙터 조절). 복제는 기존 `RoundResult`(총점) 와이어 포맷 그대로, 항목별 내역은 호스트 콘솔 로그.

| 구현 요소 | 설명 | 우선순위 |
|---|---|---|
| 진행도 점수 | `heightScoreMax`(700) × (최고 높이 × 0.7 + 종료 시점 높이 × 0.3). 각 높이는 정상 높이로 정규화 후 `heightCurve` 적용. 최고 높이는 탈락·부활에도 유지(`PlayerRuntime.BestY`, 낙하 추적용 ApexY 와 별개), 정상 도달자는 최고 높이 만점 고정. 탈락자의 종료 높이 = 사망 지점. | 필수 |
| 정상 도달 시간 점수 | 도달자만: `topTimeScoreMax`(300) × 남은 시간 비율(`timeCurve`). 분모 = 최초 설정된 전체 라운드 시간(finishGrace 단축 무시), 도달 시각은 PLAY 시작 기준 실측(최초 도달만 인정, 이후 낙하해도 유지). | 필수 |
| 청크 선착순 보너스 | 맵 청크(레인 청크 1..5 + 도착 청크, `PlatformRegion.ChunkIndex` 시드 생성 순서라 전 피어 동일)마다 독립 판정: 도달 순번별 `chunkArrivalBonus`(40/20/10, 4번째부터 0). 상위 청크 진입 시 건너뛴 하위 청크도 도달 인정(구조물 지름길을 벌하지 않음). 플레이어별 청크당 최초 1회, 동시 도달은 서버 프레임·ClientId 순. | 필수 |
| 참가자 수 비례 순위 점수 | `rankScoreMax`(300) × (참가자 수 - 순위) / (참가자 수 - 1): 1위 만점, 최하위 0, 선형 배분. 1인 참가는 1위 취급(만점). 동점은 정상 도달 시각 → ClientId 순(기존 유지). | 필수 |
| 안정성 보너스 | 최고 높이 ≥ 정상의 `stabilityMinHeight01`(60%) + 탈락 0회 → `stabilityBonus`(150). 시작 지점 버티기는 높이 조건으로 배제. | 필수 |
| 반복 탈락 감점 | 첫 탈락 0, 두 번째 `secondDeathPenalty`(-10), 세 번째부터 회당 `extraDeathPenalty`(-20). 라운드 상한 `deathPenaltyCap`(100), 최종 점수 하한 `roundScoreMin`(0). | 필수 |
| 투명 구조물 영향 점수 | 상대가 내 투명 구조물 접촉 후 `baitWindowSeconds`(6초) 안에 탈락하면 설치자 `baitScore`(+40). 셀프 낚시 제외, 접촉 1회 = 탈락 1회 귀속, 같은 (설치자, 피해자) 쌍 라운드당 `baitRepeatLimit`(2회), 라운드 상한 `baitScoreCap`(120). 낚시왕 통계(RoundStats)와 집계 분리. | 필수 |
| 3라운드 누적·순위 | `NetworkList<RoundResult>`로 동기화, RESULT 에서 누적 총점 최종 순위. | 필수 |
| 실시간 리더보드 | 라운드 중 Top-N 표시(총점 기준). | 권장 |

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
| 맵 (구간 기반 레인) | 시드 결정론 로컬 생성(`ClimbMapGenerator`, 전 피어 동일·NetworkObject 없음). 레인 = 진입 발판 + **구간 체인 6유형** + 정상 발판: 발판 에셋은 **크기 티어 소/중/대**로 분리 관리(`Assets/Prefabs/Platforms/{Small,Medium,Large}` 폴더 = 생성기 `smallPlatforms/mediumPlatforms/largePlatforms` 배열과 1:1, 소 4/중 12/대 4종): ① **지그재그형**(발판마다 소 티어 랜덤, 좌우 교대 ±1.3~1.7) ② **수직 샤프트형**(소/중 티어 혼합 `shaftMediumChance` 35%, 상승 0.75~0.88 풀홀드 요구, 확률적 미등반 허용) ③ **가구 방형**(랜덤 사다리 소2→중2→대1, 윗면 간 `roomStepRise` 0.9, 관통 `roomOverlapTolerance` 0.3 초과 시 재배치) ④ **지름길형**(정직한 소 티어 우회 아치 `shortcutDetourWidth` 3.5 + 직선 단절 `shortcutGap` 4.8~5.6 - 구조물 1개를 놓으면 지름길이 열린다) ⑤ **대단절형**(`grandGap` 9~13m 큰 단절 - 구조물 1개가 ~3m 라 여러 플레이어의 구조물을 이어야 건넌다, 협동 게이트) ⑥ **랜드마크형**(대 티어 에셋 하나를 `landmarkScale` 2.2배로 - 초대형 단일 발판). 게이트(지름길/대단절)는 레인당 1회 강제·최대 2회, 가구 방·랜드마크는 레인당 1회. **한 청크 안에 같은 발판 에셋은 두 번 나오지 않는다**(중복 없이 뽑고 종수 소진 시 청크 조기 종료). 일자 길(계단형)·무너진 다리형·회전형(자전 대 티어)은 선택 풀에서 제외(코드 베이스는 보존, `useSectionLanes=false` 레거시와 함께). 진입/휴게 발판 = 중 티어. **모든 발판은 `tiltChance`(기본 100%) 확률로 무작위 각도로 기울어져 등장**(도착 청크 제외): 좌우 회전(yaw)은 0~360도 완전 무작위, 상하 기울기(pitch·roll)는 ±`tiltMaxAngle`(22도, 1도보다 잘게) - "난잡한" 배치를 위해 90도 스텝을 연속 각도로 대체. 틸트는 체인 앵커·바운즈 측정 전에 적용되어 회전된 포즈 기준으로 배치·간격이 계산된다. 생성 후처리로 **겹침 발판 정리**: 발판 AABB 가 세 축 모두 `overlapCullDepth`(0.25m) 이상 서로 관통하면 나중에 생성된 쪽을 삭제하되, 삭제로 레인 경로가 점프 불가가 되는 계단형 겹침은 예외적으로 유지(점프 포락선 기반 경로 보존 검사: 인접 이웃 직접 또는 겹침 상대 경유. 순수 기하 + 생성 순서 규칙이라 전 피어 동일). **공유 굽이 레인 배치**: 맵 시드에서 **청크 배열(유형 순서·步 수·다리 갭)과 청크별 진행 방향(±`chunkTurnMax` 20도, 누적 ±`headingClamp` 45도), 중앙 스파인 경계점을 한 번 뽑아 전 레인 공유** - 레인들은 스파인에서 수직으로 `laneSpacing` 만큼 평행 오프셋되어 같은 굽이를 그리므로 **같은 순번 청크 간 레인 폭이 정확히 유지**된다(실측 8.0m 전 경계 일치). 구성 에셋·상승·측면 배치는 레인별 랜덤("배열은 같고 내용물은 다르게"). 청크가 밴드보다 짧게 끝나면 소 티어 디딤돌(Link, `linkStepMax`)로 경계까지 잇고 휴게 발판을 정확히 경계에 놓는다. 청크는 **레인당 `chunksPerLane`(5)개 고정**(상승 예산 정책 폐지 - 맵 높이는 뽑힌 청크 구성에서 자연히 결정, 실측 12~22m 분포)이며 **레인 top 은 제각각**(다양성 우선). 맵 구조: **시작 청크**(시작 섬, 전 레인 공용) → 레인별 청크들 → **도착 청크 1개**(전 레인 공용 합류점: 스파인 끝의 진행 방향 정렬 큰 하얀 발판 `finishChunkSize` 10x10, 높이는 가장 높은 레인 끝 기준이며 각 레인이 디딤돌로 수평·수직 합류). 반투명 도달 볼륨 마커는 기본 꺼짐(`showFinishMarker=false`, 하얀 발판만 - 시각 연출은 추후 교체 예정, `finishMaterial`/마커 코드는 보존). **도달 판정은 높이가 아니라 도착 청크 위에 올라서는 것**(`IsAtFinish`: 발끝이 판 위 `finishDetectHeight` 2.5m 안) - 점수 정규화 기준 = 도착 청크 윗면(전원 동일). 대각 진행 시 단절 갭이 모서리 지름길로 줄어드는 것은 실측 보정으로 차단. **모든 발판은 에셋 기반**(티어 배열 미배선 시에만 기본 도형 슬래브 폴백). 지오메트리 `Ground` 레이어 + 투명 경계벽 유지. 랜덤 소비 규율: 공유 계획(배열/步 수/갭/굽이) = 맵 파생 시드 1회, 레인 지오메트리 = 레인 파생 rng, 도착 합류 디딤돌 = 합류 전용 파생 rng. 기존 균일 체인은 `useSectionLanes=false` 폴백. 검증: 시드 32종 전수 감사(점프 포락선·단절 수·틸트 적용률·청크 풀 구성) FAIL 1(물리 한계 내 경계 사례 1건). | 필수 |
| 캐릭터 컨트롤러 | `CharacterController` 기반 이동·점프, 지면·장애물 콜라이더. 물리는 Unity 내장(웹의 커스텀 키네매틱 대체). **가변 점프**: 스페이스를 누른 시간에 비례해 dy가 `jumpHeightMin`(0.45, 탭)에서 `jumpHeight`(1, 꾹)까지 - 상승 중 키를 떼면 상승 속도를 최소 점프 수준으로 자른다. 시작 배치: 접속·라운드 시작 시 시작 섬 위에 Z축 줄 세우기 + 레인 진행 방향(+X)을 보고 시작. | 필수 |
| 카메라 (마우스룩 조준) | 전 페이즈 3인칭 센터 뷰 마우스룩: 카메라가 플레이어 정후방(`aimShoulder` 0)이라 **플레이어와 조준 방향이 모두 화면 정중앙**에 온다. 카메라 기준 높이 `aimHeight`(1.1)를 낮춰 시점이 살짝 아래를 향하고 화면에서 플레이어가 위쪽에 온다. 마우스=시선, WASD 는 카메라 기준 이동, 몸은 카메라 방향. 조준 레이(`AimRay`/`AimPoint`)는 구조물 설치가 쓰는 내부 계산으로만 남고 화면에 조준점은 그리지 않는다. Ground 레이어 SphereCast 오클루전, 커서 잠금(Esc 토글). 어깨 오프셋은 `aimShoulder`(양수 = 오버숄더)로 조절 가능, 후방추적 시점은 `useAimView=false` 로 보존. | 필수 |
| UI (uGUI 코드 생성) | 프리팹 없는 코드 생성 HUD: 페이즈 배너·타이머·누적 점수판·현재 높이·탈락/관전 배너·하이라이트 카드·최종 결과·머리 위 이름표(**화면 중앙 조준점 없음**). **이름표는 머리 바로 위에 붙는다**: 텍스트 렉트의 피벗을 밑변(0.5, 0)에 두고 루트 + `_nameplateHeight`(1.0m, SerializeField) 지점에 밑변을 투영 - 실측 기준 시각적 머리 꼭대기(루트 +0.8m) 위 0.2m. 점프 스트레치(순간 최대 +1.2m) 정점의 0.1초가량만 글자가 머리에 살짝 걸치는 것은 "바로 위" 우선의 의도된 트레이드오프. 주의: 스크립트 기본값만 바꾸면 열려 있던 씬의 도메인 리로드가 이전 기본값을 in-memory 직렬화로 붙들고 있을 수 있다 - 씬 컴포넌트에 값을 명시하고 저장해야 확실하다(실측으로 확인). PREP 은 구조물 종류/잔여 개수 패널. **공용 폰트**: SOYO 메이플 볼드(`Assets/Resources/Fonts/SoyoMaple`, 둥근 게임체)를 프로젝트에 번들해 uGUI(GameHUD)·IMGUI(로비/PREP/설정) 전부에 적용 - 빌드/전 피어 동일 보장, 누락 시 OS 한글 폰트 폴백. **공용 스타일 키트(`UiKit`)**: 얼티밋 치킨 호스 풍 디자인 시스템(크림 패널 + 잉크 테두리/텍스트 + 채도 높은 포인트 컬러 8종) + 라운드 코너/테두리 9-slice 스프라이트·텍스처(런타임 생성, 색 조합별 캐시) + 버튼 3상태(기본/호버/눌림) 텍스처. 타이틀/방 참가/대기방(LobbyUI)은 UCH 스타일 적용 완료: 살짝 기울어진 컬러 배너 헤더(노랑/청록/파랑), 컬러 버튼(방 만들기 빨강·참가 청록·준비 초록·시작 빨강), 흰 입력 필드. **닉네임 한글 입력(IME)**: Input System 백엔드는 IME 를 기본으로 꺼 둔다(게임은 키를 조작으로 쓰므로 합리적 기본값) - 그 탓에 한/영 키를 눌러도 조합이 시작되지 않아 한글만 안 쳐졌다(영문은 조합이 없어 그냥 입력됨). `Keyboard.SetIMEEnabled` 로 **닉네임 화면(Title)에서만** 켜고 그 외에는 끈다: 참가 코드/IP/포트는 전부 ASCII 라 IME 가 켜져 있으면 오히려 방해되고, 게임 중에는 WASD·점프가 조합에 먹힌다. 레거시 `Input.imeCompositionMode` 는 쓰지 않는다(Active Input Handling 이 Input System 전용이라 빌드에서 예외 위험). 후보 창 위치는 `SetIMECursorPosition` 으로 입력 칸 아래에 붙인다(`GUIToScreenPoint` 로 ImguiScale 의 GUI.matrix 보정). **닉네임 TextField 에 maxLength 인자를 주지 않는다** - IMGUI 의 maxLength 는 조합 중인 글자까지 길이에 넣어 잘라내서 한글 조합을 깨뜨린다(길이 제한은 조합이 끝난 결과에만 적용). 닉네임은 한글 12자 = UTF8 36바이트로 `FixedString64Bytes`(61바이트) 한도 안(실측). **F1 = 설정/설명 탭 패널**(SettingsManager, UCH 크림 스킨, 660x700): 설정 탭(감도/볼륨/창 모드) + 설명 탭(기본 조작·구조물 설치·투명 구조물·점수 요약 - PREP 화면의 상세 안내를 전부 이관). 설명 탭의 조작 안내는 줄글이 아니라 **[키캡] + 설명 2단**(`KeyRow`, 키 열 폭 고정 168 → 설명이 세로 정렬)과 **섹션 헤더 + 구분선**(`Section`)으로 그려 필요한 키 하나를 훑어 찾을 수 있다. MatchManager 디버그 패널은 기본 꺼짐(showDebugGui, 개발 시에만 켬). **게임 HUD 도 UCH 스타일**: 상단 = 기울어진 페이즈 컬러 배너(준비 노랑/플레이 청록/하이라이트·대기 파랑/결과 빨강) + 크림 알약(라운드·타이머, 10초 이하 빨간 경고), 점수판 = 크림 패널 + 잉크 텍스트 + 1등 ★ + 로컬 노란 하이라이트(인원 비례 높이), 플레이 정보 = 크림 알약, 탈락 배너 = 빨간 스트립, 하이라이트/최종 결과 = 크림 카드 + 컬러 헤더 배너(파랑/빨강, 1등 노란 행·크림 줄무늬), 카드 화면에서는 이름표 숨김. **주의: 이 URP 구성에서 오버레이 캔버스의 반투명은 3D 월드 위에서 블렌딩되지 않는다**(uGUI 패널 위 반투명은 정상) - 월드 위 요소는 불투명으로, 전체 화면 딤은 사용하지 않음(GameHUD 주석 참조). **IMGUI 글자색은 상태별로 못 박는다**(`UiKit.WithTextColor`): 기본 스킨은 호버 시 글자를 흰색으로 바꾸는데, 라벨 스타일을 토글처럼 호버가 있는 컨트롤에 넘기면 크림 패널 위에서 글씨가 사라진다 - 라벨/키캡은 잉크 고정, 버튼은 채색 위 흰색 고정. 플레이 정보 알약(현재 **높이·등수·생존 수**)은 상단 라운드/타이머 패널 바로 아래에 배치, 목표 문구는 라운드 시작 4초만 화면 하단에 표시(시야 확보). 로비 UI 는 배경 딤을 낮춰(타이틀 0.45/대기방 0.30) 스테이지·스카이 배경이 비친다. | 필수 |
| 캐릭터 (스틱맨) | 휴머노이드 스틱맨 모델(PolyOne Free Stickman)을 Player 프리팹에 네스티드 프리팹으로 부착. 1D 블렌드트리(Speed: Idle 0 / Walk 2 / Run 6) + Airborne(Grounded) 전이, `PlayerAnimDriver` 가 transform 프레임 차분으로 속도를 추정해 원격 플레이어도 팔다리가 움직인다. `PlayerTint` 로 플레이어별 색 적용. | 필수(구현됨) |
| 캐릭터 juice (`PlayerJuice`) | 폴가이즈식 통통함: **스쿼시&스트레치**(도약 시 세로로 늘어남, 착지 시 납작→언더댐프 스프링으로 튕겨 복원) + **착지 먼지 파티클**(`Prefabs/Env/Dust`, 낙하속도 비례 크기, 자동 소멸). 스케일은 자식 `Model` 트랜스폼에만(콜라이더/CC·판정 불변, 피벗이 발이라 발 붙인 채 눌림/늘어남). 수직속도=transform 차분, 접지=소유자 CC / 원격 레이캐스트 → **전 피어 공통 동작**(원격 플레이어의 juice·먼지도 보임). 스프링 강성/감쇠·과장(`_scaleAmount`)·임펄스 전부 인스펙터 조절. | 권장(구현됨) |
| 맵 장식 에셋 (가구) | Furniture Mega Pack 을 등반 맵("거대한 방" 테마)용으로 선별 도입: 테이블 8·의자 6·소파 5·침대 8·옷장 6·서랍장 6·쿠션 8·주방 20(냉장고·캐비닛 등)·욕조 7 = 프리팹 74종. 원본 1.7GB 를 노멀/메탈릭맵 제거 + 알베도 1K 축소로 40MB 로 경량화(스타일라이즈드 룩 기준 시각 손실 없음, URP/Lit). 이 중 8종은 발판 래퍼로 맵 생성 풀에 사용 중(위 '맵' 행), 나머지는 장식·구조물 후보. | 권장(사용 중) |
| 배경·분위기 | 커스텀 그라데이션 스카이박스(`RouletteParty/SkyGradient`): 상/지평선/하 3색 보간 + **태양 원반·글로우**(크기/색/halo 는 머티리얼 슬라이더). 태양 방향은 `SunSync`(디렉셔널 라이트 부착, ExecuteAlways)가 라이트 방향을 스카이 머티리얼에 동기화 - 라이트를 돌리면 해가 따라 움직인다. **라운드 진행 = 하루의 흐름(`DayCycleController`, 게임 씬)**: 라운드 1 정오(중천 65도, 쨍한 파란 하늘) → 라운드 2 오후(해 지기 4시간 전, 38도, 웜 크림) → 라운드 3 골든아워(해 지기 1시간 전, 16도, 노을 세트). 라운드가 바뀌면 하늘 3색/태양/라이트/포그/앰비언트를 4초간 부드럽게 보간(프리셋·전환 시간은 인스펙터 배열). 스카이는 원본(`M_Sky`)을 복제한 런타임 인스턴스에 써서 에셋을 오염시키지 않고, 라운드 폴링이라 늦게 합류해도 현재 라운드 하늘로 수렴. **대기방 씬은 해 없는 노을 하늘**(`M_Sky_Lobby`: 태양 색 검정 = 가산이라 제거). 해는 -X 하늘(등반 기준 뒤쪽)이라 게임에서는 순광 + 라운드가 갈수록 길어지는 그림자. 대형 구 방식 대신 스카이박스를 쓴 이유 = 깊이 무한대(far plane 에 안 잘림)·컬링/추적 불필요·포그와 자연스러운 혼합(셰이더 주석에 기록). 하늘 입체감은 **저폴리 구름**(`Prefabs/Env/Cloud`, 콜라이더 없음/그림자 끔) 로비 4개·게임 7개. 선형 포그(로비 35~120m / 게임 60~220m). 대기방 씬은 우드 톤 스테이지(`M_LobbyStage`)에 나무/버섯/바위 장식으로 타이틀 배경을 겸한다. | 필수(구현됨) |
| 포스트프로세싱 (URP Volume) | **공용 PostFX 프로파일**(`Assets/Settings/PostFX.asset`, 두 씬 Global Volume 공유): Bloom(threshold 0.85·강도 1.0, 해·하늘 발광) + Tonemapping(ACES) + Color Adjustments(노출 +0.12·대비 +8·채도 +14 → 쨍한 파스텔) + Vignette(0.28, 시선 집중) + Depth of Field(Gaussian, 55~140m 원경만 소프트). 두 씬 Main Camera 후처리 ON + SMAA(High). **SSAO**(PC_Renderer 렌더러 피처, Intensity 0.4→0.85·Radius 0.3→0.5로 강화 - 접지·크레비스 음영으로 "붕 뜬" 느낌 완화) + URP MSAA 4x(저폴리 에지). 파라미터는 PostFX 프로파일에서 조절. | 권장(구현됨) |
| 사운드 (`AudioManager`) | 전역 사운드 매니저(`Resources/NetworkRig` 프리팹에 상주, DontDestroyOnLoad). SFX 9종(점프·착지·설치·투명 발동·탈락·라운드 시작·종료·최종결과·**UI 클릭**) + 로비/플레이 BGM 크로스페이드 슬롯. `AudioManager.Play(Sfx.X)` 정적 호출 한 줄(인스턴스/클립 없으면 무음), 라운드 스팅어·BGM 은 매니저가 페이즈를 폴링해 자동 처리. 볼륨은 `SettingsManager`(마스터=AudioListener, SFX/BGM 개별) 연동. **음원 = Kenney(CC0, 출시 안전)**: `Assets/Audio/SFX/` 에 슬롯당 1클립 선별 임포트(팩 전체가 아니라 사용분만 - 리포 경량). 점프·설치·UI 클릭은 별도 선정 클립으로 교체(원본 앞 무음은 트림해 입력과 동시에 소리남): 점프=freesound 398256, **설치=freesound CC0 pack 27878(robbeman, 나무 위 머그 내려놓기) 2종을 무작위 재생**(0.16s 단단한 나무 쿵, -1.5dB 노멀라이즈), UI 클릭=freesound 345983(0.11s). 착지는 무음(요청). 나머지는 Kenney: 발동=interface `bong`, 탈락=digital `lowThreeTone`, 시작/종료/결과=music-jingles `Pizzicato/Steel/Sax`. **반복 기계감 완화**: 설치는 클립 2종 무작위(`_placeVariants` 배열 - 개수 자유 확장), 점프·설치는 재생마다 피치 0.95~1.06 랜덤. **UI 버튼 클릭음**: LobbyUI·SettingsManager 의 모든 IMGUI 버튼을 `Clk()` 래퍼로 감싸 클릭 프레임에 `Sfx.UIClick` 재생. 트리거는 게임플레이 코드에 배선 완료(설치·발동·탈락은 전 클라 재생). **BGM**: `Assets/Audio/BGM/bgm_main.mp3`(Pixabay 무표기·상업무료 앰비언트, Streaming+Vorbis 임포트)를 메인 테마로 로비/등반 두 슬롯에 배선 → 전 페이즈 끊김 없이 이어짐(같은 클립이라 페이즈 전환 시 재시작 없음). 등반용 별도 곡을 넣고 싶으면 `_bgmPlay` 만 교체, 등반을 무음으로 하려면 비우면 된다. 확장 후보: 3D 공간 음향 + 전원 재생, AudioMixer 그룹·더킹, 높이 비례 바람 앰비언스. | 권장(구현됨) |
| 아트·이펙트 | 이모트, 낙사 파티클 등(숏폼용 '웃긴 장면'). | 권장 |

### 7. 기술 스택 · 빌드 · Steam 출시

| 구현 요소 | 설명 | 우선순위 |
|---|---|---|
| Unity 6 LTS + NGO | Unity 6 LTS(6000.x) + `com.unity.netcode.gameobjects`. 언어 C#. | 필수 |
| Steamworks 연동 | Facepunch.Steamworks(App ID·로비·친구·업적) + Facepunch Transport. 초기엔 개발용 App ID 480(Spacewar)로 테스트. | 선택(출시 단계) |
| 프로젝트 구조 | 단일 Unity 프로젝트. `Assets/Scripts/{Core, Net, Match, Map, UI, Audio}` (네임스페이스 `RouletteParty.*` 로 통일). asmdef 어셈블리 분리는 규모 증가 시 도입. | 필수 |
| 버전관리 | Git, Unity `.gitignore`, `.meta` 커밋 규칙. 대용량 에셋 팩은 LFS 대신 **선별 + 텍스처 축소 후 커밋**(가구 팩 1.7GB→40MB 사례). | 필수 |
| 빌드 & 출시 | Windows·**macOS(Universal: Intel+Apple Silicon)** 빌드 → Steamworks 파트너 depot 업로드. 스크립팅 백엔드는 Mono(Windows 에디터에서 macOS 크로스 빌드가 되는 조건 - IL2CPP 는 Mac 실기 필요). macOS 산출물은 미서명이라 배포 시 수신자가 `xattr -cr` + `chmod +x Contents/MacOS/*` 후 우클릭 열기(zip 은 UTF-8 파일명 도구로 - 탐색기 기본 압축은 한글명이 깨진다). Steam Direct(앱 수수료 $100·30일 대기·심사), 상점 페이지·Coming Soon. | 필수(빌드 구현됨) |
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
| 전송 | Unity Relay(참가 코드) 기본 + LAN 직접 IP 폴백 → Facepunch/Steam(출시) | NAT 우회로 이종 네트워크 접속, 교체 지점은 `ConnectionService` 한 곳 |
| 물리·이동 | CharacterController + Unity Physics | 플랫포머 이동에 충분, 콜라이더 공유 |
| 카메라·UI | 커스텀 마우스룩 카메라 + uGUI(코드 생성 HUD) | 조준 시점 요구사항 직접 구현·프리팹 없는 HUD |
| 버전관리 | Git (대용량 에셋은 선별·축소 후 커밋) | 무료 플랜 LFS 한도 회피, 클론만으로 실행 가능 유지 |
| 배포 | Steamworks (Steam Direct) | 스팀 출시 |

---

## 아키텍처

**Unity + Netcode for GameObjects 호스트 권위** 구조. 한 명이 호스트(서버 겸용)로 게임 원장을 소유하고, 나머지가 접속한다. 전송 계층은 **Unity Relay(참가 코드) 기본**이며, `ConnectionService._useRelay` 를 끄면 LAN 직접 IP 접속으로 폴백한다. 출시 단계에 Steam(Facepunch) 릴레이로 전환한다(교체 지점은 `ConnectionService` 한 곳).

```
 [Unity 클라이언트 x N]                          [호스트 (서버+클라 겸용)]
  로컬 이동·렌더·입력       --ServerRpc-->         MatchManager (NetworkBehaviour)
  (맵·구조물·캐릭터)        <-NetworkVariable/RPC-  페이즈·점수·탈락 판정 (권위)
  UI (준비/HUD/결과)        <-NetworkTransform---   NetworkObject 복제 (플레이어·구조물)
        |                                                  |
        +--- UnityTransport + Unity Relay (참가 코드, 기본) -+
             폴백: LAN 직접 IP · 출시 단계: Steam 릴레이
```

- 호스트가 단일 권위. `NetworkVariable`(자동 동기화 상태) + `ServerRpc`/`ClientRpc`(요청·이벤트) + `ClientNetworkTransform`(이동)으로 구성.
- 대규모 확장 시 호스트 대신 헤드리스 Unity 전용 서버(DGS)로 교체(NGO 코드 재사용).

**프로젝트 구조** (단일 Unity 프로젝트)

- `Assets/Scenes`: `LobbyScene`(타이틀·대기방) / `MainScene`(게임)
- `Assets/Scripts/Net`: 접속 창구(`ConnectionService`, Relay/LAN)·플레이어 컨트롤러(이동/비행/관전)·색상·애니메이션
- `Assets/Scripts/Match`: 매치 FSM(`MatchManager`)·로비(`LobbyManager`)·점수(`MatchScoring`)·구조물(`Structure`)·준비 페이즈 설치 UI
- `Assets/Scripts/Map`: 랜덤 타워 생성(`ClimbMapGenerator`, 시드 결정론)
- `Assets/Scripts/UI`: 코드 생성 HUD·로비 UI·공용 스타일 키트(`UiKit`)·플레이어 팔레트
- `Assets/Scripts/Core` / `Audio`: 전역 부트스트랩(`SystemsBootstrap`)·설정(F1)·오디오
- (참고) `web-prototype/`: 초기 웹 개념검증 프로토타입 (설계 검증용, 프로덕션 아님)

---

## 설계 문서

> 프로젝트 성격에 따라 필요한 항목만 작성

### 화면 / 인터페이스 설계

씬 2개 구성. **LobbyScene**(자유 커서): 타이틀(닉네임, 한글 가능) → 방 만들기(참가 코드 발급)/방 참가(코드 입력) → 대기방(참가자 목록·준비·라운드별 시간/구조물 개수 설정·[표준 모드]·게임 시작). **MainScene**(마우스룩 시점·커서 잠금, 3D 씬 위 UI 오버레이): 준비(자유 비행 카메라, 좌상단 슬롯 바 배치 큐 - [Alt] 전환·[1]/[2] 점프, [R]/[T]/[G] 3축 회전, 화면 중앙 조준 지점에 좌클릭 설치 - 위치 피드백은 블루프린트 프리뷰) → 플레이(기울어진 페이즈 컬러 배너·라운드·타이머, 높이·등수·생존 알약, 머리 위 이름표, 탈락 시 관전 배너 후 자동 부활) → 하이라이트(라운드 우승자·Top3 카드) → 결과(누적 최종 순위) → 대기방 자동 복귀. 우상단 누적 점수판 상시 표시, [F1] 설정/설명 탭, [Esc] 커서 잠금 해제. 화면 중앙 조준점(크로스헤어)은 없다.

### 데이터 구조

라이브 상태는 호스트가 소유하고 NGO 로 동기화한다.

- 플레이어: `NetworkObject` + `ClientNetworkTransform`(위치). 낙하 추적 상태(ApexY 등)·탈락 위치·라운드 통계는 서버 전용(복제 안 함)
- 구조물: `NetworkObject` 프리팹 `{ type(가구·자연물 형태 8종), ownerId, hidden(투명 플래그 - 형태와 독립), revealUntil }` 3라운드 누적
- 맵: `NetworkVariable<int> MapSeed` 1개(전 피어 결정론적 로컬 생성 - 시작 섬 + 구간 기반 굽이 레인 + 도착 청크)
- 매치: `NetworkVariable<Phase>`(LOBBY|PREP|PLAY|HIGHLIGHT|INTERMISSION|RESULT), `round(1~3)` + 로비 `NetworkList<LobbyPlayerState>`(닉네임·준비)
- 점수: 라운드당 7항목 합산(4절 참조, 공식은 `MatchScoring.RoundScore` 단일 지점), 3라운드 누적. `NetworkList<RoundResult>`(총점 와이어 포맷)

### 네트워크 계약 (NGO)

외부 API 는 사용하지 않는다. 통신은 NGO 의 RPC·NetworkVariable 로 이뤄진다.

| 종류 | 이름 | 방향 | 설명 |
|---|---|---|---|
| ServerRpc | `PlaceStructureServerRpc(pos,yaw,pitch,roll,type,invisible)` | C→H | 구조물 설치 요청(호스트가 검증 후 스폰, 3축 90도 스텝 회전, invisible = 투명 함정 여부) |
| ServerRpc | `ReportFallServerRpc(fallHeight)` | C→H | 착지 낙하高 보고(서버가 낙하 탈락 규칙 ② 판정 + 통계 기록) |
| ServerRpc | `RevealStructureServerRpc(ref)` | C→H | 보이지 않는 구조물과의 충돌 보고(전원 일시 공개) |
| Rpc(Single) | `TeleportPlayerRpc(pos)` | H→소유 클라 | 라운드 시작 바닥 배치(소유자가 스스로 텔레포트) |
| Rpc(Single) | `EliminatedRpc()` | H→소유 클라 | 탈락 통보(입력 잠금 + 관전 전환) |
| NetworkVariable | `Phase / Round / PhaseEndTime / AliveCount / RoundWinner / MatchWinner / MapSeed` | H→C | 페이즈·타이머·승자·맵 시드 동기화 |
| NetworkList | `Results` (라운드별 순위·점수 행) | H→C | 점수판·하이라이트·결과 UI 데이터 |
| NetworkVariable | 구조물 `Type / OwnerId / Hidden / RevealUntil` | H→C | 구조물 상태(가시성 규칙의 근거. Type 기본값 255 = 도착 전 전부 숨김) |
| NetworkTransform | 플레이어 위치 | 소유 클라→전체 | ClientNetworkTransform(소유자 권위) 보간 |

(C=클라이언트, H=호스트)

낙하 추적 상태·탈락 위치·항목별 점수 내역은 서버 전용으로만 존재하며 복제하지 않는다. 구조물 가시성은 복제된 `Hidden/RevealUntil` 과 현재 페이즈로 각 클라가 로컬 판정하며, 콜라이더는 항상 전원 동일하다(물리 공정성). 가시성·설치 검증 상세 규칙은 위 구현 명세서 2절 참조.

---

## 산출물 및 실행 방법

- **산출물 설명:** Unity 기반 실시간 멀티플레이어 수직 클라이밍 파티 게임 「얼렁뚱탑」(Steam 출시 목표. '얼렁뚱땅'과 '탑'의 합성어로, 얼렁뚱땅 쌓은 구조물 사이에 거짓말이 섞이는 게임성을 이름에 담음. 빌드 제품명 productName 도 동일). 초기 웹 프로토타입으로 설계·룰 검증 후 Unity 로 프로덕션, 이후 클라이밍 컨셉으로 전환.
- **실행 환경:** Unity 6 LTS(6000.x), Windows·macOS(Universal) 빌드. 멀티플레이는 **Unity Relay 참가 코드 접속이 기본**(서로 다른 네트워크에서도 연결, UGS 익명 로그인 자동). LAN 직접 IP 접속 폴백은 [docs/LAN_테스트_가이드.md](docs/LAN_테스트_가이드.md).
- **실행 방법:** 아래 "실행 방법" 참조(타이틀 → 방 만들기/참가 코드 → 대기방 → 게임 시작).
- **시연 영상 / 이미지:** (선택)

### 실행 방법

```text
# Unity 프로젝트 (프로덕션)
1. Unity Hub 에서 Unity 6 LTS(6000.x) 설치 후 프로젝트 열기 (NGO 등 패키지는 manifest 로 자동 설치)
2. Play(어느 씬이든 전역 부트스트랩이 동작, 기본은 LobbyScene) → 닉네임 입력 → [방 만들기]
   → 대기방 상단에 표시된 6자리 참가 코드 확인
3. 다른 인스턴스(빌드 실행 또는 Multiplayer Play Mode 가상 플레이어)에서 [방 참가] → 참가 코드 입력
   (Unity Relay 라 서로 다른 네트워크에서도 접속됨. LAN 직접 IP 접속은 docs/LAN_테스트_가이드.md)
4. 전원 [준비하기] → 호스트가 라운드별 시간·구조물 개수 설정(또는 [표준 모드]) 후 [게임 시작] → 3라운드 → 결과 → 대기방 자동 복귀
5. 빌드: File > Build Profiles (Windows 또는 macOS Universal, LobbyScene·MainScene 포함) → 빌드 다중 실행으로 멀티 테스트
   (macOS 는 미서명: 받는 쪽에서 xattr -cr 얼렁뚱탑.app && chmod +x 얼렁뚱탑.app/Contents/MacOS/* 후 우클릭 열기)
6. (출시 단계) Facepunch.Steamworks + Facepunch Transport 전환, Steamworks depot 업로드

# 초기 웹 개념검증 프로토타입 (참고용, web-prototype/)
cd web-prototype && npm install -g pnpm && pnpm install && pnpm dev   # http://localhost:5173
```

### 기술 구성

| 분류 | 사용 기술 |
|---|---|
| 엔진 / 언어 | Unity 6 LTS, C# |
| 네트워킹 | Netcode for GameObjects (호스트 권위) |
| 전송 | Unity Relay(참가 코드, 기본) + UnityTransport LAN 직접 IP 폴백 → Facepunch/Steam(출시 단계) |
| 물리 / 카메라 / UI | CharacterController, 커스텀 마우스룩 카메라, uGUI(코드 생성 HUD + UiKit 스타일 키트) |
| 버전관리 / 배포 | Git(대용량 에셋은 선별·텍스처 축소 후 커밋), Steamworks (Steam Direct) |
| 초기 검증 (웹) | TypeScript, Socket.IO, Three.js (web-prototype/, 참고용) |

---

## 회고 문서

> [KPT 방법론 참고](https://velog.io/@habwa/%EB%8B%A8%EA%B8%B0-%ED%94%84%EB%A1%9C%EC%A0%9D%ED%8A%B8-%ED%9A%8C%EA%B3%A0-KPT-%EB%B0%A9%EB%B2%95%EB%A1%A0)

### Keep: 잘 된 점, 다음에도 유지할 것

- 초기에는 엔진 없이 간단한 기능만을 구현하려 했으나, 한계를 빠르게 파악하고 유니티 엔진을 사용했다.
- 초기에는 완성된 3D모델을 사용하지 않고 간단한 구조물만을 사용해 빠르게 구조를 확인할 수 있었다.
- Unity MCP를 사용해 Claude Code 등을 유니티 환경에서 사용할 수 있었다.
- 게임개발의 특징을 고려해 명세서 등의 문서를 작성하는 데에 많은 시간을 사용하지 않고 유동적으로 기능을 구현했다.

### Problem: 아쉬웠던 점, 개선이 필요한 것

- 기한을 고려해 어떤 기능을 구현할지를 잘 정해야 한다.
- 이번에는 단순한 3D 에셋만을 사용해 최적화가 크게 중요하지 않았으나, 추가 개발로 인하여 게임의 규모가 커지면 메모리/응답 시간 등의 최적화가 필요할 것으로 사료된다.
- 이번에는 기본적인 물리엔진을 사용했는데, 게임의 특성을 고려한 엔진을 사용하지 않아 부자연스러운 동작(경사로에서 자연스럽게 올라가지지 않음)이 발견되었다.

### Try: 다음번에 시도해볼 것

- 현재는 최소한의 구조물만 구현되었지만, 이후에 추가 구조물(예: 스프링 등의 고유한 특성을 지닌 함정)들을 추가해보고 싶다.
- 현재는 플레이어간의 상호작용이 단순히 밀쳐지는 것 이외에는 없는데, 이후에 잡기 등의 플레이어 상호작용을 추가해보고 싶다.
- 현재 프로젝트는 부족한 점이 많지만 가능성을 느껴 이후 발전시켜 Steam에 정식으로 출시해볼 계획이다.

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
