using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem; // 새 Input System (Mouse.current)

namespace RouletteParty.Match
{
    /// <summary>
    /// Day5 클라이언트 PREP UI (OnGUI). 씬의 단일 GameObject 에 부착한다(호스트/클라 공용).
    ///
    /// 기능(PREP 페이즈에서만 활성):
    ///  - 장애물 팔레트 3버튼(Wall/Cylinder/Ghost) 선택.
    ///  - 바닥 좌클릭 -> 카메라 레이캐스트 히트점을 MatchManager.PlaceObstacleServerRpc 로 서버에 요청.
    ///    (지면 스냅/검증은 서버가 한다. 검증 실패는 조용히 거부됨.)
    ///  - 주제 투표 3버튼(Race/Height/Survive) + 실시간 표수(서버 복제 NetworkVariable) 표시.
    ///  - 내 남은 배치 개수 표시(현재 씬에 스폰된 내 소유 장애물 수로 계산 -> 서버 확정값과 일치).
    ///
    /// MatchManager 상태 접근은 정적 싱글턴 <see cref="MatchManager.Instance"/> 로 한다. 서버 소유
    /// 오브젝트에 대해 비소유 클라도 [Rpc(SendTo.Server, InvokePermission=Everyone)] 를 호출할 수 있다.
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

        // 패널 위 클릭이 지면 배치로 새지 않도록 하는 사각형(GUI 좌표계: 좌상단 원점, y-down).
        // NetworkBootstrap(y:10..280) 아래에 배치해 겹치지 않게 한다.
        private readonly Rect _panelRect = new Rect(10, 290, 260, 250);
        private GUIStyle _rich;

        // MatchManager 가 스폰되어 있고, 현재 PREP 페이즈일 때만 UI/입력을 활성화.
        private bool Active =>
            MatchManager.Instance != null &&
            MatchManager.Instance.IsSpawned &&
            MatchManager.Instance.CurrentPhase == MatchPhase.Prep;

        // ============================ 입력(배치 요청) ============================
        private void Update()
        {
            if (!Active) return;
            if (Mouse.current == null) return;
            if (!Mouse.current.leftButton.wasPressedThisFrame) return;

            // 패널 위 클릭은 배치로 처리하지 않는다(GUI 는 좌상단 원점, Mouse 는 좌하단 원점 -> y 변환).
            Vector2 mp = Mouse.current.position.ReadValue();
            Vector2 guiPoint = new Vector2(mp.x, Screen.height - mp.y);
            if (_panelRect.Contains(guiPoint)) return;

            // 이미 최대치면 요청하지 않음(서버도 거부하지만 대역폭 절약).
            if (MyObstacleCount() >= MatchManager.Instance.MaxPerPlayer) return;

            Camera cam = Camera.main; // 로컬 플레이어 카메라(MainCamera 태그 필요)
            if (cam == null) return;

            Ray ray = cam.ScreenPointToRay(mp);
            if (Physics.Raycast(ray, out RaycastHit hit, _rayDistance, _groundMask, QueryTriggerInteraction.Ignore))
            {
                // 서버에 배치 요청(검증+스폰은 서버). byte 로 캐스팅해 전송.
                MatchManager.Instance.PlaceObstacleServerRpc(hit.point, (byte)_selected);
            }
        }

        // ============================ OnGUI ============================
        private void OnGUI()
        {
            if (!Active) return;
            if (_rich == null) _rich = new GUIStyle(GUI.skin.label) { richText = true };

            var mm = MatchManager.Instance;

            GUILayout.BeginArea(_panelRect, GUI.skin.box);
            GUILayout.Label("<b>PREP</b> — 장애물 배치 & 투표", _rich);

            // (a) 장애물 팔레트
            GUILayout.Label("장애물 종류:");
            GUILayout.BeginHorizontal();
            if (PaletteBtn(ObstacleType.Wall,     "벽"))     _selected = ObstacleType.Wall;
            if (PaletteBtn(ObstacleType.Cylinder, "원기둥"))  _selected = ObstacleType.Cylinder;
            if (PaletteBtn(ObstacleType.Ghost,    "투명벽"))  _selected = ObstacleType.Ghost;
            GUILayout.EndHorizontal();

            int remaining = Mathf.Max(0, mm.MaxPerPlayer - MyObstacleCount());
            GUILayout.Label($"바닥 좌클릭으로 배치 (남은 개수: {remaining}/{mm.MaxPerPlayer})");

            GUILayout.Space(8);

            // (b) 주제 투표 + 실시간 표수(서버 복제 NetworkVariable 읽기)
            GUILayout.Label("주제 투표:");
            VoteBtn(TopicMode.Race,    "Race (달리기)",  mm.RaceVotes.Value);
            VoteBtn(TopicMode.Height,  "Height (높이)",  mm.HeightVotes.Value);
            VoteBtn(TopicMode.Survive, "Survive (생존)", mm.SurviveVotes.Value);

            GUILayout.EndArea();
        }

        // 토글형 팔레트 버튼: 꺼진 것을 클릭한 순간에만 true.
        private bool PaletteBtn(ObstacleType type, string label)
        {
            bool on = _selected == type;
            bool now = GUILayout.Toggle(on, label, GUI.skin.button);
            return now && !on;
        }

        private void VoteBtn(TopicMode topic, string label, int count)
        {
            string mark = _myVote == topic ? "▶ " : "";
            if (GUILayout.Button($"{mark}{label}   [{count}]"))
            {
                _myVote = topic;
                MatchManager.Instance.VoteServerRpc((byte)topic); // 클라 -> 서버 집계
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
            var all = Object.FindObjectsByType<Obstacle>(FindObjectsSortMode.None);
            foreach (var ob in all)
                if (ob.IsSpawned && ob.OwnerId.Value == me) n++;
            return n;
        }
    }
}
