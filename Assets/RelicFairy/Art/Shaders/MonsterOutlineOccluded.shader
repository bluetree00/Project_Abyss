// MonsterOutlineOccluded.shader
// 적 외곽선(가림 영역) — 인버티드 헐 + ZTest Greater: 벽/지형에 가렸을 때도 "테두리만" 비침.
// Render Objects 피처(LayerMask=Monster, AfterRenderingOpaques)의 override 머티리얼로 사용.
// ZTest Greater = 기존 깊이(벽)보다 뒤인 픽셀만 → 가려진 부분의 외곽 링만 그림(몸 안 비침).
// MonsterOutline과 동일한 인버티드 헐, depth test만 반전. 반투명 알파로 은은하게.
Shader "RelicFairy/MonsterOutlineOccluded"
{
    Properties
    {
        _OutlineColor ("Outline Color", Color)        = (0.45, 0.7, 1.0, 0.6)
        _OutlineWidth ("Outline Width (object)", Range(0, 0.15)) = 0.02
        // 피격 플래시: VictimHitFeedback이 렌더러 MPB로 _HitFlash(0~1)를 구동 → _HitFlashColor로 블렌드.
        _HitFlash      ("Hit Flash Amount", Range(0, 1)) = 0
        _HitFlashColor ("Hit Flash Color", Color)        = (1.0, 0.12, 0.1, 1.0)
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+10" "RenderPipeline"="UniversalPipeline" }

        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            Cull Front
            ZWrite Off
            ZTest Greater    // 가려진(뒤) 영역만

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
                // 플래시 중엔 _HitFlashColor(a=1)로 블렌드 → 가려진 부분도 또렷한 빨강.
                return lerp(_OutlineColor, _HitFlashColor, saturate(_HitFlash));
            }
            ENDHLSL
        }
    }
    Fallback Off
}
