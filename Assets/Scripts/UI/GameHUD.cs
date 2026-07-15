// GameHUD.cs
// 표시 전용 HUD. 씬 배치: 전용 GameObject 에 컴포넌트로 추가한다(에디터 Add Component).
// UI 요소(캔버스/텍스트)는 런타임에 코드로 생성한다 — 추후 uGUI 프리팹 교체 대상.
// Unity 6000.5.3f1 / URP / com.unity.ugui 2.5.0 (레거시 UnityEngine.UI) / NGO 2.13.0.
// TextMeshPro 미사용, 신규 Input System 전용(구 UnityEngine.Input 사용 금지 → 이 파일은 키 입력 없음).
// 호스트와 순수 클라이언트 모두에서 안전하게 동작해야 함(서버 전용 API 접근 금지).

using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;
using RouletteParty.Match;
using RouletteParty.Net; // PlayerController (탈락 여부·발끝 높이)

namespace RouletteParty.UI
{
    public class GameHUD : MonoBehaviour
    {
        // ---- 상수(레이아웃) ----
        private const float ROWH = 34f;     // 점수판 한 줄 높이
        private const float HEADER = 44f;   // 점수판 제목 아래 첫 줄 오프셋
        private const string TTF = "LegacyRuntime.ttf";

        [Tooltip("이름표 밑변 높이: 플레이어 루트(캡슐 중심) 기준 위로 띄우는 거리(m). 텍스트 렉트의 " +
                 "피벗이 밑변이라 이 지점에 글자 아랫단이 닿는다. 실측 기준: 모델 머리 꼭대기 = 루트 +0.8m, " +
                 "점프 스트레치(PlayerJuice) 순간 최대 +1.2m. 1.0 이면 평상시 머리 바로 위(+0.2m)에 붙고, " +
                 "스트레치 정점의 0.1초가량만 글자가 머리에 살짝 걸친다(바로 위 우선의 의도된 트레이드오프).")]
        [SerializeField] private float _nameplateHeight = 1.0f;

        // ---- 공유 폰트 (한 번만 획득) ----
        private Font _font;

        // ---- 정적 스켈레톤 참조 ----
        private Canvas _canvas;
        private RectTransform _canvasRT;

        // 상단 배너(UCH: 기울어진 페이즈 컬러 배너 + 크림 알약에 라운드/타이머)
        private Image _bannerPanel;   // 페이즈명 컬러 배너(페이즈별 색)
        private Image _subPanel;      // 라운드/타이머 크림 알약
        private Text _phaseLabel;
        private Text _subLabel;       // "라운드 n/3"
        private Text _countdownLabel; // "n초"

        // 페이즈 전환 시각(목표 문구 잠깐 표시용)
        private MatchPhase _lastPhase = (MatchPhase)byte.MaxValue;
        private float _phaseEnterTime;

        // 좌측 점수판
        private Image _scorePanel;
        private Text _scoreTitle;
        private readonly List<Row> _scoreRows = new List<Row>();

        // 중앙 페이즈별 루트
        private RectTransform _playRoot;
        private Text _playObjective;
        private Text _playSurvive;   // 등반 정보(현재 높이·생존 수)
        private GameObject _playInfoGo; // 등반 정보 알약 패널(텍스트의 부모, 패널째 토글)
        private Text _deadBanner;    // 탈락·관전 안내
        private GameObject _deadPanelGo; // 탈락 배너 패널(패널째 토글)

        private RectTransform _highlightRoot;
        private Text _hlBannerText; // 파란 헤더 배너 안 "라운드 n 결과"
        private Text _hlTrophy;
        private Text _hlTopic;
        private Text _hlStats;   // 라운드 통계(최대 낙하·낚시왕·낚임왕·탈락 수)
        private RectTransform _hlRowsParent;
        private readonly List<Row> _hlRows = new List<Row>();

        private RectTransform _resultRoot;   // 최종 결과(딤 + 중앙 카드)
        private Text _resChampion;
        private RectTransform _resRowsParent;
        private readonly List<Row> _resRows = new List<Row>();

        // 이름표(월드 오브젝트 → 화면) 풀
        private readonly List<Nameplate> _nameplates = new List<Nameplate>();

        // ---- 재사용 임시 버퍼 (프레임당 GC 최소화) ----
        private readonly List<NetworkObject> _players = new List<NetworkObject>();
        private readonly List<Entry> _standings = new List<Entry>();
        private readonly List<RoundResult> _roundResults = new List<RoundResult>();

        private bool _built;

        // 정렬 비교자(할당 재사용)
        private static readonly System.Comparison<Entry> _byScoreDesc =
            (a, b) => a.Score != b.Score ? b.Score.CompareTo(a.Score) : a.Id.CompareTo(b.Id);
        private static readonly System.Comparison<RoundResult> _byRankAsc =
            (a, b) => a.Rank.CompareTo(b.Rank);

        // ===================================================================
        // 수명주기
        // ===================================================================
        private void Awake()
        {
            _font = AcquireFont();
            BuildUI();
        }

        private void LateUpdate()
        {
            // 카메라/월드 이동이 정리된 뒤 이름표를 배치하기 위해 LateUpdate에서 전체 갱신.
            RefreshDynamic();
        }

        private void OnDestroy()
        {
            // 코드 생성 캔버스 정리(코루틴 없음 → 누수 없음).
            if (_canvas != null) Destroy(_canvas.gameObject);
        }

        // ===================================================================
        // 폰트 획득. ★한글이 렌더되는 OS 폰트를 최우선으로 선택★
        // 빌트인 LegacyRuntime.ttf 는 라틴 전용이라 그것만 쓰면 한글이 전부 두부(□)로 깨진다.
        // Malgun Gothic 은 Windows 10/11 기본 탑재라 에디터/윈도우 빌드 모두 안전하고,
        // 동적 OS 폰트는 런타임에 필요한 글리프를 그때그때 텍스처에 올려 한글을 정상 렌더한다.
        // ===================================================================
        private static readonly string[] KoreanFonts =
            { "Malgun Gothic", "맑은 고딕", "NanumGothic", "나눔고딕", "Gulim", "굴림", "Dotum", "돋움" };

        private static Font AcquireFont()
        {
            // 0) 프로젝트 번들 폰트(SOYO 메이플, 빌드/전 피어 동일 보장) 최우선.
            var bundled = UiKit.Font;
            if (bundled != null) return bundled;

            // 1) 한글 가능한 OS 폰트(번들 누락 시 폴백).
            foreach (var name in KoreanFonts)
            {
                Font f = null;
                try { f = Font.CreateDynamicFontFromOSFont(name, 16); } catch { f = null; }
                if (f != null) return f;
            }
            // 2) 설치된 아무 OS 폰트(첫 목록). 한글이 안 될 수 있으나 최소한 렌더는 됨.
            try
            {
                string[] osNames = Font.GetOSInstalledFontNames();
                if (osNames != null && osNames.Length > 0)
                {
                    var f = Font.CreateDynamicFontFromOSFont(osNames, 16);
                    if (f != null) return f;
                }
            }
            catch { /* 무시하고 최후 폴백으로 */ }
            // 3) 최후: 빌트인 라틴 폰트(한글 미지원이지만 크래시는 없음).
            try { return Resources.GetBuiltinResource<Font>(TTF); } catch { return null; }
        }

        // ===================================================================
        // 정적 스켈레톤 1회 생성
        // ===================================================================
        private void BuildUI()
        {
            // --- 캔버스 (Canvas → CanvasScaler 순서) ---
            var canvasGO = new GameObject("HUDCanvas", typeof(Canvas), typeof(CanvasScaler));
            canvasGO.transform.SetParent(transform, false);
            _canvas = canvasGO.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay; // 자식 추가 전에 설정
            _canvas.sortingOrder = 100;                          // 다른 캔버스 위에 그림

            var scaler = canvasGO.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            // 가로형 PC 게임 표준: 높이(1) 기준 매칭. 21:9 등 와이드 화면에서 UI 가 커지지 않고
            // 가로 여백만 늘어난다(요소들은 앵커로 모서리/중앙에 붙어 있어 비율 차이를 흡수).
            scaler.matchWidthOrHeight = 1f;
            // 표시 전용: GraphicRaycaster/EventSystem 불필요(투표·장애물은 별도 PrepClientUI가 담당).

            _canvasRT = _canvas.GetComponent<RectTransform>();

            // --- 상단 배너(UCH): 기울어진 페이즈 컬러 배너 + 크림 알약(라운드/타이머) ---
            _bannerPanel = MakeStrip(_canvasRT, UiKit.Teal);
            SetRect(_bannerPanel.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                    new Vector2(340, 64), new Vector2(0, -16));
            _bannerPanel.rectTransform.localEulerAngles = new Vector3(0f, 0f, 1.5f);

            _phaseLabel = MakeText(_bannerPanel.transform, "", 34, TextAnchor.MiddleCenter, Color.white, false);
            _phaseLabel.fontStyle = FontStyle.Bold;
            Stretch(_phaseLabel.rectTransform);

            _subPanel = MakeCream(_canvasRT);
            SetRect(_subPanel.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                    new Vector2(280, 46), new Vector2(0, -88));

            _subLabel = MakeText(_subPanel.transform, "", 22, TextAnchor.MiddleLeft, UiKit.InkSoft, false);
            _subLabel.fontStyle = FontStyle.Bold;
            SetRect(_subLabel.rectTransform, new Vector2(0f, 0f), new Vector2(0.6f, 1f), new Vector2(0f, 0.5f),
                    new Vector2(-20, 0), new Vector2(20, 0));

            _countdownLabel = MakeText(_subPanel.transform, "", 24, TextAnchor.MiddleRight, UiKit.Ink, false);
            _countdownLabel.fontStyle = FontStyle.Bold;
            SetRect(_countdownLabel.rectTransform, new Vector2(0.6f, 0f), new Vector2(1f, 1f), new Vector2(1f, 0.5f),
                    new Vector2(-20, 0), new Vector2(-20, 0));

            // --- 점수판(우상단, 크림 패널) ---
            _scorePanel = MakeCream(_canvasRT);
            SetRect(_scorePanel.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f),
                    new Vector2(320, 470), new Vector2(-20, -20));

            _scoreTitle = MakeText(_scorePanel.transform, "점수판", 22, TextAnchor.MiddleLeft, UiKit.Ink, false);
            _scoreTitle.fontStyle = FontStyle.Bold;
            SetRect(_scoreTitle.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                    new Vector2(-32, 32), new Vector2(0, -9));
            var scoreDivider = MakeImage(_scorePanel.transform, new Color(0.231f, 0.192f, 0.161f, 0.25f));
            SetRect(scoreDivider.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                    new Vector2(-28, 2), new Vector2(0, -42));

            // --- 탈락(관전) 배너(빨간 스트립) ---
            var deadPanel = MakeStrip(_canvasRT, UiKit.Red);
            SetRect(deadPanel.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                    new Vector2(640, 62), new Vector2(0, 140));
            deadPanel.rectTransform.localEulerAngles = new Vector3(0f, 0f, -1f);
            _deadBanner = MakeText(deadPanel.transform, "", 32, TextAnchor.MiddleCenter, Color.white, false);
            _deadBanner.fontStyle = FontStyle.Bold;
            Stretch(_deadBanner.rectTransform);
            _deadPanelGo = deadPanel.gameObject; // 패널째 켜고 끈다(텍스트만 끄면 패널이 남음)
            _deadPanelGo.SetActive(false);

            // --- 플레이 루트(목표 문구, 화면 중앙 하단에서만 잠깐) ---
            _playRoot = MakeRect(_canvasRT, "PlayRoot");
            SetRect(_playRoot, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                    new Vector2(1000, 70), new Vector2(0, 150));
            _playObjective = MakeText(_playRoot, "", 40, TextAnchor.MiddleCenter, Color.white);
            _playObjective.fontStyle = FontStyle.Bold;
            Stretch(_playObjective.rectTransform);

            // --- 플레이 정보 알약(상단, 라운드/타이머 패널 바로 아래): 현재 높이 · 등수 · 생존 수 ---
            var playInfoPanel = MakeCream(_canvasRT);
            SetRect(playInfoPanel.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                    new Vector2(440, 44), new Vector2(0, -150));
            _playSurvive = MakeText(playInfoPanel.transform, "", 22, TextAnchor.MiddleCenter, UiKit.Ink, false);
            _playSurvive.fontStyle = FontStyle.Bold;
            Stretch(_playSurvive.rectTransform);
            _playInfoGo = playInfoPanel.gameObject;

            // --- 하이라이트 루트(크림 카드 + 파란 헤더 배너) ---
            _highlightRoot = MakeRect(_canvasRT, "HighlightRoot");
            SetRect(_highlightRoot, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                    new Vector2(560, 440), new Vector2(0, 0));
            var hlBg = MakeCream(_highlightRoot);
            Stretch(hlBg.rectTransform);
            hlBg.rectTransform.SetAsFirstSibling();
            var hlBanner = MakeStrip(_highlightRoot, UiKit.Blue);
            SetRect(hlBanner.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                    new Vector2(280, 58), new Vector2(0, 26));
            hlBanner.rectTransform.localEulerAngles = new Vector3(0f, 0f, -1.5f);
            _hlBannerText = MakeText(hlBanner.transform, "", 28, TextAnchor.MiddleCenter, Color.white, false);
            _hlBannerText.fontStyle = FontStyle.Bold;
            Stretch(_hlBannerText.rectTransform);

            _hlTrophy = MakeText(_highlightRoot, "", 40, TextAnchor.MiddleCenter, UiKit.Ink, false);
            _hlTrophy.fontStyle = FontStyle.Bold;
            SetRect(_hlTrophy.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                    new Vector2(520, 54), new Vector2(0, -48));
            _hlTopic = MakeText(_highlightRoot, "", 22, TextAnchor.MiddleCenter, UiKit.InkSoft, false);
            SetRect(_hlTopic.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                    new Vector2(520, 32), new Vector2(0, -104));
            _hlRowsParent = MakeRect(_highlightRoot, "HLRows");
            SetRect(_hlRowsParent, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                    new Vector2(500, 200), new Vector2(0, -148));

            _hlStats = MakeText(_highlightRoot, "", 21, TextAnchor.UpperCenter, UiKit.InkSoft, false);
            SetRect(_hlStats.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                    new Vector2(520, 140), new Vector2(0, -280));

            // --- 결과 루트(중앙 크림 카드 + 빨간 헤더 배너) ---
            // 전체 화면 딤은 두지 않는다: 이 프로젝트 URP 구성에서 오버레이 캔버스의 반투명이
            // 3D 월드 위에서는 블렌딩되지 않는 현상 확인(불투명/패널 위 반투명은 정상).
            // 카드 자체가 불투명 크림이라 딤 없이도 가독성이 충분하다.
            _resultRoot = MakeRect(_canvasRT, "ResultRoot");
            Stretch(_resultRoot);

            var resCard = MakeCream(_resultRoot);
            SetRect(resCard.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                    new Vector2(640, 620), new Vector2(0, -10));
            var resBanner = MakeStrip(resCard.transform, UiKit.Red);
            SetRect(resBanner.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                    new Vector2(300, 62), new Vector2(0, 28));
            resBanner.rectTransform.localEulerAngles = new Vector3(0f, 0f, -1.5f);
            var resBannerText = MakeText(resBanner.transform, "최종 결과", 30, TextAnchor.MiddleCenter, Color.white, false);
            resBannerText.fontStyle = FontStyle.Bold;
            Stretch(resBannerText.rectTransform);

            _resChampion = MakeText(resCard.transform, "", 52, TextAnchor.MiddleCenter, UiKit.Ink, false);
            _resChampion.fontStyle = FontStyle.Bold;
            SetRect(_resChampion.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                    new Vector2(600, 74), new Vector2(0, -52));
            _resRowsParent = MakeRect(resCard.transform, "ResRows");
            SetRect(_resRowsParent, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                    new Vector2(540, 440), new Vector2(0, -146));

            // 화면 중앙 조준점은 두지 않는다: 등반 중에는 겨눌 대상이 없고, 설치 단계의
            // 위치 피드백은 블루프린트(초록/빨강 실물 프리뷰)가 조준점보다 정확히 전달한다.
            HideAllPhaseRoots();
            _built = true;
        }

        // ===================================================================
        // 프레임별 동적 갱신
        // ===================================================================
        private void RefreshDynamic()
        {
            if (!_built) return;

            var cam = Camera.main; // 이름표용(핫 루프에서 한 번만 조회)

            // --- 안전 가드(순서 중요) ---
            var nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsListening) { SetIdle(cam); return; }
            if (!nm.IsClient && !nm.IsServer) { SetIdle(cam); return; }
            if (nm.IsClient && !nm.IsServer && !nm.IsConnectedClient) { SetIdle(cam); return; }

            var m = MatchManager.Instance;
            if (m == null || !m.IsSpawned) { SetIdle(cam); return; }

            // 로비(대기방) 동안 HUD 는 전부 숨긴다(화면은 LobbyUI 대기방이 사용, 겹침 방지).
            if (m.CurrentPhase == MatchPhase.Lobby) { SetIdle(cam); return; }

            // --- 플레이어 오브젝트 클라이언트 안전 열거 ---
            // NGO 2.13: SpawnManager.SpawnedObjectsList 는 클라이언트에서도 유효.
            // ConnectedClients* 는 순수 클라이언트에서 throw 하므로 절대 사용하지 않음.
            _players.Clear();
            var sm = nm.SpawnManager;
            if (sm != null)
            {
                foreach (NetworkObject no in sm.SpawnedObjectsList)
                {
                    if (no == null || !no.IsPlayerObject) continue;
                    _players.Add(no);
                }
            }

            ulong localId = nm.LocalClientId;

            // 페이즈 전환 시각 기록(목표 문구 잠깐 표시 등 연출용).
            if (m.CurrentPhase != _lastPhase)
            {
                _lastPhase = m.CurrentPhase;
                _phaseEnterTime = Time.unscaledTime;
            }

            // --- 상단 배너(페이즈별 컬러 스트립) ---
            if (_bannerPanel != null)
            {
                _bannerPanel.gameObject.SetActive(true);
                Color fill = PhaseColor(m.CurrentPhase);
                _bannerPanel.sprite = UiKit.BorderedSprite(fill, UiKit.Ink); // 색 조합 캐시라 매 프레임 안전
                _phaseLabel.color = fill == UiKit.Yellow ? UiKit.Ink : Color.white; // 노란 배너만 잉크 텍스트
            }
            if (_subPanel != null) _subPanel.gameObject.SetActive(true);
            _phaseLabel.text = PhaseKorean(m.CurrentPhase);

            _subLabel.text = m.Round >= 1 ? "라운드 " + m.Round + "/3" : "";

            float rem = m.PhaseRemaining; // 이미 클램프됨, 클라이언트 안전
            _countdownLabel.text = Mathf.CeilToInt(Mathf.Max(0f, rem)) + "초";
            _countdownLabel.color = rem <= 10f ? UiKit.Red : UiKit.Ink; // 임박 경고

            // --- 좌측 점수판(누적 점수) ---
            BuildStandings(m);
            FillScoreboard(localId);

            // --- 로컬 플레이어 상태(탈락·높이) ---
            bool localDead = false;
            float localY = 0f;
            for (int i = 0; i < _players.Count; i++)
            {
                if (_players[i].OwnerClientId != localId) continue;
                var pc = _players[i].GetComponent<PlayerController>();
                if (pc != null) localDead = pc.Dead.Value;
                localY = pc != null ? pc.FootY : _players[i].transform.position.y; // 발끝 기준(채점과 동일)
                break;
            }

            // --- 중앙 페이즈별 콘텐츠 ---
            HideAllPhaseRoots();
            switch (m.CurrentPhase)
            {
                case MatchPhase.Play:     RenderPlay(m, localY, localDead); break;
                case MatchPhase.Highlight:RenderHighlight(m);     break;
                case MatchPhase.Intermission: RenderHighlight(m); break; // 다음 라운드 대기 중에도 직전 하이라이트 유지 표시
                case MatchPhase.Result:   RenderResult(m);        break;
                default: /* Lobby/Prep: 중앙 콘텐츠 없음 */ break;
            }

            // --- 탈락 배너 ---
            if (_deadPanelGo != null)
            {
                bool showDead = localDead && m.CurrentPhase == MatchPhase.Play;
                _deadPanelGo.SetActive(showDead);
                if (showDead) _deadBanner.text = "탈락! 관전 중 (좌클릭: 대상 전환)";
            }

            // 카드 화면(하이라이트/대기/결과): 이름표를 숨긴다(카드 위 겹침 방지).
            bool cardPhase = m.CurrentPhase == MatchPhase.Highlight ||
                             m.CurrentPhase == MatchPhase.Intermission ||
                             m.CurrentPhase == MatchPhase.Result;

            // --- 이름표 ---
            if (cardPhase)
                for (int i = 0; i < _nameplates.Count; i++) _nameplates[i].go.SetActive(false);
            else
                RenderNameplates(cam);
        }

        // 네트워크 미준비/스폰 전: 빈 HUD.
        private void SetIdle(Camera cam)
        {
            if (!_built) return;
            _phaseLabel.text = "";
            _subLabel.text = "";
            _countdownLabel.text = "";
            if (_bannerPanel != null) _bannerPanel.gameObject.SetActive(false); // 빈 배너 잔상 방지
            if (_subPanel != null) _subPanel.gameObject.SetActive(false);
            EnsureRows(_scoreRows, _scorePanel.transform, 0);
            HideAllPhaseRoots();
            if (_deadPanelGo != null) _deadPanelGo.SetActive(false);
            for (int i = 0; i < _nameplates.Count; i++)
                _nameplates[i].go.SetActive(false);
        }

        // ===================================================================
        // 점수판
        // ===================================================================
        private void BuildStandings(MatchManager m)
        {
            _standings.Clear();
            for (int i = 0; i < _players.Count; i++)
            {
                ulong id = _players[i].OwnerClientId;
                _standings.Add(new Entry { Id = id, Score = CumulativeScore(m, id) });
            }
            _standings.Sort(_byScoreDesc);
        }

        // 누적 점수 = Results 중 ClientId 일치 행의 Score 합(float 누적 후 마지막에 한 번만 반올림).
        // (행마다 반올림하면 소수 오차가 누적돼 표시 총점·정렬 순위가 실제와 어긋날 수 있음.)
        private int CumulativeScore(MatchManager m, ulong id)
        {
            float s = 0f;
            var res = m.Results;
            if (res != null)
            {
                int c = res.Count;
                for (int i = 0; i < c; i++)
                {
                    var r = res[i];
                    if (r.ClientId == id) s += r.Score;
                }
            }
            return Mathf.RoundToInt(s);
        }

        private void FillScoreboard(ulong localId)
        {
            // 패널 높이 = 제목 + 행 수(인원이 적을 때 빈 패널이 늘어지지 않게).
            _scorePanel.rectTransform.sizeDelta = new Vector2(320, HEADER + _standings.Count * ROWH + 14);
            EnsureRows(_scoreRows, _scorePanel.transform, _standings.Count);
            for (int i = 0; i < _standings.Count; i++)
            {
                var e = _standings[i];
                var row = _scoreRows[i];
                row.rt.anchoredPosition = new Vector2(0, -(HEADER + i * ROWH));

                Color col = PlayerPalette.ColorFor(e.Id);
                row.swatch.color = col;

                string nm = PlayerPalette.NameFor(e.Id);
                if (i == 0) nm = "★ " + nm; // 1등 표시(★ 는 한글 폰트에 포함 → 두부 안 남)
                row.nameText.text = nm;

                bool isLocal = e.Id == localId;
                row.nameText.fontStyle = isLocal ? FontStyle.Bold : FontStyle.Normal;
                row.nameText.color = UiKit.Ink;
                row.scoreText.text = e.Score.ToString();
                row.scoreText.color = UiKit.Ink;
                row.scoreText.fontStyle = isLocal ? FontStyle.Bold : FontStyle.Normal;

                // 로컬 플레이어 강조 박스(노란 하이라이트)
                row.bg.color = isLocal
                    ? new Color(UiKit.Yellow.r, UiKit.Yellow.g, UiKit.Yellow.b, 0.55f)
                    : new Color(1f, 1f, 1f, 0f);
            }
        }

        // ===================================================================
        // 페이즈별 렌더링
        // ===================================================================
        private void RenderPlay(MatchManager m, float localY, bool localDead)
        {
            _playRoot.gameObject.SetActive(true);
            // 목표 문구는 라운드 시작 직후 잠깐만(계속 떠 있으면 시야/플레이어와 겹친다).
            bool showObjective = Time.unscaledTime - _phaseEnterTime < 4f;
            _playObjective.gameObject.SetActive(showObjective);
            if (showObjective) _playObjective.text = "더 높이 올라가라!";
            if (_playInfoGo != null) _playInfoGo.SetActive(true);
            // 현재 높이(점수의 근거) · 등수 · 생존 수. 체력은 비공개 규칙이라 절대 표시하지 않는다.
            int rank = LocalRank();
            string rankStr = rank > 0 ? $"   {rank}등" : "";
            _playSurvive.text = localDead
                ? $"관전 중{rankStr}   생존 {m.AliveCount}"
                : $"높이 {Mathf.Max(0f, localY):0.0} m{rankStr}   생존 {m.AliveCount}";
        }

        // 로컬 플레이어의 현재 등수(누적 점수 기준, _standings 는 내림차순 정렬됨). 없으면 0.
        private int LocalRank()
        {
            var nm = NetworkManager.Singleton;
            if (nm == null) return 0;
            ulong me = nm.LocalClientId;
            for (int i = 0; i < _standings.Count; i++)
                if (_standings[i].Id == me) return i + 1;
            return 0;
        }

        private void RenderHighlight(MatchManager m)
        {
            _highlightRoot.gameObject.SetActive(true);
            _hlBannerText.text = "라운드 " + m.Round + " 결과";

            // 라운드 우승자(없으면 ulong.MaxValue).
            if (m.RoundWinnerId != ulong.MaxValue)
            {
                _hlTrophy.text = "★ " + PlayerPalette.NameFor(m.RoundWinnerId);
                _hlTrophy.color = PlayerPalette.ColorFor(m.RoundWinnerId);
            }
            else
            {
                _hlTrophy.text = "★ -";
                _hlTrophy.color = Color.white;
            }

            _hlTopic.text = "누적 점수는 우상단 점수판";

            // 이번 라운드 결과만 필터 → Rank 오름차순 → 상위 3.
            _roundResults.Clear();
            var res = m.Results;
            if (res != null)
            {
                int c = res.Count;
                for (int i = 0; i < c; i++)
                {
                    var r = res[i];
                    if (r.Round == m.Round) _roundResults.Add(r);
                }
            }
            _roundResults.Sort(_byRankAsc);
            int show = Mathf.Min(3, _roundResults.Count);

            // 라운드 통계(현재 라운드 것일 때만 표시. Round == 0 = 아직 집계 없음).
            var st = m.RoundStats;
            if (st.Round == m.Round)
            {
                string s = "";
                if (st.BiggestFallVictim != RoundStats.None && st.BiggestFallHeight > 0f)
                    s += $"최대 낙하  {PlayerPalette.NameFor(st.BiggestFallVictim)}  {st.BiggestFallHeight:0.0} m\n";
                if (st.BestBaiter != RoundStats.None && st.BestBaiterCount > 0)
                    s += $"낚시왕  {PlayerPalette.NameFor(st.BestBaiter)}  ({st.BestBaiterCount}회 낚음)\n";
                if (st.MostBaited != RoundStats.None && st.MostBaitedCount > 0)
                    s += $"낚임왕  {PlayerPalette.NameFor(st.MostBaited)}  ({st.MostBaitedCount}회 당함)\n";
                if (st.DeathCount > 0)
                    s += $"탈락  {st.DeathCount}명";
                _hlStats.text = s;
            }
            else
            {
                _hlStats.text = "";
            }

            EnsureRows(_hlRows, _hlRowsParent.transform, show);
            for (int i = 0; i < show; i++)
            {
                var r = _roundResults[i];
                var row = _hlRows[i];
                row.rt.anchoredPosition = new Vector2(0, -(i * ROWH));
                row.bg.color = new Color(1f, 1f, 1f, 0f);
                row.swatch.color = PlayerPalette.ColorFor(r.ClientId);
                row.nameText.text = "#" + r.Rank + "  " + PlayerPalette.NameFor(r.ClientId);
                row.nameText.color = UiKit.Ink;
                row.nameText.fontStyle = FontStyle.Normal;
                row.scoreText.text = Mathf.RoundToInt(r.Score).ToString();
                row.scoreText.color = UiKit.Ink;
                row.scoreText.fontStyle = FontStyle.Normal;
            }
        }

        private void RenderResult(MatchManager m)
        {
            _resultRoot.gameObject.SetActive(true);

            // 최종 우승자.
            if (m.MatchWinnerId != ulong.MaxValue)
            {
                _resChampion.text = "★ " + PlayerPalette.NameFor(m.MatchWinnerId);
                _resChampion.color = PlayerPalette.ColorFor(m.MatchWinnerId);
            }
            else
            {
                _resChampion.text = "★ -";
                _resChampion.color = Color.white;
            }

            // _standings 는 이미 누적 점수 내림차순 정렬됨.
            EnsureRows(_resRows, _resRowsParent.transform, _standings.Count);
            for (int i = 0; i < _standings.Count; i++)
            {
                var e = _standings[i];
                var row = _resRows[i];
                row.rt.anchoredPosition = new Vector2(0, -(i * (ROWH + 8f)));
                // 1등 = 노란 하이라이트, 나머지 = 크림 다크 줄무늬(UCH 종이 느낌).
                row.bg.color = i == 0
                    ? new Color(UiKit.Yellow.r, UiKit.Yellow.g, UiKit.Yellow.b, 0.55f)
                    : new Color(UiKit.CreamDark.r, UiKit.CreamDark.g, UiKit.CreamDark.b, 0.6f);
                row.swatch.color = PlayerPalette.ColorFor(e.Id);
                row.nameText.text = (i + 1) + ".  " + PlayerPalette.NameFor(e.Id);
                row.nameText.color = UiKit.Ink;
                row.nameText.fontStyle = i == 0 ? FontStyle.Bold : FontStyle.Normal;
                row.scoreText.text = e.Score.ToString();
                row.scoreText.color = UiKit.Ink;
                row.scoreText.fontStyle = i == 0 ? FontStyle.Bold : FontStyle.Normal;
            }
        }

        // ===================================================================
        // 이름표
        // ===================================================================
        private void RenderNameplates(Camera cam)
        {
            // 필요한 만큼 풀 확보.
            EnsureNameplates(_players.Count);

            // 카메라 없으면 전부 숨김.
            if (cam == null)
            {
                for (int i = 0; i < _nameplates.Count; i++)
                    _nameplates[i].go.SetActive(false);
                return;
            }

            for (int i = 0; i < _nameplates.Count; i++)
            {
                if (i >= _players.Count)
                {
                    _nameplates[i].go.SetActive(false);
                    continue;
                }

                var no = _players[i];
                if (no == null)
                {
                    _nameplates[i].go.SetActive(false);
                    continue;
                }

                var np = _nameplates[i];

                // 탈락자는 이름표 숨김: 본체 렌더러는 꺼지지만 오브젝트는 탈락 위치에 남으므로
                // Dead 를 확인하지 않으면 이름표만 허공에 떠 있게 된다.
                if (np.source != no)
                {
                    np.source = no;
                    np.pc = no.GetComponent<RouletteParty.Net.PlayerController>();
                }
                if (np.pc != null && np.pc.Dead.Value)
                {
                    np.go.SetActive(false);
                    continue;
                }

                ulong id = no.OwnerClientId;
                Vector3 world = no.transform.position + Vector3.up * _nameplateHeight;
                Vector3 sp = cam.WorldToScreenPoint(world);
                if (sp.z < 0f) // 카메라 뒤 → 숨김
                {
                    np.go.SetActive(false);
                    continue;
                }

                np.go.SetActive(true);
                np.text.text = PlayerPalette.NameFor(id);
                np.text.color = PlayerPalette.ColorFor(id);
                // ScreenSpaceOverlay: 화면 픽셀 == 캔버스 월드 좌표 → position 직접 대입.
                np.rt.position = sp;
            }
        }

        private void EnsureNameplates(int count)
        {
            while (_nameplates.Count < count)
            {
                var t = MakeText(_canvasRT, "", 22, TextAnchor.LowerCenter, Color.white);
                // 피벗을 밑변(0.5, 0)에 둔다: 투영점(position 대입)이 글자 아랫단이 되어
                // "_nameplateHeight = 머리에서 글자까지 거리"가 직관 그대로 맞는다.
                // 가운데 피벗이면 렉트 절반(17px)이 여백으로 더 떠 보인다.
                SetRect(t.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0f),
                        new Vector2(180, 34), Vector2.zero);
                t.fontStyle = FontStyle.Bold;
                _nameplates.Add(new Nameplate { go = t.gameObject, rt = t.rectTransform, text = t });
            }
        }

        // ===================================================================
        // 공용 헬퍼
        // ===================================================================
        private void HideAllPhaseRoots()
        {
            if (_playRoot != null) _playRoot.gameObject.SetActive(false);
            if (_playInfoGo != null) _playInfoGo.SetActive(false); // 상단 알약은 _playRoot 밖(캔버스 직속)이라 따로 끈다
            if (_highlightRoot != null) _highlightRoot.gameObject.SetActive(false);
            if (_resultRoot != null) _resultRoot.gameObject.SetActive(false);
        }

        private void EnsureRows(List<Row> pool, Transform parent, int count)
        {
            while (pool.Count < count) pool.Add(MakeRow(parent));
            for (int i = 0; i < pool.Count; i++)
                pool[i].go.SetActive(i < count);
        }

        private Row MakeRow(Transform parent)
        {
            var r = new Row();
            r.bg = MakePanel(parent, new Color(1f, 1f, 1f, 0f)); // 라운드 코너(로컬 강조/결과 행 배경)
            r.rt = r.bg.rectTransform;
            SetRect(r.rt, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                    new Vector2(-12, ROWH), Vector2.zero);
            r.go = r.bg.gameObject;

            r.swatch = MakeImage(r.go.transform, Color.white);
            SetRect(r.swatch.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                    new Vector2(22, 22), new Vector2(8, 0));

            r.nameText = MakeText(r.go.transform, "", 20, TextAnchor.MiddleLeft, UiKit.Ink, false);
            SetRect(r.nameText.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                    new Vector2(180, ROWH), new Vector2(38, 0));

            r.scoreText = MakeText(r.go.transform, "", 20, TextAnchor.MiddleRight, UiKit.Ink, false);
            SetRect(r.scoreText.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                    new Vector2(90, ROWH), new Vector2(-8, 0));
            return r;
        }

        // outline: 월드 위에 뜨는 텍스트(목표 문구/이름표)만 검정 외곽선. 패널 위 텍스트는 끈다.
        private Text MakeText(Transform parent, string content, int fontSize, TextAnchor anchor, Color col,
                              bool outline = true)
        {
            var go = new GameObject("Text", typeof(Text)); // CanvasRenderer 자동 추가
            go.transform.SetParent(parent, false);          // worldPositionStays=false (필수)
            var t = go.GetComponent<Text>();
            t.font = _font;
            t.text = content;
            t.fontSize = fontSize;
            t.alignment = anchor;
            t.color = col;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            var rt = t.rectTransform;
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(300, 60);
            rt.anchoredPosition = Vector2.zero;

            if (outline)
            {
                var o = go.AddComponent<Outline>();
                o.effectColor = new Color(0f, 0f, 0f, 0.9f);
                o.effectDistance = new Vector2(1.5f, -1.5f);
                o.useGraphicAlpha = true;
            }
            return t;
        }

        private Image MakeImage(Transform parent, Color col)
        {
            var go = new GameObject("Image", typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            // 흰색 1x1 스프라이트를 명시 지정한다. sprite 를 비워 두면(빌트인 폴백) 일부 환경에서
            // 단색 사각형이 그려지지 않는 문제가 있었다(딤 미표시) - 명시 지정이 안전.
            img.sprite = UiKit.WhiteSprite;
            img.color = col;
            img.raycastTarget = false;
            return img;
        }

        // 라운드 코너 패널(UiKit 9-slice 스프라이트). 코드 생성 HUD 의 공통 배경.
        private Image MakePanel(Transform parent, Color col)
        {
            var img = MakeImage(parent, col);
            img.sprite = UiKit.RoundSprite;
            img.type = Image.Type.Sliced;
            return img;
        }

        // UCH 크림 패널(잉크 테두리가 구워진 9-slice).
        private Image MakeCream(Transform parent)
        {
            var img = MakeImage(parent, Color.white);
            img.sprite = UiKit.BorderedSprite(UiKit.Cream, UiKit.Ink);
            img.type = Image.Type.Sliced;
            return img;
        }

        // UCH 컬러 스트립(배너) - 색은 스프라이트에 구워져 있어 틴트는 흰색 유지.
        private Image MakeStrip(Transform parent, Color fill)
        {
            var img = MakeImage(parent, Color.white);
            img.sprite = UiKit.BorderedSprite(fill, UiKit.Ink);
            img.type = Image.Type.Sliced;
            return img;
        }

        private RectTransform MakeRect(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go.GetComponent<RectTransform>();
        }

        private static void SetRect(RectTransform rt, Vector2 aMin, Vector2 aMax, Vector2 pivot,
                                    Vector2 sizeDelta, Vector2 anchoredPos)
        {
            rt.anchorMin = aMin;
            rt.anchorMax = aMax;
            rt.pivot = pivot;
            rt.sizeDelta = sizeDelta;
            rt.anchoredPosition = anchoredPos;
        }

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        // 페이즈별 배너 색(UCH: 준비 노랑 / 플레이 청록 / 하이라이트·대기 파랑 / 결과 빨강).
        private static Color PhaseColor(MatchPhase p)
        {
            switch (p)
            {
                case MatchPhase.Prep:         return UiKit.Yellow;
                case MatchPhase.Play:         return UiKit.Teal;
                case MatchPhase.Highlight:    return UiKit.Blue;
                case MatchPhase.Intermission: return UiKit.Blue;
                case MatchPhase.Result:       return UiKit.Red;
                default:                      return UiKit.Grey;
            }
        }

        private static string PhaseKorean(MatchPhase p)
        {
            switch (p)
            {
                case MatchPhase.Lobby:     return "로비";
                case MatchPhase.Prep:      return "준비";
                case MatchPhase.Play:      return "플레이";
                case MatchPhase.Highlight: return "하이라이트";
                case MatchPhase.Intermission: return "대기 중";
                case MatchPhase.Result:    return "결과";
                default:                   return "";
            }
        }

        // ---- 내부 데이터 타입 ----
        private struct Entry
        {
            public ulong Id;
            public int Score;
        }

        private class Row
        {
            public GameObject go;
            public RectTransform rt;
            public Image bg;
            public Image swatch;
            public Text nameText;
            public Text scoreText;
        }

        private class Nameplate
        {
            public GameObject go;
            public RectTransform rt;
            public Text text;
            // Dead 판정용 컴포넌트 캐시. _players 순서가 바뀔 수 있으므로 어느 플레이어의
            // 캐시인지(source)를 함께 저장하고, 다르면 갱신한다(매 프레임 GetComponent 회피).
            public NetworkObject source;
            public RouletteParty.Net.PlayerController pc;
        }
    }
}
