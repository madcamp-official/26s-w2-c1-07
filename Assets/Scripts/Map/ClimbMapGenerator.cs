namespace RouletteParty.Map
{
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;
using RouletteParty.Match; // MatchManager (복제된 MapSeed 폴링)

/// <summary>
/// 클라이밍 레인 맵 생성기(결정론). 씬 오브젝트에 부착한다.
///
/// 맵 구조(Only Up 스타일 — "길"이 명확하게 보이는 대각선 레인):
///  - 시작 섬: 직사각형 섬 1개(윗면 y=0). 기존 지면/스폰 패드를 대체하며, 세로(Z) 길이는
///    레인 수에 비례해 자동으로 길어진다. 섬 밖은 낭떠러지 — 추락하면 낙하 탈락
///    (판정은 MatchManager). 섬 영역(여유 포함)에는 구조물을 설치할 수 없다.
///  - 레인: 섬 동쪽(+X)에서 시작해 올라가는 가상의 대각선 길. 발판(PlatformSegment 프리팹)을
///    입구/출구 앵커로 체인처럼 이어붙인다. 간격은 랜덤 2종(점프 가능/단절 — 단절은 구조물
///    설치 없이는 못 건넘)이지만, 합계를 목표 길이로 정규화하므로 개별 간격은 제각각이어도
///    레인 총 길이(=정상 높이)는 시드와 무관하게 거의 일정하다(레인 간 공정성).
///  - 발판 영역: 발판마다 여유 폭을 더한 AABB 영역을 기록(Regions, 시작 섬 = Id 0).
///    추후 하이라이트("영역 내 단기 킬 집중") 귀속과 설치 금지 판정의 공통 기반.
///  - 결정론: 호스트가 복제한 시드(MatchManager.MapSeed)로 System.Random 을 돌려 모든 피어가
///    동일한 맵을 로컬 생성한다(NetworkObject 스폰 없음, late-join 자동 대응). 레인마다
///    파생 시드를 쓰므로 레인끼리는 서로 다른 배치가 나온다.
///  - 지오메트리는 전부 Ground 레이어(카메라 오클루전 호환).
///
/// 시드 변화 감지는 Update 폴링(정수 비교 1회/프레임)으로 한다: MatchManager 가 씬 NetworkObject 라
/// 스폰 시점이 이 컴포넌트의 활성 시점과 어긋날 수 있어 이벤트 배선보다 폴링이 단순·안전하다.
/// </summary>
public class ClimbMapGenerator : MonoBehaviour
{
    public static ClimbMapGenerator Instance { get; private set; }

    /// <summary>
    /// 발판 영역(하이라이트 귀속·설치 금지 판정용). 시드 기반 생성 순서로 Id 를 매기므로
    /// 전 피어에서 동일한 Id 가 나온다 — 서버가 "탈락 위치 -> 영역 Id" 귀속만 하면 된다.
    /// </summary>
    public struct PlatformRegion
    {
        public int Id;             // 0 = 시작 섬, 1.. = 발판(생성 순서)
        public int LaneIndex;      // -1 = 시작 섬
        public int Ordinal;        // 레인 내 순번(0 = 첫 발판)
        public int ChunkIndex;     // 점수 선착순용 청크 순번: 0 = 시작 섬/진입, 1..n = 레인 청크, n+1 = 도착 청크
        public Bounds InnerBounds; // 발판 실제 렌더 AABB
        public Bounds AreaBounds;  // InnerBounds + regionPadding (영역 판정 기준)
    }

    [Header("시작 섬 — 기존 지면 대체. 윗면 y=0, 밖은 낭떠러지. 스폰 위치이자 설치 금지 구역.")]
    [Tooltip("섬 가로(X).")]
    [SerializeField] private float islandWidth = 10f;
    [Tooltip("섬 두께(Y). 아래로 내려간다(윗면은 항상 y=0).")]
    [SerializeField] private float islandThickness = 2f;
    [Tooltip("섬 세로(Z) 여유(양쪽 각각). 섬 세로 = (레인 수 - 1) x 레인 간격 + 여유 x 2.")]
    [SerializeField] private float islandPadding = 4f;
    [Tooltip("섬 머티리얼(비우면 기본).")]
    [FormerlySerializedAs("floorMaterial")]
    [SerializeField] private Material islandMaterial;

    [Header("레인(대각선 길)")]
    [Tooltip("레인 수. 추후 호스트 로비 설정으로 승격 예정 — 지금은 인스펙터 값.")]
    [SerializeField] private int laneCount = 3;
    [Tooltip("레인 간 Z 간격.")]
    [SerializeField] private float laneSpacing = 8f;
    [Tooltip("레인당 발판 수(마지막 발판 = 정상).")]
    [SerializeField] private int platformsPerLane = 14;
    [Tooltip("전진(+X) 1m 당 상승(Y). 간격의 수평 거리에 곱해져 대각선 기울기가 된다.")]
    [SerializeField] private float risePerMeter = 0.3f;

    [Header("간격 규칙 — 개별은 랜덤, 합계는 목표 길이로 정규화(레인 총 길이 일정)")]
    [Tooltip("간격 평균 목표(수평 m). (레인당 발판 수 - 1) x 이 값 = 레인 간격 합계. 아래 두 범위/확률의 기대값과 비슷하게 두면 정규화로 인한 개별 간격 왜곡이 적다.")]
    [SerializeField] private float avgGap = 2.5f;
    [Tooltip("점프 가능 간격 범위(수평 m). 플레이어 점프 제원(jumpHeight 1, 수평 도달 ~3m)과 기울기를 고려해 조정.")]
    [SerializeField] private Vector2 jumpableGapRange = new Vector2(1.2f, 2.2f);
    [Tooltip("단절 간격 범위(수평 m). 구조물을 설치하지 않으면 넘어갈 수 없는 거리로 설정.")]
    [SerializeField] private Vector2 blockedGapRange = new Vector2(4f, 6f);
    [Tooltip("간격이 단절로 뽑힐 확률(0~1).")]
    [SerializeField, Range(0f, 1f)] private float blockedGapChance = 0.25f;
    [Tooltip("섬 동쪽 가장자리 -> 첫 발판 간격(항상 점프 가능해야 하므로 고정값, 정규화 제외).")]
    [SerializeField] private float startGap = 1.5f;
    [Tooltip("발판이 레인 중심선에서 Z 로 벗어날 수 있는 최대 폭(±).")]
    [SerializeField] private float laneHalfWidth = 1.5f;
    [Tooltip("발판 1개당 Z 지터 최대치(±). 위 폭 안으로 클램프된다(단조로움 방지).")]
    [SerializeField] private float lateralJitter = 1f;

    [Header("발판 프리팹 (루트에 PlatformSegment 필수, NetworkObject 금지)")]
    [Tooltip("발판 프리팹 풀. 가중치(PlatformSegment.Weight) 랜덤 선택. 비우면 프리미티브 평판 폴백.")]
    [SerializeField] private PlatformSegment[] platformPrefabs;
    [Tooltip("프리팹 폴백용 평판 크기(X = 진행 방향 길이).")]
    [SerializeField] private Vector3 fallbackPlatformSize = new Vector3(5f, 0.5f, 3f);
    [Tooltip("폴백 평판 머티리얼(팔레트 순환). 비우면 기본 머티리얼.")]
    [SerializeField] private Material[] structureMaterials;

    [Header("영역/범위 여유")]
    [Tooltip("발판 영역(AreaBounds) 여유: 발판 AABB 를 각 면으로 이만큼 확장. 하이라이트 귀속·시작 섬 설치 금지의 기준.")]
    [SerializeField] private float regionPadding = 3f;
    [Tooltip("구조물 설치 허용 범위: 맵 footprint 를 수평으로 이만큼 확장.")]
    [SerializeField] private float placementPadding = 4f;
    [Tooltip("설치 허용 상한: 정상 위로 이만큼.")]
    [SerializeField] private float placementHeadroom = 6f;
    [Tooltip("이동/비행 가능 범위(경계벽 위치): 맵 footprint 를 수평으로 이만큼 확장.")]
    [SerializeField] private float movementPadding = 8f;
    [Tooltip("이동/비행 상한: 정상 위로 이만큼.")]
    [SerializeField] private float movementHeadroom = 8f;

    [Header("표시")]
    [Tooltip("경계벽을 눈에 보이게 렌더링할지(기본: 콜라이더만).")]
    [SerializeField] private bool showBoundaryWalls = false;

    [Header("도착 청크 — 레인 끝의 큰 발판. 위에 올라서면 도달(라운드 목표)")]
    [Tooltip("도착 발판 크기(진행 방향 x 옆 방향, m). 두께는 시작 청크와 동일 2m.")]
    [SerializeField] private Vector2 finishChunkSize = new Vector2(10f, 10f);
    [Tooltip("도달 판정 높이 여유: 발끝이 윗면 위 이 범위 안이면 도달.")]
    [SerializeField] private float finishDetectHeight = 2.5f;
    [Tooltip("도착 청크 위에 반투명 도달 볼륨을 표시한다(시각 전용).")]
    [SerializeField] private bool showFinishMarker = true;
    [Tooltip("도달 볼륨 머티리얼(반투명 권장). 비우면 표시 생략.")]
    [SerializeField] private Material finishMaterial;

    [Header("섹션 레인 — 청크(구간) 유형 기반 생성 (끄면 기존 균일 체인)")]
    [Tooltip("켜면 레인 = 청크(지그재그/수직 샤프트/가구 방/지름길/대단절/랜드마크) 체인 + 도착 청크. 일자 길·무너진 다리·회전은 풀 제외(베이스 보존).")]
    [SerializeField] private bool useSectionLanes = true;
    [Tooltip("레인당 청크 개수(고정). 맵 높이는 뽑힌 청크 구성에서 자연히 결정된다(상승 예산 없음).")]
    [SerializeField] private int chunksPerLane = 5;

    [Header("청크 연결 — 공유 굽이(전 레인 동일 방향 경로, 폭 유지)")]
    [Tooltip("청크 경계에서 진행 방향 회전(도, ±). 전 레인이 같은 굽이를 그려 레인 간 폭이 유지된다.")]
    [SerializeField] private float chunkTurnMax = 20f;
    [Tooltip("진행 방향 누적 한계(도, ±). 레인이 옆·뒤로 말리는 것 방지.")]
    [SerializeField] private float headingClamp = 45f;
    [Tooltip("경계 접속 디딤돌 한 걸음 최대 길이(m). 청크가 밴드보다 짧게 끝나면 이 걸음으로 경계까지 잇는다.")]
    [SerializeField] private float linkStepMax = 1.9f;

    [Header("크기 티어 발판 — Prefabs/Platforms/{Small,Medium,Large} 폴더와 1:1")]
    [Tooltip("소(최대 변 ~2m): 계단/지그재그/샤프트 스텝. 비우면 기본 도형 슬래브 폴백.")]
    [SerializeField] private PlatformSegment[] smallPlatforms;
    [Tooltip("중(최대 변 ~4m): 진입/휴게 발판, 샤프트 일부, 가구 방 중단.")]
    [SerializeField] private PlatformSegment[] mediumPlatforms;
    [Tooltip("대(그 이상): 가구 방 최상단, 랜드마크(x landmarkScale).")]
    [SerializeField] private PlatformSegment[] largePlatforms;
    [Tooltip("발판이 90도 회전(눕힘/세움)된 형태로 나올 확률. 모든 정적 발판 대상(회전/다리 조각·도착 청크 제외).")]
    [SerializeField, Range(0f, 1f)] private float tiltChance = 1f;

    [Header("발판 겹침 정리 (생성 후처리)")]
    [Tooltip("발판 AABB 가 세 축 모두 이 깊이(m) 이상 서로 관통하면 나중에 생성된 쪽을 삭제한다(생성 순서 기준 결정론 규칙 = 전 피어 동일). 단, 삭제로 레인 경로가 점프 불가가 되는 경우(계단형 겹침)는 예외적으로 유지한다. 시작 섬·도착 청크는 항상 유지. 0 이하 = 비활성.")]
    [SerializeField] private float overlapCullDepth = 0.25f;

    [Header("청크: 계단 (소 티어, 발판마다 랜덤)")]
    [Tooltip("步당 전진(min,max). 조각 폭(~2)보다 작으면 겹침 계단, 크면 소갭.")]
    [SerializeField] private Vector2 stairsForward = new Vector2(1.5f, 2.1f);
    [Tooltip("步당 상승(min,max).")]
    [SerializeField] private Vector2 stairsRise = new Vector2(0.5f, 0.7f);

    [Header("청크: 지그재그 (소 티어, 발판마다 랜덤)")]
    [Tooltip("步당 전진(min,max).")]
    [SerializeField] private Vector2 zigzagForward = new Vector2(0.9f, 1.2f);
    [Tooltip("步당 상승(min,max).")]
    [SerializeField] private Vector2 zigzagRise = new Vector2(0.45f, 0.6f);
    [Tooltip("레인 중심선 기준 좌우 진폭(min,max). 소 조각 z 폭 ~2 기준 1.7 초과 금지(점프 포락선).")]
    [SerializeField] private Vector2 zigzagAmp = new Vector2(1.3f, 1.7f);

    [Header("청크: 수직 샤프트 (소/중 티어)")]
    [Tooltip("步당 전진 최소(min,max). 실제 전진은 '앞뒤 조각 절반폭 합 + 0.25'와의 최댓값 — 머리 박힘(오버행) 원천 차단.")]
    [SerializeField] private Vector2 shaftForward = new Vector2(0.9f, 1.1f);
    [Tooltip("步당 상승(min,max). 풀홀드 점프 dy 1.0 기준 0.88 초과 금지.")]
    [SerializeField] private Vector2 shaftRise = new Vector2(0.75f, 0.88f);
    [Tooltip("스텝이 중 티어로 나올 확률(나머지는 소).")]
    [SerializeField, Range(0f, 1f)] private float shaftMediumChance = 0.35f;

    [Header("청크: 가구 방 (소/중/대 랜덤 구성 사다리)")]
    [Tooltip("구성 개수: 소(낮은 단부터).")]
    [SerializeField] private int roomSmallCount = 2;
    [Tooltip("구성 개수: 중.")]
    [SerializeField] private int roomMediumCount = 2;
    [Tooltip("구성 개수: 대(최상단).")]
    [SerializeField] private int roomLargeCount = 1;
    [Tooltip("단 사이 윗면 상승 간격(m). 풀홀드 점프 dy 1.0 미만.")]
    [SerializeField] private float roomStepRise = 0.9f;
    [Tooltip("인접 단 사이 수평 중심거리(min,max). 점프 도달 범위 안.")]
    [SerializeField] private Vector2 roomStepDist = new Vector2(2.6f, 3.6f);
    [Tooltip("겹침 허용 한도(m): 조각 XZ 박스가 이 깊이 이상 관통하면 다른 위치를 재시도한다.")]
    [SerializeField] private float roomOverlapTolerance = 0.3f;

    [Header("청크: 무너진 다리 (선택 풀 제외 — 베이스 보존)")]
    [Tooltip("단절 갭(min,max). 4.3 미만이면 구조물 없이 건너져 게이트가 무력화된다.")]
    [SerializeField] private Vector2 bridgeGap = new Vector2(4.8f, 5.6f);
    [Tooltip("다리 상판 조각(평평한 윗면 3m 내외). 비우면 기본 도형 플랭크.")]
    [SerializeField] private PlatformSegment bridgePiece;

    [Header("청크: 지름길 (구조물 1개로 우회로를 건너뛴다)")]
    [Tooltip("지름길 단절 폭(min,max). 구조물 1개로 건너는 규모(4.3 이상 유지).")]
    [SerializeField] private Vector2 shortcutGap = new Vector2(4.8f, 5.6f);
    [Tooltip("정직한 우회로가 옆으로 부푸는 폭(m).")]
    [SerializeField] private float shortcutDetourWidth = 3.5f;
    [Tooltip("우회로 걸음 수(소 티어, 중복 없음 — 소 티어 종수 이하 권장).")]
    [SerializeField] private int shortcutDetourSteps = 4;
    [Tooltip("지름길/우회로의 총 상승(min,max).")]
    [SerializeField] private Vector2 shortcutRise = new Vector2(0.5f, 0.8f);

    [Header("청크: 대단절 (여러 플레이어의 구조물을 이어야 건넌다)")]
    [Tooltip("대단절 폭(min,max). 구조물 1개가 ~3m 이므로 2~4개를 이어야 하는 규모.")]
    [SerializeField] private Vector2 grandGap = new Vector2(9f, 13f);

    [Header("청크: 랜드마크 (아주 큰 에셋 하나)")]
    [Tooltip("대 티어 에셋에 곱하는 배율(기존 대 티어보다 훨씬 커야 함).")]
    [SerializeField] private float landmarkScale = 2.2f;
    [Tooltip("랜드마크 진입 상승(min,max).")]
    [SerializeField] private Vector2 landmarkRise = new Vector2(0.45f, 0.7f);

    [Header("청크: 회전 (선택 풀 제외 — 베이스 보존)")]
    [Tooltip("회전 발판 개수.")]
    [SerializeField] private int spinCount = 3;
    [Tooltip("자전 속도(도/초). 시드 위상으로 전 피어 동일.")]
    [SerializeField] private float spinSpeed = 35f;
    [Tooltip("이웃 발판과의 스윕 원(회전 궤적) 사이 여유(m). 회전 중 상호 충돌 방지 + 점프 갭.")]
    [SerializeField] private float spinGapMargin = 0.7f;
    [Tooltip("발판당 상승(min,max).")]
    [SerializeField] private Vector2 spinRise = new Vector2(0.4f, 0.6f);

    // ============================ 공개 API ============================
    /// <summary>정상 높이 = 점수 정규화·정상 도달 판정의 기준(전 피어 동일).
    /// 구간 레인 모드에선 도착 청크 윗면, 레거시(useSectionLanes=false)에선 가장 낮은 레인 정상.
    /// 맵 생성 전에는 설정 기반 추정치를 돌려준다.</summary>
    public float MapHeight => _built ? _topY : EstimatedTop();

    /// <summary>플레이어 스폰 기준점(시작 섬 중심, 윗면 y=0). MatchManager 가 이 주변에 스폰시킨다.</summary>
    public Vector3 SpawnAreaCenter => Vector3.zero;
    /// <summary>스폰 줄세우기 가용 Z 폭(= 섬 세로 길이).</summary>
    public float SpawnAreaDepth => IslandDepth;

    /// <summary>플레이어 이동/PREP 비행 가능 범위(경계벽 기준). 생성 결과 footprint 에서 파생 —
    /// 레인 수/길이를 바꾸면 자동으로 따라온다.</summary>
    public Bounds MovementBounds => _built ? _movementBounds : FallbackMovementBounds();

    /// <summary>발판 영역 목록(0 = 시작 섬). 시드 기반이라 전 피어 동일.</summary>
    public IReadOnlyList<PlatformRegion> Regions => _regions;

    public int LaneCount => laneCount;

    /// <summary>구조물 설치 허용 위치인지(서버 검증·클라 프리뷰 공용):
    /// 설치 허용 범위(footprint + 여유) 안 && 시작 섬 영역(여유 포함) 밖.</summary>
    public bool IsPlacementAllowed(Vector3 p, float margin = 0f)
    {
        if (!_built) return false;
        Bounds b = _placementBounds;
        if (p.x < b.min.x + margin || p.x > b.max.x - margin ||
            p.z < b.min.z + margin || p.z > b.max.z - margin ||
            p.y < b.min.y          || p.y > b.max.y - margin) return false;
        return !_islandArea.Contains(p); // 시작 섬 영역은 설치 금지(스폰 봉쇄 방지)
    }

    /// <summary>위치가 속한 발판 영역 조회(하이라이트 귀속용, 추후 사용).
    /// 여유 영역이 겹치면 중심이 더 가까운 쪽으로 귀속한다.</summary>
    public bool TryGetRegionAt(Vector3 p, out int regionId)
    {
        regionId = -1;
        float best = float.PositiveInfinity;
        for (int i = 0; i < _regions.Count; i++)
        {
            if (!_regions[i].AreaBounds.Contains(p)) continue;
            float d = (_regions[i].AreaBounds.center - p).sqrMagnitude;
            if (d < best) { best = d; regionId = _regions[i].Id; }
        }
        return regionId >= 0;
    }

    /// <summary>점수 선착순용 청크 수(1..이 값, 마지막 = 도착 청크). 섹션 레인이 아니면 0(선착순 비활성).</summary>
    public int ScoringChunkCount { get; private set; }

    /// <summary>위치가 속한 발판의 청크 순번 조회(1..ScoringChunkCount, MatchManager 선착순 판정용).
    /// 시작 섬/진입 발판(0)과 영역 밖은 false. 청크 순번은 시드 기반 생성 순서라 전 피어 동일.</summary>
    public bool TryGetChunkAt(Vector3 p, out int chunk)
    {
        chunk = 0;
        if (!TryGetRegionAt(p, out int regionId)) return false;
        chunk = _regions[regionId].ChunkIndex; // Id == _regions 인덱스(등록 순서)
        return chunk > 0;
    }

    // ============================ 내부 상태 ============================
    int _builtSeed = int.MinValue;
    bool _built;
    float _topY;                 // 정상 높이(가장 낮은 레인 정상 - 판정 여유)
    Bounds _placementBounds;     // 설치 허용 범위
    Bounds _movementBounds;      // 이동/비행 범위(경계벽 기준)
    Bounds _islandArea;          // 시작 섬 영역(여유 포함, 설치 금지)
    readonly List<PlatformRegion> _regions = new List<PlatformRegion>();
    // _regions 와 1:1 정렬된 실물 발판(겹침 정리용). 시작 섬 등 세그먼트 없는 영역은 null.
    readonly List<PlatformSegment> _regionSegs = new List<PlatformSegment>();
    int _fallbackMatIndex;

    GameObject _staticRoot;      // 시작 섬(설정만 의존, 1회)
    GameObject _structureRoot;   // 레인/경계벽(시드마다 재생성)

    float IslandDepth => (Mathf.Max(1, laneCount) - 1) * laneSpacing + islandPadding * 2f;

    Bounds IslandBounds() => new Bounds(
        new Vector3(0f, -islandThickness * 0.5f, 0f),
        new Vector3(islandWidth, islandThickness, IslandDepth));

    // 설정 기반 정상 추정(생성 전 폴백). 섹션 모드는 청크당 평균 상승 ~3m 근사.
    float EstimatedTop() => useSectionLanes
        ? chunksPerLane * 3f
        : (startGap + Mathf.Max(0, platformsPerLane - 1) * avgGap) * risePerMeter;

    Bounds FallbackMovementBounds()
    {
        Bounds b = Expanded(IslandBounds(), movementPadding);
        b.Encapsulate(new Vector3(b.center.x, EstimatedTop() + movementHeadroom, b.center.z));
        return b;
    }

    void Awake()
    {
        Instance = this;
        BuildStatic();
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void Update()
    {
        // 복제된 시드를 폴링. 매치 시작(LOBBY)마다 시드가 바뀌면 맵을 다시 만든다.
        var mm = MatchManager.Instance;
        if (mm == null || !mm.IsSpawned) return;
        int seed = mm.MapSeed.Value;
        if (seed == 0 || seed == _builtSeed) return;
        _builtSeed = seed;
        BuildStructures(seed);
    }

    // ============================ 시작 섬 (설정만 의존, 1회) ============================
    void BuildStatic()
    {
        _staticRoot = new GameObject("ClimbMap_Static");
        _staticRoot.transform.SetParent(transform, false);
        int ground = LayerMask.NameToLayer("Ground");

        Bounds b = IslandBounds();
        var island = GameObject.CreatePrimitive(PrimitiveType.Cube);
        island.name = "StartIsland";
        island.transform.SetParent(_staticRoot.transform, false);
        island.transform.localScale = b.size;
        island.transform.position = b.center;
        island.layer = ground;
        island.isStatic = true;
        if (islandMaterial != null) island.GetComponent<MeshRenderer>().sharedMaterial = islandMaterial;
    }

    // ============================ 레인 생성 (시드마다) ============================
    void BuildStructures(int seed)
    {
        if (_structureRoot != null) Destroy(_structureRoot);
        _structureRoot = new GameObject("ClimbMap_Structures");
        _structureRoot.transform.SetParent(transform, false);
        _regions.Clear();
        _regionSegs.Clear();
        _fallbackMatIndex = 0;
        _regionChunk = 0;
        ScoringChunkCount = useSectionLanes ? Mathf.Max(1, chunksPerLane) + 1 : 0;

        int ground = LayerMask.NameToLayer("Ground");

        // 영역 0 = 시작 섬(하이라이트 귀속·설치 금지 공용).
        Bounds island = IslandBounds();
        _islandArea = Expanded(island, regionPadding);
        _regions.Add(new PlatformRegion
        {
            Id = 0, LaneIndex = -1, Ordinal = 0,
            InnerBounds = island, AreaBounds = _islandArea,
        });
        _regionSegs.Add(null); // 시작 섬은 세그먼트 없음(겹침 정리 대상 아님)

        Bounds footprint = island;
        float minLaneTop = float.PositiveInfinity;
        int made = 0;
        _finishPlates.Clear();

        if (useSectionLanes)
        {
            PlanChunks(seed); // 전 레인 공유: 청크 배열/步 수/갭/굽이 방향/경계점

            // 1) 레인별 청크 체인(도착 청크 제외).
            var laneEnds = new Vector3[laneCount];
            var laneRoots = new Transform[laneCount];
            var laneOrds = new int[laneCount];
            float maxEndY = 0f;
            for (int lane = 0; lane < laneCount; lane++)
            {
                laneEnds[lane] = BuildLaneSections(lane, seed, ground, ref footprint, ref made,
                                                   out laneRoots[lane], out laneOrds[lane]);
                maxEndY = Mathf.Max(maxEndY, laneEnds[lane].y);
            }

            // 2) 도착 청크는 하나(시작 청크처럼): 스파인 끝, 가장 높은 레인 끝 기준 높이.
            float lastHeading = _planHeading[_planHeading.Length - 1];
            float plateTop = maxEndY + 0.45f;
            _regionChunk = ScoringChunkCount; // 도착 청크(+ 합류 디딤돌) = 마지막 청크 순번
            BuildSharedFinish(plateTop, lastHeading, ground, ref footprint, ref made);

            // 3) 레인별 합류: 디딤돌로 도착 청크 입구까지(수평 + 높이 동시 접속).
            Vector3 F = Fwd(lastHeading);
            Vector3 Lt = Lat(lastHeading);
            Bounds plate = _finishPlates[0];
            for (int lane = 0; lane < laneCount; lane++)
            {
                float off = (lane - (laneCount - 1) * 0.5f) * laneSpacing;
                float offClamped = Mathf.Clamp(off, -(finishChunkSize.y * 0.5f - 1.5f), finishChunkSize.y * 0.5f - 1.5f);
                var target = new Vector3(plate.center.x, plateTop, plate.center.z)
                             - F * (finishChunkSize.x * 0.5f - 0.8f) + Lt * offClamped;
                var rng = new System.Random(unchecked(seed * 486187739 + 7919 + lane)); // 합류 전용 파생(레인 rng 와 분리)
                Vector3 cur = laneEnds[lane];
                int ord = laneOrds[lane];
                LinkTo(rng, laneRoots[lane], ground, lane, target, ref cur, ref ord, ref footprint, ref made, plateTop);
            }
            _topY = plateTop;
        }
        else
        {
            for (int lane = 0; lane < laneCount; lane++)
                minLaneTop = Mathf.Min(minLaneTop, BuildLane(lane, seed, ground, ref footprint, ref made));
            // 레거시: 가장 낮은 레인 정상 - 판정 여유(기존 계약 유지).
            _topY = (float.IsPositiveInfinity(minLaneTop) ? EstimatedTop() : minLaneTop) - 0.05f;
        }

        CullOverlappingPlatforms(); // 겹침 발판 정리(생성 순서 기준 결정론 -> 전 피어 동일)

        // footprint 파생 범위: 설치 허용(좁게) / 이동·경계벽(넓게).
        _placementBounds = WithPad(footprint, placementPadding, -0.5f, _topY + placementHeadroom);
        _movementBounds  = WithPad(footprint, movementPadding, island.min.y - 4f, _topY + movementHeadroom);
        BuildWalls(ground);
        _built = true;

        Debug.Log($"[ClimbMap] seed={seed} 레인 {laneCount} x 발판 {platformsPerLane} = {made}개, 정상 y={_topY:0.0}");
    }

    /// <summary>레인 하나 생성. 반환값 = 이 레인 정상(마지막 발판 입구 앵커) 높이.</summary>
    float BuildLane(int laneIndex, int seed, int ground, ref Bounds footprint, ref int made)
    {
        // 레인별 파생 시드: 레인끼리 다른 배치, 전 피어 동일.
        var rng = new System.Random(unchecked(seed * 486187739 + laneIndex));
        float laneZ = (laneIndex - (laneCount - 1) * 0.5f) * laneSpacing;

        var laneRoot = new GameObject($"Lane_{laneIndex}");
        laneRoot.transform.SetParent(_structureRoot.transform, false);

        // 간격 샘플링(점프 가능/단절) 후 합계를 목표 길이로 정규화:
        // 개별 간격은 제각각이지만 합계(=정상 높이)는 시드와 무관하게 일정 -> 레인 간 공정성.
        int gapCount = Mathf.Max(0, platformsPerLane - 1);
        float[] gaps = new float[gapCount];
        float sum = 0f;
        for (int i = 0; i < gapCount; i++)
        {
            bool blocked = rng.NextDouble() < blockedGapChance;
            Vector2 r = blocked ? blockedGapRange : jumpableGapRange;
            gaps[i] = Mathf.Lerp(r.x, r.y, (float)rng.NextDouble());
            sum += gaps[i];
        }
        if (sum > 0f)
        {
            float scale = gapCount * avgGap / sum;
            for (int i = 0; i < gapCount; i++) gaps[i] *= scale;
        }

        // 체인: 커서(직전 출구) + 간격(전진 g, 상승 g x 기울기) -> 다음 입구 앵커 목표점.
        Vector3 cursor = new Vector3(islandWidth * 0.5f, 0f, laneZ);
        float lastTop = 0f;
        for (int i = 0; i < platformsPerLane; i++)
        {
            float g = i == 0 ? startGap : gaps[i - 1];
            float z = cursor.z + ((float)rng.NextDouble() * 2f - 1f) * lateralJitter;
            z = Mathf.Clamp(z, laneZ - laneHalfWidth, laneZ + laneHalfWidth);
            var entryTarget = new Vector3(cursor.x + g, cursor.y + g * risePerMeter, z);

            PlatformSegment seg = SpawnPlatform(rng, laneRoot.transform);
            seg.transform.position += entryTarget - seg.EntryWorld; // 입구 앵커를 목표점에 정렬

            // 회전/이동 발판: 시드 기반 위상 주입(전 피어 동일). 동적 발판은 static 제외.
            bool dynamic = false;
            foreach (var rot in seg.GetComponentsInChildren<RotatingPlatform>())
            { rot.Initialize((float)(rng.NextDouble() * 360.0)); dynamic = true; }
            foreach (var mov in seg.GetComponentsInChildren<MovingPlatform>())
            { mov.Initialize((float)rng.NextDouble()); dynamic = true; }

            SetLayerRecursive(seg.gameObject, ground);
            if (!dynamic) SetStaticRecursive(seg.gameObject);

            Bounds inner = seg.WorldBounds;
            _regions.Add(new PlatformRegion
            {
                Id = _regions.Count, LaneIndex = laneIndex, Ordinal = i,
                InnerBounds = inner, AreaBounds = Expanded(inner, regionPadding),
            });
            _regionSegs.Add(seg);
            footprint.Encapsulate(inner.min);
            footprint.Encapsulate(inner.max);

            lastTop = seg.EntryWorld.y; // 밟는 면 기준(형상이 위로 튀어나온 프리팹도 안전)
            cursor = seg.ExitWorld;
            made++;
        }
        return lastTop;
    }

    /// <summary>프리팹 풀에서 가중치 선택 후 인스턴스화. 풀이 비면 프리미티브 평판 폴백
    /// (프리팹 연결 전에도 씬이 동작하게 — 우아한 성능 저하 규약).</summary>
    PlatformSegment SpawnPlatform(System.Random rng, Transform parent)
    {
        PlatformSegment prefab = PickPrefab(rng);
        if (prefab != null) return Instantiate(prefab, parent);

        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = "Platform_Fallback";
        go.transform.SetParent(parent, false);
        go.transform.localScale = fallbackPlatformSize;
        if (structureMaterials != null && structureMaterials.Length > 0)
        {
            var mat = structureMaterials[_fallbackMatIndex++ % structureMaterials.Length];
            if (mat != null) go.GetComponent<MeshRenderer>().sharedMaterial = mat;
        }
        return go.AddComponent<PlatformSegment>();
    }

    PlatformSegment PickPrefab(System.Random rng)
    {
        if (platformPrefabs == null || platformPrefabs.Length == 0) return null;
        float total = 0f;
        foreach (var p in platformPrefabs)
            if (p != null) total += Mathf.Max(0f, p.Weight);
        if (total <= 0f) return null;

        float pick = (float)rng.NextDouble() * total;
        foreach (var p in platformPrefabs)
        {
            if (p == null) continue;
            pick -= Mathf.Max(0f, p.Weight);
            if (pick <= 0f) return p;
        }
        return null; // 도달 불가(부동소수 방어)
    }

    // ============================ 섹션 레인 (구간 유형 기반) ============================
    // 레인 = [진입 발판] + 구간들(계단/지그재그/샤프트/가구 방/무너진 다리) + [정상 발판].
    // 구간은 상승 예산(targetRisePerLane)을 채울 때까지 이어붙이고, 步 상승은 잔여 예산으로
    // 캡(CapDy)해 오버슈트를 없앤 뒤 정상 발판 입구를 정확히 targetRise + 0.5 에 둔다
    // -> 전 레인 top 동일(기존 '레인 간 공정성' 계약 유지).
    //
    // 랜덤 소비 규율(전 피어 동일 맵의 핵심):
    //  - 레인 지오메트리는 레인 파생 rng 만 소비하고, 소비 순서는 코드 경로에 대해 고정.
    //  - 티어 선택(PickTier)은 티어가 비어도 항상 정확히 1 드로우(폴백이 소비 순서를 못 바꾸게).
    //  - 회전 청크는 시드 위상 + 서버시간 순수함수 회전이라 접속 시각과 무관하게 전 피어 동일.

    enum SectionKind
    {
        Stairs = 0,   // 일자 길 — 선택 풀 제외(베이스 보존)
        Zigzag = 1,
        Shaft = 2,
        Room = 3,
        Bridge = 4,   // 무너진 다리 — 선택 풀 제외(지름길/대단절이 게이트 역할 대체)
        Spin = 5,     // 회전(구 교차로) — 선택 풀 제외(베이스 보존)
        Shortcut = 6, // 지름길: 우회로(점프 가능) + 구조물 1개로 여는 단절
        GrandGap = 7, // 대단절: 여러 구조물을 이어야 하는 큰 단절
        Landmark = 8, // 랜드마크: 아주 큰 에셋 하나
    }

    // 랜덤 선택 가능한 청크 풀(일자 길·무너진 다리·회전 제외 — 코드 베이스는 보존).
    static readonly SectionKind[] SelectableKinds =
    {
        SectionKind.Zigzag, SectionKind.Shaft, SectionKind.Room,
        SectionKind.Shortcut, SectionKind.GrandGap, SectionKind.Landmark,
    };

    // 공유 청크 계획(맵 시드에서 1회): 모든 레인이 같은 유형 배열/步 수/다리 갭/경계 반경을 쓴다.
    // 구성 에셋·상승·측면 지터는 레인별 rng — "배열은 같고 내용물은 다르게".
    struct ChunkPlan
    {
        public int Kind;
        public int Steps;
        public float Gap;    // Bridge 전용(단절 갭)
        public float Length; // 이 청크에 할당된 밴드 길이(경계 반경 계산용)
    }
    ChunkPlan[] _plan = System.Array.Empty<ChunkPlan>();
    float[] _planHeading = System.Array.Empty<float>(); // 청크별 공유 진행 방향(도)
    Vector3[] _spine = System.Array.Empty<Vector3>();   // 중앙 경계점 S_0..S_n (xz, 전 레인 공유)

    void PlanChunks(int seed)
    {
        var mrng = new System.Random(unchecked(seed * 743 + 101));
        int n = Mathf.Max(1, chunksPerLane);
        _plan = new ChunkPlan[n];
        int gates = 0; bool roomUsed = false, landmarkUsed = false; int last = -1;
        int forcedGateAt = 1 + mrng.Next(2); // 청크 1 또는 2 에 게이트(지름길/대단절) 1회 보장
        for (int i = 0; i < n; i++)
        {
            int kind = PickSection(mrng, i, forcedGateAt, ref roomUsed, ref landmarkUsed, ref gates, last);
            last = kind;
            var p = new ChunkPlan { Kind = kind };
            switch ((SectionKind)kind)
            {
                // 밴드 길이 = 최대 전진 + 조각 최대 길이 기준(관대) — 청크가 경계를 넘지 않게 하고,
                // 짧게 끝난 몫은 경계 접속 디딤돌이 채운다. 조각 길이: 소 ~2.2 / 샤프트(중 혼합) ~2.6.
                case SectionKind.Zigzag: p.Steps = 4 + mrng.Next(3); p.Length = p.Steps * (zigzagForward.y + 2.3f); break;
                case SectionKind.Shaft:  p.Steps = 5 + mrng.Next(3); p.Length = p.Steps * (shaftForward.y + 2.6f); break;
                case SectionKind.Room:
                    p.Steps = roomSmallCount + roomMediumCount + roomLargeCount;
                    p.Length = p.Steps * roomStepDist.y * 0.8f + 4f; break;
                case SectionKind.Spin:
                    p.Steps = spinCount;
                    p.Length = spinCount * (2f * 2.9f + spinGapMargin) + 2f; break;
                case SectionKind.Shortcut:
                    p.Steps = Mathf.Max(2, shortcutDetourSteps);
                    p.Gap = Range(mrng, shortcutGap);
                    p.Length = p.Gap + 4.5f + 3.5f; break;
                case SectionKind.GrandGap:
                    p.Gap = Range(mrng, grandGap);
                    p.Length = p.Gap + 12f; break;
                case SectionKind.Landmark:
                    p.Length = landmarkScale * 4.4f + 4f; break;
            }
            _plan[i] = p;
        }

        // 공유 굽이: 청크마다 진행 방향을 ±chunkTurnMax 회전(누적 ±headingClamp).
        // 전 레인이 같은 방향 경로를 쓰므로 레인 간 폭(laneSpacing)이 그대로 유지된다.
        _planHeading = new float[n];
        float h = 0f;
        for (int i = 0; i < n; i++)
        {
            h = Mathf.Clamp(h + Jit(mrng, chunkTurnMax), -headingClamp, headingClamp);
            _planHeading[i] = h;
        }

        // 중앙 스파인 경계점: S_0 = 진입 구간 끝, S_{k+1} = S_k + 방향_k x (밴드 길이 + 접속 여유).
        _spine = new Vector3[n + 1];
        _spine[0] = new Vector3(islandWidth * 0.5f, 0f, 0f) + Fwd(_planHeading[0]) * (startGap + 4.5f);
        for (int i = 0; i < n; i++)
            _spine[i + 1] = _spine[i] + Fwd(_planHeading[i]) * (_plan[i].Length + 2.2f);
    }

    static float Range(System.Random r, Vector2 v) => Mathf.Lerp(v.x, v.y, (float)r.NextDouble());

    // 대칭 지터: ±m 균등. rng 1 드로우 고정.
    static float Jit(System.Random r, float m) => ((float)r.NextDouble() * 2f - 1f) * m;

    // 진행 방향(heading, 도) 기준 전진/측면 단위 벡터. 청크 내부 오프셋은 전부 이 좌표계로 변환된다.
    static Vector3 Fwd(float headingDeg)
    {
        float rad = headingDeg * Mathf.Deg2Rad;
        return new Vector3(Mathf.Cos(rad), 0f, Mathf.Sin(rad));
    }
    static Vector3 Lat(float headingDeg)
    {
        float rad = headingDeg * Mathf.Deg2Rad;
        return new Vector3(-Mathf.Sin(rad), 0f, Mathf.Cos(rad));
    }

    // ---- 도착 청크(도달 판정) ----
    readonly List<Bounds> _finishPlates = new List<Bounds>();

    /// <summary>도착 청크(큰 발판)가 있는 맵인지. 없으면(레거시 균일 체인) 높이 판정으로 폴백.</summary>
    public bool HasFinishPlates => _finishPlates.Count > 0;

    /// <summary>발끝 지점이 어느 도착 청크 위에 있는가(도달 판정, MatchManager 가 호출).</summary>
    public bool IsAtFinish(Vector3 foot)
    {
        for (int i = 0; i < _finishPlates.Count; i++)
        {
            Bounds b = _finishPlates[i];
            if (foot.x < b.min.x || foot.x > b.max.x || foot.z < b.min.z || foot.z > b.max.z) continue;
            if (foot.y >= b.max.y - 0.4f && foot.y <= b.max.y + finishDetectHeight) return true;
        }
        return false;
    }

    // 프리미티브 슬래브 발판(캔디 머티리얼 순환) + PlatformSegment.
    PlatformSegment Slab(Transform parent, Vector3 size, string name)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localScale = size;
        if (structureMaterials != null && structureMaterials.Length > 0)
        {
            var mat = structureMaterials[_fallbackMatIndex++ % structureMaterials.Length];
            if (mat != null) go.GetComponent<MeshRenderer>().sharedMaterial = mat;
        }
        return go.AddComponent<PlatformSegment>();
    }

    // 구간 전용 조각: 배선된 에셋 래퍼를 인스턴스화, 미배선이면 기본 도형 슬래브 폴백.
    PlatformSegment Piece(PlatformSegment prefab, Transform parent, Vector3 fallbackSize, string name)
    {
        if (prefab == null) return Slab(parent, fallbackSize, name);
        var seg = Instantiate(prefab, parent);
        seg.gameObject.name = name;
        return seg;
    }

    // 발판 등록 시 부여할 청크 순번(점수 선착순용). 각 빌드 단계가 등록 전에 설정한다.
    int _regionChunk;

    // 플레이어 점프 물리 포락선(자동 감사와 동일 상수: 풀점프 v0 6.32, 중력 20, 수평 6m/s).
    // 반환 = 해당 상승(rise)으로 도달 가능한 최대 수평 간격(불가능하면 음수).
    static float JumpEnvelope(float rise)
    {
        float disc = 39.9f - 40f * rise;
        if (disc < 0f) return -1f;
        return 6f * (6.32f + Mathf.Sqrt(disc)) / 20f;
    }

    // a 윗면에서 b 윗면으로 점프 가능한가(모서리 수평 간격 vs 포락선, 감사와 같은 여유 0.2).
    static bool Jumpable(Bounds a, Bounds b)
    {
        float gx = Mathf.Max(0f, Mathf.Max(b.min.x - a.max.x, a.min.x - b.max.x));
        float gz = Mathf.Max(0f, Mathf.Max(b.min.z - a.max.z, a.min.z - b.max.z));
        float gap = Mathf.Sqrt(gx * gx + gz * gz);
        float env = JumpEnvelope(b.max.y - a.max.y);
        return env >= 0f && gap <= env - 0.2f;
    }

    // 겹침 발판 정리(생성 후처리): 앞서 유지된 발판과 AABB 가 세 축 모두 overlapCullDepth 이상
    // 관통하는 발판(나중 생성 쪽)을 삭제한다. 단, 삭제 후에도 레인 경로가 이어져야 한다:
    // 같은 레인의 인접 유지 이웃(prev/next)끼리 직접, 또는 겹침 상대(partner)를 경유해
    // 점프 가능할 때만 삭제(계단형 겹침 = 둘 다 경로 필수 -> 유지). 순수 기하 + 생성 순서
    // 규칙이라 전 피어 동일. 시작 섬·도착 청크(LaneIndex < 0)는 항상 유지.
    void CullOverlappingPlatforms()
    {
        if (overlapCullDepth <= 0f) return;

        int n = _regions.Count;
        var removed = new bool[n];
        int removedCount = 0, keptForPath = 0;

        for (int i = 0; i < n; i++)
        {
            if (_regions[i].LaneIndex < 0 || _regionSegs[i] == null) continue;
            Bounds b = _regions[i].InnerBounds;

            // 겹침 상대: 먼저 생성됐고 아직 유지 중인 발판 중 첫 번째(결정론).
            int partner = -1;
            for (int j = 0; j < i; j++)
            {
                if (removed[j] || _regions[j].LaneIndex < 0) continue;
                Bounds a = _regions[j].InnerBounds;
                float px = Mathf.Min(b.max.x, a.max.x) - Mathf.Max(b.min.x, a.min.x);
                float py = Mathf.Min(b.max.y, a.max.y) - Mathf.Max(b.min.y, a.min.y);
                float pz = Mathf.Min(b.max.z, a.max.z) - Mathf.Max(b.min.z, a.min.z);
                if (px > overlapCullDepth && py > overlapCullDepth && pz > overlapCullDepth)
                { partner = j; break; }
            }
            if (partner < 0) continue;

            // 경로 보존 검사: 같은 레인의 인접 유지 이웃(순번 기준 prev/next).
            int lane = _regions[i].LaneIndex, ord = _regions[i].Ordinal;
            int prev = -1, next = -1;
            for (int j = 0; j < n; j++)
            {
                if (j == i || removed[j] || _regions[j].LaneIndex != lane) continue;
                int o = _regions[j].Ordinal;
                if (o < ord && (prev < 0 || o > _regions[prev].Ordinal)) prev = j;
                if (o > ord && (next < 0 || o < _regions[next].Ordinal)) next = j;
            }
            Bounds pa = _regions[partner].InnerBounds;
            bool ok;
            if (prev >= 0 && next >= 0)
                ok = Jumpable(_regions[prev].InnerBounds, _regions[next].InnerBounds)
                     || (Jumpable(_regions[prev].InnerBounds, pa) && Jumpable(pa, _regions[next].InnerBounds));
            else if (next >= 0) ok = Jumpable(pa, _regions[next].InnerBounds);
            else if (prev >= 0) ok = Jumpable(_regions[prev].InnerBounds, pa);
            else ok = true;

            if (!ok) { keptForPath++; continue; }
            removed[i] = true;
            removedCount++;
            Destroy(_regionSegs[i].gameObject);
        }

        if (removedCount == 0) return;
        var keptRegions = new List<PlatformRegion>(n - removedCount);
        var keptSegs = new List<PlatformSegment>(n - removedCount);
        for (int i = 0; i < n; i++)
        {
            if (removed[i]) continue;
            var r = _regions[i];
            r.Id = keptRegions.Count; // Id == 리스트 인덱스 계약 유지(TryGetRegionAt)
            keptRegions.Add(r);
            keptSegs.Add(_regionSegs[i]);
        }
        _regions.Clear(); _regions.AddRange(keptRegions);
        _regionSegs.Clear(); _regionSegs.AddRange(keptSegs);
        Debug.Log($"[ClimbMap] 겹침 발판 정리: {removedCount}개 삭제, {keptForPath}개는 경로 보존을 위해 유지(관통 한도 {overlapCullDepth}m)");
    }

    // 조각 공통 등록: 레이어/정적/영역/footprint. boundsOverride = 회전체(교차로)용 정적 AABB.
    void Register(PlatformSegment seg, int ground, int laneIndex, ref int ordinal,
                  ref Bounds footprint, ref int made, bool makeStatic, Bounds? boundsOverride = null)
    {
        SetLayerRecursive(seg.gameObject, ground);
        if (makeStatic) SetStaticRecursive(seg.gameObject);
        Bounds inner = boundsOverride ?? seg.WorldBounds;
        _regions.Add(new PlatformRegion
        {
            Id = _regions.Count, LaneIndex = laneIndex, Ordinal = ordinal++, ChunkIndex = _regionChunk,
            InnerBounds = inner, AreaBounds = Expanded(inner, regionPadding),
        });
        _regionSegs.Add(seg);
        footprint.Encapsulate(inner.min);
        footprint.Encapsulate(inner.max);
        made++;
    }

    // 입구 앵커를 목표점에 정렬하고 등록. 반환 = 새 커서(출구 앵커).
    Vector3 Chain(PlatformSegment seg, Vector3 entryTarget, int ground, int laneIndex,
                  ref int ordinal, ref Bounds footprint, ref int made, bool makeStatic = true)
    {
        seg.transform.position += entryTarget - seg.EntryWorld;
        Register(seg, ground, laneIndex, ref ordinal, ref footprint, ref made, makeStatic);
        return seg.ExitWorld;
    }

    // 티어 배열에서 균등 랜덤 1개(티어가 비어도 항상 1 드로우 = 소비 순서 고정), 미배선이면 슬래브 폴백.
    PlatformSegment PickTier(System.Random rng, PlatformSegment[] tier, Transform parent,
                             Vector3 fallbackSize, string name)
    {
        double roll = rng.NextDouble();
        if (tier == null || tier.Length == 0) return Slab(parent, fallbackSize, name);
        var prefab = tier[Mathf.Min((int)(roll * tier.Length), tier.Length - 1)];
        if (prefab == null) return Slab(parent, fallbackSize, name);
        var seg = Instantiate(prefab, parent);
        seg.gameObject.name = name;
        return seg;
    }

    // 청크 내부용 중복 금지 선택: 이 청크에서 아직 안 쓴 프리팹 중 균등 랜덤(항상 1 드로우).
    // 전부 소진되면 null 반환 -> 청크를 그 자리에서 끝낸다("한 청크 안에 같은 발판 없음" 규칙).
    PlatformSegment PickTierDistinct(System.Random rng, PlatformSegment[] tier, Transform parent,
                                     Vector3 fallbackSize, string name, HashSet<PlatformSegment> used)
    {
        double roll = rng.NextDouble();
        if (tier == null || tier.Length == 0) return Slab(parent, fallbackSize, name);

        var avail = new List<PlatformSegment>(tier.Length);
        foreach (var p in tier)
            if (p != null && !used.Contains(p)) avail.Add(p);
        if (avail.Count == 0) return null;

        var prefab = avail[Mathf.Min((int)(roll * avail.Count), avail.Count - 1)];
        used.Add(prefab);
        var seg = Instantiate(prefab, parent);
        seg.gameObject.name = name;
        return seg;
    }

    // 90도 틸트(눕힘/세움): tiltChance 확률로 X±90/Z±90 중 하나. 드로우 2 고정(확률과 무관하게 소비).
    // 체인 앵커/바운즈 측정 전에 호출해야 회전된 포즈 기준으로 배치된다.
    void ApplyTilt(System.Random rng, PlatformSegment seg)
    {
        double tiltRoll = rng.NextDouble();
        double variantRoll = rng.NextDouble();
        if (tiltRoll >= tiltChance) return;
        int v = (int)(variantRoll * 4.0) & 3; // X±90 / Z±90 중 하나
        Vector3 axis = (v & 2) == 0 ? Vector3.right : Vector3.forward;
        seg.transform.rotation = Quaternion.AngleAxis((v & 1) == 0 ? 90f : -90f, axis);
    }

    // 진입/휴게/정상 발판: 티어 랜덤 + tiltChance 90도 회전(눕힘/세움) + 동적 조각 위상.
    // 드로우 수 고정: 티어 1 + 틸트 2 (+ 조각 내 회전/이동 컴포넌트 수만큼 — 프리팹별 고정).
    PlatformSegment RestPiece(System.Random rng, PlatformSegment[] tier, Transform parent,
                              string name, out bool isStatic)
    {
        var seg = PickTier(rng, tier, parent, new Vector3(2.6f, 0.5f, 2.6f), name);
        ApplyTilt(rng, seg);
        bool dynamic = false;
        foreach (var rot in seg.GetComponentsInChildren<RotatingPlatform>())
        { rot.Initialize((float)(rng.NextDouble() * 360.0)); dynamic = true; } // 회전 적용 후 위상 캡처
        foreach (var mov in seg.GetComponentsInChildren<MovingPlatform>())
        { mov.Initialize((float)rng.NextDouble()); dynamic = true; }
        isStatic = !dynamic;
        return seg;
    }

    // 레인 체인 생성(도착 청크 제외). 반환 = 레인 끝 커서(도착 합류는 BuildStructures 가 처리).
    Vector3 BuildLaneSections(int laneIndex, int seed, int ground, ref Bounds footprint, ref int made,
                              out Transform laneRootOut, out int ordinalOut)
    {
        var rng = new System.Random(unchecked(seed * 486187739 + laneIndex)); // 기존 파생식 유지
        float laneZ = (laneIndex - (laneCount - 1) * 0.5f) * laneSpacing;

        var laneRoot = new GameObject($"Lane_{laneIndex}").transform;
        laneRoot.SetParent(_structureRoot.transform, false);

        int ordinal = 0;
        // 공유 굽이: 모든 레인이 같은 방향 경로(_planHeading)와 스파인 경계점(_spine)을 쓰고,
        // 레인은 스파인에서 측면으로 laneOff 만큼 평행 오프셋 -> 레인 간 폭이 청크마다 유지된다.
        float laneOff = laneZ; // (laneIndex - (n-1)/2) x laneSpacing
        Vector3 cursor = new Vector3(islandWidth * 0.5f, 0f, laneZ);

        // 진입 발판(중 티어): 시작 청크(섬) 가장자리에서 startGap(항상 점프 가능).
        _regionChunk = 0; // 진입 발판 = 시작 청크 소속(선착순 대상 아님)
        var first = RestPiece(rng, mediumPlatforms, laneRoot, "Entry", out bool firstStatic);
        cursor = Chain(first, cursor + Fwd(_planHeading[0]) * startGap + Vector3.up * 0.45f,
                       ground, laneIndex, ref ordinal, ref footprint, ref made, firstStatic);

        for (int chunkIdx = 0; chunkIdx < _plan.Length; chunkIdx++)
        {
            _regionChunk = chunkIdx + 1; // 이 청크의 발판(경계 디딤돌·휴게 포함) = 청크 순번 chunkIdx+1
            var plan = _plan[chunkIdx];
            float heading = _planHeading[chunkIdx];
            var bandOrigin = _spine[chunkIdx] + Lat(heading) * laneOff; // 이 청크 밴드의 레인측 시작점
            float stopR = plan.Length - 2.2f;                           // 밴드 내 정지선(경계 침범 방지)

            switch ((SectionKind)plan.Kind)
            {
                case SectionKind.Stairs:   SectionStairs(rng, laneRoot, ground, laneIndex, heading, bandOrigin, stopR, plan, ref cursor, ref ordinal, ref footprint, ref made); break;
                case SectionKind.Zigzag:   SectionZigzag(rng, laneRoot, ground, laneIndex, heading, bandOrigin, stopR, plan, ref cursor, ref ordinal, ref footprint, ref made); break;
                case SectionKind.Shaft:    SectionShaft(rng, laneRoot, ground, laneIndex, heading, bandOrigin, stopR, plan, ref cursor, ref ordinal, ref footprint, ref made); break;
                case SectionKind.Room:     SectionRoom(rng, laneRoot, ground, laneIndex, heading, bandOrigin, stopR, plan, ref cursor, ref ordinal, ref footprint, ref made); break;
                case SectionKind.Bridge:   SectionBridge(rng, laneRoot, ground, laneIndex, heading, plan, ref cursor, ref ordinal, ref footprint, ref made); break;
                case SectionKind.Spin:     SectionSpin(rng, laneRoot, ground, laneIndex, heading, bandOrigin, stopR, plan, ref cursor, ref ordinal, ref footprint, ref made); break;
                case SectionKind.Shortcut: SectionShortcut(rng, laneRoot, ground, laneIndex, heading, plan, ref cursor, ref ordinal, ref footprint, ref made); break;
                case SectionKind.GrandGap: SectionGrandGap(rng, laneRoot, ground, laneIndex, heading, plan, ref cursor, ref ordinal, ref footprint, ref made); break;
                case SectionKind.Landmark: SectionLandmark(rng, laneRoot, ground, laneIndex, heading, ref cursor, ref ordinal, ref footprint, ref made); break;
            }

            // 경계 접속: 짧게 끝난 몫을 디딤돌로 잇고, 휴게 발판을 공유 경계점(레인 오프셋 적용)에 놓는다.
            // 경계점의 측면 기준은 "다음 청크의 방향" — 다음 밴드 시작점과 정확히 일치해 자연스럽게 꺾인다.
            float nextHeading = chunkIdx + 1 < _plan.Length ? _planHeading[chunkIdx + 1] : heading;
            var bound = _spine[chunkIdx + 1] + Lat(nextHeading) * laneOff;
            if (chunkIdx < _plan.Length - 1)
            {
                LinkTo(rng, laneRoot, ground, laneIndex, bound, ref cursor, ref ordinal, ref footprint, ref made);
                var rest = RestPiece(rng, mediumPlatforms, laneRoot, "Rest", out bool restStatic);
                cursor = Chain(rest, new Vector3(bound.x, cursor.y + 0.4f, bound.z),
                               ground, laneIndex, ref ordinal, ref footprint, ref made, restStatic);
            }
        }

        // 도착 청크는 전 레인 공유(BuildStructures 가 스파인 끝에 1개 생성 후 레인별 합류를 잇는다).
        laneRootOut = laneRoot;
        ordinalOut = ordinal;
        return cursor;
    }

    // 경계 접속 디딤돌(소 티어): 목표 수평 지점까지 2.2m 이내(+ 수직 목표가 있으면 상승 0.85 이내)가
    // 될 때까지 걸음을 놓는다. targetY 지정 시 걸음 상승을 목표 높이에 맞춰 배분한다(도착 합류용).
    void LinkTo(System.Random rng, Transform root, int ground, int laneIndex, Vector3 targetXZ,
                ref Vector3 cursor, ref int ordinal, ref Bounds footprint, ref int made,
                float targetY = float.NaN)
    {
        bool vertical = !float.IsNaN(targetY);
        for (int guard = 0; guard < 12; guard++)
        {
            Vector3 to = targetXZ - cursor; to.y = 0f;
            float d = to.magnitude;
            float riseLeft = vertical ? targetY - cursor.y : 0f;
            if (d <= 2.2f && riseLeft <= 0.85f) break;

            float dy = vertical ? Mathf.Clamp(riseLeft, -1.2f, 0.7f) : 0.3f;
            var stone = PickTier(rng, smallPlatforms, root, new Vector3(1.6f, 0.4f, 1.6f), "Link");
            ApplyTilt(rng, stone);
            cursor = Chain(stone, cursor + to.normalized * Mathf.Min(linkStepMax, Mathf.Max(0.6f, d)) + Vector3.up * dy,
                           ground, laneIndex, ref ordinal, ref footprint, ref made);
        }
    }

    // 도착 청크 생성(전 레인 공유 1개): 스파인 끝에 진행 방향 정렬 큰 발판 + (선택) 반투명 도달 볼륨.
    void BuildSharedFinish(float plateTop, float heading, int ground, ref Bounds footprint, ref int made)
    {
        Vector3 F = Fwd(heading);
        Vector3 centerXZ = _spine[_spine.Length - 1] + F * (2.5f + finishChunkSize.x * 0.5f);
        var centerTop = new Vector3(centerXZ.x, plateTop, centerXZ.z);

        var plate = GameObject.CreatePrimitive(PrimitiveType.Cube);
        plate.name = "FinishChunk";
        plate.transform.SetParent(_structureRoot.transform, false);
        plate.transform.localScale = new Vector3(finishChunkSize.x, 2f, finishChunkSize.y);
        plate.transform.SetPositionAndRotation(centerTop - Vector3.up * 1f, Quaternion.Euler(0f, -heading, 0f));
        if (islandMaterial != null) plate.GetComponent<MeshRenderer>().sharedMaterial = islandMaterial;
        var seg = plate.AddComponent<PlatformSegment>();
        int ordinal = 1; // 영역 관례: LaneIndex -1(공용) + Ordinal 1 = 도착 청크(0 = 시작 청크)
        Register(seg, ground, -1, ref ordinal, ref footprint, ref made, true);

        // 도달 판정 영역 = 발판의 월드 AABB(회전 포함). 콜라이더 bounds 는 같은 프레임의
        // 트랜스폼 이동을 물리 동기화 전까지 반영하지 않으므로(스테일) 렌더러 bounds 를 쓴다.
        _finishPlates.Add(plate.GetComponent<Renderer>().bounds);

        if (showFinishMarker && finishMaterial != null)
        {
            var marker = GameObject.CreatePrimitive(PrimitiveType.Cube);
            marker.name = "FinishMarker";
            marker.transform.SetParent(plate.transform, false); // 발판 로컬 기준(회전 승계)
            marker.transform.localScale = new Vector3(0.98f, finishDetectHeight * 0.5f, 0.98f);
            marker.transform.localPosition = new Vector3(0f, 0.5f + finishDetectHeight * 0.25f, 0f);
            Destroy(marker.GetComponent<Collider>()); // 시각 전용
            var mr = marker.GetComponent<MeshRenderer>();
            mr.sharedMaterial = finishMaterial;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
        }
    }

    int PickSection(System.Random rng, int idx, int forcedGateAt,
                    ref bool roomUsed, ref bool landmarkUsed, ref int gates, int last)
    {
        // 게이트(구조물 설치 유도) 1회 보장: 지름길 또는 대단절.
        if (idx == forcedGateAt && gates == 0)
        {
            gates++;
            return rng.NextDouble() < 0.5 ? (int)SectionKind.Shortcut : (int)SectionKind.GrandGap;
        }
        for (int tries = 0; tries < 10; tries++)
        {
            int k = (int)SelectableKinds[rng.Next(SelectableKinds.Length)];
            bool isGate = k == (int)SectionKind.Shortcut || k == (int)SectionKind.GrandGap;
            if (k == last) continue;
            if (k == (int)SectionKind.Room && roomUsed) continue;         // 방은 레인당 1회
            if (k == (int)SectionKind.Landmark && landmarkUsed) continue; // 랜드마크도 1회(스펙터클 유지)
            if (isGate && (gates >= 2 || idx == 0)) continue;             // 첫 청크는 게이트 금지
            if (k == (int)SectionKind.Room) roomUsed = true;
            if (k == (int)SectionKind.Landmark) landmarkUsed = true;
            if (isGate) gates++;
            return k;
        }
        return (int)SectionKind.Zigzag;
    }

    static float ForwardOf(Vector3 p, Vector3 origin, Vector3 f) =>
        (p.x - origin.x) * f.x + (p.z - origin.z) * f.z;

    // ---- 계단형: 짧고 안정적인 연속 점프. 발판 하나하나 소 티어 랜덤. 步 수는 공유 계획. ----
    void SectionStairs(System.Random rng, Transform root, int ground, int laneIndex, float heading,
                       Vector3 laneOrigin, float stopR, ChunkPlan plan,
                       ref Vector3 cursor, ref int ordinal, ref Bounds footprint, ref int made)
    {
        Vector3 F = Fwd(heading); Vector3 L = Lat(heading);
        var used = new HashSet<PlatformSegment>();
        for (int i = 0; i < plan.Steps && ForwardOf(cursor, laneOrigin, F) < stopR; i++)
        {
            float dx = Range(rng, stairsForward);
            float dy = Range(rng, stairsRise);
            float lat = Jit(rng, 0.35f);
            var seg = PickTierDistinct(rng, smallPlatforms, root, new Vector3(1.6f, 0.4f, 1.8f), "Stair", used);
            if (seg == null) break; // 청크 내 중복 금지: 종류 소진 시 조기 종료
            ApplyTilt(rng, seg);
            cursor = Chain(seg, cursor + F * dx + L * lat + Vector3.up * dy,
                           ground, laneIndex, ref ordinal, ref footprint, ref made);
        }
    }

    // ---- 지그재그형: 청크 중심선 기준 좌우 교대. 발판 하나하나 소 티어 랜덤. 步 수는 공유 계획. ----
    void SectionZigzag(System.Random rng, Transform root, int ground, int laneIndex, float heading,
                       Vector3 laneOrigin, float stopR, ChunkPlan plan,
                       ref Vector3 cursor, ref int ordinal, ref Bounds footprint, ref int made)
    {
        Vector3 F = Fwd(heading); Vector3 L = Lat(heading);
        var used = new HashSet<PlatformSegment>();
        float sign = rng.NextDouble() < 0.5 ? 1f : -1f;
        float lat = 0f; // 청크 진입점 기준 현재 측면 오프셋
        for (int i = 0; i < plan.Steps && ForwardOf(cursor, laneOrigin, F) < stopR; i++)
        {
            float dx = Range(rng, zigzagForward);
            float dy = Range(rng, zigzagRise);
            float amp = Range(rng, zigzagAmp);
            float dLat = sign * amp - lat;
            lat = sign * amp;
            var seg = PickTierDistinct(rng, smallPlatforms, root, new Vector3(1.8f, 0.4f, 2.2f), "Zig", used);
            if (seg == null) break; // 청크 내 중복 금지
            ApplyTilt(rng, seg);
            cursor = Chain(seg, cursor + F * dx + L * dLat + Vector3.up * dy,
                           ground, laneIndex, ref ordinal, ref footprint, ref made);
            sign = -sign;
        }
    }

    // ---- 수직 샤프트형: 좁은 공간 큰 상승(풀홀드 점프 요구). 소/중 티어 혼합. ----
    // 머리 박힘 없음: 체인 전진은 "이전 조각 출구 모서리 -> 다음 조각 입구 모서리"의 순수 간격이라
    // 조각 몸통(윗면 아래로 늘어진 부분)이 진행 방향 머리 위를 덮는 배치가 기하적으로 안 나온다.
    void SectionShaft(System.Random rng, Transform root, int ground, int laneIndex, float heading,
                      Vector3 laneOrigin, float stopR, ChunkPlan plan,
                      ref Vector3 cursor, ref int ordinal, ref Bounds footprint, ref int made)
    {
        Vector3 F = Fwd(heading); Vector3 L = Lat(heading);
        var used = new HashSet<PlatformSegment>(); // 소/중 티어 공용(청크 전체 중복 금지)
        float sign = rng.NextDouble() < 0.5 ? 1f : -1f;
        float lat = 0f;
        for (int i = 0; i < plan.Steps && ForwardOf(cursor, laneOrigin, F) < stopR; i++)
        {
            double tierRoll = rng.NextDouble();
            var tier = tierRoll < shaftMediumChance ? mediumPlatforms : smallPlatforms;
            var seg = PickTierDistinct(rng, tier, root, new Vector3(1.4f, 0.35f, 1.4f), "ShaftStep", used);
            if (seg == null) break; // 청크 내 중복 금지
            ApplyTilt(rng, seg);

            float dx = Range(rng, shaftForward);
            float dy = Range(rng, shaftRise);
            float dLat = sign * 0.7f - lat;
            lat = sign * 0.7f;
            cursor = Chain(seg, cursor + F * dx + L * dLat + Vector3.up * dy,
                           ground, laneIndex, ref ordinal, ref footprint, ref made);
            sign = -sign;
        }
    }

    // ---- 가구 방형: 소/중/대 랜덤 구성 사다리(낮은 단 소 -> 높은 단 대). ----
    // 위치는 이전 단 기준 랜덤 방향(전진 편향)으로 뽑되, 이미 놓인 조각과 XZ 박스가
    // roomOverlapTolerance 이상 관통하면 재시도한다("심하게 겹치지만 않으면" 규칙).
    void SectionRoom(System.Random rng, Transform root, int ground, int laneIndex, float heading,
                     Vector3 laneOrigin, float stopR, ChunkPlan plan,
                     ref Vector3 cursor, ref int ordinal, ref Bounds footprint, ref int made)
    {
        Vector3 Ff = Fwd(heading);
        float baseY = cursor.y;
        var prevXZ = new Vector2(cursor.x, cursor.z);
        var placed = new List<Bounds>();
        var used = new HashSet<PlatformSegment>();
        PlatformSegment topPiece = null;
        int total = roomSmallCount + roomMediumCount + roomLargeCount;

        for (int k = 0; k < total; k++)
        {
            PlatformSegment[] tier = k < roomSmallCount ? smallPlatforms
                : k < roomSmallCount + roomMediumCount ? mediumPlatforms : largePlatforms;
            float targetTop = baseY + roomStepRise * (k + 1);
            var piece = PickTierDistinct(rng, tier, root, new Vector3(2f, roomStepRise * (k + 1), 2f), "Room", used);
            if (piece == null) break; // 청크 내 중복 금지
            ApplyTilt(rng, piece); // 바운즈 측정 전(회전된 포즈로 겹침/높이 계산)
            Bounds pb = piece.WorldBounds;
            var half = new Vector2(pb.extents.x, pb.extents.z);

            // 위치 샘플: 이전 단에서 roomStepDist 거리, 방향은 진행 방향(heading) 반구 편향.
            // 겹침 또는 청크 경계(stopR) 초과 시 재시도(경계 정합 유지).
            var fwd2 = new Vector2(Ff.x, Ff.z);
            Vector2 posXZ = prevXZ + fwd2 * roomStepDist.x;
            for (int t = 0; t < 10; t++)
            {
                float ang = (heading + Jit(rng, 100f)) * Mathf.Deg2Rad; // 진행 방향 기준 ±100도
                // 첫 단은 진입 홉(상승 0.9)과 겹쳐 빡빡해지므로 거리를 좁힌다(점프 여유 확보).
                float dist = k == 0 ? Mathf.Lerp(2.1f, 2.7f, (float)rng.NextDouble()) : Range(rng, roomStepDist);
                var cand = prevXZ + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * dist;
                if (ForwardOf(new Vector3(cand.x, 0f, cand.y), laneOrigin, Ff) > stopR) continue;

                bool bad = false;
                foreach (var b in placed)
                {
                    float penX = Mathf.Min(cand.x + half.x, b.max.x) - Mathf.Max(cand.x - half.x, b.min.x);
                    float penZ = Mathf.Min(cand.y + half.y, b.max.z) - Mathf.Max(cand.y - half.y, b.min.z);
                    if (penX > roomOverlapTolerance && penZ > roomOverlapTolerance) { bad = true; break; }
                }
                if (!bad) { posXZ = cand; break; }
            }

            var bottomCenter = new Vector3(pb.center.x, pb.min.y, pb.center.z);
            var desired = new Vector3(posXZ.x, targetTop - pb.size.y, posXZ.y);
            piece.transform.position += desired - bottomCenter;
            Register(piece, ground, laneIndex, ref ordinal, ref footprint, ref made, true);
            placed.Add(piece.WorldBounds);
            prevXZ = posXZ;
            topPiece = piece;
        }
        if (topPiece != null) cursor = topPiece.ExitWorld;
    }

    // ---- 무너진 다리형: 플랭크 + 단절 갭(구조물 설치 필요) + 플랭크 ----
    void SectionBridge(System.Random rng, Transform root, int ground, int laneIndex, float heading,
                       ChunkPlan plan,
                       ref Vector3 cursor, ref int ordinal, ref Bounds footprint, ref int made)
    {
        Vector3 F = Fwd(heading); Vector3 L = Lat(heading);
        var plankFallback = new Vector3(3.2f, 0.3f, 2.2f);
        int lead = plan.Steps; // 공유 계획(전 레인 동일 상판 수)
        for (int i = 0; i < lead; i++)
        {
            float dx = 1.3f + (float)rng.NextDouble() * 0.3f;
            var seg = Piece(bridgePiece, root, plankFallback, "BridgePlank");
            cursor = Chain(seg, cursor + F * dx + L * Jit(rng, 0.3f) + Vector3.up * 0.12f,
                           ground, laneIndex, ref ordinal, ref footprint, ref made);
        }
        // 단절: 점프 불가 거리(>= 4.3). 구조물을 놓아야 건넌다(설치 게이트 게임 핵심).
        // 대각 진행(heading != 0) 시 축정렬 상판의 모서리끼리 가까워져 갭이 줄어드는 지름길을
        // 실측(수평 AABB 간격)으로 보정: gap 에 못 미치면 전진 방향으로 보충한다.
        float gap = plan.Gap; // 공유 계획(전 레인 동일 단절 폭)
        Bounds nearB = default;
        {
            // 직전 상판(단절의 이쪽 끝) 바운즈.
            var lastPlank = root.GetChild(root.childCount - 1).GetComponent<PlatformSegment>();
            if (lastPlank != null) nearB = lastPlank.WorldBounds;
        }
        var far = Piece(bridgePiece, root, plankFallback, "BridgeFar");
        far.transform.position += (cursor + F * gap + L * Jit(rng, 0.3f) + Vector3.up * 0.4f) - far.EntryWorld;
        for (int it = 0; it < 2 && nearB.size.sqrMagnitude > 0f; it++)
        {
            Bounds fb = far.WorldBounds;
            float gx = Mathf.Max(0f, Mathf.Max(fb.min.x - nearB.max.x, nearB.min.x - fb.max.x));
            float gz = Mathf.Max(0f, Mathf.Max(fb.min.z - nearB.max.z, nearB.min.z - fb.max.z));
            float hull = Mathf.Sqrt(gx * gx + gz * gz);
            if (hull >= gap - 0.05f) break;
            far.transform.position += F * (gap - hull);
        }
        Register(far, ground, laneIndex, ref ordinal, ref footprint, ref made, true);
        cursor = far.ExitWorld;
        var tail = Piece(bridgePiece, root, plankFallback, "BridgePlank");
        cursor = Chain(tail, cursor + F * 1.4f + Vector3.up * 0.12f,
                       ground, laneIndex, ref ordinal, ref footprint, ref made);
    }

    // ---- 지름길형: 정직한 우회로(점프 가능) + 구조물 1개로 여는 직선 단절 ----
    // 진입 -> 착지 발판 직선 거리 = plan.Gap(점프 불가). 옆으로 부푼 소 티어 아치가 정직 루트.
    // 구조물을 단절에 놓으면 우회로를 건너뛴다(지름길이 열린다).
    void SectionShortcut(System.Random rng, Transform root, int ground, int laneIndex, float heading,
                         ChunkPlan plan, ref Vector3 cursor, ref int ordinal, ref Bounds footprint, ref int made)
    {
        Vector3 F = Fwd(heading); Vector3 L = Lat(heading);
        var used = new HashSet<PlatformSegment>();
        float side = rng.NextDouble() < 0.5 ? 1f : -1f;
        float rise = Range(rng, shortcutRise);
        Vector3 entry = cursor;

        // 우회로: 옆으로 부푼 아치(소 티어, 중복 없음). 진행분은 단절 폭을 등분.
        int steps = Mathf.Max(2, plan.Steps);
        for (int i = 1; i <= steps; i++)
        {
            float t = i / (float)(steps + 1);
            var p = PickTierDistinct(rng, smallPlatforms, root, new Vector3(1.7f, 0.4f, 1.7f), "Detour", used);
            if (p == null) break; // 청크 내 중복 금지
            ApplyTilt(rng, p);
            var target = entry + F * (plan.Gap * t)
                               + L * (side * shortcutDetourWidth * Mathf.Sin(Mathf.PI * t))
                               + Vector3.up * (rise * t);
            Chain(p, target, ground, laneIndex, ref ordinal, ref footprint, ref made);
        }

        // 착지 발판(중 티어): 우회로 합류점이자 지름길 건너편.
        var land = PickTierDistinct(rng, mediumPlatforms, root, new Vector3(2.6f, 0.5f, 2.6f), "ShortcutLand", used);
        if (land == null) land = Slab(root, new Vector3(2.6f, 0.5f, 2.6f), "ShortcutLand");
        ApplyTilt(rng, land);
        cursor = Chain(land, entry + F * plan.Gap + Vector3.up * rise,
                       ground, laneIndex, ref ordinal, ref footprint, ref made);
    }

    // ---- 대단절형: 여러 플레이어의 구조물을 이어야 건너는 큰 단절 ----
    // 구조물 하나가 ~3m 이므로 grandGap(9~13m)은 2~4개를 이어 붙여야 한다(협동 게이트).
    void SectionGrandGap(System.Random rng, Transform root, int ground, int laneIndex, float heading,
                         ChunkPlan plan, ref Vector3 cursor, ref int ordinal, ref Bounds footprint, ref int made)
    {
        Vector3 F = Fwd(heading); Vector3 L = Lat(heading);
        var used = new HashSet<PlatformSegment>();

        // 이쪽 끝(중 티어).
        var edge = PickTierDistinct(rng, mediumPlatforms, root, new Vector3(2.6f, 0.5f, 2.6f), "GapEdge", used);
        if (edge == null) edge = Slab(root, new Vector3(2.6f, 0.5f, 2.6f), "GapEdge");
        ApplyTilt(rng, edge);
        cursor = Chain(edge, cursor + F * 1.6f + L * Jit(rng, 0.4f) + Vector3.up * 0.3f,
                       ground, laneIndex, ref ordinal, ref footprint, ref made);
        Bounds nearB = edge.WorldBounds;

        // 대단절: 건너편 착지(중 티어). 대각 진행 모서리 지름길은 실측 보정으로 차단.
        var land = PickTierDistinct(rng, mediumPlatforms, root, new Vector3(2.6f, 0.5f, 2.6f), "GapLand", used);
        if (land == null) land = Slab(root, new Vector3(2.6f, 0.5f, 2.6f), "GapLand");
        ApplyTilt(rng, land); // 앵커/실측 보정 전(회전된 포즈 기준)
        land.transform.position += (cursor + F * plan.Gap + L * Jit(rng, 0.4f) + Vector3.up * 0.4f) - land.EntryWorld;
        for (int it = 0; it < 2; it++)
        {
            Bounds fb = land.WorldBounds;
            float gx = Mathf.Max(0f, Mathf.Max(fb.min.x - nearB.max.x, nearB.min.x - fb.max.x));
            float gz = Mathf.Max(0f, Mathf.Max(fb.min.z - nearB.max.z, nearB.min.z - fb.max.z));
            float hull = Mathf.Sqrt(gx * gx + gz * gz);
            if (hull >= plan.Gap - 0.05f) break;
            land.transform.position += F * (plan.Gap - hull);
        }
        Register(land, ground, laneIndex, ref ordinal, ref footprint, ref made, true);
        cursor = land.ExitWorld;
    }

    // ---- 랜드마크형: 아주 큰 에셋 하나(대 티어 x landmarkScale) ----
    void SectionLandmark(System.Random rng, Transform root, int ground, int laneIndex, float heading,
                         ref Vector3 cursor, ref int ordinal, ref Bounds footprint, ref int made)
    {
        Vector3 F = Fwd(heading);
        var seg = PickTier(rng, largePlatforms, root, new Vector3(4f, 0.6f, 4f), "Landmark");
        seg.transform.localScale *= Mathf.Max(1f, landmarkScale);
        ApplyTilt(rng, seg);
        float dy = Range(rng, landmarkRise);
        cursor = Chain(seg, cursor + F * 1.8f + Vector3.up * dy,
                       ground, laneIndex, ref ordinal, ref footprint, ref made);
    }

    // ---- 회전형: 자전하는 대 티어 발판들의 연속. ----
    // 각 발판이 자기 중심 Y축으로 회전(서버시간 순수함수 + 시드 위상 -> 전 피어 동일).
    // 배치는 중심 간격 기준: 이웃 스윕 원(회전 궤적 반지름 = XZ 대각 절반)이 spinGapMargin
    // 만큼 떨어지게 -> 회전 중 상호 충돌이 없고, 점프 갭은 스윕 원 사이 간격이 된다.
    // 라이브 바운즈는 체인 계산에 쓰지 않는다(커서는 중심+스윕 반지름의 순수 데이터).
    void SectionSpin(System.Random rng, Transform root, int ground, int laneIndex, float heading,
                     Vector3 laneOrigin, float stopR, ChunkPlan plan,
                     ref Vector3 cursor, ref int ordinal, ref Bounds footprint, ref int made)
    {
        Vector3 F = Fwd(heading); Vector3 L = Lat(heading);
        var used = new HashSet<PlatformSegment>();
        float prevSweep = 0f;      // 0 = 첫 조각(커서는 일반 발판 모서리)
        Vector3 prevCenter = cursor;
        float prevTop = cursor.y;

        for (int i = 0; i < plan.Steps; i++)
        {
            var seg = PickTierDistinct(rng, largePlatforms, root, new Vector3(3.5f, 0.5f, 3.5f), "Spin", used);
            if (seg == null) break; // 청크 내 중복 금지
            Bounds b = seg.WorldBounds;
            float sweep = 0.5f * Mathf.Sqrt(b.size.x * b.size.x + b.size.z * b.size.z);

            // 경계 정지선: 이 조각의 스윕 원이 경계를 넘으면 청크를 여기서 끝낸다(조각 파기).
            float dxCenter = prevSweep + sweep + spinGapMargin;
            if (ForwardOf(prevCenter, laneOrigin, F) + dxCenter + sweep > stopR + 2.0f)
            { Destroy(seg.gameObject); break; }

            float dy = Range(rng, spinRise);
            float targetTop = prevTop + dy;
            Vector3 centerV = prevCenter + F * dxCenter + L * Jit(rng, 0.8f);
            var center = new Vector3(centerV.x, 0f, centerV.z);

            // 중심을 목표 XZ 에, 윗면을 targetTop 에 정렬.
            seg.transform.position += new Vector3(center.x - b.center.x,
                                                  targetTop - b.max.y,
                                                  center.z - b.center.z);
            // 위상 주입은 최종 포즈 확정 후. 영역/footprint 는 스윕 정적 AABB(라이브 바운즈 금지).
            seg.gameObject.AddComponent<RotatingPlatform>()
               .Initialize((float)(rng.NextDouble() * 360.0), spinSpeed);
            var sweepBounds = new Bounds(
                new Vector3(center.x, targetTop - b.size.y * 0.5f, center.z),
                new Vector3(sweep * 2f, b.size.y, sweep * 2f));
            Register(seg, ground, laneIndex, ref ordinal, ref footprint, ref made,
                     makeStatic: false, boundsOverride: sweepBounds);

            prevCenter = center;
            prevTop = targetTop;
            prevSweep = sweep;
            cursor = new Vector3(center.x, targetTop, center.z) + F * sweep;
        }
        // 다음 청크는 마지막 스윕 원 가장자리에서 이어진다(회전 조각 위에서 뛰어내리는 감각).
    }

    // ============================ 경계벽 (footprint 파생, 시드마다) ============================
    void BuildWalls(int ground)
    {
        Bounds b = _movementBounds;
        float h = b.size.y + 8f;
        float cy = b.min.y + h * 0.5f;
        const float t = 0.5f;
        BuildWall("Wall_XPos", new Vector3(b.max.x + t * 0.5f, cy, b.center.z), new Vector3(t, h, b.size.z + 2f * t), ground);
        BuildWall("Wall_XNeg", new Vector3(b.min.x - t * 0.5f, cy, b.center.z), new Vector3(t, h, b.size.z + 2f * t), ground);
        BuildWall("Wall_ZPos", new Vector3(b.center.x, cy, b.max.z + t * 0.5f), new Vector3(b.size.x + 2f * t, h, t), ground);
        BuildWall("Wall_ZNeg", new Vector3(b.center.x, cy, b.min.z - t * 0.5f), new Vector3(b.size.x + 2f * t, h, t), ground);
    }

    void BuildWall(string name, Vector3 pos, Vector3 scale, int layer)
    {
        var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wall.name = name;
        wall.transform.SetParent(_structureRoot.transform, false);
        wall.transform.position = pos;
        wall.transform.localScale = scale;
        wall.layer = layer;
        wall.isStatic = true;
        wall.GetComponent<MeshRenderer>().enabled = showBoundaryWalls;
    }

    // ============================ 헬퍼 ============================
    static Bounds Expanded(Bounds b, float pad)
    {
        b.Expand(pad * 2f); // Expand 는 전체 크기 기준 -> 면당 pad
        return b;
    }

    static Bounds WithPad(Bounds fp, float padXZ, float bottomY, float topY)
    {
        var b = new Bounds();
        b.SetMinMax(new Vector3(fp.min.x - padXZ, bottomY, fp.min.z - padXZ),
                    new Vector3(fp.max.x + padXZ, topY,    fp.max.z + padXZ));
        return b;
    }

    static void SetLayerRecursive(GameObject go, int layer)
    {
        go.layer = layer;
        foreach (Transform child in go.transform)
            SetLayerRecursive(child.gameObject, layer);
    }

    static void SetStaticRecursive(GameObject go)
    {
        go.isStatic = true;
        foreach (Transform child in go.transform)
            SetStaticRecursive(child.gameObject);
    }
}
}
