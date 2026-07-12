using UnityEngine;

namespace RouletteParty.Map
{
    /// <summary>
    /// 발판 프리팹 루트에 부착하는 메타데이터 컴포넌트. ClimbMapGenerator 가 레인(대각선 길)을
    /// 만들 때 이 컴포넌트로 발판을 선택(가중치)하고, 입구/출구 앵커로 발판을 체인처럼 이어붙인다.
    ///
    ///  - 진행 방향 규약: 프리팹의 +X 가 레인 진행 방향.
    ///    입구(EntryWorld) = 이전 간격에서 넘어와 착지하는 지점, 출구(ExitWorld) = 다음 간격으로
    ///    점프하는 지점. 둘 다 "밟는 면 위"의 점이어야 한다.
    ///  - 앵커 미지정 시 렌더 바운즈로 폴백: 입구 = -X 가장자리 윗면, 출구 = +X 가장자리 윗면.
    ///    평평한 판이면 폴백으로 충분하다. 기묘한 형상(누운 말 등)은 빈 자식 오브젝트 2개를
    ///    만들어 실제 밟는 지점에 앵커로 지정해야 체인이 자연스럽게 이어진다.
    ///  - 회전/이동 발판: 같은 프리팹에 RotatingPlatform/MovingPlatform 을 함께 부착하면
    ///    생성기가 시드 기반 위상으로 초기화한다(전 피어 동일).
    ///  - 이 프리팹은 시드 기반 로컬 생성 전용이라 NetworkObject 를 붙이면 안 된다.
    ///    (플레이어 설치 구조물은 같은 시각 에셋을 Structure_* 프리팹으로 따로 감싼다.)
    /// </summary>
    [DisallowMultipleComponent]
    public class PlatformSegment : MonoBehaviour
    {
        [Header("체인 앵커 (비우면 렌더 바운즈 가장자리로 폴백)")]
        [Tooltip("입구: 이전 발판에서 넘어와 착지하는 지점(밟는 면 위). 프리팹 -X 쪽.")]
        [SerializeField] private Transform _entryAnchor;
        [Tooltip("출구: 다음 발판으로 점프하는 지점(밟는 면 위). 프리팹 +X 쪽.")]
        [SerializeField] private Transform _exitAnchor;

        [Header("선택 가중치")]
        [Tooltip("생성기 프리팹 목록 안에서의 상대 선택 확률(0 = 선택 안 됨).")]
        [SerializeField, Min(0f)] private float _weight = 1f;

        public float Weight => _weight;

        /// <summary>입구 지점(월드). 생성기가 이 점을 레인 목표점에 정렬한다.</summary>
        public Vector3 EntryWorld => _entryAnchor != null ? _entryAnchor.position : BoundsEdge(-1f);
        /// <summary>출구 지점(월드). 다음 간격의 시작점이 된다.</summary>
        public Vector3 ExitWorld  => _exitAnchor  != null ? _exitAnchor.position  : BoundsEdge(+1f);

        /// <summary>이 발판(자식 포함)의 렌더 기준 월드 AABB. 영역 기록/footprint 계산용.</summary>
        public Bounds WorldBounds
        {
            get
            {
                var rs = GetComponentsInChildren<Renderer>(true);
                if (rs.Length == 0) return new Bounds(transform.position, Vector3.one);
                Bounds b = rs[0].bounds;
                for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
                return b;
            }
        }

        // 앵커 폴백: 렌더 AABB 의 ±X 가장자리 윗면 중앙.
        private Vector3 BoundsEdge(float signX)
        {
            Bounds b = WorldBounds;
            return new Vector3(signX > 0f ? b.max.x : b.min.x, b.max.y, b.center.z);
        }
    }
}
