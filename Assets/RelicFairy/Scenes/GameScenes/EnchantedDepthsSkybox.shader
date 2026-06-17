Shader "RelicFairy/EnchantedDepthsSkybox"
{
    Properties
    {
        _TopColor     ("Top (Forest Canopy)", Color) = (0.09, 0.21, 0.16, 1)
        _HorizonColor ("Horizon (Mist Glow)", Color)  = (0.32, 0.44, 0.38, 1)
        _BottomColor  ("Bottom (Abyss)", Color)        = (0.015, 0.035, 0.045, 1)
        _HorizonSharp ("Horizon Sharpness", Range(1,12)) = 4.0
        _MistColor    ("Mist Color", Color)            = (0.40, 0.52, 0.46, 1)
        _MistIntensity("Mist Intensity", Range(0,2))   = 0.55
        _MistScale    ("Mist Scale", Float)            = 2.2
        _MistSpeed    ("Mist Drift Speed", Float)      = 0.015
    }
    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" }
        Cull Off ZWrite Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata { float4 vertex : POSITION; };
            struct v2f { float4 pos : SV_POSITION; float3 dir : TEXCOORD0; };

            fixed4 _TopColor, _HorizonColor, _BottomColor, _MistColor;
            float _HorizonSharp, _MistIntensity, _MistScale, _MistSpeed;

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.dir = v.vertex.xyz;
                return o;
            }

            float hash13 (float3 p)
            {
                p = frac(p * 0.1031);
                p += dot(p, p.yzx + 33.33);
                return frac((p.x + p.y) * p.z);
            }
            float noise3 (float3 p)
            {
                float3 i = floor(p); float3 f = frac(p); f = f * f * (3.0 - 2.0 * f);
                float n000 = hash13(i+float3(0,0,0)); float n100 = hash13(i+float3(1,0,0));
                float n010 = hash13(i+float3(0,1,0)); float n110 = hash13(i+float3(1,1,0));
                float n001 = hash13(i+float3(0,0,1)); float n101 = hash13(i+float3(1,0,1));
                float n011 = hash13(i+float3(0,1,1)); float n111 = hash13(i+float3(1,1,1));
                return lerp(lerp(lerp(n000,n100,f.x), lerp(n010,n110,f.x), f.y),
                            lerp(lerp(n001,n101,f.x), lerp(n011,n111,f.x), f.y), f.z);
            }
            float fbm (float3 p)
            {
                float v = 0.0; float a = 0.5;
                for (int k = 0; k < 4; k++) { v += a * noise3(p); p *= 2.0; a *= 0.5; }
                return v;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float3 d = normalize(i.dir);
                float h = d.y; // -1 down .. 1 up

                // vertical gradient: abyss(bottom) -> horizon mist -> forest(top)
                float upT  = saturate(pow(saturate(h), 1.0 / _HorizonSharp));
                float dnT  = saturate(pow(saturate(-h), 1.0 / _HorizonSharp));
                fixed3 col = _HorizonColor.rgb;
                col = lerp(col, _TopColor.rgb, upT);
                col = lerp(col, _BottomColor.rgb, dnT);

                // drifting mist banded near horizon
                float band = 1.0 - saturate(abs(h) * 2.2);
                float3 mp = d * _MistScale + float3(_Time.y * _MistSpeed, 0.0, _Time.y * _MistSpeed * 0.7);
                float m = fbm(mp);
                m = pow(saturate(m), 2.0) * band;
                col += _MistColor.rgb * m * _MistIntensity;

                return fixed4(col, 1.0);
            }
            ENDCG
        }
    }
    Fallback Off
}
