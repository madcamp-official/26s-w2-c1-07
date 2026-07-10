using UnityEngine;
using UnityEngine.Rendering; // IndexFormat

/// <summary>
/// 결정론적 하이트맵으로 지형 Mesh 를 코드 생성하고 MeshCollider 를 붙인다.
/// 각 클라이언트가 동일한 함수로 동일한 메시를 만들므로 네트워크 동기화가 필요 없다.
/// (NetworkObject / NetworkBehaviour 를 붙이지 말 것 — 평범한 씬 오브젝트로 둔다.)
/// SampleHeight(x,z) 는 static 이라 카메라 클램프·스폰 y 계산에 재사용한다.
/// </summary>
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class TerrainGenerator : MonoBehaviour
{
    // === 결정론적 하이트맵 파라미터 (모든 클라 동일해야 함) ===
    public const int   SIZE_X  = 80;
    public const int   SIZE_Z  = 160;
    public const float WATER_Y = -1.2f;
    public const float DEATH_Y = WATER_Y - 2.5f; // -3.7

    [Header("머티리얼")]
    [Tooltip("Custom/VertexColorLit 셰이더를 쓰는 머티리얼. 비우면 코드에서 자동 생성한다.")]
    public Material terrainMaterial;
    [Tooltip("선택: 강물 수면용 머티리얼(비우면 URP 기본 머티리얼).")]
    public Material waterMaterial;

    [Header("옵션")]
    [Tooltip("강물 수면 Plane 을 자동 생성할지 여부.")]
    public bool createWater = true;

    const string ShaderName = "Custom/VertexColorLit";

    // 씬 로드 시점에 즉시 생성해 두어야 플레이어가 스폰 직후 지형 위에 선다.
    void Awake()
    {
        BuildTerrain();
        if (createWater) BuildWater();
    }

    /// <summary>
    /// 결정론적 지면 높이. 어느 월드 좌표(x,z)든 지면 y 를 돌려준다.
    /// 카메라 지형 클램프·안전 스폰 y 계산에 재사용한다. (메시 생성과 독립적으로 언제나 호출 가능)
    /// </summary>
    public static float SampleHeight(float x, float z)
    {
        float h = 3.2f * Mathf.Sin(x * 0.06f) * Mathf.Cos(z * 0.045f)
                + 1.8f * Mathf.Sin(x * 0.11f + z * 0.07f)
                + 1.0f * Mathf.Sin(z * 0.19f);

        float edge = Mathf.Abs(x) / (SIZE_X / 2f);
        h += Mathf.Pow(edge, 2.2f) * 10f;

        float riverCenter = 12f * Mathf.Sin(z * 0.028f) + 6f * Mathf.Sin(z * 0.011f);
        float d    = Mathf.Abs(x - riverCenter);
        float bank = 9f;
        if (d < bank)
        {
            float t = Mathf.Clamp01(1f - d / bank);
            float smooth = t * t * (3f - 2f * t); // smoothstep
            h -= smooth * 6.5f;
        }
        return h;
    }

    void BuildTerrain()
    {
        int vx = SIZE_X + 1;         // X축 정점 수 (81)
        int vz = SIZE_Z + 1;         // Z축 정점 수 (161)
        int vertCount = vx * vz;     // 13,041 (< 65,535 -> UInt16 인덱스로 충분)

        var vertices = new Vector3[vertCount];
        var uvs      = new Vector2[vertCount];
        var colors   = new Color[vertCount];

        float halfX = SIZE_X / 2f;   // 월드 X: [-40, 40]
        float halfZ = SIZE_Z / 2f;   // 월드 Z: [-80, 80] (중앙 정렬 -> 매치 좌표 START_Z=68/GOAL_Z=-70 와 일치)

        for (int zi = 0; zi < vz; zi++)
        {
            for (int xi = 0; xi < vx; xi++)
            {
                float x = xi - halfX;      // -40 .. 40
                float z = zi - halfZ;      // -80 .. 80 (중앙 정렬)
                float y = SampleHeight(x, z);

                int i = zi * vx + xi;
                vertices[i] = new Vector3(x, y, z);
                uvs[i]      = new Vector2((float)xi / SIZE_X, (float)zi / SIZE_Z);
                // .linear: URP 는 기본 Linear 컬러스페이스. mesh vertex color 는 자동 감마 변환이
                // 안 되므로 여기서 sRGB->linear 로 넣어 화면에서 색이 물빠져 보이지 않게 한다.
                colors[i] = HeightColor(y).linear;
            }
        }

        int quadCount = SIZE_X * SIZE_Z;
        var triangles = new int[quadCount * 6];
        int t = 0;
        for (int zi = 0; zi < SIZE_Z; zi++)
        {
            for (int xi = 0; xi < SIZE_X; xi++)
            {
                int bl = zi * vx + xi;     // bottom-left
                int br = bl + 1;           // bottom-right
                int tl = bl + vx;          // top-left
                int tr = bl + vx + 1;      // top-right

                // 이 와인딩이 +Y(위쪽) 노멀을 만든다. (cross((0,0,1),(1,0,1)) = (0,1,0) 로 검증)
                triangles[t++] = bl; triangles[t++] = tl; triangles[t++] = tr;
                triangles[t++] = bl; triangles[t++] = tr; triangles[t++] = br;
            }
        }

        var mesh = new Mesh { name = "ProceduralTerrain" };
        if (vertCount > 65535) mesh.indexFormat = IndexFormat.UInt32; // 현재는 불필요, 방어적 처리
        mesh.vertices  = vertices;
        mesh.uv        = uvs;
        mesh.colors    = colors;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();  // 반드시 vertices/triangles 지정 후
        mesh.RecalculateBounds();

        GetComponent<MeshFilter>().sharedMesh = mesh;

        var mr  = GetComponent<MeshRenderer>();
        var mat = terrainMaterial != null ? terrainMaterial : CreateFallbackMaterial();
        if (mat != null) mr.sharedMaterial = mat;

        var mc = GetComponent<MeshCollider>();
        if (mc == null) mc = gameObject.AddComponent<MeshCollider>();
        mc.sharedMesh = mesh; // non-convex(기본) -> CharacterController 가 걸어다님
    }

    // terrainMaterial 을 인스펙터에서 지정하지 않았을 때의 안전망.
    // (에디터 플레이엔 문제없음. 빌드 시엔 Graphics > Always Included Shaders 에 셰이더 추가 권장.)
    Material CreateFallbackMaterial()
    {
        var sh = Shader.Find(ShaderName);
        if (sh != null) return new Material(sh);
        Debug.LogWarning($"[TerrainGenerator] '{ShaderName}' 셰이더를 못 찾음. " +
                         "M_Terrain 머티리얼을 인스펙터의 Terrain Material 에 지정하세요.");
        return null;
    }

    Color HeightColor(float y)
    {
        if (y < WATER_Y + 0.6f) return new Color(0.76f, 0.70f, 0.50f); // 모래
        if (y > 7.5f)           return new Color(0.95f, 0.95f, 0.97f); // 설산
        if (y > 3.5f)           return new Color(0.45f, 0.42f, 0.40f); // 바위
        return                         new Color(0.30f, 0.55f, 0.25f); // 풀
    }

    void BuildWater()
    {
        var water = GameObject.CreatePrimitive(PrimitiveType.Plane);
        water.name = "WaterSurface";

        // 수면은 충돌 없음(낙사는 y좌표로 판정, 물 위를 걷게 되면 안 됨).
        var wc = water.GetComponent<MeshCollider>();
        if (wc != null) Destroy(wc);

        water.transform.SetParent(transform, false);
        // Plane 프리미티브는 scale 1 에서 10x10 유닛, 원점 중심.
        water.transform.position   = new Vector3(0f, WATER_Y, 0f);
        water.transform.localScale = new Vector3(SIZE_X / 10f, 1f, SIZE_Z / 10f);

        if (waterMaterial != null)
            water.GetComponent<MeshRenderer>().sharedMaterial = waterMaterial;
        // waterMaterial 이 비면 URP 런타임 기본 머티리얼(회색 반투명 아님, 불투명 회색)로 보인다.
        // 물처럼 보이려면 M_Water(Transparent, 파란색) 를 지정할 것.
    }
}
