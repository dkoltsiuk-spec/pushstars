Shader "PushStars/UI Boss Map Fog"
{
    // Drifting fog over the islands the player has not reached. uv0 carries map coordinates in
    // 390-wide reference units, so the fog scrolls with the islands; _FogLine is the map height
    // where it starts. The biome palette comes per vertex: colour = deep shadow, uv1 = lit haze,
    // uv2 = glow. Everything is procedural: two drifting fbm banks, slanted light and fireflies.
    Properties
    {
        [PerRendererData] _MainTex ("Unused", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _Density ("Density", Range(0,1)) = .9
        _Depth ("Darkening deep in the fog", Range(0,1)) = .45
        _Shafts ("Light shafts", Range(0,1)) = .3
        _Motes ("Fireflies", Range(0,1)) = .8
        _FogLine ("Fog line (map units)", Float) = 0
        _Edge ("Edge width", Float) = 150
        _Ragged ("Edge raggedness", Float) = 170
        _Clear ("Clearing pulse", Range(0,1)) = 0
        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "PreviewType"="Plane" }
        Stencil { Ref [_Stencil] Comp [_StencilComp] Pass [_StencilOp] ReadMask [_StencilReadMask] WriteMask [_StencilWriteMask] }
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
            #pragma target 3.0
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"
            struct appdata { float4 vertex:POSITION; float4 color:COLOR; float2 uv:TEXCOORD0; float4 haze:TEXCOORD1; float4 glow:TEXCOORD2; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f
            {
                float4 vertex:SV_POSITION; fixed4 color:COLOR; float2 map:TEXCOORD0; float4 local:TEXCOORD1;
                fixed3 haze:TEXCOORD2; fixed3 glow:TEXCOORD3; UNITY_VERTEX_OUTPUT_STEREO
            };
            float4 _ClipRect;
            fixed4 _Color;
            float _Density, _Depth, _Shafts, _Motes, _FogLine, _Edge, _Ragged, _Clear;

            float hash(float2 p) { p = frac(p * float2(123.34, 456.21)); p += dot(p, p + 45.32); return frac(p.x * p.y); }
            float noise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                f = f * f * (3 - 2 * f);
                return lerp(lerp(hash(i), hash(i + float2(1, 0)), f.x), lerp(hash(i + float2(0, 1)), hash(i + 1), f.x), f.y);
            }
            float fbm(float2 p)
            {
                float v = 0, a = .5;
                const float2x2 r = float2x2(.8, .6, -.6, .8);
                for (int k = 0; k < 4; k++) { v += a * noise(p); p = mul(r, p) * 2.03 + 17.1; a *= .5; }
                return v;
            }

            v2f vert(appdata v)
            {
                v2f o; UNITY_SETUP_INSTANCE_ID(v); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.local = v.vertex; o.vertex = UnityObjectToClipPos(v.vertex);
                o.map = v.uv; o.color = v.color * _Color;
                o.haze = v.haze.rgb; o.glow = v.glow.rgb;
                return o;
            }

            // Fireflies: one in a few 80-unit cells, wandering inside it, rising slowly and pulsing.
            float motes(float2 p, float t)
            {
                p.y -= t * 6;
                float2 cell = floor(p / 80), f = p / 80 - cell;
                float r = hash(cell + 3.7) / .3;
                if (r > 1) return 0;
                float2 at = .5 + .3 * float2(sin(t * (.35 + r) + r * 40), cos(t * (.28 + r * .7) + r * 23));
                float2 d = (f - at) * 80;
                float dist2 = dot(d, d);
                float pulse = .35 + .65 * pow(.5 + .5 * sin(t * (1.1 + r * 2) + r * 60), 3);
                return (exp(-dist2 / 5) + exp(-dist2 / 60) * .45) * pulse;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float t = _Time.y;
                float2 p = i.map;
                // Two banks drifting against each other give the fog depth while it stands still.
                float far = fbm(p * float2(.0062, .0105) + float2(t * .021, t * .006));
                float nearBank = fbm(p * float2(.0105, .017) + float2(-t * .034, t * .011) + far * 1.1);
                float body = saturate(far * .55 + nearBank * .65);

                // The lower edge is torn by the same noise and breathes slowly, so it never reads as a line.
                float wave = sin(p.x * .021 + t * .35) * 18 + sin(p.x * .047 - t * .22) * 9;
                float d = p.y - _FogLine + wave + (body - .5) * _Ragged;
                float edge = smoothstep(-_Edge * .35, _Edge, d);
                // Clearing: the fog thins from the edge upward and whitens before it lifts.
                edge *= 1 - _Clear * smoothstep(_Edge * 2.6, 0, d);

                float a = edge * _Density * lerp(.74, 1, smoothstep(.25, .75, body));
                // Slanted light falling through gaps in the fog, drifting across very slowly.
                float shaft = smoothstep(.62, 1, sin((p.x * .9 + p.y * .5) * .016 + t * .07 + far * 2.2) * .5 + .5)
                    * smoothstep(0, _Edge * 1.5, d) * _Shafts;
                float lit = smoothstep(.42, .9, nearBank) * .8 + shaft * .45 + _Clear * .35;
                fixed3 rgb = lerp(i.color.rgb, i.haze, saturate(lit));
                // The further in, the darker: what lies beyond is still unknown.
                rgb *= 1 - _Depth * smoothstep(_Edge * .8, _Edge * 4.5, d) * (1 - shaft);
                // Wisps right at the edge catch the island's light, like fog curling off it.
                float rim = smoothstep(.05, .45, edge) * (1 - smoothstep(.45, .95, edge));
                rgb = lerp(rgb, lerp(i.haze, i.glow, .55), rim * (.16 + nearBank * .2) + _Clear * .25);
                // Fireflies live in the fog, thickest along the edge where the next island waits.
                float m = motes(p, t) * _Motes * smoothstep(.25, .6, edge) * lerp(1, .45, smoothstep(_Edge, _Edge * 5, d));
                rgb = lerp(rgb, i.glow, saturate(m));
                a = max(a, saturate(m) * edge);
                fixed4 c = fixed4(rgb, saturate(a) * i.color.a);
                #ifdef UNITY_UI_CLIP_RECT
                c.a *= UnityGet2DClipping(i.local.xy, _ClipRect);
                #endif
                return c;
            }
            ENDCG
        }
    }
}
