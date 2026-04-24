Shader "Abyss/Elements/ElementRim"
{
    // URP Unlit 기반 Fresnel Rim Glow + 원소별 애니메이션 분기.
    // _ElementType 값에 따라 Fresnel 강도가 시간/위치 함수로 다르게 변조되어
    // 원소마다 고유한 느낌을 준다:
    //   0 Lightning : 빠르고 불규칙한 flicker
    //   1 Water     : 위치(y) 기반 부드러운 파동
    //   2 Fire      : 강한 박동 + 타는 흔들림
    //   3 Grass     : 부드러운 숨쉬기 pulsing
    //   4 Earth     : 거의 정적 + 저주파 진동
    //
    // 사용 방법: 몬스터 렌더러의 sharedMaterials 배열에 PAMaskTint 다음 슬롯으로 이 쉐이더 기반 머티리얼을 추가.
    // ElementNativePalette가 (원본, 원소)별로 Rim 인스턴스를 캐시하고 _BaseColor/_ElementType에 주입한다.

    Properties
    {
        [HDR] _BaseColor("Rim Color (HDR)", Color) = (1,1,1,1)
        _RimPower("Rim Power", Range(0.1, 8)) = 2.5
        _RimIntensity("Rim Intensity", Range(0, 5)) = 1.8
        _RimMinCutoff("Rim Min Cutoff", Range(0, 1)) = 0.02
        [Enum(Lightning,0,Water,1,Fire,2,Grass,3,Earth,4)] _ElementType("Element Type", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderType"   = "Transparent"
            "Queue"        = "Transparent+100"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "ElementRim"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha One
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float3 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float3 viewDirWS  : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            // SRP Batcher 호환을 위해 모든 uniform을 UnityPerMaterial CBuffer에 넣는다.
            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                float _RimPower;
                float _RimIntensity;
                float _RimMinCutoff;
                float _ElementType;
            CBUFFER_END

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);

                VertexPositionInputs pos = GetVertexPositionInputs(IN.positionOS);
                VertexNormalInputs nrm = GetVertexNormalInputs(IN.normalOS);

                OUT.positionCS = pos.positionCS;
                OUT.positionWS = pos.positionWS;
                OUT.normalWS   = nrm.normalWS;
                OUT.viewDirWS  = GetWorldSpaceViewDir(pos.positionWS);
                return OUT;
            }

            // 원소별 Fresnel 강도 변조 — 1.0 기준으로 위/아래 흔들어 생동감 부여.
            float ElementModulation(int etype, float t, float3 wpos)
            {
                // Lightning: 2개의 sin을 곱해 불규칙 flicker + 가끔 깜빡 꺼지는 느낌
                float lightning = 0.55 + 0.45 * (sin(t * 23.7) * sin(t * 11.1) + 0.5);

                // Water: 위치 y축 + x축 오프셋으로 파동이 몸체를 따라 흐름
                float water = 0.65 + 0.35 * sin(t * 1.8 + wpos.y * 3.5 + wpos.x * 1.5);

                // Fire: 2개의 다른 주기 sin을 합산 → 타오르는 듯한 불규칙 박동
                float fire = 0.6 + 0.5 * (0.5 * sin(t * 11.0) + 0.5 * sin(t * 17.0) + 0.5);

                // Grass: 부드럽고 느린 pulsing
                float grass = 0.80 + 0.20 * sin(t * 2.2);

                // Earth: 거의 정적. 미세한 저주파 진동으로 "살아있음"만 표현
                float earth = 0.92 + 0.08 * sin(t * 0.8);

                // 분기 최소화: step/lerp 체인으로 분기 비용 낮춤
                float m = lightning;
                m = (etype == 1) ? water    : m;
                m = (etype == 2) ? fire     : m;
                m = (etype == 3) ? grass    : m;
                m = (etype == 4) ? earth    : m;
                return m;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);

                float3 n = normalize(IN.normalWS);
                float3 v = normalize(IN.viewDirWS);
                float ndv = saturate(dot(n, v));
                float fresnel = 1.0 - ndv;

                fresnel = pow(fresnel, _RimPower);
                fresnel = saturate(fresnel - _RimMinCutoff) / max(1e-4, (1.0 - _RimMinCutoff));
                fresnel *= _RimIntensity;

                int etype = (int)round(_ElementType);
                fresnel *= ElementModulation(etype, _Time.y, IN.positionWS);

                half3 rgb = _BaseColor.rgb * fresnel;
                half  a   = saturate(fresnel * _BaseColor.a);
                return half4(rgb, a);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
