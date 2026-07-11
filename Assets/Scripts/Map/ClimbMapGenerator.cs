namespace RouletteParty.Map
{
using System.Collections.Generic;
using UnityEngine;
using RouletteParty.Match; // MatchManager (복제된 MapSeed 폴링)

/// <summary>
/// 클라이밍 타워 랜덤 맵 생성기(결정론). 씬 오브젝트에 부착한다.
///
///  - 맵 볼륨(구조물 생성 범위): mapWidth x mapDepth x mapHeight (기본 6 x 6 x 50). 랜덤 발판 +
///    플레이어 설치 구조물의 위치 검증 기준(InsideVolume). 지면(floorWidth x floorDepth, 기본 24x24)은
///    이보다 넓으며, 플레이어 이동 가능 범위(경계벽)와 PREP 비행 범위의 기준이 된다. 바닥 + 투명
///    경계벽은 Awake 에 1회 생성.
///  - 랜덤 구조물: 맵 볼륨을 높이 기준 sliceCount(x) 등분하고, 각 슬라이스마다 structuresPerSlice(y)개의
///    "보이는 구조물"(박스/원기둥 프리미티브)을 서로 겹치지 않게 랜덤 배치한다.
///  - 결정론: 호스트가 복제한 시드(MatchManager.MapSeed)로 System.Random 을 돌려 모든 피어가
///    동일한 맵을 로컬 생성한다(NetworkObject 스폰 없음, late-join 자동 대응).
///  - 지오메트리는 전부 Ground 레이어(카메라 오클루전 호환).
///
/// 시드 변화 감지는 Update 폴링(정수 비교 1회/프레임)으로 한다: MatchManager 가 씬 NetworkObject 라
/// 스폰 시점이 이 컴포넌트의 활성 시점과 어긋날 수 있어 이벤트 배선보다 폴링이 단순·안전하다.
/// </summary>
public class ClimbMapGenerator : MonoBehaviour
{
    public static ClimbMapGenerator Instance { get; private set; }

    [Header("맵 볼륨 (docs/클라이밍_전환_명세서.md 3절) — 구조물 생성 범위 전용(랜덤 발판 + 플레이어 설치 구조물 검증). 플레이어 이동/비행 범위와는 별개, 건드리지 말 것")]
    [SerializeField] private float mapWidth  = 6f;   // 가로(X)
    [SerializeField] private float mapDepth  = 6f;   // 세로(Z)
    [SerializeField] private float mapHeight = 50f;  // 높이(Y)

    [Header("지면(바닥) 크기 — 구조물 생성 범위(위 맵 볼륨)와 별개, 더 넓다. 경계벽(플레이어 이동 가능 범위)과 PREP 비행 범위의 기준이 된다(기본 24x24 = 구조물 생성 범위 6x6 대비 16배 면적).")]
    [SerializeField] private float floorWidth = 24f;
    [SerializeField] private float floorDepth = 24f;

    [Header("랜덤 생성 규칙")]
    [Tooltip("높이 등분 수(x). 슬라이스 높이 = mapHeight / sliceCount.")]
    [SerializeField] private int sliceCount = 75;
    [Tooltip("슬라이스당 보이는 구조물 개수(y).")]
    [SerializeField] private int structuresPerSlice = 2;
    [Tooltip("구조물 간 겹침 판정 여유(AABB 확장).")]
    [SerializeField] private float minStructureSpacing = 0.4f;
    [Tooltip("슬라이스당 배치 재시도 상한(겹침 시).")]
    [SerializeField] private int maxPlacementAttempts = 30;

    [Header("표시")]
    [Tooltip("경계벽을 눈에 보이게 렌더링할지(기본: 콜라이더만).")]
    [SerializeField] private bool showBoundaryWalls = false;
    [Tooltip("랜덤 구조물 머티리얼(캔디 팔레트). 비우면 기본 머티리얼.")]
    [SerializeField] private Material[] structureMaterials;
    [Tooltip("바닥 머티리얼.")]
    [SerializeField] private Material floorMaterial;

    public float MapWidth  => mapWidth;
    public float MapDepth  => mapDepth;
    public float MapHeight => mapHeight;
    public float FloorWidth => floorWidth;
    public float FloorDepth => floorDepth;

    int _builtSeed = int.MinValue;
    GameObject _staticRoot;     // 바닥/경계벽(시드 무관, 1회)
    GameObject _structureRoot;  // 랜덤 구조물(시드마다 재생성)

    // 겹침 판정용 배치 기록(순수 수학 AABB -> 물리와 무관하게 결정론 보장)
    readonly List<Bounds> _placed = new List<Bounds>();

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

    /// <summary>월드 좌표가 맵 볼륨(구조물 생성 범위) 안인지. 랜덤 발판 + 플레이어 설치 구조물 위치 검증용(서버·클라 공용).
    /// 플레이어 이동/비행 범위(지면 크기, FloorWidth/FloorDepth)와는 별개 — 구조물은 이 좁은 범위에만 존재한다.</summary>
    public bool InsideVolume(Vector3 p, float margin = 0f)
    {
        return Mathf.Abs(p.x) <= mapWidth * 0.5f - margin &&
               Mathf.Abs(p.z) <= mapDepth * 0.5f - margin &&
               p.y >= -0.5f && p.y <= mapHeight + margin;
    }

    // ============================ 바닥 + 경계벽 ============================
    void BuildStatic()
    {
        _staticRoot = new GameObject("ClimbMap_Static");
        _staticRoot.transform.SetParent(transform, false);
        int ground = LayerMask.NameToLayer("Ground");

        // 바닥: 윗면 y=0. 발판(구조물) 생성 영역(mapWidth/mapDepth)과 별개로 더 넓게(floorWidth/floorDepth).
        var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        floor.name = "Floor";
        floor.transform.SetParent(_staticRoot.transform, false);
        floor.transform.localScale = new Vector3(floorWidth, 1f, floorDepth);
        floor.transform.position = new Vector3(0f, -0.5f, 0f);
        floor.layer = ground;
        floor.isStatic = true;
        if (floorMaterial != null) floor.GetComponent<MeshRenderer>().sharedMaterial = floorMaterial;

        // 경계벽 4면: 콜라이더만(이탈 방지). 카메라 오클루전이 벽에 걸려 맵 안에 머물게 Ground 레이어.
        // 플레이어 이동 가능 범위 = 지면 크기(floorWidth/floorDepth) 기준. 구조물 생성 범위(mapWidth/mapDepth)와는 별개.
        float h = mapHeight + 6f;
        float t = 0.5f;
        BuildWall("Wall_XPos", new Vector3( floorWidth * 0.5f + t * 0.5f, h * 0.5f - 1f, 0f), new Vector3(t, h, floorDepth + 2f * t), ground);
        BuildWall("Wall_XNeg", new Vector3(-floorWidth * 0.5f - t * 0.5f, h * 0.5f - 1f, 0f), new Vector3(t, h, floorDepth + 2f * t), ground);
        BuildWall("Wall_ZPos", new Vector3(0f, h * 0.5f - 1f,  floorDepth * 0.5f + t * 0.5f), new Vector3(floorWidth + 2f * t, h, t), ground);
        BuildWall("Wall_ZNeg", new Vector3(0f, h * 0.5f - 1f, -floorDepth * 0.5f - t * 0.5f), new Vector3(floorWidth + 2f * t, h, t), ground);
    }

    void BuildWall(string name, Vector3 pos, Vector3 scale, int layer)
    {
        var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wall.name = name;
        wall.transform.SetParent(_staticRoot.transform, false);
        wall.transform.position = pos;
        wall.transform.localScale = scale;
        wall.layer = layer;
        wall.isStatic = true;
        wall.GetComponent<MeshRenderer>().enabled = showBoundaryWalls;
    }

    // ============================ 랜덤 구조물 ============================
    void BuildStructures(int seed)
    {
        if (_structureRoot != null) Destroy(_structureRoot);
        _structureRoot = new GameObject("ClimbMap_Structures");
        _structureRoot.transform.SetParent(transform, false);
        _placed.Clear();

        int ground = LayerMask.NameToLayer("Ground");
        var rng = new System.Random(seed);
        float sliceH = mapHeight / Mathf.Max(1, sliceCount);
        int made = 0;

        for (int si = 0; si < sliceCount; si++)
        {
            float baseY = si * sliceH;
            for (int j = 0; j < structuresPerSlice; j++)
            {
                for (int attempt = 0; attempt < maxPlacementAttempts; attempt++)
                {
                    // 종류/크기/회전을 시드 기반으로 결정(모든 피어 동일).
                    bool cylinder = rng.NextDouble() < 0.4;
                    bool rot90 = rng.NextDouble() < 0.5;
                    // 발판 크기: 좁은 맵(6x6)에 맞춘 소형 플랫폼.
                    Vector3 size = cylinder
                        ? new Vector3(1.2f, 0.3f, 1.2f)
                        : new Vector3(1.6f, 0.3f, 0.9f);
                    if (rot90 && !cylinder) size = new Vector3(size.z, size.y, size.x);

                    float halfX = (mapWidth - size.x) * 0.5f - 0.1f;
                    float halfZ = (mapDepth - size.z) * 0.5f - 0.1f;
                    Vector3 pos = new Vector3(
                        (float)(rng.NextDouble() * 2.0 - 1.0) * halfX,
                        baseY + (float)rng.NextDouble() * sliceH,
                        (float)(rng.NextDouble() * 2.0 - 1.0) * halfZ);

                    // 순수 AABB 겹침 검사(물리 미사용 -> 결정론 보장).
                    var b = new Bounds(pos, size + Vector3.one * minStructureSpacing);
                    bool overlap = false;
                    for (int k = 0; k < _placed.Count; k++)
                        if (_placed[k].Intersects(b)) { overlap = true; break; }
                    if (overlap) continue;

                    _placed.Add(b);
                    var go = GameObject.CreatePrimitive(cylinder ? PrimitiveType.Cylinder : PrimitiveType.Cube);
                    go.name = $"S{si}_{j}";
                    go.transform.SetParent(_structureRoot.transform, false);
                    // Cylinder 프리미티브는 scale.y 1 당 높이 2 -> y 스케일 보정.
                    go.transform.localScale = cylinder
                        ? new Vector3(size.x, size.y * 0.5f, size.z)
                        : size;
                    go.transform.position = pos;
                    go.layer = ground;
                    go.isStatic = true;
                    if (structureMaterials != null && structureMaterials.Length > 0)
                    {
                        var mat = structureMaterials[made % structureMaterials.Length];
                        if (mat != null) go.GetComponent<MeshRenderer>().sharedMaterial = mat;
                    }
                    made++;
                    break;
                }
            }
        }

        Debug.Log($"[ClimbMap] seed={seed} 구조물 {made}개 생성 (슬라이스 {sliceCount} x {structuresPerSlice})");
    }
}
}
