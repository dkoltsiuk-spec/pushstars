Shader "PushStars/UI Aura Vortex"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite", 2D) = "white" {}
        _Flow ("Time, power, release", Vector) = (0,0,0,0)
        _Viewport ("Aspect", Vector) = (1,2.164,0,0)
        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "PreviewType"="Plane" }
        Stencil { Ref [_Stencil] Comp [_StencilComp] Pass [_StencilOp] ReadMask [_StencilReadMask] WriteMask [_StencilWriteMask] }
        Cull Off Lighting Off ZWrite Off ZTest [unity_GUIZTestMode]
        Blend One One
        ColorMask [_ColorMask]
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"
            struct appdata { float4 vertex:POSITION; fixed4 color:COLOR; float2 uv:TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 vertex:SV_POSITION; fixed4 color:COLOR; float2 uv:TEXCOORD0; float4 local:TEXCOORD1; UNITY_VERTEX_OUTPUT_STEREO };
            float4 _Flow, _Viewport, _ClipRect;
            v2f vert(appdata v)
            {
                v2f o; UNITY_SETUP_INSTANCE_ID(v); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.local = v.vertex; o.vertex = UnityObjectToClipPos(v.vertex); o.uv = v.uv; o.color = v.color;
                return o;
            }
            float hash(float2 p)
            {
                float3 q = frac(float3(p.xyx) * .1031);
                q += dot(q, q.yzx + 33.33);
                return frac((q.x + q.y) * q.z);
            }
            float noise(float2 p)
            {
                float2 c = floor(p), f = frac(p); f = f * f * (3 - 2 * f);
                return lerp(lerp(hash(c), hash(c + float2(1,0)), f.x),
                    lerp(hash(c + float2(0,1)), hash(c + 1), f.x), f.y);
            }
            float fbm(float2 p)
            {
                float v = .57 * noise(p);
                p = float2(p.x * 1.6 - p.y * 1.2, p.x * 1.2 + p.y * 1.6);
                v += .28 * noise(p + 8.3);
                return v + .15 * noise(p * 2.03 + 17.1);
            }
            float2 rotate(float2 p, float a)
            {
                float s, c; sincos(a, s, c);
                return float2(c * p.x - s * p.y, s * p.x + c * p.y);
            }
            float4 frag(v2f i):SV_Target
            {
                float power = saturate(_Flow.y);
                float t = _Flow.x, release = saturate(_Flow.z);
                float impact = sin(release * 3.14159265);
                float2 p = (i.uv - float2(.5, .52)) * _Viewport.xy * 2;
                // Large flame arms shear at different radii, with advected fine tongues.
                float r = max(length(p), .008), a = atan2(p.y, p.x);
                float phase = a * 3 + log(r + .12) * 6.8 - t * 1.05;
                // Stretch noise along the flow: smooth tapered fire rather than round smoke.
                float2 flowUv = float2(r * 7 - t * .8, sin(a * 3 + log(r + .15) * 3) * 1.8 + cos(a * 2) * .3);
                float n = fbm(flowUv);
                float fine = fbm(flowUv * float2(2.8, .65) + float2(-t * 1.2, 0));
                float curl = phase + (n - .5) * 1.2 + sin(a * 5 - r * 9 + t) * .14;
                float body = pow(saturate(.5 + .5 * sin(curl)), 11);
                float tongues = pow(saturate(.5 + .5 * sin(curl + fine * 1.2)), 42);
                float strands = pow(saturate(.5 + .5 * sin(phase * 4 + n * 2 + fine * .7)), 38);
                float envelope = exp(-r * 1.0) * smoothstep(.10, .30, r);
                float torn = smoothstep(.28, .78, fine + n * .25);
                float fire = body * (.24 + torn * .95) * envelope;
                float hot = tongues * torn * envelope;
                float threads = strands * (.08 + .92 * body) * envelope;
                float3 violet = float3(.17, .015, .70), pink = float3(.86, .12, .68);
                float3 light = violet * fire * .75 + pink * hot * 1.5;
                light += float3(.80, .48, 1) * threads * .65;
                light += float3(.12, .015, .40) * body * exp(-r * .65) * .18;
                float tips = pow(saturate(.5 + .5 * sin(a * 11 - r * 17 + t * 1.4 + n)), 8);
                light += float3(1, .64, .96) * hot * tips * 2.6;
                // White-lilac fire around the eye relaxes behind the revealed reward.
                float eyeRadius = .20 + release * .16;
                float rim = abs(r - eyeRadius - (n - .5) * .055);
                float corona = exp(-rim * 21) * (.5 + .5 * body);
                float core = exp(-r * r * 29) * (1 - smoothstep(.10, .75, release));
                light += float3(.56, .12, .88) * corona * (.5 + impact * 1.1);
                light += float3(1, .74, 1) * exp(-rim * 95) * (.35 + impact * 1.0);
                light += float3(.95, .57, 1) * core * (1.3 + impact);
                // Embers rotate with the flow, with sparse four-point glints.
                float2 sp = rotate(p, t * .12) * 17;
                float2 cell = floor(sp), local = frac(sp) - .5;
                float seed = hash(cell);
                local -= float2(hash(cell + 7), hash(cell + 19)) * .46 - .23;
                float spark = exp(-dot(local, local) * 450);
                float cross = exp(-abs(local.x) * 150 - abs(local.y) * 18) + exp(-abs(local.y) * 150 - abs(local.x) * 18);
                float twinkle = .45 + .55 * pow(.5 + .5 * sin(t * 2.1 + seed * 50), 3);
                light += float3(.72, .29, 1) * (spark + cross * .38) * step(.96, seed) * twinkle * smoothstep(.24,.4,r);
                // Purple flame wash at the physical border, without straight wire outlines.
                float edge = 1 - min(min(i.uv.x, 1 - i.uv.x), min(i.uv.y, 1 - i.uv.y)) * 6;
                light += violet * pow(saturate(edge), 3) * (.025 + .06 * n + .08 * impact);
                light *= power * (1 + impact * .30);
                light = 1 - exp(-light);
                light *= i.color.rgb * i.color.a;
                #ifdef UNITY_UI_CLIP_RECT
                light *= UnityGet2DClipping(i.local.xy, _ClipRect);
                #endif
                #ifdef UNITY_UI_ALPHACLIP
                clip(power - .001);
                #endif
                return float4(light, 0);
            }
            ENDCG
        }
    }
}
