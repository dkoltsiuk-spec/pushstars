// One step of the frosted-glass blur (FrostedBackdrop): four bilinear taps around the pixel,
// _Offset texels apart (Kawase). Run a few times over a shrinking copy of the screen it turns a
// sharp frame into soft colour in a fraction of a millisecond. _FlipY turns the screen capture
// the right way up on the first step where the platform hands it over upside down.
Shader "Hidden/PushStars/UI Blur"
{
    Properties
    {
        _MainTex ("Source", 2D) = "white" {}
        _Offset ("Offset", Float) = 1
        _FlipY ("Flip Y", Float) = 0
    }

    SubShader
    {
        Cull Off
        ZWrite Off
        ZTest Always

        Pass
        {
        CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag

            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            float _Offset;
            float _FlipY;

            fixed4 frag(v2f_img IN) : SV_Target
            {
                float2 uv = IN.uv;
                uv.y = lerp(uv.y, 1 - uv.y, _FlipY);
                float2 o = _MainTex_TexelSize.xy * (_Offset + 0.5);
                fixed4 sum = tex2D(_MainTex, uv + float2(o.x, o.y));
                sum += tex2D(_MainTex, uv + float2(-o.x, o.y));
                sum += tex2D(_MainTex, uv + float2(o.x, -o.y));
                sum += tex2D(_MainTex, uv + float2(-o.x, -o.y));
                return fixed4(sum.rgb * 0.25, 1);
            }
        ENDCG
        }
    }
}
