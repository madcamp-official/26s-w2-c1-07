// GameHUD.cs
// 코드로 전부 생성하는 표시 전용 HUD (프리팹/씬 배선 없음).
// Unity 6000.5.3f1 / URP / com.unity.ugui 2.5.0 (레거시 UnityEngine.UI) / NGO 2.13.0.
// TextMeshPro 미사용, 신규 Input System 전용(구 UnityEngine.Input 사용 금지 → 이 파일은 키 입력 없음).
// 호스트와 순수 클라이언트 모두에서 안전하게 동작해야 함(서버 전용 API 접근 금지).

using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;
using RouletteParty.Match;
using RouletteParty.Net; // PlayerController.IsAiming (조준점 표시 판정)

namespace RouletteParty.UI
{
    public class GameHUD : MonoBehaviour
    {
        // ---- 상수(레이아웃) ----
        private const float ROWH = 34f;     // 점수판 한 줄 높이
        private const float HEADER = 44f;   // 점수판 제목 아래 첫 줄 오프셋
        private const string TTF = "LegacyRuntime.ttf";

        // ---- 공유 폰트 (한 번만 획득) ----
        private Font _font;

        // ---- 정적 스켈레톤 참조 ----
        private Canvas _canvas;
        private RectTransform _canvasRT;

        // 상단 배너
        private Text _phaseLabel;
        private Text _subLabel;       // "라운드 n/3   모드명"
        private Text _countdownLabel; // "n초"

        // 좌측 점수판
        private Image _scorePanel;
        private Text _scoreTitle;
        private readonly List<Row> _scoreRows = new List<Row>();

        // 중앙 페이즈별 루트
        private RectTransform _rouletteRoot;
        private Text _rouletteText;

        private RectTransform _playRoot;
        private Text _playObjective;
        private Text _playSurvive;

        private RectTransform _highlightRoot;
        private Text _hlTitle;
        private Text _hlTrophy;
        private Text _hlTopic;
        private RectTransform _hlRowsParent;
        private readonly List<Row> _hlRows = new List<Row>();

        private RectTransform _resultRoot;   // 전체 화면 최종 순위
        private Text _resChampion;
        private RectTransform _resRowsParent;
        private readonly List<Row> _resRows = new List<Row>();

        // 이름표(월드 오브젝트 → 화면) 풀
        private readonly List<Nameplate> _nameplates = new List<Nameplate>();

        // 화면 중앙 조준점(PLAY 중에만 표시). 마우스룩 조준 시점과 짝을 이룬다.
        private RectTransform _crosshair;

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
        // 자동 부트스트랩: 씬에 GameHUD 가 없으면 플레이 시작 시 자동 생성한다.
        // → 에디터에서 오브젝트를 배치할 필요가 없다. 수동 배치해 두면 이 가드가 중복을 막는다.
        //   끄고 싶으면 이 메서드(또는 [RuntimeInitializeOnLoadMethod] 특성)를 제거하면 된다.
        // ===================================================================
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoBootstrap()
        {
            if (FindAnyObjectByType<GameHUD>() != null) return; // 이미 있으면(수동 배치 포함) 생성 안 함
            var go = new GameObject("GameHUD (auto)");
            go.AddComponent<GameHUD>();
            DontDestroyOnLoad(go);
        }

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
            // 1) 한글 가능한 OS 폰트 우선(설치돼 있으면 그대로 사용).
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
            scaler.matchWidthOrHeight = 0.5f;
            // 표시 전용: GraphicRaycaster/EventSystem 불필요(투표·장애물은 별도 PrepClientUI가 담당).

            _canvasRT = _canvas.GetComponent<RectTransform>();

            // --- 상단 배너 ---
            _phaseLabel = MakeText(_canvasRT, "", 52, TextAnchor.MiddleCenter, Color.white);
            SetRect(_phaseLabel.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                    new Vector2(900, 72), new Vector2(0, -18));

            _subLabel = MakeText(_canvasRT, "", 30, TextAnchor.MiddleCenter, Color.white);
            SetRect(_subLabel.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                    new Vector2(900, 40), new Vector2(0, -92));

            _countdownLabel = MakeText(_canvasRT, "", 34, TextAnchor.MiddleCenter, new Color(1f, 0.92f, 0.4f, 1f));
            SetRect(_countdownLabel.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                    new Vector2(400, 44), new Vector2(0, -134));

            // --- 점수판(우상단) ---
            // 좌상단은 접속(Host/Client) OnGUI 버튼·디버그 패널이 쓰므로 우상단에 배치해 겹침을 피한다.
            _scorePanel = MakeImage(_canvasRT, new Color(0f, 0f, 0f, 0.5f));
            SetRect(_scorePanel.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f),
                    new Vector2(300, 470), new Vector2(-20, -20));

            _scoreTitle = MakeText(_scorePanel.transform, "점수판", 24, TextAnchor.MiddleLeft, Color.white);
            SetRect(_scoreTitle.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                    new Vector2(-16, 32), new Vector2(0, -6));

            // --- 룰렛 루트 ---
            _rouletteRoot = MakeRect(_canvasRT, "RouletteRoot");
            SetRect(_rouletteRoot, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                    new Vector2(700, 200), new Vector2(0, 0));
            _rouletteText = MakeText(_rouletteRoot, "", 84, TextAnchor.MiddleCenter, Color.white);
            Stretch(_rouletteText.rectTransform);

            // --- 플레이 루트 ---
            _playRoot = MakeRect(_canvasRT, "PlayRoot");
            SetRect(_playRoot, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                    new Vector2(1000, 150), new Vector2(0, 120));
            _playObjective = MakeText(_playRoot, "", 44, TextAnchor.MiddleCenter, Color.white);
            SetRect(_playObjective.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                    new Vector2(1000, 60), new Vector2(0, -10));
            _playSurvive = MakeText(_playRoot, "", 32, TextAnchor.MiddleCenter, new Color(1f, 0.55f, 0.55f, 1f));
            SetRect(_playSurvive.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                    new Vector2(1000, 44), new Vector2(0, -74));

            // --- 하이라이트 루트(카드) ---
            _highlightRoot = MakeRect(_canvasRT, "HighlightRoot");
            SetRect(_highlightRoot, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                    new Vector2(560, 440), new Vector2(0, 0));
            var hlBg = MakeImage(_highlightRoot, new Color(0f, 0f, 0f, 0.72f));
            Stretch(hlBg.rectTransform);
            hlBg.rectTransform.SetAsFirstSibling();

            _hlTitle = MakeText(_highlightRoot, "", 36, TextAnchor.MiddleCenter, Color.white);
            SetRect(_hlTitle.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                    new Vector2(540, 48), new Vector2(0, -16));
            _hlTrophy = MakeText(_highlightRoot, "", 40, TextAnchor.MiddleCenter, Color.white);
            SetRect(_hlTrophy.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                    new Vector2(540, 52), new Vector2(0, -70));
            _hlTopic = MakeText(_highlightRoot, "", 26, TextAnchor.MiddleCenter, new Color(0.8f, 0.85f, 1f, 1f));
            SetRect(_hlTopic.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                    new Vector2(540, 36), new Vector2(0, -126));
            _hlRowsParent = MakeRect(_highlightRoot, "HLRows");
            SetRect(_hlRowsParent, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                    new Vector2(500, 200), new Vector2(0, -170));

            // --- 결과 루트(전체 화면) ---
            _resultRoot = MakeRect(_canvasRT, "ResultRoot");
            Stretch(_resultRoot);
            var resBg = MakeImage(_resultRoot, new Color(0f, 0f, 0f, 0.8f));
            Stretch(resBg.rectTransform);
            resBg.rectTransform.SetAsFirstSibling();

            _resChampion = MakeText(_resultRoot, "", 60, TextAnchor.MiddleCenter, Color.white);
            SetRect(_resChampion.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                    new Vector2(1200, 90), new Vector2(0, -120));
            _resRowsParent = MakeRect(_resultRoot, "ResRows");
            SetRect(_resRowsParent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                    new Vector2(520, 500), new Vector2(0, -40));

            // --- 조준점(화면 중앙 십자) ---
            _crosshair = MakeRect(_canvasRT, "Crosshair");
            SetRect(_crosshair, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                    new Vector2(24, 24), Vector2.zero);
            var chH = MakeImage(_crosshair, new Color(1f, 1f, 1f, 0.85f)); // 가로 바
            SetRect(chH.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                    new Vector2(18, 2), Vector2.zero);
            var chV = MakeImage(_crosshair, new Color(1f, 1f, 1f, 0.85f)); // 세로 바
            SetRect(chV.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                    new Vector2(2, 18), Vector2.zero);
            _crosshair.gameObject.SetActive(false);

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

            // --- 상단 배너 ---
            _phaseLabel.text = PhaseKorean(m.CurrentPhase);
            _phaseLabel.color = Color.white;

            string sub = "";
            if (m.Round >= 1) sub = "라운드 " + m.Round + "/3";
            string tk = TopicKorean(m.Topic);
            if (tk.Length > 0) sub += (sub.Length > 0 ? "   " : "") + tk;
            _subLabel.text = sub;

            float rem = m.PhaseRemaining; // 이미 클램프됨, 클라이언트 안전
            _countdownLabel.text = Mathf.CeilToInt(Mathf.Max(0f, rem)) + "초";

            // --- 좌측 점수판(누적 점수) ---
            BuildStandings(m);
            FillScoreboard(localId);

            // --- 중앙 페이즈별 콘텐츠 ---
            HideAllPhaseRoots();
            switch (m.CurrentPhase)
            {
                case MatchPhase.Roulette: RenderRoulette(m, rem); break;
                case MatchPhase.Play:     RenderPlay(m);          break;
                case MatchPhase.Highlight:RenderHighlight(m);     break;
                case MatchPhase.Result:   RenderResult(m);        break;
                default: /* Lobby/Prep: 중앙 콘텐츠 없음 */ break;
            }

            // --- 조준점: 로컬 플레이어가 조준 시점일 때 표시(결과 화면 제외) ---
            bool localAiming = false;
            for (int i = 0; i < _players.Count; i++)
            {
                if (_players[i].OwnerClientId != localId) continue;
                var pc = _players[i].GetComponent<PlayerController>();
                if (pc != null) localAiming = pc.IsAiming;
                break;
            }
            if (_crosshair != null)
                _crosshair.gameObject.SetActive(localAiming && m.CurrentPhase != MatchPhase.Result);

            // --- 이름표 ---
            RenderNameplates(cam);
        }

        // 네트워크 미준비/스폰 전: 빈 HUD.
        private void SetIdle(Camera cam)
        {
            if (!_built) return;
            _phaseLabel.text = "";
            _subLabel.text = "";
            _countdownLabel.text = "";
            EnsureRows(_scoreRows, _scorePanel.transform, 0);
            HideAllPhaseRoots();
            if (_crosshair != null) _crosshair.gameObject.SetActive(false);
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
                row.nameText.color = Color.white;
                row.scoreText.text = e.Score.ToString();
                row.scoreText.fontStyle = isLocal ? FontStyle.Bold : FontStyle.Normal;

                // 로컬 플레이어 강조 박스
                row.bg.color = isLocal ? new Color(1f, 0.95f, 0.35f, 0.20f) : new Color(1f, 1f, 1f, 0f);
            }
        }

        // ===================================================================
        // 페이즈별 렌더링
        // ===================================================================
        private static readonly string[] ModeNames = { "달리기", "높이", "생존" }; // Race,Height,Survive

        private void RenderRoulette(MatchManager m, float rem)
        {
            _rouletteRoot.gameObject.SetActive(true);

            int landing = TopicIndex(m.Topic); // 이미 결정된 실제 Topic

            int idx;
            if (rem <= 0.4f)
            {
                // 마지막엔 실제 Topic에 정착.
                idx = landing;
                _rouletteText.color = Color.white;
            }
            else
            {
                // 남은 시간이 줄수록 간격이 커져 감속(약 5초 페이즈 가정, 순수 시각 효과).
                float interval = Mathf.Lerp(0.05f, 0.35f, 1f - Mathf.Clamp01(rem / 5f));
                idx = Mathf.FloorToInt(Time.time / Mathf.Max(0.02f, interval)) % 3;
                if (idx < 0) idx += 3;
                _rouletteText.color = Color.white;
            }
            _rouletteText.text = "◤ " + ModeNames[idx] + " ◢";
        }

        private void RenderPlay(MatchManager m)
        {
            _playRoot.gameObject.SetActive(true);
            switch (m.Topic)
            {
                case TopicMode.Race:    _playObjective.text = "결승선까지 달려라!"; break;
                case TopicMode.Height:  _playObjective.text = "가장 높이 올라가라!"; break;
                case TopicMode.Survive: _playObjective.text = "끝까지 살아남아라!"; break;
                default:                _playObjective.text = ""; break;
            }
            if (m.Topic == TopicMode.Survive)
            {
                _playSurvive.gameObject.SetActive(true);
                _playSurvive.text = "생존 " + m.AliveCount;
            }
            else
            {
                _playSurvive.gameObject.SetActive(false);
            }
        }

        private void RenderHighlight(MatchManager m)
        {
            _highlightRoot.gameObject.SetActive(true);
            _hlTitle.text = "라운드 " + m.Round + " 결과";

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

            _hlTopic.text = TopicKorean(m.Topic);

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

            EnsureRows(_hlRows, _hlRowsParent.transform, show);
            for (int i = 0; i < show; i++)
            {
                var r = _roundResults[i];
                var row = _hlRows[i];
                row.rt.anchoredPosition = new Vector2(0, -(i * ROWH));
                row.bg.color = new Color(1f, 1f, 1f, 0f);
                row.swatch.color = PlayerPalette.ColorFor(r.ClientId);
                row.nameText.text = "#" + r.Rank + "  " + PlayerPalette.NameFor(r.ClientId);
                row.nameText.color = PlayerPalette.ColorFor(r.ClientId);
                row.nameText.fontStyle = FontStyle.Normal;
                row.scoreText.text = Mathf.RoundToInt(r.Score).ToString();
                row.scoreText.color = Color.white;
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
                row.rt.anchoredPosition = new Vector2(0, -(i * (ROWH + 4f)));
                row.bg.color = new Color(1f, 1f, 1f, 0.06f);
                row.swatch.color = PlayerPalette.ColorFor(e.Id);
                row.nameText.text = (i + 1) + ".  " + PlayerPalette.NameFor(e.Id);
                row.nameText.color = PlayerPalette.ColorFor(e.Id);
                row.nameText.fontStyle = i == 0 ? FontStyle.Bold : FontStyle.Normal;
                row.scoreText.text = e.Score.ToString();
                row.scoreText.color = Color.white;
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

                ulong id = no.OwnerClientId;
                Vector3 world = no.transform.position + Vector3.up * 2.2f;
                Vector3 sp = cam.WorldToScreenPoint(world);

                var np = _nameplates[i];
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
                var t = MakeText(_canvasRT, "", 22, TextAnchor.MiddleCenter, Color.white);
                SetRect(t.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
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
            if (_rouletteRoot != null) _rouletteRoot.gameObject.SetActive(false);
            if (_playRoot != null) _playRoot.gameObject.SetActive(false);
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
            r.bg = MakeImage(parent, new Color(1f, 1f, 1f, 0f));
            r.rt = r.bg.rectTransform;
            SetRect(r.rt, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                    new Vector2(-12, ROWH), Vector2.zero);
            r.go = r.bg.gameObject;

            r.swatch = MakeImage(r.go.transform, Color.white);
            SetRect(r.swatch.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                    new Vector2(22, 22), new Vector2(8, 0));

            r.nameText = MakeText(r.go.transform, "", 20, TextAnchor.MiddleLeft, Color.white);
            SetRect(r.nameText.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                    new Vector2(180, ROWH), new Vector2(38, 0));

            r.scoreText = MakeText(r.go.transform, "", 20, TextAnchor.MiddleRight, Color.white);
            SetRect(r.scoreText.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                    new Vector2(90, ROWH), new Vector2(-8, 0));
            return r;
        }

        private Text MakeText(Transform parent, string content, int fontSize, TextAnchor anchor, Color col)
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

            var o = go.AddComponent<Outline>();
            o.effectColor = new Color(0f, 0f, 0f, 0.9f);
            o.effectDistance = new Vector2(1.5f, -1.5f);
            o.useGraphicAlpha = true;
            return t;
        }

        private Image MakeImage(Transform parent, Color col)
        {
            var go = new GameObject("Image", typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.color = col; // sprite 없음 → 빌트인 흰색 1x1을 color로 틴트
            img.raycastTarget = false;
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

        private static int TopicIndex(TopicMode t)
        {
            int i = (int)t - 1; // None=-1, Race=0, Height=1, Survive=2
            if (i < 0 || i > 2) i = 0;
            return i;
        }

        private static string PhaseKorean(MatchPhase p)
        {
            switch (p)
            {
                case MatchPhase.Lobby:     return "로비";
                case MatchPhase.Prep:      return "준비";
                case MatchPhase.Roulette:  return "룰렛";
                case MatchPhase.Play:      return "플레이";
                case MatchPhase.Highlight: return "하이라이트";
                case MatchPhase.Result:    return "결과";
                default:                   return "";
            }
        }

        private static string TopicKorean(TopicMode t)
        {
            switch (t)
            {
                case TopicMode.Race:    return "달리기";
                case TopicMode.Height:  return "높이";
                case TopicMode.Survive: return "생존";
                default:                return "";
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
        }
    }
}
