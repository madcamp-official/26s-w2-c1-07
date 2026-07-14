namespace RouletteParty.Net
{
using Unity.Netcode;
using UnityEngine;
using RouletteParty.UI; // PlayerPalette

/// <summary>
/// 플레이어 몸체를 소유 clientId 색으로 칠한다. 서로를 한눈에 구분하는 플레이어 식별 장치로,
/// 스틱맨 모델 머티리얼에 적용 중이며 이름표(GameHUD)와 색을 공유한다.
///
/// 안전 설계:
///  - 색은 결정론적(PlayerPalette.ColorFor)이라 모든 클라가 같은 색을 그린다 → 네트워크 동기화 불필요.
///  - MaterialPropertyBlock 으로 인스턴스별 색만 덮어써 공유 머티리얼을 훼손하지 않는다.
///  - URP/Lit(_BaseColor)와 레거시(_Color) 둘 다 세팅(없는 프로퍼티는 무시되므로 무해).
///
/// 사용: Player 프리팹 루트(NetworkObject 있는 곳)에 이 컴포넌트를 추가하면 끝(인스펙터 설정 없음).
/// 캐릭터 모델을 붙인 뒤에도 그대로 두면 모델 머티리얼에 색이 입혀진다(원치 않으면 제거).
/// </summary>
[DisallowMultipleComponent]
public class PlayerTint : NetworkBehaviour
{
    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor"); // URP/Lit
    static readonly int ColorId     = Shader.PropertyToID("_Color");     // 레거시/폴백

    public override void OnNetworkSpawn()
    {
        // OwnerClientId 는 스폰 이후 유효. 소유자/원격 모두 동일 색을 계산해 칠한다.
        Color c = PlayerPalette.ColorFor(OwnerClientId);

        var mpb = new MaterialPropertyBlock();
        var renderers = GetComponentsInChildren<Renderer>(true);
        foreach (var r in renderers)
        {
            if (r == null) continue;
            r.GetPropertyBlock(mpb);   // 기존 값 보존 후 색만 덮어씀
            mpb.SetColor(BaseColorId, c);
            mpb.SetColor(ColorId, c);
            r.SetPropertyBlock(mpb);
        }
    }
}
}
