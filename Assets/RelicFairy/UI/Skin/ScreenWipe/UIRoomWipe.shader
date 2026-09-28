Shader "UI/RoomWipe"
{
    // 방 전환 와이프 — 단색 판 대신 잉크처럼 찢긴 가장자리 · 심연 구름 몸통 · 가운데 룬 마법진.
    // 진행(_Progress 0→1)만 C#이 올리고 나머지는 스킨(ScreenWipeSkinSO) 값. 화면 전체 Image 하나에 입힌다.
    // 덮기(_Reveal=0): 가장자리가 _Dir 방향으로 들어와 덮는다 / 걷기(_Reveal=1): 뒷가장자리가 _Dir 방향으로 빠진다.
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Noise        ("Ink Noise (repeat)", 2D) = "gray" {}
        _Sigil        ("Sigil", 2D) = "black" {}

        _Abyss        ("Abyss", Color) = (0.025, 0.022, 0.045, 1)
        _Tint         ("Room Tint", Color) = (0.05, 0.06, 0.10, 1)
        _Rim          ("Rim Color", Color) = (0.62, 0.70, 1, 1)

        _Progress     ("Progress", Range(0, 1)) = 0
        _Fade         ("Fade (alpha)", Range(0, 1)) = 1
        _Reveal       ("Reveal (0 cover / 1 reveal)", Float) = 0
        _Dir          ("Travel Dir (xy)", Vector) = (1, 0, 0, 0)
        _Aspect       ("Screen Aspect", Float) = 1.7778

        _Ragged       ("Edge Ragged", Range(0, 0.4)) = 0.12
        _NoiseScale   ("Edge Noise Scale", Range(0.2, 3)) = 0.7
        _RimWidth     ("Rim Width", Range(0, 0.1)) = 0.022
        _RimIntensity ("Rim Intensity", Range(0, 3)) = 1.1
        _TintMin      ("Tint Min", Range(0, 1)) = 0.12
        _TintMax      ("Tint Max", Range(0, 1)) = 0.62
        _Drift        ("Cloud Drift", Range(0, 0.2)) = 0.04
        _SigilScale   ("Sigil Scale (screen height)", Range(0.1, 1.5)) = 0.62
        _SigilAlpha   ("Sigil Alpha", Range(0, 1)) = 0.3
        _SigilSpin    ("Sigil Spin (rad/s)", Float) = 0.5

        _StencilComp  ("Stencil Comparison", Float) = 8
        _Stencil      ("Stencil ID", Float) = 0
        _StencilOp    ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask  ("Stencil Read Mask", Float) = 255
        _ColorMask    ("Color Mask", Float) = 15
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "IgnoreProjector"="True"
            "RenderType"="Transparent"
            "PreviewType"="Plane"
            "CanUseSpriteAtlas"="True"
        }

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0

            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float4 color  : COLOR;
                float2 uv     : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                fixed4 color  : COLOR;
                float2 uv     : TEXCOORD0;
                float4 worldPosition : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _Noise;
            sampler2D _Sigil;
            fixed4 _Abyss, _Tint, _Rim;
            float  _Progress, _Reveal, _Aspect, _Fade;
            float4 _Dir;
            float  _Ragged, _NoiseScale, _RimWidth, _RimIntensity, _TintMin, _TintMax, _Drift;
            float  _SigilScale, _SigilAlpha, _SigilSpin;
            float4 _ClipRect;

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.worldPosition = v.vertex;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.color = v.color;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float2 uv  = i.uv;
                float2 auv = float2((uv.x - 0.5) * _Aspect, uv.y - 0.5);   // 화면 비율을 편 좌표(세로 1 기준)

                // 진행 방향 좌표 — 0 = 판이 들어오는 쪽, 1 = 반대쪽 (방향은 상하좌우 넷)
                float t = dot(uv - 0.5, _Dir.xy) + 0.5;

                // 잉크 가장자리 — 큰 결의 노이즈로 경계를 흔든다(작은 결이면 판 안에 구멍이 뚫린다)
                float n  = tex2D(_Noise, auv * _NoiseScale * 0.5 + 0.5).r;
                float n2 = tex2D(_Noise, auv * 0.55 + float2(0.37, 0.11) + _Time.y * _Drift * float2(0.6, 0.3)).r;
                float te = t + (n - 0.5) * _Ragged;
                float p  = -_Ragged + _Progress * (1.0 + 2.0 * _Ragged);
                float dist = lerp(p - te, te - p, _Reveal);   // > 0 이면 덮인 곳, 값 = 가장자리에서 안쪽까지 거리

                float cover = smoothstep(0.0, 0.006, dist);
                float rim   = (1.0 - smoothstep(0.0, _RimWidth, dist)) * step(0.0, dist);

                // 몸통 — 심연색 위에 방 종류색 구름(흐르며 옅게)
                float3 body = lerp(_Abyss.rgb, _Tint.rgb, _TintMin + (_TintMax - _TintMin) * n2 * n2);

                // 가운데 룬 마법진 — 천천히 돈다
                float a = _Time.y * _SigilSpin;
                float2 s = auv / _SigilScale;
                float2 r = float2(s.x * cos(a) - s.y * sin(a), s.x * sin(a) + s.y * cos(a)) + 0.5;
                float inside = step(0.0, r.x) * step(r.x, 1.0) * step(0.0, r.y) * step(r.y, 1.0);
                float sg = tex2D(_Sigil, r).r * inside;

                float3 col = body + _Rim.rgb * (sg * _SigilAlpha + rim * _RimIntensity);
                fixed4 o = fixed4(col, cover * _Fade * i.color.a);   // _Fade: 덮을 때 서서히 짙어지고 걷을 때 옅어진다

                #ifdef UNITY_UI_CLIP_RECT
                o.a *= UnityGet2DClipping(i.worldPosition.xy, _ClipRect);
                #endif
                return o;
            }
            ENDCG
        }
    }
}
