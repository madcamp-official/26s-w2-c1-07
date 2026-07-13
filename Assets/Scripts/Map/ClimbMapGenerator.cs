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

    [Header("도착선")]
    [Tooltip("정상 높이(y = MapHeight, 도달 판정과 동일 기준)에 반투명 도착 평면을 표시한다.")]
    [SerializeField] private bool showFinishPlane = true;
    [Tooltip("도착 평면 머티리얼(반투명 권장). 비우면 표시 생략.")]
    [SerializeField] private Material finishMaterial;

    // ============================ 공개 API ============================
    /// <summary>정상 높이. 임시 점수 로직(높이 비례)의 만점 기준 — 점수 개편 예정.
    /// 맵 생성 전에는 설정 기반 추정치를 돌려준다(생성 후엔 실측: 가장 낮은 레인 정상).</summary>
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

    // ============================ 내부 상태 ============================
    int _builtSeed = int.MinValue;
    bool _built;
    float _topY;                 // 정상 높이(가장 낮은 레인 정상 - 판정 여유)
    Bounds _placementBounds;     // 설치 허용 범위
    Bounds _movementBounds;      // 이동/비행 범위(경계벽 기준)
    Bounds _islandArea;          // 시작 섬 영역(여유 포함, 설치 금지)
    readonly List<PlatformRegion> _regions = new List<PlatformRegion>();
    int _fallbackMatIndex;

    GameObject _staticRoot;      // 시작 섬(설정만 의존, 1회)
    GameObject _structureRoot;   // 레인/경계벽(시드마다 재생성)

    float IslandDepth => (Mathf.Max(1, laneCount) - 1) * laneSpacing + islandPadding * 2f;

    Bounds IslandBounds() => new Bounds(
        new Vector3(0f, -islandThickness * 0.5f, 0f),
        new Vector3(islandWidth, islandThickness, IslandDepth));

    // 설정 기반 정상 추정(생성 전 폴백): 간격 합계는 정규화로 고정이므로 근사 정확.
    float EstimatedTop() =>
        (startGap + Mathf.Max(0, platformsPerLane - 1) * avgGap) * risePerMeter;

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
        _fallbackMatIndex = 0;

        int ground = LayerMask.NameToLayer("Ground");

        // 영역 0 = 시작 섬(하이라이트 귀속·설치 금지 공용).
        Bounds island = IslandBounds();
        _islandArea = Expanded(island, regionPadding);
        _regions.Add(new PlatformRegion
        {
            Id = 0, LaneIndex = -1, Ordinal = 0,
            InnerBounds = island, AreaBounds = _islandArea,
        });

        Bounds footprint = island;
        float minLaneTop = float.PositiveInfinity;
        int made = 0;

        for (int lane = 0; lane < laneCount; lane++)
        {
            float laneTop = BuildLane(lane, seed, ground, ref footprint, ref made);
            minLaneTop = Mathf.Min(minLaneTop, laneTop);
        }

        // 정상 높이 = 가장 낮은 레인 정상(어느 레인으로 올라도 도달 가능해야 하므로) - 판정 여유.
        _topY = (float.IsPositiveInfinity(minLaneTop) ? EstimatedTop() : minLaneTop) - 0.05f;

        // footprint 파생 범위: 설치 허용(좁게) / 이동·경계벽(넓게).
        _placementBounds = WithPad(footprint, placementPadding, -0.5f, _topY + placementHeadroom);
        _movementBounds  = WithPad(footprint, movementPadding, island.min.y - 4f, _topY + movementHeadroom);
        BuildWalls(ground);
        BuildFinishPlane(footprint);
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

    // ============================ 도착선 (footprint 파생, 시드마다) ============================
    // 정상 높이(_topY = 도달 판정 기준)에 맵 전체를 덮는 반투명 평면을 띄운다:
    // "이 면을 넘으면 도착"이 판정과 1:1 로 일치하는 정직한 표시. 콜라이더 없음(시각 전용),
    // Ground 레이어가 아니므로 카메라 오클루전에도 걸리지 않는다.
    void BuildFinishPlane(Bounds footprint)
    {
        if (!showFinishPlane || finishMaterial == null) return;

        var plane = GameObject.CreatePrimitive(PrimitiveType.Cube);
        plane.name = "FinishPlane";
        plane.transform.SetParent(_structureRoot.transform, false);
        plane.transform.position = new Vector3(footprint.center.x, _topY, footprint.center.z);
        plane.transform.localScale = new Vector3(footprint.size.x + 6f, 0.05f, footprint.size.z + 6f);
        Destroy(plane.GetComponent<Collider>()); // 시각 전용 — 이동/설치에 간섭 금지
        var mr = plane.GetComponent<MeshRenderer>();
        mr.sharedMaterial = finishMaterial;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
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
