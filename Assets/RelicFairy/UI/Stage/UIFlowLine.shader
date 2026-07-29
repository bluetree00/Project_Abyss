Shader "UI/FlowLine"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color        ("Tint", Color) = (1,1,1,1)

        _DashCount    ("Dash Count (along length)", Float) = 6
        _DashRatio    ("Dash Ratio (0~1, filled portion)", Range(0,1)) = 0.5
        _ScrollSpeed  ("Scroll Speed", Float) = 1

        // Glow
        _GlowColor    ("Glow Color", Color) = (1, 0.8, 0.4, 1)
        _GlowWidth    ("Glow Width (UV, 0=off)", Range(0, 0.5)) = 0.15
        _GlowIntensity("Glow Intensity", Range(0, 3)) = 1.5
        _GlowPulseSpeed("Glow Pulse Speed (0=steady)", Float) = 2

        // UI Masking
        _StencilComp  ("Stencil Comparison", Float) = 8
        _Stencil      ("Stencil ID", Float) = 0
        _StencilOp    ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask  ("Stencil Read Mask", Float) = 255
        _ColorMask    ("Color Mask", Float) = 15
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "IgnoreProjector"="True"
            "RenderType"="Transparent"
            "PreviewType"="Plane"
            "CanUseSpriteAtlas"="True"
        }

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0

            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float4 color  : COLOR;
                float2 uv     : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                fixed4 color  : COLOR;
                float2 uv     : TEXCOORD0;
                float4 worldPosition : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            fixed4 _Color;
            float  _DashCount;
            float  _DashRatio;
            float  _ScrollSpeed;
            fixed4 _GlowColor;
            float  _GlowWidth;
            float  _GlowIntensity;
            float  _GlowPulseSpeed;
            float4 _ClipRect;

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.worldPosition = v.vertex;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.color = v.color * _Color;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // ── 대시 패턴 ──
                float u = i.uv.x;
                u = u * _DashCount - _Time.y * _ScrollSpeed;
                float pattern = frac(u);
                float dashAlpha = step(pattern, _DashRatio);

                // ── 기본 선 색상 ──
                fixed4 col = i.color;
                col.a *= dashAlpha;

                // ── Glow (가장자리 발광) ──
                if (_GlowWidth > 0)
                {
                    // V축 0~1에서 중심(0.5)으로부터의 거리
                    float distFromCenter = abs(i.uv.y - 0.5) * 2.0; // 0(중심)~1(가장자리)

                    // 중심부 = 1, 가장자리 = 0 으로 부드럽게 감쇠
                    float coreFade = 1.0 - smoothstep(1.0 - _GlowWidth * 2.0, 1.0, distFromCenter);

                    // 펄스 (시간에 따라 밝기 변동)
                    float pulse = 1.0;
                    if (_GlowPulseSpeed > 0)
                        pulse = lerp(0.6, 1.0, (sin(_Time.y * _GlowPulseSpeed) * 0.5 + 0.5));

                    // Glow 합성: 기본 색에 발광색을 가산
                    float glowFactor = coreFade * _GlowIntensity * pulse * dashAlpha;
                    col.rgb += _GlowColor.rgb * glowFactor * _GlowColor.a;

                    // 알파도 glow로 약간 확장 (가장자리가 살짝 보임)
                    float edgeGlow = (1.0 - smoothstep(0.7, 1.0, distFromCenter)) * dashAlpha;
                    col.a = max(col.a, edgeGlow * _GlowColor.a * pulse * 0.5);
                }

                // ── UI clipping ──
                col.a *= UnityGet2DClipping(i.worldPosition.xy, _ClipRect);

                clip(col.a - 0.001);
                return col;
            }
            ENDCG
        }
    }
}
