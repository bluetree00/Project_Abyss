// MonsterOutline.shader
// 적 외곽선(보이는 영역) — 인버티드 헐: 정점을 노멀 방향으로 _OutlineWidth만큼 팽창 + Cull Front + 단색.
// Render Objects 피처(LayerMask=Monster, AfterRenderingOpaques)의 override 머티리얼로 사용.
// ZTest LEqual = 가시 영역에서만 테두리. ZWrite Off = 깊이 미기록 → 본체 불투명 패스가 z-reject 안 됨(본체 항상 보임).
// 두께/색 튜닝은 머티리얼 인스펙터(_OutlineWidth/_OutlineColor)에서 사용자가 조정. 기본은 은은하게.
Shader "RelicFairy/MonsterOutline"
{
    Properties
    {
        _OutlineColor ("Outline Color", Color)        = (0.6, 0.85, 1.0, 1.0)
        _OutlineWidth ("Outline Width (object)", Range(0, 0.15)) = 0.02
        // 피격 플래시: VictimHitFeedback이 렌더러 MPB로 _HitFlash(0~1)를 구동 → _HitFlashColor로 블렌드.
        _HitFlash      ("Hit Flash Amount", Range(0, 1)) = 0
        _HitFlashColor ("Hit Flash Color", Color)        = (1.0, 0.12, 0.1, 1.0)
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }

        Pass
        {
            Cull Front       // 팽창한 껍데기의 바깥(뒷)면만 → 외곽 링
            ZWrite Off       // 깊이 미기록 → 본체 불투명 패스가 막히지 않음(본체 항상 렌더)
            ZTest LEqual     // 보이는 영역

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _OutlineColor;
                float  _OutlineWidth;
                float4 _HitFlashColor;
                float  _HitFlash;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings   { float4 positionHCS : SV_POSITION; };

            Varyings vert (Attributes IN)
            {
                Varyings OUT;
                float3 inflated = IN.positionOS.xyz + normalize(IN.normalOS) * _OutlineWidth;
                OUT.positionHCS = TransformObjectToHClip(inflated);
                return OUT;
            }

            half4 frag (Varyings IN) : SV_Target
            {
                return lerp(_OutlineColor, _HitFlashColor, saturate(_HitFlash));
            }
            ENDHLSL
        }
    }
    Fallback Off
}
