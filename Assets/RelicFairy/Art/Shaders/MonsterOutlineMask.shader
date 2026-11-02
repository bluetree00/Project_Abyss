// MonsterOutlineMask.shader
// 적 외곽선 「바깥 테두리만」용 스텐실 표시(10-30 사용자 「보스 테두리가 아직 두껍다」).
// Render Objects 피처 MonsterOutlineMask(LayerMask=Monster, AfterRenderingOpaques, MonsterRim 바로 앞)의 override 머티리얼.
// 몸이 보이는 픽셀(ZTest LEqual — 본체 불투명 패스가 깊이를 썼다)에 스텐실 비트 128만 찍는다. 색 · 깊이는 쓰지 않는다.
// MonsterOutline은 이 비트가 없는 곳에만 그린다 → 인버티드 헐이 몸 안쪽(겹친 부위 · 갑옷 · 사슬 조각)에 긋던 선이 사라진다.
// 비트 128 — UI 마스크(아래 비트부터 쓴다)와 겹치지 않게 맨 위 비트만 읽고 쓴다.
Shader "RelicFairy/MonsterOutlineMask"
{
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }

        Pass
        {
            ColorMask 0
            ZWrite Off
            ZTest LEqual
            Offset -1, -1    // 본체(셰이더 그래프)와 같은 면 — 깊이 계산이 조금만 달라도 LEqual이 군데군데 떨어져 안쪽 선이 점으로 남는다
            Cull Back
            Stencil
            {
                Ref 128
                WriteMask 128
                Comp Always
                Pass Replace
            }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings { float4 positionHCS : SV_POSITION; };

            Varyings vert (Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                return OUT;
            }

            half4 frag (Varyings IN) : SV_Target { return 0; }
            ENDHLSL
        }
    }
    Fallback Off
}
