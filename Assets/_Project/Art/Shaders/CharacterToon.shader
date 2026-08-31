// Stylised character shader — flat cartoon shading plus a drawn black outline, the Brawl Stars
// read: the silhouette does the work, the interior stays bright and legible on a phone.
//
// The outline is an inverted hull: the mesh is drawn a second time, expanded along its normals,
// with the front faces culled so only the sliver poking out past the real silhouette survives.
// It costs one extra draw of the same skinned mesh and needs no post-processing, which matters
// here — the character renders into a small transparent RenderTexture, where screen-space edge
// detection has nothing to work with.
//
// ONLY THE OUTER CONTOUR IS DRAWN. A bare inverted hull also inks every internal boundary where
// one shell sits in front of another — around the shorts, the sleeve, the hair — because each
// expanded shell pokes out over whatever lies behind it. So the body pass stamps the stencil
// buffer wherever the character covers the screen, and the outline pass is masked to everywhere
// he does not: the line survives against the background and nowhere else. Pass order is what
// carries that, which is why the body pass is declared first.
//
// That mask assumes ONE character per camera, which is what both stages draw today. Two of them
// sharing a camera would share the stencil too, and the one drawn second would lose its outline
// wherever it overlaps the first. Give each its own material with a different stencil Ref if that
// day comes — the depth test already sorts out which one is in front.
//
// Built for the BUILT-IN pipeline, which is what this project actually renders with (no URP asset
// is assigned in Graphics settings). MainCharacterSetup.CharacterShader() only hands this shader
// out while that stays true, and falls back to a plain lit shader otherwise.
Shader "Push Stars/Character Toon"
{
    Properties
    {
        [MainTexture] _BaseMap   ("Albedo", 2D)    = "white" {}
        [MainColor]   _BaseColor ("Tint", Color)   = (1, 1, 1, 1)

        [Header(Shading)]
        [Space(4)]
        _ShadeColor     ("Shade tint", Color)              = (0.42, 0.46, 0.62, 1)
        _ShadeThreshold ("Shade threshold", Range(-1, 1))  = 0.35
        // Narrow: the near-hard edge between lit and shaded is what reads as cartoon shading
        // rather than as a lighting gradient. Widen it and the character goes soft and realistic.
        _ShadeSoftness  ("Shade softness", Range(0.001, 1))= 0.12
        _ShadeStrength  ("Shade strength", Range(0, 1))    = 0.85
        _LightInfluence ("Scene light influence", Range(0, 1)) = 0.35

        [Header(Rim)]
        [Space(4)]
        _RimColor    ("Rim colour", Color)          = (1, 1, 1, 1)
        _RimPower    ("Rim falloff", Range(0.5, 12))= 4
        _RimStrength ("Rim strength", Range(0, 1))  = 0.15

        [Header(Outline)]
        [Space(4)]
        _OutlineColor ("Outline colour", Color)             = (0, 0, 0, 1)
        // In the model's own units: the hull has to grow in three dimensions, not just across the
        // screen, and that ties the width to the model's scale. The line therefore keeps its weight
        // relative to the character however large he is drawn — which is what you want when the CV
        // mirror resizes him to match the player. Roughly 0.5% of the model's height reads as an
        // ink line; past ~1% the hair shell starts swelling across the face.
        _OutlineWidth ("Outline width (model units)", Range(0, 0.03)) = 0.009
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" }

        // ─────────────────────────────────────────────────────────────────────────
        //  BODY — flat albedo with one soft shade band.
        //  Declared first on purpose: it stamps the stencil the outline is masked against.
        // ─────────────────────────────────────────────────────────────────────────
        Pass
        {
            Name "BODY"
            Tags { "LightMode" = "ForwardBase" }

            Cull Back
            ZWrite On

            // Mark every pixel the character covers.
            Stencil
            {
                Ref 1
                Comp Always
                Pass Replace
            }

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            #include "Lighting.cginc"

            sampler2D _BaseMap;
            float4    _BaseMap_ST;
            fixed4    _BaseColor;
            fixed4    _ShadeColor;
            fixed4    _RimColor;
            half      _ShadeThreshold;
            half      _ShadeSoftness;
            half      _ShadeStrength;
            half      _LightInfluence;
            half      _RimPower;
            half      _RimStrength;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float2 uv     : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos         : SV_POSITION;
                float2 uv          : TEXCOORD0;
                float3 worldNormal : TEXCOORD1;
                float3 worldPos    : TEXCOORD2;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos         = UnityObjectToClipPos(v.vertex);
                o.uv          = TRANSFORM_TEX(v.uv, _BaseMap);
                o.worldNormal = UnityObjectToWorldNormal(v.normal);
                o.worldPos    = mul(unity_ObjectToWorld, v.vertex).xyz;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 albedo = tex2D(_BaseMap, i.uv) * _BaseColor;
                float3 n = normalize(i.worldNormal);

                // A scene with no directional light hands over a zero vector; light the character
                // from above rather than returning NaN across the whole silhouette.
                float3 l = _WorldSpaceLightPos0.xyz;
                l = dot(l, l) > 1e-6 ? normalize(l) : float3(0, 1, 0);

                // One band instead of a gradient — that hard-ish edge is the whole cartoon look.
                half ramp = smoothstep(_ShadeThreshold - _ShadeSoftness,
                                       _ShadeThreshold + _ShadeSoftness,
                                       dot(n, l));

                half3 col = lerp(albedo.rgb * _ShadeColor.rgb, albedo.rgb, ramp);
                col = lerp(albedo.rgb, col, _ShadeStrength);

                // Scene lights tint the character, they don't decide him: this same body appears on
                // the menu stage and over the camera feed, and it has to read the same in both.
                col *= lerp(half3(1, 1, 1), _LightColor0.rgb, _LightInfluence);

                float3 viewDir = normalize(_WorldSpaceCameraPos - i.worldPos);
                half rim = pow(saturate(1 - saturate(dot(n, viewDir))), _RimPower);
                col += _RimColor.rgb * rim * _RimStrength;

                return fixed4(col, albedo.a);
            }
            ENDCG
        }
        // ─────────────────────────────────────────────────────────────────────────
        //  OUTLINE — the expanded hull, seen from inside, kept off the body itself
        // ─────────────────────────────────────────────────────────────────────────
        Pass
        {
            Name "OUTLINE"
            Tags { "LightMode" = "Always" }

            Cull Front
            ZWrite On
            ZTest LEqual

            // Draw only where the body did not: the contour against the background, nothing else.
            Stencil
            {
                Ref 1
                Comp NotEqual
            }

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            float  _OutlineWidth;
            fixed4 _OutlineColor;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
            };

            v2f vert(appdata v)
            {
                v2f o;

                // The hull grows along the normal in three dimensions, depth included. That is what
                // makes it safe: only back faces are drawn, their normals point away from the
                // camera, so expanding along them pushes every one of them DEEPER, and the body's
                // front faces win the depth test everywhere except past the silhouette. Displacing
                // the hull only across the screen — which would buy a constant pixel width — loses
                // that guarantee: in a concavity like the crease between the pecs the hull lands
                // over a part of the body that is further away, and the black bleeds through as
                // scratches across the chest.
                //
                // Skinning has already run by the time this sees the vertex, so the hull follows
                // the character through every pose.
                v.vertex.xyz += normalize(v.normal) * _OutlineWidth;
                o.pos = UnityObjectToClipPos(v.vertex);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                return _OutlineColor;
            }
            ENDCG
        }

    }

    // No fallback on purpose. The character stands alone on a transparent stage with nothing to
    // cast a shadow onto, and inheriting Diffuse's ForwardAdd pass would let a second scene light
    // paint realistic lighting straight over the flat look this shader exists to produce.
    Fallback Off
}
