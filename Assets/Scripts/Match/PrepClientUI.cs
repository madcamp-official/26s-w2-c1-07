using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem; // 새 Input System (Keyboard.current / Mouse.current)

namespace RouletteParty.Match
{
    /// <summary>
    /// PREP 클라이언트 UI. 씬의 단일 GameObject 에 부착한다(호스트/클라 공용).
    ///
    /// 전 페이즈가 마우스룩 조준 시점(커서 잠금)이므로, PREP 조작은 자유 커서에 의존하지 않는다:
    ///  - 장애물 종류: 숫자키 1/2/3 (벽/원기둥/투명벽)
    ///  - 배치: 화면 중앙 조준점에 좌클릭 -> 카메라 중앙 레이캐스트 히트점을 서버에 요청.
    ///          커서가 잠긴 상태에서만 배치(Esc 로 커서 푼 메뉴 모드에서는 배치 안 함).
    ///  - 주제 투표: 숫자키 4/5/6 (달리기/높이/생존) + 실시간 표수(서버 복제 NetworkVariable) 표시.
    ///  - OnGUI 패널은 상태·키 안내 표시용(클릭 버튼 아님).
    ///
    /// 검증/스폰은 서버가 한다(검증 실패는 조용히 거부). 비소유 클라도
    /// [Rpc(SendTo.Server, InvokePermission=Everyone)] 를 호출할 수 있다.
    /// </summary>
    [DisallowMultipleComponent]
    public class PrepClientUI : MonoBehaviour
    {
        [Tooltip("지면 레이캐스트 대상 레이어. 기본은 전체(~0). 지형에 'Ground' 레이어를 두면 여기서 좁히는 것을 권장.")]
        [SerializeField] private LayerMask _groundMask = ~0;
        [Tooltip("레이캐스트 최대 거리.")]
        [SerializeField] private float _rayDistance = 500f;

        private ObstacleType _selected = ObstacleType.Wall;
        private TopicMode _myVote = TopicMode.None;

        // 좌상단 NetworkBootstrap(y:10..280) 아래에 배치. 정보/안내 표시용.
        private readonly Rect _panelRect = new Rect(10, 290, 300, 220);
        private GUIStyle _rich;

        // MatchManager 가 스폰되어 있고, 현재 PREP 페이즈일 때만 UI/입력을 활성화.
        private bool Active =>
            MatchManager.Instance != null &&
            MatchManager.Instance.IsSpawned &&
            MatchManager.Instance.CurrentPhase == MatchPhase.Prep;

        // ============================ 입력 ============================
        private void Update()
        {
            if (!Active) return;

            // (a) 숫자키: 장애물 종류 선택 + 주제 투표.
            var kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.digit1Key.wasPressedThisFrame) _selected = ObstacleType.Wall;
                else if (kb.digit2Key.wasPressedThisFrame) _selected = ObstacleType.Cylinder;
                else if (kb.digit3Key.wasPressedThisFrame) _selected = ObstacleType.Ghost;

                if (kb.digit4Key.wasPressedThisFrame) CastVote(TopicMode.Race);
                else if (kb.digit5Key.wasPressedThisFrame) CastVote(TopicMode.Height);
                else if (kb.digit6Key.wasPressedThisFrame) CastVote(TopicMode.Survive);
            }

            // (b) 좌클릭: 화면 중앙(조준점)에서 지면 레이캐스트 -> 배치 요청.
            var mouse = Mouse.current;
            if (mouse == null || !mouse.leftButton.wasPressedThisFrame) return;
            if (Cursor.lockState != CursorLockMode.Locked) return; // 커서 푼 상태(메뉴)면 배치 안 함
            if (MyObstacleCount() >= MatchManager.Instance.MaxPerPlayer) return;

            Camera cam = Camera.main; // 로컬 플레이어 카메라(MainCamera 태그 필요)
            if (cam == null) return;

            // 조준점 = 화면 중앙. 카메라 전방과 일치.
            Vector2 center = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            Ray ray = cam.ScreenPointToRay(center);
            if (Physics.Raycast(ray, out RaycastHit hit, _rayDistance, _groundMask, QueryTriggerInteraction.Ignore))
            {
                // 서버에 배치 요청(검증+스폰은 서버). byte 로 캐스팅해 전송.
                MatchManager.Instance.PlaceObstacleServerRpc(hit.point, (byte)_selected);
            }
        }

        private void CastVote(TopicMode topic)
        {
            _myVote = topic;
            MatchManager.Instance.VoteServerRpc((byte)topic); // 클라 -> 서버 집계
        }

        // ============================ OnGUI (정보/안내) ============================
        private void OnGUI()
        {
            if (!Active) return;
            if (_rich == null) _rich = new GUIStyle(GUI.skin.label) { richText = true };

            var mm = MatchManager.Instance;
            int remaining = Mathf.Max(0, mm.MaxPerPlayer - MyObstacleCount());

            GUILayout.BeginArea(_panelRect, GUI.skin.box);
            GUILayout.Label("<b>PREP</b> — 장애물 배치 & 투표", _rich);

            // (a) 장애물 팔레트(숫자키)
            GUILayout.Label($"장애물 종류: <b>[1]</b> 벽  <b>[2]</b> 원기둥  <b>[3]</b> 투명벽", _rich);
            GUILayout.Label($"선택: <b>{TypeKorean(_selected)}</b>", _rich);
            GUILayout.Label($"화면 중앙 조준점에 <b>좌클릭</b>으로 배치 (남은 {remaining}/{mm.MaxPerPlayer})", _rich);

            GUILayout.Space(6);

            // (b) 주제 투표(숫자키) + 실시간 표수(서버 복제 NetworkVariable)
            GUILayout.Label($"주제 투표: <b>[4]</b> 달리기  <b>[5]</b> 높이  <b>[6]</b> 생존", _rich);
            GUILayout.Label($"표수  달리기 {mm.RaceVotes.Value} / 높이 {mm.HeightVotes.Value} / 생존 {mm.SurviveVotes.Value}");
            GUILayout.Label($"내 투표: <b>{TopicKorean(_myVote)}</b>", _rich);

            GUILayout.EndArea();
        }

        private static string TypeKorean(ObstacleType t)
        {
            switch (t)
            {
                case ObstacleType.Wall:     return "벽";
                case ObstacleType.Cylinder: return "원기둥";
                case ObstacleType.Ghost:    return "투명벽";
                default:                    return "-";
            }
        }

        private static string TopicKorean(TopicMode t)
        {
            switch (t)
            {
                case TopicMode.Race:    return "달리기";
                case TopicMode.Height:  return "높이";
                case TopicMode.Survive: return "생존";
                default:                return "-";
            }
        }

        // 현재 씬에 스폰된 "내 소유" 장애물 수. 서버가 확정한 실제 스폰 결과를 반영하므로
        // 거부된 요청은 세지 않는다(정확). Unity 6: FindObjectsByType.
        private int MyObstacleCount()
        {
            var nm = NetworkManager.Singleton;
            if (nm == null) return 0;
            ulong me = nm.LocalClientId;

            int n = 0;
            var all = Object.FindObjectsByType<Obstacle>(); // 정렬 불필요(개수만 셈)
            foreach (var ob in all)
                if (ob.IsSpawned && ob.OwnerId.Value == me) n++;
            return n;
        }
    }
}
