// MonsterOutline.shader
// 적 외곽선(보이는 영역) — 인버티드 헐: 뒷면을 노멀 방향으로 밀어 낸 껍데기 + Cull Front + 단색.
// Render Objects 피처(LayerMask=Monster, AfterRenderingOpaques)의 override 머티리얼로 사용.
// ZTest LEqual = 가시 영역에서만 테두리. ZWrite Off = 깊이 미기록 → 본체 불투명 패스가 z-reject 안 됨(본체 항상 보임).
//
// [09-25] 두께를 오브젝트 공간 → 화면 픽셀로 바꿨다. 오브젝트 공간이면 스케일 큰 보스(화룡)·가까운 카메라일수록
//         테두리가 몇 배로 굵어졌다(같은 0.02가 숲 6~8 px, 화룡 근접 10~20 px). 이제 크기·거리와 무관하게 같은 굵기.
//         _OutlineWidth = 1080p 기준 픽셀(해상도에 비례해 맞춘다).
// [09-25] DOTS 인스턴싱 변형 추가 — GPU Resident Drawer로 그리는 MeshRenderer(기사 검 등)에 이 패스가 붙으면
//         변형이 없어 에러 → 에디터 Error Pause가 플레이를 멈췄다.
Shader "RelicFairy/MonsterOutline"
{
    Properties
    {
        _OutlineColor ("Outline Color", Color)                    = (0.6, 0.85, 1.0, 1.0)
        _OutlineWidth ("Outline Width (px @1080p)", Range(0, 6))  = 2
        // 피격 플래시: VictimHitFeedback이 렌더러 MPB로 _HitFlash(0~1)를 구동 → _HitFlashColor로 블렌드.
        _HitFlash      ("Hit Flash Amount", Range(0, 1)) = 0
        _HitFlashColor ("Hit Flash Color", Color)        = (1.0, 0.12, 0.1, 1.0)
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }

        Pass
        {
            Cull Front       // 밀어 낸 껍데기의 바깥(뒷)면만 → 외곽 링
            ZWrite Off       // 깊이 미기록 → 본체 불투명 패스가 막히지 않음(본체 항상 렌더)
            ZTest LEqual     // 보이는 영역

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _OutlineColor;
                float  _OutlineWidth;
                float4 _HitFlashColor;
                float  _HitFlash;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings { float4 positionHCS : SV_POSITION; };

            Varyings vert (Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                float4 clip = TransformObjectToHClip(IN.positionOS.xyz);
                // 노멀을 클립 공간으로 → 화면에서 바깥을 향하는 방향
                float3 nWS  = TransformObjectToWorldNormal(IN.normalOS);
                float2 nCS  = mul((float3x3)GetWorldToHClipMatrix(), nWS).xy;
                float2 dir  = nCS / max(length(nCS), 1e-5);
                // 1080p 기준 픽셀 → NDC(가로는 화면비 보정). clip.w를 곱해 원근 나눗셈 뒤에도 같은 픽셀 폭.
                float2 px   = float2(_ScreenParams.y / _ScreenParams.x, 1.0) * (_OutlineWidth / 540.0);
                clip.xy += dir * px * clip.w;
                OUT.positionHCS = clip;
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
