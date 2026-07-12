using UnityEngine;

namespace RouletteParty.UI
{
    /// <summary>
    /// IMGUI(OnGUI) 해상도 스케일링 헬퍼.
    ///
    /// uGUI 의 CanvasScaler(ScaleWithScreenSize) 가 하는 일을 IMGUI 에 적용한다:
    /// 모든 OnGUI 레이아웃을 1080p 기준 "가상 픽셀"로 작성하고, 각 OnGUI 첫 줄에서
    /// Apply() 를 호출하면 실제 해상도에 맞춰 통째로 확대/축소된다.
    ///
    ///  - 스케일은 화면 "높이" 기준(CanvasScaler match=height 와 동일 정책):
    ///    가로가 넓은 모니터(21:9 등)에서는 UI 크기는 그대로 두고 시야만 넓어진다.
    ///  - GUI.matrix 는 렌더뿐 아니라 마우스 히트 판정에도 함께 적용되므로
    ///    버튼/슬라이더 입력 좌표를 따로 보정할 필요가 없다.
    ///  - GUI.matrix 는 OnGUI 호출마다 초기화되므로 컴포넌트별로 각자 Apply() 하면 된다.
    /// </summary>
    public static class ImguiScale
    {
        /// <summary>레이아웃 기준 높이(가상 픽셀). uGUI CanvasScaler 기준 해상도와 동일.</summary>
        public const float ReferenceHeight = 1080f;

        /// <summary>현재 해상도의 스케일 팩터(1080p 에서 1.0, 4K 에서 2.0).</summary>
        public static float Factor => Screen.height / ReferenceHeight;

        /// <summary>스케일 적용 후 가상 화면 폭(중앙/우측 정렬 계산용).</summary>
        public static float VirtualWidth => Screen.width / Factor;

        /// <summary>스케일 적용 후 가상 화면 높이(항상 ReferenceHeight 와 같다).</summary>
        public static float VirtualHeight => Screen.height / Factor;

        /// <summary>OnGUI 첫 줄에서 호출. 이후 모든 Rect 는 1080p 기준 좌표로 해석된다.</summary>
        public static void Apply()
        {
            float f = Factor;
            GUI.matrix = Matrix4x4.Scale(new Vector3(f, f, 1f));
        }
    }
}
