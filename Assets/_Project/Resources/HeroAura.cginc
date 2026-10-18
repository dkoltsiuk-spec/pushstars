// Shared by HeroAura.shader (the on-screen UI pass) and HeroAuraField.shader (the soft-distance
// bake). Kept apart because UGUI draws every pass of a UI material, so the bake cannot be a
// second pass of the UI shader.
#ifndef PUSHSTARS_HERO_AURA_INCLUDED
#define PUSHSTARS_HERO_AURA_INCLUDED

#include "UnityCG.cginc"
#include "UnityUI.cginc"

struct appdata_t
{
    float4 vertex : POSITION;
    float4 color : COLOR;
    float2 texcoord : TEXCOORD0;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct v2f
{
    float4 vertex : SV_POSITION;
    fixed4 color : COLOR;
    float2 texcoord : TEXCOORD0;
    float4 worldPosition : TEXCOORD1;
    UNITY_VERTEX_OUTPUT_STEREO
};

sampler2D _MainTex, _FieldTex;
float4 _FieldRect;
fixed4 _Color, _Inner, _Mid, _Outer, _Edge, _SparkColor;
half _Glow, _Radius, _Flame, _Reach, _FlameSpeed, _Rise, _Sparks, _Darken, _Pulse;
float4 _ClipRect;
// On-screen width / height of one UV unit (set by HeroAura.cs), so a circle in UV space
// stays a circle on screen.
float _Aspect;
// 1 / visible UV height: keeps flame and spark size tied to the hero, not the texture.
float _Zoom;

float hash(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }

float noise(float2 p)
{
    float2 i = floor(p), f = frac(p);
    f = f * f * (3 - 2 * f);
    return lerp(lerp(hash(i), hash(i + float2(1, 0)), f.x),
                lerp(hash(i + float2(0, 1)), hash(i + float2(1, 1)), f.x), f.y);
}

float fbm(float2 p) { return noise(p) * 0.65 + noise(p * 2.1 + 3.1) * 0.35; }

// Value noise with straight (not eased) interpolation: its contours turn into facets, which gives
// the flames the angular, cut-paper edges of stylized game VFX instead of smooth blobs.
float facetNoise(float2 p)
{
    float2 i = floor(p), f = frac(p);
    return lerp(lerp(hash(i), hash(i + float2(1, 0)), f.x),
                lerp(hash(i + float2(0, 1)), hash(i + float2(1, 1)), f.x), f.y);
}

float facetFbm(float2 p) { return facetNoise(p) * 0.7 + facetNoise(p * 2.3 + 3.1) * 0.3; }

float inside(float2 uv) { float2 s = step(0, uv) * step(uv, 1); return s.x * s.y; }

half alphaAt(float2 uv) { return tex2Dlod(_MainTex, float4(uv, 0, 0)).a * inside(uv); }

// Soft silhouette from the mip chain; fades toward the texture edge instead of clamping the
// border texels outward.
half blurAt(float2 uv, float lod)
{
    float edge = saturate(1 - length(max(abs(uv - 0.5) - 0.5, 0)) * 12);
    return tex2Dlod(_MainTex, float4(saturate(uv), 0, lod)).a * edge;
}

// Centre + 8 points on a ring: a tight, crisp halo for the rim light.
half halo(float2 uv, float r)
{
    float2 k = float2(r / _Aspect, r);
    float2 d = k * 0.7071;
    half s = alphaAt(uv);
    s += alphaAt(uv + float2(k.x, 0)) + alphaAt(uv - float2(k.x, 0));
    s += alphaAt(uv + float2(0, k.y)) + alphaAt(uv - float2(0, k.y));
    s += alphaAt(uv + d) + alphaAt(uv - d);
    s += alphaAt(uv + float2(d.x, -d.y)) + alphaAt(uv + float2(-d.x, d.y));
    return s / 9;
}

// Soft distance to the body: ~0.5 on the outline, fading to 0 about _Reach away from it.
half distanceAt(float2 uv)
{
    float2 k = float2(_Reach / _Aspect, _Reach);
    half c = blurAt(uv, 1.0), nearRing = 0, farRing = 0;
    [unroll] for (int j = 0; j < 8; j++)
    {
        float2 d = float2(cos(j * 0.785398), sin(j * 0.785398)) * k;
        nearRing += blurAt(uv + d * 0.5, 1.5);
        farRing += blurAt(uv + d, 2.0);
    }
    return c * 0.3 + nearRing * 0.05 + farRing * 0.0375;
}

// The baked distance at silhouette uv (HeroAuraField.shader), over the aura quad.
half fieldAt(float2 uv)
{
    float2 f = (uv - _FieldRect.xy) / _FieldRect.zw;
    return tex2D(_FieldTex, f).a * inside(f);
}

#endif
