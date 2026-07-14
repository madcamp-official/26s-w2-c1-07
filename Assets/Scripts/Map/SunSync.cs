using UnityEngine;

namespace RouletteParty.Map
{
    /// <summary>
    /// 스카이박스 태양 동기화: 디렉셔널 라이트의 방향을 스카이 머티리얼(_SunDirection)에 전달한다.
    /// 라이트를 회전시키면 하늘의 태양 원반이 따라 움직인다(에디터에서도 즉시 반영 - ExecuteAlways).
    /// 씬 배치: 각 씬의 Directional Light 에 부착. 회전이 바뀔 때만 머티리얼에 쓴다.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Light))]
    public class SunSync : MonoBehaviour
    {
        private static readonly int SunDirectionId = Shader.PropertyToID("_SunDirection");

        private Quaternion _lastRotation;
        private bool _applied;

        private void LateUpdate()
        {
            if (_applied && transform.rotation == _lastRotation) return;
            _lastRotation = transform.rotation;

            var sky = RenderSettings.skybox;
            if (sky == null || !sky.HasProperty(SunDirectionId)) return;
            sky.SetVector(SunDirectionId, -transform.forward); // 태양을 향하는 방향 = 빛 진행의 반대
            _applied = true;
        }
    }
}
