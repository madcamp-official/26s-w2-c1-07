using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using RouletteParty.Match; // MatchManager (로비 목록/RPC)
using RouletteParty.Net;   // ConnectionService (방 만들기/참가/나가기)

namespace RouletteParty.UI
{
    /// <summary>
    /// 타이틀 -> 방 만들기/방 참가 -> 대기방 -> 게임 의 사용자 화면 흐름(LAN 시연용).
    ///
    /// 화면 상태(동시에 하나만 표시, 게임 중에는 아무것도 그리지 않음):
    ///   Title(타이틀/닉네임) -> Join(IP:포트 입력) -> Connecting(접속 중) -> Waiting(대기방) -> InGame
    ///
    /// 상태는 이벤트 구독 없이 매 프레임 NetworkManager/MatchManager 에서 유도한다
    /// (중복 구독·해제 누락 버그 원천 차단). 전환 감지로 끊김 사유 메시지만 채운다.
    ///
    /// UI 는 "의도"만 ConnectionService(연결)와 LobbyManager(로비 RPC)에 전달한다.
    /// 렌더링은 기존 프로젝트 관례(IMGUI + ImguiScale 1080p 가상 픽셀)를 따른다
    /// (다른 OnGUI 패널과 동일한 해상도 대응. 정식 UI 는 추후 uGUI 프리팹 교체 대상).
    /// 씬 배치: 전용 GameObject(예: "LobbyUI")에 컴포넌트로 추가한다(에디터 Add Component).
    /// </summary>
    [DisallowMultipleComponent]
    public class LobbyUI : MonoBehaviour
    {
        private enum View { Title, Join, Connecting, Waiting, InGame }
        private enum NetState { Offline, Busy, Connecting, Online }

        private const string PREF_NICK = "lobby_nickname";
        private const int NAME_MAX = 12;

        [Header("접속")]
        [Tooltip("\"연결 중\" 상태의 무한 대기 방지 타임아웃(초).")]
        [SerializeField] private float _connectTimeout = 10f;

        private View _view = View.Title;
        private NetState _prevNet = NetState.Offline;

        private string _nickname;
        private string _joinIp = "192.168.";
        private string _joinPortText = "7777";
        private string _joinCode = "";      // 릴레이 모드 참가 코드 입력
        private string _message = "";   // 상태/오류 안내(화면 하단)
        private float _connectStart;
        private bool _timedOut;         // 타임아웃으로 인한 종료였는지(메시지 구분용)
        private bool _localLeave;       // 내가 나가기/취소를 눌렀는지(메시지 구분용)
        private bool _nameSent;         // 접속 후 닉네임 RPC 1회 전송 플래그

        // IMGUI 스타일(1080p 가상 픽셀 기준, OnGUI 안에서 1회 생성)
        private GUIStyle _stTitle, _stH2, _stLabel, _stSmall, _stInput, _stRow, _stPanel, _stBannerText;
        private readonly System.Collections.Generic.Dictionary<string, GUIStyle> _btnCache
            = new System.Collections.Generic.Dictionary<string, GUIStyle>();
        private readonly System.Collections.Generic.Dictionary<string, GUIStyle> _stripCache
            = new System.Collections.Generic.Dictionary<string, GUIStyle>();

        private void Awake()
        {
            _nickname = PlayerPrefs.GetString(PREF_NICK, "플레이어");
        }

        private void Start()
        {
            // 기본 포트는 ConnectionService 인스펙터 설정을 따른다(모든 Awake 이후라 안전).
            var cs = ConnectionService.Instance;
            if (cs != null) _joinPortText = cs.DefaultPort.ToString();
        }

        // ============================ 상태 머신 ============================
        private static NetState DeriveNet()
        {
            var nm = NetworkManager.Singleton;
            if (nm == null) return NetState.Offline;
            if (nm.ShutdownInProgress) return NetState.Busy;
            if (nm.IsServer) return NetState.Online;                               // 호스트/서버
            if (nm.IsClient) return nm.IsConnectedClient ? NetState.Online : NetState.Connecting;
            return NetState.Offline;
        }

        private void Update()
        {
            NetState net = DeriveNet();
            if (net != _prevNet)
            {
                HandleTransition(_prevNet, net);
                _prevNet = net;
            }

            // 릴레이 비동기 절차(로그인/할당/참가)의 실패 사유를 UI 메시지로 수거.
            var csvc = ConnectionService.Instance;
            if (csvc != null)
            {
                string asyncErr = csvc.ConsumeLastError();
                if (!string.IsNullOrEmpty(asyncErr)) _message = asyncErr;
            }

            switch (net)
            {
                case NetState.Offline:
                case NetState.Busy:
                    if (csvc != null && csvc.RelayBusy)
                    {
                        // 릴레이 준비 중(아직 NGO 시작 전이라 NetState 는 Offline) -> 접속 중 화면 유지.
                        _view = View.Connecting;
                    }
                    else if (_view == View.Connecting || _view == View.Waiting || _view == View.InGame)
                    {
                        // 오프라인이 된 화면 정리(메시지는 HandleTransition 이 채움).
                        _view = View.Title;
                    }
                    break;

                case NetState.Connecting:
                    _view = View.Connecting;
                    if (!_timedOut && Time.unscaledTime - _connectStart > _connectTimeout)
                    {
                        _timedOut = true;
                        NetworkManager.Singleton.Shutdown(); // 전환 감지가 타임아웃 메시지를 채운다
                    }
                    break;

                case NetState.Online:
                {
                    // 접속 직후 닉네임 1회 보고(호스트 포함. LobbyManager 스폰 대기).
                    var lm = LobbyManager.Instance;
                    if (!_nameSent && lm != null && lm.IsSpawned)
                    {
                        lm.SetPlayerNameServerRpc(new FixedString64Bytes(SanitizeName(_nickname)));
                        _nameSent = true;
                    }
                    var mm = MatchManager.Instance;
                    bool inLobby = mm == null || !mm.IsSpawned || mm.CurrentPhase == MatchPhase.Lobby;
                    _view = inLobby ? View.Waiting : View.InGame;
                    break;
                }
            }

            // 게임 화면 밖에서는 커서를 항상 풀어준다(플레이 중 커서는 PlayerController 정책).
            if (_view != View.InGame)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
        }

        // 연결 상태 전환 시 사용자 안내 메시지 결정. 확인 가능한 정보(DisconnectReason)까지만
        // 정확히 표시하고, 알 수 없는 원인을 단정하지 않는다.
        private void HandleTransition(NetState prev, NetState now)
        {
            var nm = NetworkManager.Singleton;
            string reason = nm != null ? nm.DisconnectReason : null;

            // 접속 시작 시각 보정: LobbyUI 밖(디버그 패널 등)에서 StartClient 된 경우에도
            // 타임아웃 기준점이 "지금"이 되도록 전환 시점에 갱신한다.
            if (now == NetState.Connecting) _connectStart = Time.unscaledTime;

            if (prev == NetState.Connecting && (now == NetState.Offline || now == NetState.Busy))
            {
                if (_timedOut)                          _message = "연결 시간이 초과되었습니다. 주소·포트·방화벽을 확인하세요.";
                else if (_localLeave)                   _message = "접속을 취소했습니다.";
                else if (!string.IsNullOrEmpty(reason)) _message = reason; // 서버 거절 사유(가득 참/진행 중 등)
                else                                    _message = "서버에 연결할 수 없습니다. 주소·포트·방화벽을 확인하세요.";
                _view = View.Join; // 입력값을 유지한 채 참가 화면으로 복귀
            }
            else if (prev == NetState.Online && (now == NetState.Offline || now == NetState.Busy))
            {
                if (_localLeave)                        _message = "방에서 나왔습니다.";
                else if (!string.IsNullOrEmpty(reason)) _message = reason;
                else                                    _message = "호스트와 연결이 끊어졌습니다.";
                _view = View.Title;
            }
            else if (now == NetState.Online)
            {
                _message = "";
            }

            if (now == NetState.Offline || now == NetState.Busy) _nameSent = false;
            _timedOut = false;
            _localLeave = false;
        }

        // ============================ 버튼 동작 ============================
        private void OnCreateRoom()
        {
            SaveNickname();
            var cs = ConnectionService.Instance;
            if (cs == null) { _message = "ConnectionService 가 씬에 없습니다(NetworkManager 오브젝트 확인)."; return; }
            if (!cs.CreateRoom(cs.DefaultPort, out string err)) { _message = err; return; }
            // 성공 시 Update 가 Online 전환을 감지해 대기방으로 넘어간다.
            // 릴레이 모드는 비동기(로그인+할당, 수 초)라 그동안 접속 중 화면이 표시된다.
            _message = "";
            _connectStart = Time.unscaledTime;
        }

        private void OnJoin()
        {
            SaveNickname();

            var cs = ConnectionService.Instance;
            if (cs == null) { _message = "ConnectionService 가 씬에 없습니다(NetworkManager 오브젝트 확인)."; return; }

            if (cs.UseRelay)
            {
                // 릴레이: 참가 코드로 접속(대소문자 무관 -> 대문자 정규화).
                string code = (_joinCode ?? "").Trim().ToUpperInvariant();
                if (code.Length < 4)
                {
                    _message = "참가 코드를 입력하세요(호스트 대기방 화면에 표시됩니다).";
                    return;
                }
                if (!cs.JoinRoomByCode(code, out string errRelay)) { _message = errRelay; return; }
            }
            else
            {
                string ip = (_joinIp ?? "").Trim();
                if (ip.Equals("localhost", System.StringComparison.OrdinalIgnoreCase))
                    ip = "127.0.0.1"; // 같은 PC 개발 테스트 전용(다른 PC 접속에는 호스트 LAN IP 필요)
                // IPAddress.TryParse 는 "192.168.0" 같은 축약 표기도 통과시키므로 4옥텟을 강제한다.
                if (ip.Split('.').Length != 4 ||
                    !System.Net.IPAddress.TryParse(ip, out var parsed) ||
                    parsed.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
                {
                    _message = "IPv4 주소 형식이 아닙니다. 예: 192.168.0.10";
                    return;
                }
                if (!ushort.TryParse((_joinPortText ?? "").Trim(), out ushort port) || port == 0)
                {
                    _message = "포트는 1~65535 사이 숫자여야 합니다.";
                    return;
                }
                if (!cs.JoinRoom(ip, port, out string err)) { _message = err; return; }
            }

            _message = "";
            _timedOut = false;
            _localLeave = false;
            _connectStart = Time.unscaledTime;
            _view = View.Connecting;
        }

        private void OnLeave()
        {
            _localLeave = true;
            var cs = ConnectionService.Instance;
            if (cs != null) cs.Leave();
        }

        private static void QuitGame()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private void SaveNickname()
        {
            _nickname = SanitizeName(_nickname);
            PlayerPrefs.SetString(PREF_NICK, _nickname);
            PlayerPrefs.Save();
        }

        // 앞뒤 공백 제거 / 빈 문자열 기본 이름 / 길이 제한(서버도 같은 규칙으로 재검증).
        private static string SanitizeName(string raw)
        {
            string s = (raw ?? "").Trim();
            if (s.Length == 0) return "플레이어";
            return s.Length > NAME_MAX ? s.Substring(0, NAME_MAX) : s;
        }

        // ============================ 렌더링 ============================
        private void OnGUI()
        {
            if (_view == View.InGame) return;

            ImguiScale.Apply(); // 이하 좌표는 1080p 기준 가상 픽셀
            EnsureStyles();

            float w = ImguiScale.VirtualWidth, h = ImguiScale.VirtualHeight;

            // 배경 딤: 패널이 불투명 크림이라 옅은 딤으로 충분(스테이지/스카이 배경 노출).
            Color old = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, _view == View.Waiting ? 0.20f : 0.30f);
            GUI.DrawTexture(new Rect(0, 0, w, h), Texture2D.whiteTexture);
            GUI.color = old;

            switch (_view)
            {
                case View.Title:      DrawTitle(w, h); break;
                case View.Join:       DrawJoin(w, h); break;
                case View.Connecting: DrawConnecting(w, h); break;
                case View.Waiting:    DrawWaiting(w, h); break;
            }
        }

        private void EnsureStyles()
        {
            if (_stTitle != null) return;
            // 얼티밋 치킨 호스 톤: 크림 패널 + 잉크 텍스트/테두리 + 채도 높은 버튼.
            _stTitle  = new GUIStyle(GUI.skin.label)     { fontSize = 50, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            _stH2     = new GUIStyle(GUI.skin.label)     { fontSize = 28, alignment = TextAnchor.MiddleLeft, fontStyle = FontStyle.Bold };
            _stLabel  = new GUIStyle(GUI.skin.label)     { fontSize = 24 };
            _stSmall  = new GUIStyle(GUI.skin.label)     { fontSize = 20, wordWrap = true };
            _stRow    = new GUIStyle(GUI.skin.label)     { fontSize = 26, richText = true };
            _stTitle.normal.textColor = UiKit.Ink;
            _stH2.normal.textColor    = UiKit.Ink;
            _stLabel.normal.textColor = UiKit.Ink;
            _stSmall.normal.textColor = UiKit.InkSoft;
            _stRow.normal.textColor   = UiKit.Ink;

            // 배너 위 흰색 볼드(컬러 스트립 헤더 텍스트).
            _stBannerText = new GUIStyle(GUI.skin.label)
            { fontSize = 44, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            _stBannerText.normal.textColor = Color.white;

            // 입력 필드: 밝은 바탕 + 잉크 테두리 + 잉크 텍스트.
            _stInput = new GUIStyle(GUI.skin.textField)
            {
                fontSize = 28,
                alignment = TextAnchor.MiddleLeft,
                border = new RectOffset(UiKit.ImguiBorder, UiKit.ImguiBorder, UiKit.ImguiBorder, UiKit.ImguiBorder),
                padding = new RectOffset(16, 16, 8, 8),
            };
            var inputTex = UiKit.BorderedTex(Color.white, UiKit.Ink);
            _stInput.normal.background = inputTex;
            _stInput.focused.background = inputTex;
            _stInput.hover.background = inputTex;
            _stInput.normal.textColor = UiKit.Ink;
            _stInput.focused.textColor = UiKit.Ink;
            _stInput.hover.textColor = UiKit.Ink;

            // 크림 패널(잉크 테두리 9-slice).
            _stPanel = new GUIStyle(GUI.skin.box)
            {
                border = new RectOffset(UiKit.ImguiBorder, UiKit.ImguiBorder, UiKit.ImguiBorder, UiKit.ImguiBorder),
                padding = new RectOffset(30, 30, 24, 24),
            };
            _stPanel.normal.background = UiKit.BorderedTex(UiKit.Cream, UiKit.Ink);
        }

        // 컬러 버튼(기본/호버/눌림 3상태, 잉크 테두리 + 흰색 볼드 텍스트). 색·크기별 캐시.
        private GUIStyle Btn(Color fill, int fontSize)
        {
            string key = ColorUtility.ToHtmlStringRGB(fill) + fontSize;
            if (_btnCache.TryGetValue(key, out var st)) return st;
            st = new GUIStyle(GUI.skin.button)
            {
                fontSize = fontSize,
                fontStyle = FontStyle.Bold,
                border = new RectOffset(UiKit.ImguiBorder, UiKit.ImguiBorder, UiKit.ImguiBorder, UiKit.ImguiBorder),
                padding = new RectOffset(14, 14, 8, 8),
            };
            Texture2D n, h, a;
            UiKit.ButtonTex(fill, out n, out h, out a);
            st.normal.background = n; st.hover.background = h; st.active.background = a; st.focused.background = n;
            st.normal.textColor = Color.white; st.hover.textColor = Color.white;
            st.active.textColor = Color.white; st.focused.textColor = Color.white;
            _btnCache[key] = st;
            return st;
        }

        // 컬러 스트립(배너 배경) 스타일. 색별 캐시.
        private GUIStyle Strip(Color fill)
        {
            string key = ColorUtility.ToHtmlStringRGB(fill);
            if (_stripCache.TryGetValue(key, out var st)) return st;
            st = new GUIStyle(GUI.skin.box)
            { border = new RectOffset(UiKit.ImguiBorder, UiKit.ImguiBorder, UiKit.ImguiBorder, UiKit.ImguiBorder) };
            st.normal.background = UiKit.BorderedTex(fill, UiKit.Ink);
            _stripCache[key] = st;
            return st;
        }

        // 살짝 기울어진 컬러 배너(패널 상단에 겹쳐 그리는 헤더 탭 - 얼티밋 치킨 호스 특유의 장난기).
        private void DrawBanner(Rect area, string text, Color fill, float tiltDeg, Color textColor)
        {
            Matrix4x4 old = GUI.matrix;
            GUIUtility.RotateAroundPivot(tiltDeg, area.center);
            GUI.Box(area, GUIContent.none, Strip(fill));
            Color oc = _stBannerText.normal.textColor;
            _stBannerText.normal.textColor = textColor;
            GUI.Label(area, text, _stBannerText);
            _stBannerText.normal.textColor = oc;
            GUI.matrix = old;
        }

        private static Rect CenterRect(float w, float h, float pw, float ph) =>
            new Rect((w - pw) * 0.5f, (h - ph) * 0.5f, pw, ph);

        // ---------------- 타이틀 ----------------
        private void DrawTitle(float w, float h)
        {
            Rect panel = CenterRect(w, h, 580, 580);
            GUILayout.BeginArea(panel, _stPanel);
            GUILayout.Space(58); // 상단 배너(패널 밖에 겹침) 자리
            GUILayout.Label("참가 코드로 함께 오르는 파티 클라이밍", new GUIStyle(_stSmall) { alignment = TextAnchor.MiddleCenter });
            GUILayout.Space(30);

            GUILayout.Label($"닉네임 (최대 {NAME_MAX}자)", _stLabel);
            _nickname = GUILayout.TextField(_nickname ?? "", NAME_MAX, _stInput, GUILayout.Height(52));

            GUILayout.Space(30);
            bool busy = DeriveNet() == NetState.Busy;
            GUI.enabled = !busy;
            if (GUILayout.Button("방 만들기", Btn(UiKit.Red, 30), GUILayout.Height(64))) OnCreateRoom();
            GUILayout.Space(12);
            if (GUILayout.Button("방 참가", Btn(UiKit.Teal, 30), GUILayout.Height(64))) { _message = ""; _view = View.Join; }
            GUILayout.Space(12);
            if (GUILayout.Button("종료", Btn(UiKit.Grey, 24), GUILayout.Height(48))) QuitGame();
            GUI.enabled = true;

            GUILayout.FlexibleSpace();
            if (busy) GUILayout.Label("이전 세션 종료 처리 중...", _stSmall);
            if (!string.IsNullOrEmpty(_message)) GUILayout.Label(_message, _stSmall);
            GUILayout.Space(14);
            GUILayout.EndArea();

            // 타이틀 배너(패널 상단에 겹쳐 살짝 기울임).
            DrawBanner(new Rect(panel.x + 50, panel.y - 34, panel.width - 100, 82), "클라이밍 파티", UiKit.Yellow, -2f, UiKit.Ink);
        }

        // ---------------- 방 참가 ----------------
        private void DrawJoin(float w, float h)
        {
            Rect panel = CenterRect(w, h, 580, 520);
            GUILayout.BeginArea(panel, _stPanel);
            GUILayout.Space(52);

            bool relayJoin = ConnectionService.Instance != null && ConnectionService.Instance.UseRelay;
            if (relayJoin)
            {
                GUILayout.Label("참가 코드 (호스트 대기방 화면에 표시)", _stLabel);
                _joinCode = GUILayout.TextField(_joinCode ?? "", 12, _stInput, GUILayout.Height(52));
                GUILayout.Label("서로 다른 네트워크(다른 와이파이/집)에서도 접속됩니다.", _stSmall);
            }
            else
            {
                GUILayout.Label("Host IP (호스트 화면에 표시된 접속 주소)", _stLabel);
                _joinIp = GUILayout.TextField(_joinIp ?? "", 64, _stInput, GUILayout.Height(52));

                GUILayout.Space(10);
                GUILayout.Label("Port", _stLabel);
                _joinPortText = GUILayout.TextField(_joinPortText ?? "", 6, _stInput, GUILayout.Height(52));
            }

            GUILayout.Space(26);
            if (GUILayout.Button("참가", Btn(UiKit.Teal, 30), GUILayout.Height(64))) OnJoin();
            GUILayout.Space(12);
            if (GUILayout.Button("뒤로", Btn(UiKit.Grey, 24), GUILayout.Height(48))) { _message = ""; _view = View.Title; }

            GUILayout.FlexibleSpace();
            if (!string.IsNullOrEmpty(_message)) GUILayout.Label(_message, _stSmall);
            GUILayout.Space(14);
            GUILayout.EndArea();

            DrawBanner(new Rect(panel.x + 90, panel.y - 32, panel.width - 180, 74), "방 참가", UiKit.Teal, 1.5f, Color.white);
        }

        // ---------------- 접속 중 ----------------
        private void DrawConnecting(float w, float h)
        {
            var cs = ConnectionService.Instance;
            bool relayPrep = cs != null && cs.RelayBusy; // 릴레이 로그인/할당 진행 중(NGO 시작 전)
            bool hostSide = NetworkManager.Singleton == null ||
                            (!NetworkManager.Singleton.IsClient && string.IsNullOrEmpty(cs != null ? cs.JoinTarget : ""));

            GUILayout.BeginArea(CenterRect(w, h, 560, 300), _stPanel);
            GUILayout.Space(30);
            int sec = Mathf.FloorToInt(Time.unscaledTime - _connectStart);
            GUILayout.Label(relayPrep ? "릴레이 준비 중…" : "서버에 연결하는 중…", _stTitle);
            GUILayout.Label($"{(cs != null && !hostSide ? cs.JoinTarget : "")}  ({sec}초)",
                            new GUIStyle(_stLabel) { alignment = TextAnchor.MiddleCenter });
            GUILayout.FlexibleSpace();
            // 릴레이 준비 단계는 중간 취소가 불가(비동기 완료 후 상태로 정리됨) -> 버튼 숨김.
            if (!relayPrep && GUILayout.Button("취소", Btn(UiKit.Grey, 24), GUILayout.Height(48)))
            {
                _localLeave = true;
                var nm = NetworkManager.Singleton;
                if (nm != null) nm.Shutdown();
            }
            GUILayout.Space(16);
            GUILayout.EndArea();
        }

        // ---------------- 대기방 ----------------
        private void DrawWaiting(float w, float h)
        {
            var nm = NetworkManager.Singleton;
            var lm = LobbyManager.Instance;
            var cs = ConnectionService.Instance;
            bool isServer = nm != null && nm.IsServer;

            Rect panel = CenterRect(w, h, 820, 760);
            GUILayout.BeginArea(panel, _stPanel);
            GUILayout.Space(50);

            // ---- 접속 정보(릴레이: 참가 코드 / LAN: IP:포트) ----
            bool relay = cs != null && cs.UseRelay;
            string addr;
            if (isServer)
                addr = relay ? (string.IsNullOrEmpty(cs.JoinCode) ? "발급 중..." : cs.JoinCode)
                             : (cs != null ? cs.HostFullAddress : "?");
            else
                addr = cs != null ? cs.JoinTarget : "?";

            GUILayout.BeginHorizontal();
            GUILayout.Label(relay ? $"참가 코드  <b>{addr}</b>" : $"접속 주소  <b>{addr}</b>", _stRow);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(relay ? "코드 복사" : "주소 복사", Btn(UiKit.Blue, 20), GUILayout.Width(140), GUILayout.Height(42)))
            {
                GUIUtility.systemCopyBuffer = addr;
                _message = relay ? "참가 코드를 복사했습니다." : "접속 주소를 복사했습니다.";
            }
            GUILayout.EndHorizontal();

            if (isServer && cs != null)
            {
                if (relay)
                {
                    GUILayout.Label("참가자에게 위 참가 코드를 알려주세요. 서로 다른 네트워크(다른 와이파이/집)에서도 접속됩니다.", _stSmall);
                }
                else
                {
                    if (cs.PortFallbackUsed)
                        GUILayout.Label($"기본 포트({cs.DefaultPort})가 사용 중이라 {cs.ActivePort} 포트로 열렸습니다. 참가자는 이 포트를 입력해야 합니다.", _stSmall);
                    if (string.IsNullOrEmpty(cs.PrimaryLanIp))
                        GUILayout.Label("LAN IPv4 주소를 찾지 못했습니다. 호스트 PC 에서 ipconfig 로 IPv4 주소를 확인해 참가자에게 알려주세요.", _stSmall);
                    else if (cs.LanIpCandidates.Count > 1)
                        GUILayout.Label("주소가 여러 개 감지됨(VPN/가상 어댑터 가능): " + string.Join(", ", cs.LanIpCandidates), _stSmall);
                    GUILayout.Label("같은 네트워크(공유기)에 연결된 참가자에게 위 접속 주소를 알려주세요.", _stSmall);
                }
            }

            GUILayout.Space(12);

            // ---- 참가자 목록 ----
            int count = 0, min = 2;
            bool allReady = false, meHost = false, meReady = false;
            if (lm != null && lm.IsSpawned && nm != null)
            {
                var list = lm.Players;
                count = list.Count;
                min = lm.MinPlayers;
                allReady = count > 0;

                GUILayout.Label($"인원  {count}/{lm.MaxPlayers}", _stH2);
                GUILayout.Space(4);

                for (int i = 0; i < count; i++)
                {
                    var p = list[i];
                    if (!p.Ready) allReady = false;
                    bool isMe = p.ClientId == nm.LocalClientId;
                    if (isMe) { meHost = p.IsHost; meReady = p.Ready; }

                    GUILayout.BeginHorizontal(GUILayout.Height(46));
                    Color oc = GUI.color;
                    GUI.color = PlayerPalette.ColorFor(p.ClientId);
                    GUILayout.Label("■", _stRow, GUILayout.Width(40));
                    GUI.color = oc;
                    string label = (p.IsHost ? "[방장] " : "") + (isMe ? $"<b>{p.Name}</b> (나)" : p.Name.ToString());
                    GUILayout.Label(label, _stRow);
                    GUILayout.FlexibleSpace();
                    GUILayout.Label(p.Ready
                        ? "<color=#5FA83E><b>준비 완료</b></color>"
                        : "<color=#6B6156>준비 중</color>", _stRow, GUILayout.Width(160));
                    GUILayout.EndHorizontal();
                }
            }
            else
            {
                GUILayout.Label("동기화 중...", _stLabel);
            }

            GUILayout.FlexibleSpace();

            // ---- 매치 설정(페이즈 시간): 호스트 = 프리셋 버튼 편집, 참가자 = 표시만 ----
            if (lm != null && lm.IsSpawned)
            {
                if (meHost)
                {
                    DrawTimingRow("준비 시간", lm.PrepPresets, lm.PrepSeconds,
                                  v => lm.SetPhaseTimingServerRpc(v, lm.PlaySeconds));
                    DrawTimingRow("등반 시간", lm.PlayPresets, lm.PlaySeconds,
                                  v => lm.SetPhaseTimingServerRpc(lm.PrepSeconds, v));
                }
                else
                {
                    GUILayout.Label($"매치 설정   준비 <b>{FormatSec(lm.PrepSeconds)}</b> · 등반 <b>{FormatSec(lm.PlaySeconds)}</b>", _stRow);
                }
                GUILayout.Space(8);
            }

            // ---- 하단 버튼 ----
            GUILayout.BeginHorizontal();
            if (lm != null && lm.IsSpawned)
            {
                if (GUILayout.Button(meReady ? "준비 취소" : "준비하기", Btn(meReady ? UiKit.Grey : UiKit.Green, 30), GUILayout.Height(60)))
                    lm.SetReadyServerRpc(!meReady);
            }
            if (GUILayout.Button("방 나가기", Btn(UiKit.Grey, 24), GUILayout.Width(190), GUILayout.Height(60)))
                OnLeave();
            GUILayout.EndHorizontal();

            // 호스트 전용: 게임 시작(조건 미충족 시 비활성 + 사유 안내. 서버도 RPC 에서 재검증).
            if (meHost && lm != null && lm.IsSpawned)
            {
                GUILayout.Space(10);
                bool can = count >= min && allReady;
                GUI.enabled = can;
                if (GUILayout.Button("게임 시작", Btn(UiKit.Red, 32), GUILayout.Height(66)))
                    lm.StartGameServerRpc();
                GUI.enabled = true;
                if (!can)
                    GUILayout.Label(count < min ? $"시작하려면 최소 {min}명이 필요합니다."
                                                : "모든 참가자가 준비를 완료해야 시작할 수 있습니다.", _stSmall);
            }

            if (!string.IsNullOrEmpty(_message)) GUILayout.Label(_message, _stSmall);
            GUILayout.Space(12);
            GUILayout.EndArea();

            DrawBanner(new Rect(panel.x + 240, panel.y - 32, panel.width - 480, 74), "대기방", UiKit.Blue, -1.5f, Color.white);
        }

        // 페이즈 시간 프리셋 한 줄: 선택 = 청록, 나머지 = 회색(호스트 전용 편집).
        private void DrawTimingRow(string label, float[] presets, float current, System.Action<float> onPick)
        {
            if (presets == null || presets.Length == 0) return;
            GUILayout.BeginHorizontal(GUILayout.Height(46));
            GUILayout.Label(label, _stLabel, GUILayout.Width(130));
            for (int i = 0; i < presets.Length; i++)
            {
                bool selHere = Mathf.Approximately(presets[i], current);
                if (GUILayout.Button(FormatSec(presets[i]), Btn(selHere ? UiKit.Teal : UiKit.Grey, 20),
                                     GUILayout.Height(42)) && !selHere)
                    onPick(presets[i]);
                GUILayout.Space(6);
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }

        private static string FormatSec(float s)
        {
            int sec = Mathf.RoundToInt(s);
            if (sec >= 60 && sec % 60 == 0) return (sec / 60) + "분";
            if (sec >= 60) return $"{sec / 60}분 {sec % 60}초";
            return sec + "초";
        }
    }
}
