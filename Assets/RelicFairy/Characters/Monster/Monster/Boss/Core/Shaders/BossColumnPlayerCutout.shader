Shader "Abyss/Boss/ColumnPlayerCutout"
{
    Properties
    {
        _BaseColor("Base Color", Color) = (0.5, 0.8, 1.0, 0.55)
        _BaseOpacity("Base Opacity", Range(0, 1)) = 0.55
        _PlayerWorldPos("Player World Pos", Vector) = (0, 0, 0, 0)
        _PlayerHeight("Player Height", Float) = 1.8
        _CutoutRadius("Cutout Radius", Float) = 1.8
        _CutoutSoftness("Cutout Softness", Float) = 0.2
        _CutoutEnabled("Cutout Enabled", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
        }

        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Back

        Pass
        {
            Name "Forward"

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 worldPos : TEXCOORD0;
            };

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                float4 _PlayerWorldPos;
                float _BaseOpacity;
                float _PlayerHeight;
                float _CutoutRadius;
                float _CutoutSoftness;
                float _CutoutEnabled;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionHCS = positionInputs.positionCS;
                output.worldPos = positionInputs.positionWS;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float3 playerFoot = _PlayerWorldPos.xyz;
                float3 playerHead = playerFoot + float3(0.0, _PlayerHeight, 0.0);
                float3 axis = playerHead - playerFoot;
                float axisLengthSq = max(dot(axis, axis), 0.0001);

                float projection = saturate(dot(input.worldPos - playerFoot, axis) / axisLengthSq);
                float3 closestPoint = playerFoot + axis * projection;
                float distanceToCapsule = distance(input.worldPos, closestPoint);

                float cutout = saturate(_CutoutEnabled)
                    * (1.0 - smoothstep(_CutoutRadius - _CutoutSoftness, _CutoutRadius + _CutoutSoftness, distanceToCapsule));

                half4 color = _BaseColor;
                color.a = _BaseOpacity * (1.0 - cutout);

                clip(color.a - 0.01);
                return color;
            }
            ENDHLSL
        }
    }
}
