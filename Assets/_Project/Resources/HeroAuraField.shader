// Bakes the soft distance to the hero's body for HeroAura.shader: ~0.5 on the outline, fading to
// 0 about _Reach away, over the aura quad's rect (_FieldRect, silhouette UVs). Blitted once per
// frame by HeroAura.cs into a small texture, so the on-screen pass reads it with a few samples.
Shader "Hidden/PushStars/Hero Aura Field"
{
    Properties
    {
        _MainTex ("Silhouette", 2D) = "black" {}
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always Blend Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment fragField
            #pragma target 3.0
            #include "HeroAura.cginc"

            half4 fragField(v2f_img i) : SV_Target
            {
                return distanceAt(_FieldRect.xy + i.uv * _FieldRect.zw).xxxx;
            }
            ENDCG
        }
    }
}
