using Unity.Netcode;
using UnityEngine;

namespace RouletteParty.Net
{
    /// <summary>
    /// 씬 흐름 감시(전역 리그 상주, NetworkManager 와 같은 오브젝트에 부착).
    /// 네트워크 세션이 끝났을 때(호스트 종료/추방/연결 끊김) 게임 씬에 남아 있으면
    /// 대기방 씬으로 돌려보낸다(오프라인 로컬 로드 - NGO 는 이미 종료된 상태).
    /// 대기방 씬 안에서의 종료는 LobbyUI 가 타이틀 화면 복귀로 처리하므로 관여하지 않는다.
    /// </summary>
    [DisallowMultipleComponent]
    public class SceneFlow : MonoBehaviour
    {
        [Tooltip("세션 종료 시 돌아갈 대기방 씬 이름.")]
        [SerializeField] private string _lobbySceneName = "LobbyScene";

        private NetworkManager _nm;

        private void Start()
        {
            _nm = GetComponent<NetworkManager>();
            if (_nm != null) _nm.OnClientStopped += HandleStopped;
        }

        private void OnDestroy()
        {
            if (_nm != null) _nm.OnClientStopped -= HandleStopped;
        }

        private void HandleStopped(bool wasHost)
        {
            var active = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if (active.name == _lobbySceneName) return;
            UnityEngine.SceneManagement.SceneManager.LoadScene(_lobbySceneName);
        }
    }
}
