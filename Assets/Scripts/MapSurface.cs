using UnityEngine;

/// <summary>
/// 씬에 배치된 맵(Ground 레이어) 표면 높이를 물리 레이캐스트로 샘플링한다.
/// 절차 지형(TerrainGenerator) 폐기 후 스폰 위치·장애물 배치 검증이 이 헬퍼를 쓴다.
/// 어떤 맵을 씬에 놓아도 코드 수정 없이 동작한다(맵 콜라이더에 Ground 레이어만 지정).
/// 플레이어/장애물은 Default 레이어이므로 지면 판정에서 자연히 제외된다.
/// </summary>
public static class MapSurface
{
    public const float RAY_TOP    = 60f;   // 맵 최고점보다 높은 시작 고도
    public const float RAY_LENGTH = 120f;

    static int _mask;
    static int Mask
    {
        get
        {
            if (_mask == 0) _mask = LayerMask.GetMask("Ground");
            if (_mask == 0) _mask = ~0; // Ground 레이어가 없는 프로젝트 안전망
            return _mask;
        }
    }

    /// <summary>(x,z) 위에서 내려쏜 레이가 맞은 지면 y. 지면이 없으면 false(틈/맵 밖/슬라임 바다).</summary>
    public static bool SampleGroundY(float x, float z, out float y)
    {
        if (Physics.Raycast(new Vector3(x, RAY_TOP, z), Vector3.down, out RaycastHit hit,
                            RAY_LENGTH, Mask, QueryTriggerInteraction.Ignore))
        {
            y = hit.point.y;
            return true;
        }
        y = 0f;
        return false;
    }
}
