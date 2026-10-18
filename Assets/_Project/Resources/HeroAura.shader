// The aura drawn behind the player's hero (HeroAura.cs): compact cartoon fire around the whole
// outline, in the flat, angular style of stylized game VFX. Short faceted tongues with hard bands
// (contrasting edge kerb → deep colour → bright → hot core) leave the body
// along its outward direction, leaning a little upward, and lick outward continuously; a soft
// glow and rim light sit under them, embers drift off on the top tiers.
//
// _MainTex is a small, mip-mapped copy of the character render texture; only its alpha (the
// silhouette) is used. HeroAuraField.shader bakes it once per frame into _FieldTex: a soft
// distance to the body over the whole aura quad (_FieldRect, silhouette UVs). Its gradient gives
// the outward direction the flames flow along. The quad is larger than the portrait, so UVs run
// past 0..1 — those samples count as empty. The hero is drawn on top and hides the inside.
//
// Blend One OneMinusSrcAlpha: rgb adds light, alpha slightly darkens what is behind.
Shader "PushStars/UI Hero Aura"
{
    Properties
    {
        [PerRendererData] _MainTex ("Silhouette", 2D) = "black" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _Inner ("Hot Color", Color) = (1,0.95,0.6,1)
        _Outer ("Flame Color", Color) = (1,0.35,0,1)
        _Edge ("Edge Color", Color) = (1,1,0.8,1)
        _Mid ("Glow Middle Color", Color) = (1,0.65,0.3,1)
        _SparkColor ("Ember Color", Color) = (1,0.9,0.5,1)
        _Glow ("Glow", Range(0,2)) = 0.8
        _Radius ("Rim Radius (uv height)", Range(0.002,0.06)) = 0.012
        _Flame ("Flame", Range(0,1)) = 1
        _Reach ("Flame Reach (uv height)", Range(0.005,0.2)) = 0.05
        _FlameSpeed ("Flame Speed", Range(0,3)) = 1.2
        _Rise ("Upward Lean", Range(0,2)) = 0.6
        _Sparks ("Embers", Range(0,2)) = 0
        _Darken ("Darken Behind", Range(0,1)) = 0.15
        _Pulse ("Pulse", Range(0,1)) = 0.12
        [NoScaleOffset] _FieldTex ("Distance (HeroAuraField)", 2D) = "black" {}
        _FieldRect ("Field Rect (silhouette uv)", Vector) = (0,0,1,1)

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
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent"
            "PreviewType"="Plane" "CanUseSpriteAtlas"="True" }
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
        Blend One OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP
            #include "HeroAura.cginc"

            v2f vert(appdata_t v)
            {
                v2f OUT;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.worldPosition = v.vertex;
                OUT.vertex = UnityObjectToClipPos(v.vertex);
                OUT.texcoord = v.texcoord;
                OUT.color = v.color * _Color;
                return OUT;
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                float2 uv = IN.texcoord;
                float t = _Time.y;
                float pulse = 1 + _Pulse * sin(t * 2.4);

                half rim = halo(uv, _Radius);
                half d = fieldAt(uv);

                // Outward direction: down the distance gradient, measured over equal on-screen
                // steps, then leaned upward because fire rises.
                float2 e = float2(_Reach * 0.25 / _Aspect, _Reach * 0.25);
                float2 g = float2(fieldAt(uv + float2(e.x, 0)) - fieldAt(uv - float2(e.x, 0)),
                                  fieldAt(uv + float2(0, e.y)) - fieldAt(uv - float2(0, e.y)));
                float len = length(g);
                float2 out_ = len > 1e-4 ? -g / len : float2(0, 1);
                float2 dir = normalize(out_ + float2(0, _Rise));

                // Flow-mapped noise: two copies scroll outward along dir half a cycle apart and
                // cross-fade, so the tongues keep moving away from the body without the pattern
                // ever shearing where the direction turns.
                float2 p = float2(uv.x * _Aspect, uv.y) * _Zoom * 13;
                float phase = t * _FlameSpeed;
                float ph0 = frac(phase), ph1 = frac(phase + 0.5);
                float n0 = facetFbm(p - dir * ph0 * 2.5);
                float n1 = facetFbm(p - dir * ph1 * 2.5 + 17.3);
                float nz = lerp(n1, n0, abs(1 - 2 * ph0));

                // Toon bands: tongues only where the noise beats the falling distance, so they
                // thin to points away from the body.
                half heat = d * 1.25 + (nz - 0.5) * 1.05 * saturate(d * 5); // no tongues detached far from the body
                half flame = smoothstep(0.27, 0.31, heat) * saturate(_Flame);
                half mid = smoothstep(0.42, 0.46, heat);
                half hot = smoothstep(0.57, 0.61, heat);
                half3 bright = lerp(_Outer.rgb, _Inner.rgb, 0.5);
                half3 fire = lerp(lerp(_Outer.rgb, bright, mid), _Inner.rgb, hot);
                // A thin bright kerb of a contrasting colour along the tongues' outer edge.
                half kerb = 1 - smoothstep(0.31, 0.345, heat);
                fire = lerp(fire, _Edge.rgb, kerb);

                // Soft glow and rim under the flames.
                half glow = smoothstep(0.03, 0.5, d) * _Glow * pulse;
                // Three-stop glow: outer colour far out → middle → hot colour against the body.
                half stop = saturate(d * 1.4);
                half3 glowCol = stop < 0.5 ? lerp(_Outer.rgb, _Mid.rgb, stop * 2) : lerp(_Mid.rgb, _Inner.rgb, stop * 2 - 1);
                half3 glowRgb = glowCol * glow * 0.45;
                half3 rimRgb = _Inner.rgb * saturate(rim * 1.8) * _Glow * 0.5;

                // Embers: cells drifting up, a few lit, only in the band just off the body.
                float2 sp = float2(uv.x * _Aspect, uv.y) * 30 * _Zoom;
                sp.y -= t * 2.6;
                float2 cell = floor(sp);
                float2 pos = float2(hash(cell + 1.3), hash(cell + 7.1)) * 0.6 + 0.2;
                half lit = step(0.8, hash(cell));
                half twinkle = 0.5 + 0.5 * sin(t * 9 + hash(cell + 4.2) * 6.28);
                half ember = smoothstep(0.15, 0.0, length(frac(sp) - pos)) * lit * twinkle;
                ember *= smoothstep(0.02, 0.15, d) * (1 - smoothstep(0.3, 0.45, d)) * _Sparks;

                half3 rgb = fire * flame + (glowRgb + rimRgb) * (1 - flame) + _SparkColor.rgb * ember * 1.3;
                half a = saturate(flame * 0.92 + (glow * _Darken + saturate(rim * 1.8) * 0.2) * (1 - flame) + ember * 0.5);

                half fade = IN.color.a;
                #ifdef UNITY_UI_CLIP_RECT
                fade *= UnityGet2DClipping(IN.worldPosition.xy, _ClipRect);
                #endif
                #ifdef UNITY_UI_ALPHACLIP
                clip(a * fade + dot(rgb, 1) - 0.001);
                #endif
                return fixed4(rgb * IN.color.rgb * fade, a * fade);
            }
            ENDCG
        }
    }
}
