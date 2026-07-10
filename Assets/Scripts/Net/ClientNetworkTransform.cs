using Unity.Netcode.Components;
using UnityEngine;

namespace RouletteParty.Net
{
    /// <summary>
    /// 소유자 권위(owner-authoritative) NetworkTransform.
    ///
    /// NGO 2.x 에서는 기본 NetworkTransform 인스펙터의 "Authority Mode = Owner" 로도 동일한 효과를 낼 수 있지만,
    /// 이 컴포넌트를 쓰면 인스펙터 설정 실수 없이 코드로 소유자 권위를 확정할 수 있어 가장 확실하다.
    /// (Unity 공식 예제의 ClientNetworkTransform 과 동일한 패턴: OnIsServerAuthoritative() => false)
    ///
    /// 사용: 플레이어 프리팹에 기본 NetworkTransform 대신 이 ClientNetworkTransform 컴포넌트를 추가한다.
    /// (둘 다 붙이지는 말 것. NetworkObject 당 이동을 다루는 NetworkTransform 은 하나만.)
    /// </summary>
    [DisallowMultipleComponent]
    public class ClientNetworkTransform : NetworkTransform
    {
        /// <summary>false = 소유자 권위. 소유 클라이언트가 transform 을 바꾸면 서버/타 클라로 복제된다.</summary>
        protected override bool OnIsServerAuthoritative() => false;
    }
}
