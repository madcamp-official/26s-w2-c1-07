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

- **산출물 주제:** 다인원 파티 플랫포머 게임 (가칭: 룰렛 파티), 유저가 함정을 배치한 3D 맵에서, 룰렛이 정해주는 주제로 다수의 유저가 경쟁하는 실시간 게임
- **제작 목적:** MECCHA CHAMELEON, Fall Guys에서 착안한 "다수의 사람을 한 맵에 넣자"는 아이디어를 실시간 인터랙션 기술로 구현. 우스꽝스러운 장면(숏폼 적합)과 유저 제작 맵 기반의 반복 플레이 가치를 추구
- **선택 옵션:** 실시간 인터랙션
- **핵심 구현 요소:**
  - 다수 유저의 위치·상태 실시간 동기화 (3D 맵 내 동시 플레이)
  - 준비 페이즈: 장애물 배치(투명 벽 등) + 주제 투표 → 투표율 기반 룰렛
  - 라운드 시스템: 주제별 게임 모드 3라운드 진행, 클리어 점수 + 장애물 영향 점수 누적
  - 라운드 종료 후 가장 임팩트 있는 장면 하이라이트 리플레이
- **사용 / 시연 시나리오:** 유저들이 한 맵에 입장 → 장애물 배치·주제 투표 → 룰렛으로 주제 결정 → 3라운드 플레이(목표 지점 도달, 오래 버티기 등) → 라운드마다 하이라이트 장면 공유 → 누적 점수로 최종 순위 발표
- **팀원별 역할:**

### 개발 일정

> **재편(Day 2 종료 시점):** 코어 게임 루프 구현이 계획을 크게 앞질러, 당초 Day 2~5로 잡았던
> 네트워킹·지형·매치·준비 페이즈·점수를 **Day 2 하루에 모두 완료**했다. 이에 남은 일정을
> **맵 디자인·캐릭터 디자인(아트)에 집중**하도록 재구성한다. 코드는 골든 패스가 이미 돌아가므로,
> 이제 게임의 '완성도·재미·비주얼'이 병목이다.

| 날짜 | 목표 | 상태 |
|---|---|---|
| Day 1 | 기획·셋업·웹 프로토로 설계 검증·Unity 전환 | ✅ 완료 |
| Day 2 | **코어 전부**: NGO 접속·이동 동기화 + 산·강 지형 + 매치 FSM + 게임 모드 3종(레이스/높이/생존) + 사망/부활 + 준비 페이즈(장애물·투표·가중 룰렛) + 상세 점수(클리어 70 : 장애물 30, 3라운드 누적) | ✅ 완료 |
| Day 3 | **게임 UI 전체**(상단 HUD·스코어보드·룰렛 연출·하이라이트 카드·최종 결과 화면) + 이름표·플레이어 색 + 3라운드 통합 → **골든 패스 완주 가능한 플레이어블 빌드**. 이후 **맵 디자인 착수**(ProBuilder) | ▶ 진행 |
| Day 4 | **맵 디자인 집중**: 코스 레이아웃·난이도 밸런싱·장애물 배치존·시각 테마(초원/강/설산 구간) | ⬜ |
| Day 5 | **캐릭터 디자인 집중**: 저폴리 캐릭터 모델·머티리얼·기본 애니메이션(이동/점프)·이모트, 색상 커스터마이즈 | ⬜ |
| Day 6 | 사운드·이펙트(낙사 물보라 등 파티클)·폴리싱·다인원 플레이테스트·밸런싱 | ⬜ |
| Day 7 | Windows 빌드·시연 영상·회고(KPT)·(여유 시 Facepunch/Steam 트랜스포트 연동) | ⬜ |

---

## 구현 명세서

이 문서는 **Steam 출시를 목표로 Unity 엔진(Netcode for GameObjects + Steam)** 기준으로 작성한 구현 명세서다. 게임 설계(페이즈 흐름·게임 모드·점수식·장애물 규칙)는 엔진과 무관하므로 [docs/기획서.md](docs/기획서.md)와 동일하게 유지하고, 여기서는 그 설계를 Unity 로 구현하는 방법을 규정한다. 우선순위는 **필수**(출시 MVP 코어) / **권장**(완성도) / **선택**(확장)으로 표기한다.

> **엔진 전환 노트:** `packages/` 의 웹(Three.js + Node/Socket.IO) 코드는 설계·룰을 빠르게 검증한 1차 개념 검증 프로토타입이며, 프로덕션 빌드는 Unity 로 진행한다. 웹에서 검증된 규칙(점수 클리어 70 : 장애물 30, 3라운드 누적, 룰렛 직전 주제 제외, 장애물 1인 3개, 강물 낙사 등)은 그대로 이식한다.

### 1. 네트워킹 & 상태 동기화 (Netcode for GameObjects)

| 구현 요소 | 설명 | 우선순위 |
|---|---|---|
| NGO 호스트 권위 모델 | Netcode for GameObjects 채택. 한 명이 **호스트(서버+클라 겸용)**가 되어 게임 원장(페이즈·점수·룰렛·사망 판정)의 단일 권위를 갖는다. 웹 프로토타입의 '서버 권위'를 호스트가 대신한다. | 필수 |
| Steam 트랜스포트 (Facepunch) | 전송 계층은 **Facepunch.Steamworks + Facepunch Transport**. UDP 포트 대신 SteamId 로 주소를 잡고 Steam 릴레이(SDR)를 타서 NAT·포트 개방 없이 P2P 접속 → 서버 호스팅 비용 0, 출시 즉시 친구와 플레이. | 필수 |
| 이동 동기화 (ClientNetworkTransform) | 각 플레이어 캐릭터는 소유 클라가 로컬 이동하고 `ClientNetworkTransform`(클라 권위 + 내장 보간)으로 위치를 복제. 호스트는 재시뮬 없이 중계·검증. 웹의 '클라 권위 릴레이'와 동일 철학. | 필수 |
| 상태 = NetworkVariable / RPC | 페이즈·라운드·주제·투표수·점수는 `NetworkVariable<T>`(변경 시 자동 동기화), 일회성 이벤트(룰렛 결과·부활·하이라이트)는 `ClientRpc`, 클라 요청(배치·투표·사망 보고)은 `ServerRpc`, 목록은 `NetworkList<T>`. | 필수 |
| 접속·로비·닉네임 | Steam 로비(`SteamMatchmaking`)로 방 생성·초대·입장. 닉네임은 Steam 페르소나 기본값 + 중복 시 접미사. late-join 시 호스트가 현재 페이즈·장애물·플레이어 상태 전송. | 필수 |
| 규모 & 부하 검증 | Multiplayer Play Mode(가상 플레이어)와 빌드 다중 실행으로 다인원 테스트, 호스트 대역폭·지연 실측. | 필수 |
| 끊김 처리·재접속 | 클라 이탈 시 관전·재접속 유예. 호스트 이탈 시 매치 종료(호스트 마이그레이션은 선택). | 권장 |
| 전용 서버(DGS) 확장 | 대규모 시 헤드리스 Unity 전용 서버(KCLOUD VM 또는 Unity Multiplay)로 전환. NGO 코드 대부분 재사용. | 선택 |

### 2. 준비 페이즈 (장애물 배치 + 주제 투표 + 룰렛)

| 구현 요소 | 설명 | 우선순위 |
|---|---|---|
| 장애물 배치 (ServerRpc + NetworkObject 스폰) | 클라가 지면 레이캐스트 위치를 `PlaceObstacleServerRpc(type,pos,yaw)`로 요청 → 호스트가 검증(1인 3개·구역·겹침) 후 장애물 프리팹을 `NetworkObject.Spawn()` → 전 클라 자동 복제. 실패 시 클라에 거부 사유. | 필수 |
| 장애물 3종 프리팹 | 벽·원기둥·투명벽. 투명벽은 동일 콜라이더 + 렌더러만 반투명/비활성(모두 같은 물리라 공정, 시각만 기만). | 필수 |
| 주제 투표 | `VoteServerRpc(topicId)` → 호스트 집계(`NetworkVariable`/`NetworkList`) → UI 실시간 반영. 구현 완료 모드만 노출. | 필수 |
| 투표율 가중 룰렛 | 호스트가 가중치(투표수+1, 직전 주제 제외)로 결과 결정 후 `RouletteResultClientRpc`. 클라는 결과 각도로 연출만 재생(공정성 보장). | 필수 |
| 배치 비용제·이동형·재배치 | 예산/코스트제, 트램펄린 등 이로운 배치물, 라운드 간 재배치. | 선택 |

### 3. 라운드 시스템 & 게임 모드

| 구현 요소 | 설명 | 우선순위 |
|---|---|---|
| 매치 상태머신 (NetworkBehaviour) | 호스트의 `MatchManager : NetworkBehaviour` 가 페이즈 FSM(PREP → ROULETTE → PLAY → HIGHLIGHT ×3 → RESULT) 구동. `NetworkVariable<Phase>` + 종료 타임스탬프로 전 클라 동기화(매초 전송 없음). | 필수 |
| 게임 모드 인터페이스 (전략) | `IGameMode` 또는 ScriptableObject: `OnStart / OnTick / OnPlayerDeath / OnEnd → raw[]`. 신규 모드 = 스크립트/에셋 1개 추가. 웹 프로토의 모드 레지스트리와 동형. | 필수 |
| 핵심 3모드 | 레이스(결승 트리거)·최고 높이(peakY)·생존(생존 시간). 위치·사망 데이터만 소비. | 필수 |
| 사망/부활/관전 | 강물(수위 아래) 낙사 판정 → 부활(레이스/높이) 또는 관전(생존). 호스트 권위. | 필수 |
| 코인·왕관 모드 | 추가 NetworkObject 엔티티. | 권장 |

### 4. 점수 시스템 (검증된 설계 이식)

| 구현 요소 | 설명 | 우선순위 |
|---|---|---|
| 클리어 점수 정규화 | 모드별 라운드당 0~1000 정규화(레이스/높이/생존/코인/왕관 산식은 기획서·웹 구현과 동일). 호스트 계산. | 필수 |
| 장애물 영향 점수 & 귀속 | 사망 지점 반경 내 소유 장애물 → 킬 귀속. `킬 × 100`(상한 300). 클리어 70 : 장애물 30. | 필수 |
| 3라운드 누적·순위 | `NetworkList<ScoreRow>`로 동기화, RESULT 에서 최종 순위. | 필수 |
| 실시간 리더보드 | 라운드 중 Top-N 표시. | 권장 |

### 5. 하이라이트

| 구현 요소 | 설명 | 우선순위 |
|---|---|---|
| 사망 이벤트 로깅 | 호스트가 라운드 사망(위치·시각·원인 장애물) 기록. | 필수 |
| 임팩트 구간 선정 + 스탯 카드 | 슬라이딩 윈도우(3s)로 최다 낙사 구간·최다 유발 장애물 산정 → UI 배너/카드(`ClientRpc`). | 필수 |
| 킬캠 리플레이 | Unity Timeline/스냅샷 녹화 기반 3D 리플레이. | 선택 |
| 클립 캡처·공유 | 숏폼용 녹화 버튼. | 선택 |

### 6. 클라이언트 · 맵 · 캐릭터 · 카메라

| 구현 요소 | 설명 | 우선순위 |
|---|---|---|
| 산·강 지형 | Unity **Terrain**(하이트맵) 또는 저폴리 메시 롱맵. 강 = 수면 + 낙사 볼륨. 웹의 결정론적 지형을 Unity 지형으로 재현. | 필수 |
| 캐릭터 컨트롤러 | `CharacterController`(또는 Rigidbody) 기반 이동·점프, 지면·장애물 콜라이더. 물리는 Unity 내장(웹의 커스텀 키네매틱 대체). | 필수 |
| 카메라 (Cinemachine) | 3인칭 추적 카메라, 지형·장애물 충돌 회피. | 필수 |
| UI (UI Toolkit / uGUI) | 로비·준비(팔레트+투표)·룰렛·목표 HUD·하이라이트·결과 화면. | 필수 |
| 아트·이펙트 | 저폴리 캐릭터·이모트, 낙사 물보라 등 파티클(숏폼용 '웃긴 장면'). | 권장 |

### 7. 기술 스택 · 빌드 · Steam 출시

| 구현 요소 | 설명 | 우선순위 |
|---|---|---|
| Unity 6 LTS + NGO | Unity 6 LTS(6000.x) + `com.unity.netcode.gameobjects`. 언어 C#. | 필수 |
| Steamworks 연동 | Facepunch.Steamworks(App ID·로비·친구·업적) + Facepunch Transport. 초기엔 개발용 App ID 480(Spacewar)로 테스트. | 필수 |
| 프로젝트 구조 | 단일 Unity 프로젝트. `Assets/Scripts/{Net, Match, Modes, Obstacles, Player, UI}`, asmdef 로 어셈블리 분리. | 필수 |
| 버전관리 | Git + **Git LFS**(대용량 에셋), Unity `.gitignore`, `.meta` 커밋 규칙. | 필수 |
| 빌드 & 출시 | Windows 빌드 → Steamworks 파트너 depot 업로드. Steam Direct(앱 수수료 $100·30일 대기·심사), 상점 페이지·Coming Soon. | 필수 |
| 전용 서버 빌드 | 헤드리스 리눅스 서버 빌드(KCLOUD/Multiplay), 대규모 확장 시. | 선택 |

### 구현 규모 및 현실성

NGO 호스트 권위 + Steam P2P/릴레이 기준 **MVP 동시 접속은 8~16명**을 현실선으로 본다(파티 게임 표준 규모). 호스트 업로드 대역폭이 병목이라 상태 압축·관심영역(AOI)은 스트레치다. **수십~100명은 헤드리스 전용 서버(DGS) 전환**으로만 가능하며 MVP 범위 밖으로 둔다. NGO 는 호스트/서버 코드가 처음부터 분리돼 있어 나중에 전용 서버로 옮기기 쉽다.

### 일정 · 컷라인

Day 2~7 상세는 상단 **개발 일정** 표 참조. 밀릴 경우 **골든 패스**(접속 → 준비(배치+투표) → 룰렛 → 레이스 1모드 → 점수 → 3라운드 → 최종 순위)를 최우선 사수. 컷 순서: 왕관/코인 모드 → 3D 킬캠(2D 스탯 카드 유지) → 실시간 리더보드(개인 점수만) → 근접존 귀속.

### 권장 기술 스택

| 분류 | 선택 | 근거 |
|---|---|---|
| 엔진 | Unity 6 LTS (C#) | 스팀 배포 검증된 파이프라인, 에셋·물리·에디터 |
| 네트워킹 | Netcode for GameObjects (호스트 권위) | Unity 네이티브, 서버 권위 파티 게임에 적합, 우리 설계와 동형 |
| 전송·스팀 | Facepunch.Steamworks + Facepunch Transport | SteamId 주소·Steam 릴레이로 무설정 P2P, 로비·친구·업적 |
| 물리·이동 | CharacterController + Unity Physics | 플랫포머 이동에 충분, 콜라이더 공유 |
| 카메라·UI | Cinemachine + UI Toolkit | 표준 3인칭·반응형 UI |
| 버전관리 | Git + Git LFS | 대용량 에셋·씬 관리 |
| 배포 | Steamworks (Steam Direct) | 스팀 출시 |

---

## 아키텍처

**Unity + Netcode for GameObjects 호스트 권위 + Steam 전송** 구조. 한 명이 호스트(서버 겸용)로 게임 원장을 소유하고, 나머지는 Steam 릴레이를 통해 P2P 로 접속한다.

```
 [Unity 클라이언트 x N]                          [호스트 (서버+클라 겸용)]
  로컬 이동·렌더·입력       --ServerRpc-->         MatchManager (NetworkBehaviour)
  (지형·장애물·캐릭터)      <-NetworkVariable/RPC-  페이즈·점수·룰렛·사망 판정 (권위)
  UI (준비/룰렛/HUD/결과)   <-NetworkTransform---   NetworkObject 복제 (플레이어·장애물)
        |                                                  |
        +--------- Facepunch Transport (Steam 릴레이) ------+
                    SteamId 주소 · Steam 로비 매치메이킹
```

- 호스트가 단일 권위. `NetworkVariable`(자동 동기화 상태) + `ServerRpc`/`ClientRpc`(요청·이벤트) + `ClientNetworkTransform`(이동)으로 구성.
- 대규모 확장 시 호스트 대신 헤드리스 Unity 전용 서버(DGS)로 교체(NGO 코드 재사용).

**프로젝트 구조** (단일 Unity 프로젝트)

- `Assets/Scripts/Net`: NGO 부트스트랩·Steam 로비·트랜스포트
- `Assets/Scripts/Match`: 매치 FSM·페이즈
- `Assets/Scripts/Modes`: 게임 모드(레이스·높이·생존 등)
- `Assets/Scripts/{Obstacles, Player, UI}`: 장애물·캐릭터·화면
- (참고) `packages/`: 초기 웹 개념검증 프로토타입 (설계 검증용, 프로덕션 아님)

---

## 설계 문서

> 프로젝트 성격에 따라 필요한 항목만 작성

### 화면 / 인터페이스 설계

페이즈 구동 단일 화면(3D 씬 위 UI 오버레이). 로비(Steam 방 생성·입장) → 준비(장애물 팔레트 + 주제 투표) → 룰렛(주제 추첨 연출) → 플레이(상단 목표·타이머 HUD) → 하이라이트(라운드 요약) → 결과(최종 순위). 좌상단에 접속 인원·점수 명단 상시 표시.

### 데이터 구조

라이브 상태는 호스트가 소유하고 NGO 로 동기화한다.

- 플레이어: `NetworkObject` + `NetworkVariable`(색·사망 여부 등) + `ClientNetworkTransform`(위치)
- 장애물: `NetworkObject` 프리팹 `{ type(WALL|CYLINDER|GHOST), pos, yaw, ownerId }`
- 매치: `NetworkVariable<Phase>`(LOBBY|PREP|ROULETTE|PLAY|HIGHLIGHT|RESULT), `round(1~3)`, `topicId(race|height|survive)`
- 점수: 라운드당 클리어(0~1000) x 0.7 + 장애물 영향(킬 x 100, 상한 300), 3라운드 누적. `NetworkList<ScoreRow>`

### 네트워크 계약 (NGO)

외부 API 는 사용하지 않는다. 통신은 NGO 의 RPC·NetworkVariable 로 이뤄진다.

| 종류 | 이름 | 방향 | 설명 |
|---|---|---|---|
| ServerRpc | `PlaceObstacleServerRpc(type,pos,yaw)` | C→H | 장애물 배치 요청(호스트 검증) |
| ServerRpc | `VoteServerRpc(topicId)` | C→H | 주제 투표 |
| ServerRpc | `ReportDeathServerRpc(pos)` | C→H | 낙사 보고 |
| ClientRpc | `RouletteResultClientRpc(topicId,seed)` | H→C | 룰렛 결과(연출은 클라) |
| ClientRpc | `RespawnClientRpc(pos)` | H→C | 부활 위치 |
| ClientRpc | `HighlightClientRpc(data)` | H→C | 라운드 하이라이트 |
| NetworkVariable | `Phase / Round / TopicId / EndsAt` | H→C | 페이즈·타이머 동기화 |
| NetworkList | `Votes / Scores / Obstacles` | H→C | 투표·점수·장애물 동기화 |
| NetworkTransform | 플레이어 위치 | C→C | ClientNetworkTransform 보간 |

(C=클라이언트, H=호스트)

---

## 산출물 및 실행 방법

- **산출물 설명:** 룰렛 파티, Unity 기반 실시간 멀티플레이어 파티 게임(Steam 출시 목표). 초기 웹 프로토타입으로 설계·룰 검증 후 Unity 로 프로덕션.
- **실행 환경:** Unity 6 LTS(6000.x), Windows 빌드. 멀티플레이는 Steam 클라이언트 로그인 필요(Facepunch Transport).
- **실행 방법:** Unity Hub 로 프로젝트 열기 → NGO·Facepunch 패키지 설치 → 에디터 Play 로 호스트 시작, 친구 초대 또는 빌드 다중 실행으로 접속.
- **시연 영상 / 이미지:** (선택)

### 실행 방법

```text
# Unity 프로젝트 (프로덕션)
1. Unity Hub 에서 Unity 6 LTS(6000.x) 설치 후 프로젝트 열기
2. Package Manager: Netcode for GameObjects 설치
3. Facepunch.Steamworks + Facepunch Transport 임포트, steam_appid.txt 에 480(개발용) 기입
4. Steam 로그인 상태에서 Play → 호스트 시작 → 친구 초대(또는 빌드 여러 개로 로컬 테스트)
5. 빌드: File > Build (Windows) → Steamworks 파트너 depot 업로드

# 초기 웹 개념검증 프로토타입 (참고용, packages/)
npm install -g pnpm && pnpm install && pnpm dev   # http://localhost:5173
```

### 기술 구성

| 분류 | 사용 기술 |
|---|---|
| 엔진 / 언어 | Unity 6 LTS, C# |
| 네트워킹 | Netcode for GameObjects (호스트 권위) |
| 전송 / 스팀 | Facepunch.Steamworks + Facepunch Transport (Steam 릴레이·로비) |
| 물리 / 카메라 / UI | CharacterController, Cinemachine, UI Toolkit |
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
