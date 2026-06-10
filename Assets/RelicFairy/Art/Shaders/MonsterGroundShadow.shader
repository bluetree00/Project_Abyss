// MonsterGroundShadow.shader
// 발밑 가짜 그림자(블롭) — 절차적 소프트 원 + 선택적 외곽 링(정예/보스 표기).
// URP Unlit / 투명 / 양면. 텍스처 에셋 없이 프래그먼트에서 UV 거리로 원을 그린다.
// 시각 튜닝(_Color 진하기/_Inner·_Outer 부드러움/_Ring*)은 머티리얼 인스펙터에서 사용자가 조정.
Shader "RelicFairy/MonsterGroundShadow"
{
    Properties
    {
        _Color     ("Shadow Color", Color)     = (0, 0, 0, 0.5)
        _Inner     ("Inner Radius", Range(0,1))= 0.0
        _Outer     ("Outer Radius", Range(0,1))= 0.5
        _RingColor ("Ring Color", Color)       = (1, 1, 1, 0.0)
        _RingInner ("Ring Inner", Range(0,1))  = 0.42
        _RingOuter ("Ring Outer", Range(0,1))  = 0.5
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }

        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float  _Inner;
                float  _Outer;
                float4 _RingColor;
                float  _RingInner;
                float  _RingOuter;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings   { float4 positionHCS : SV_POSITION; float2 uv : TEXCOORD0; };

            Varyings vert (Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = IN.uv;
                return OUT;
            }

            half4 frag (Varyings IN) : SV_Target
            {
                // 중심(0.5,0.5) 기준 거리
                float dist = length(IN.uv - 0.5) * 2.0; // 0(중심)~1(모서리)

                // 채움 원 — Outer 바깥은 0, Inner 안쪽은 1
                float fill = 1.0 - smoothstep(_Inner, _Outer, dist);
                half4 col  = _Color;
                col.a     *= fill;

                // 외곽 링(정예/보스) — RingInner~RingOuter 사이에 띠
                float ring = smoothstep(_RingInner, (_RingInner + _RingOuter) * 0.5, dist)
                           * (1.0 - smoothstep((_RingInner + _RingOuter) * 0.5, _RingOuter, dist));
                half4 ringCol = _RingColor;
                ringCol.a    *= ring;

                // 링을 그림자 위에 알파 합성
                half3 rgb = lerp(col.rgb, ringCol.rgb, ringCol.a);
                half  a   = saturate(col.a + ringCol.a);
                return half4(rgb, a);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
