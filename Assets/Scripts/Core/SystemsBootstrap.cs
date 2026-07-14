using UnityEngine;

namespace RouletteParty.Core
{
    /// <summary>
    /// 전역 시스템 리그(NetworkManager/ConnectionService/오디오/설정) 부트스트랩.
    /// 씬에 배치하지 않고 앱 시작 시 Resources 프리팹을 1회 인스턴스화한다(DontDestroyOnLoad).
    ///  - 어느 씬에서 플레이를 시작해도(대기방/게임 단독 테스트) 시스템이 존재한다.
    ///  - 씬 파일에 NetworkManager 를 두지 않으므로 씬 전환/재로드 시 중복 인스턴스 문제가 없다.
    /// </summary>
    public static class SystemsBootstrap
    {
        private const string RIG_PATH = "NetworkRig"; // Assets/Resources/NetworkRig.prefab

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Init()
        {
            // 멀티플레이어 필수: 창이 포커스를 잃어도 플레이어 루프를 돌려 연결(하트비트)을 유지한다.
            Application.runInBackground = true;

            if (Unity.Netcode.NetworkManager.Singleton != null) return; // 이미 존재(중복 방지)

            var prefab = Resources.Load<GameObject>(RIG_PATH);
            if (prefab == null)
            {
                Debug.LogError($"[Bootstrap] Resources/{RIG_PATH}.prefab 이 없습니다. 전역 시스템이 비활성 상태로 시작합니다.");
                return;
            }
            var rig = Object.Instantiate(prefab);
            rig.name = prefab.name; // "(Clone)" 제거
            Object.DontDestroyOnLoad(rig);
        }
    }
}
