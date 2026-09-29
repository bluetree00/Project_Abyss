Shader "RelicFairy/CosmicVoidSkybox"
{
    Properties
    {
        _TopColor      ("Top Color", Color)        = (0.03, 0.03, 0.09, 1)
        _HorizonColor  ("Horizon Color", Color)    = (0.07, 0.04, 0.14, 1)
        _BottomColor   ("Bottom Color", Color)     = (0.0, 0.0, 0.02, 1)
        _NebulaColorA  ("Nebula Color A", Color)   = (0.28, 0.10, 0.45, 1)
        _NebulaColorB  ("Nebula Color B", Color)   = (0.05, 0.20, 0.35, 1)
        _NebulaIntensity ("Nebula Intensity", Range(0,3)) = 0.7
        _NebulaScale   ("Nebula Scale", Float)     = 2.5
        _StarDensity   ("Star Density", Range(20,600)) = 220
        _StarThreshold ("Star Threshold", Range(0.9,0.999)) = 0.982
        _StarBrightness("Star Brightness", Float)  = 3.0
        _TwinkleSpeed  ("Twinkle Speed", Float)    = 1.5
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

            fixed4 _TopColor, _HorizonColor, _BottomColor, _NebulaColorA, _NebulaColorB;
            float _NebulaIntensity, _NebulaScale, _StarDensity, _StarThreshold, _StarBrightness, _TwinkleSpeed;

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
                float3 i = floor(p);
                float3 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float n000 = hash13(i + float3(0,0,0));
                float n100 = hash13(i + float3(1,0,0));
                float n010 = hash13(i + float3(0,1,0));
                float n110 = hash13(i + float3(1,1,0));
                float n001 = hash13(i + float3(0,0,1));
                float n101 = hash13(i + float3(1,0,1));
                float n011 = hash13(i + float3(0,1,1));
                float n111 = hash13(i + float3(1,1,1));
                return lerp(lerp(lerp(n000,n100,f.x), lerp(n010,n110,f.x), f.y),
                            lerp(lerp(n001,n101,f.x), lerp(n011,n111,f.x), f.y), f.z);
            }

            float fbm (float3 p)
            {
                float v = 0.0;
                float a = 0.5;
                for (int k = 0; k < 4; k++)
                {
                    v += a * noise3(p);
                    p *= 2.0;
                    a *= 0.5;
                }
                return v;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float3 d = normalize(i.dir);
                float up = saturate(d.y * 0.5 + 0.5);

                fixed3 col = lerp(_BottomColor.rgb, _HorizonColor.rgb, smoothstep(0.0, 0.5, up));
                col = lerp(col, _TopColor.rgb, smoothstep(0.5, 1.0, up));

                float n1 = fbm(d * _NebulaScale);
                float n2 = fbm(d * _NebulaScale * 1.7 + 11.3);
                float neb = pow(saturate(n1 * 1.2), 3.0);
                fixed3 nebCol = lerp(_NebulaColorA.rgb, _NebulaColorB.rgb, saturate(n2));
                col += nebCol * neb * _NebulaIntensity;

                float3 sp = d * _StarDensity;
                float3 cell = floor(sp);
                float rnd = hash13(cell);
                if (rnd > _StarThreshold)
                {
                    float3 fpos = frac(sp);
                    float3 starPos = float3(hash13(cell + 1.7), hash13(cell + 3.3), hash13(cell + 5.9));
                    float dist = length(fpos - starPos);
                    float s = smoothstep(0.10, 0.0, dist);
                    float tw = 0.6 + 0.4 * sin(_Time.y * _TwinkleSpeed + rnd * 100.0);
                    col += s * _StarBrightness * tw;
                }

                return fixed4(col, 1.0);
            }
            ENDCG
        }
    }
    Fallback Off
}
