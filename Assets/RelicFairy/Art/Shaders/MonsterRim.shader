// MonsterRim.shader
// ① 상시 프레넬 림 — 적 외곽을 은은하게 빛내 전장 가시성 향상.
// Render Objects 피처(LayerMask=Monster, ZTest LEqual)의 override 머티리얼로 사용 — 보이는 면에 가산(Additive) 합성.
// 원본 폴리아트 셰이더를 건드리지 않고(Surgical), VictimHitFeedback MPB와도 충돌하지 않음(별도 패스).
// 색/강도/날카로움 튜닝은 머티리얼 인스펙터(_RimColor/_RimIntensity/_RimPower)에서 사용자가 조정. 기본은 "은은하게".
Shader "RelicFairy/MonsterRim"
{
    Properties
    {
        _RimColor     ("Rim Color", Color)          = (0.6, 0.85, 1.0, 1.0)
        _RimPower     ("Rim Power (날카로움)", Range(0.5, 8)) = 3.0
        _RimIntensity ("Rim Intensity", Range(0, 3)) = 0.55
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }

        Pass
        {
            Blend One One      // Additive — 원본 위에 림만 더함
            ZWrite Off
            ZTest LEqual       // 보이는(앞쪽) 면에만
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _RimColor;
                float  _RimPower;
                float  _RimIntensity;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 normalWS    : TEXCOORD0;
                float3 positionWS  : TEXCOORD1;
            };

            Varyings vert (Attributes IN)
            {
                Varyings OUT;
                OUT.positionWS  = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.positionHCS = TransformWorldToHClip(OUT.positionWS);
                OUT.normalWS    = TransformObjectToWorldNormal(IN.normalOS);
                return OUT;
            }

            half4 frag (Varyings IN) : SV_Target
            {
                float3 N = normalize(IN.normalWS);
                float3 V = normalize(GetWorldSpaceViewDir(IN.positionWS));
                float fresnel = pow(saturate(1.0 - saturate(dot(N, V))), _RimPower);
                half3 rim = _RimColor.rgb * (fresnel * _RimIntensity);
                return half4(rim, 1.0); // Additive 라 alpha 무시
            }
            ENDHLSL
        }
    }
    Fallback Off
}
