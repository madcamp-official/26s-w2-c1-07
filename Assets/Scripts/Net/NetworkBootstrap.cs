using Unity.Netcode;
using UnityEngine;
using RouletteParty.UI; // ImguiScale (OnGUI 해상도 스케일링)

namespace RouletteParty.Net
{
    /// <summary>
    /// 개발용 네트워크 디버그 패널(OnGUI, 좌상단). 실제 사용자 흐름은 LobbyUI
    /// (타이틀 -> 방 만들기/참가 -> 대기방)가 담당하므로 기본은 숨김(_showDebugGui).
    /// LobbyUI 와 겹치지 않게 하려면 플래그를 끈 채로 두면 된다.
    ///
    /// Host/Client 시작·종료는 전부 ConnectionService 로 위임한다(연결 로직 단일 창구).
    /// </summary>
    [DisallowMultipleComponent]
    public class NetworkBootstrap : MonoBehaviour
    {
        [Header("디버그 패널 (LobbyUI 와 겹치므로 기본 꺼짐)")]
        [Tooltip("켜면 좌상단에 Host/Client 즉시 시작 버튼을 표시한다(개발 전용).")]
        [SerializeField] private bool _showDebugGui = false;

        [Header("접속 정보 (디버그 패널 전용)")]
        [SerializeField] private string _ipAddress = "127.0.0.1";
        [SerializeField] private ushort _port = 7777;

        private string _statusMessage = "대기 중";

        private void OnGUI()
        {
            if (!_showDebugGui) return;

            ImguiScale.Apply(); // 이하 좌표는 1080p 기준 가상 픽셀
            GUILayout.BeginArea(new Rect(10, 10, 260, 270), GUI.skin.box);

            var nm = NetworkManager.Singleton;
            var cs = ConnectionService.Instance;
            if (nm == null || cs == null)
            {
                GUILayout.Label(nm == null
                    ? "NetworkManager.Singleton 없음\n씬에 NetworkManager 오브젝트가 필요합니다."
                    : "ConnectionService 초기화 전");
                GUILayout.EndArea();
                return;
            }

            GUILayout.Label("[디버그] 즉시 접속");

            if (nm.ShutdownInProgress)
            {
                // Shutdown 은 비동기 정리라, 완료 전에 다시 Start 하면 실패한다. 끝날 때까지 버튼 숨김.
                GUILayout.Label("이전 세션 종료 처리 중...");
            }
            else if (!nm.IsClient && !nm.IsServer)
            {
                GUILayout.Label("주소 / 포트");
                _ipAddress = GUILayout.TextField(_ipAddress);
                string portStr = GUILayout.TextField(_port.ToString());
                if (ushort.TryParse(portStr, out ushort parsed)) _port = parsed;

                GUILayout.Space(6);
                if (GUILayout.Button("Host (서버 + 내 플레이어)"))
                {
                    if (cs.CreateRoom(_port, out string err)) _statusMessage = $"호스팅 {cs.HostFullAddress}";
                    else _statusMessage = err;
                }
                if (GUILayout.Button("Client"))
                {
                    if (cs.JoinRoom(_ipAddress, _port, out string err)) _statusMessage = "접속 시도 중";
                    else _statusMessage = err;
                }
            }
            else
            {
                string mode = nm.IsHost ? "Host" : (nm.IsServer ? "Server" : "Client");
                GUILayout.Label($"모드: {mode}");
                GUILayout.Label($"내 ClientId: {nm.LocalClientId}");
                if (nm.IsServer)
                {
                    GUILayout.Label($"호스팅 포트: {cs.ActivePort}"); // 클라이언트가 입력할 포트
                    // ConnectedClients 는 서버 전용 프로퍼티라 클라에서 접근하면 예외가 난다. 반드시 IsServer 가드.
                    GUILayout.Label($"접속 수: {nm.ConnectedClients.Count}");
                }

                GUILayout.Space(6);
                if (GUILayout.Button("Shutdown"))
                {
                    cs.Leave();
                    _statusMessage = "종료됨";
                }
            }

            GUILayout.Space(6);
            GUILayout.Label($"상태: {_statusMessage}");
            GUILayout.EndArea();
        }
    }
}
