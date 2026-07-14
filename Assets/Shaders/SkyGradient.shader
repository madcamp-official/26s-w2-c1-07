// 스타일라이즈드 3색 그라데이션 스카이박스(얼티밋 치킨 호스 풍의 부드러운 파스텔 하늘).
// "반지름 1000 대형 구" 요구를 스카이박스로 대체한 이유:
//  - 스카이박스는 깊이 무한대 취급이라 카메라 far plane(500)에 잘리지 않고 어디서든 자연스럽다.
//  - 구 메시는 컬링/카메라 추적/스케일 관리가 필요하지만 스카이박스는 렌더 설정 한 줄이다.
//  - 시각 결과는 동일(방향 벡터 기반 그라데이션)하고 포그와도 자연스럽게 섞인다.
Shader "RouletteParty/SkyGradient"
{
    Properties
    {
        _TopColor    ("Top Color",     Color) = (0.30, 0.55, 0.90, 1)
        _MidColor    ("Horizon Color", Color) = (0.70, 0.88, 0.95, 1)
        _BottomColor ("Bottom Color",  Color) = (0.93, 0.88, 0.76, 1)
        _TopSpread    ("Top Spread",    Range(0.05, 1.0)) = 0.6
        _BottomSpread ("Bottom Spread", Range(0.05, 1.0)) = 0.35

        [Header(Sun)]
        _SunColor        ("Sun Color", Color) = (1.0, 0.90, 0.65, 1)
        // 태양을 향하는 방향(= -라이트 forward). SunSync 가 디렉셔널 라이트에서 매 프레임 동기화.
        _SunDirection    ("Sun Direction (to sun)", Vector) = (-0.96, 0.28, 0, 0)
        _SunSize         ("Sun Size", Range(0.001, 0.08)) = 0.015
        _SunGlowPower    ("Sun Glow Power", Range(1, 64)) = 8
        _SunGlowStrength ("Sun Glow Strength", Range(0, 2)) = 0.6
    }
    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" }
        Cull Off
        ZWrite Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            fixed4 _TopColor;
            fixed4 _MidColor;
            fixed4 _BottomColor;
            float  _TopSpread;
            float  _BottomSpread;
            fixed4 _SunColor;
            float4 _SunDirection;
            float  _SunSize;
            float  _SunGlowPower;
            float  _SunGlowStrength;

            struct appdata { float4 vertex : POSITION; };
            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 dir : TEXCOORD0;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.dir = v.vertex.xyz; // 스카이박스 메시는 방향 벡터 그 자체
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float3 dir = normalize(i.dir);
                float y = dir.y; // -1(바닥) ~ 1(천정)
                float up   = smoothstep(0.0, 1.0, saturate(y / _TopSpread));
                float down = smoothstep(0.0, 1.0, saturate(-y / _BottomSpread));
                fixed4 c = lerp(_MidColor, _TopColor, up);
                c = lerp(c, _BottomColor, down);

                // 태양: 뷰 방향-태양 방향 내적으로 원반(선명) + 글로우(부드러운 halo)를 가산.
                float3 sunDir = normalize(_SunDirection.xyz);
                float d = saturate(dot(dir, sunDir));
                float disc = smoothstep(1.0 - _SunSize, 1.0 - _SunSize * 0.35, d);
                float glow = pow(d, _SunGlowPower) * _SunGlowStrength;
                c.rgb += _SunColor.rgb * (disc + glow);
                return c;
            }
            ENDCG
        }
    }
    Fallback Off
}
