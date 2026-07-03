// MonsterSilhouette.shader
// ② 오클루전 실루엣 — 적이 벽/지형에 가렸을 때만 단색으로 비쳐 보이게.
// Render Objects 피처(LayerMask=Monster, ZTest Greater)의 override 머티리얼로 사용.
// ZTest Greater = 기존 깊이(벽)보다 "뒤"인 픽셀만 그림 → 가려진 부분만 표기. 안 가린 부분은 원본 그대로.
// 색/진하기 튜닝은 머티리얼 인스펙터(_Color)에서 사용자가 조정.
Shader "RelicFairy/MonsterSilhouette"
{
    Properties
    {
        _Color ("Silhouette Color", Color) = (0.35, 0.6, 1.0, 0.5)
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+10" "RenderPipeline"="UniversalPipeline" }

        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest Greater
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings   { float4 positionHCS : SV_POSITION; };

            Varyings vert (Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                return OUT;
            }

            half4 frag (Varyings IN) : SV_Target
            {
                return _Color;
            }
            ENDHLSL
        }
    }
    Fallback Off
}
