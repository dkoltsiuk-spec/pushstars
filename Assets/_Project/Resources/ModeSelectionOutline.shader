Shader "PushStars/UI Mode Selection Outline"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _OuterUV ("Sprite bounds", Vector) = (0,0,1,1)
        _OutlineUV ("Stroke width in UV", Vector) = (.01,.03,0,0)
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
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP
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
            sampler2D _MainTex;
            fixed4 _Color;
            float4 _OuterUV, _OutlineUV, _ClipRect;

            v2f vert(appdata_t v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.worldPosition = v.vertex;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.texcoord = v.texcoord;
                o.color = v.color * _Color;
                return o;
            }

            half SpriteAlpha(float2 uv)
            {
                // Samples outside this sprite must not pick up its atlas neighbours.
                half inside = step(_OuterUV.x, uv.x) * step(_OuterUV.y, uv.y)
                    * step(uv.x, _OuterUV.z) * step(uv.y, _OuterUV.w);
                return tex2D(_MainTex, clamp(uv, _OuterUV.xy, _OuterUV.zw)).a * inside;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float2 d = _OutlineUV.xy;
                half center = SpriteAlpha(i.texcoord);
                half interior = center;
                interior = min(interior, SpriteAlpha(i.texcoord + float2(d.x, 0)));
                interior = min(interior, SpriteAlpha(i.texcoord - float2(d.x, 0)));
                interior = min(interior, SpriteAlpha(i.texcoord + float2(0, d.y)));
                interior = min(interior, SpriteAlpha(i.texcoord - float2(0, d.y)));
                interior = min(interior, SpriteAlpha(i.texcoord + d * .7071));
                interior = min(interior, SpriteAlpha(i.texcoord - d * .7071));
                interior = min(interior, SpriteAlpha(i.texcoord + float2(d.x, -d.y) * .7071));
                interior = min(interior, SpriteAlpha(i.texcoord + float2(-d.x, d.y) * .7071));
                // Use only alpha from the artwork. Black RGB must never darken the yellow.
                fixed4 color = fixed4(i.color.rgb, i.color.a * saturate(center - interior));
                #ifdef UNITY_UI_CLIP_RECT
                    color.a *= UnityGet2DClipping(i.worldPosition.xy, _ClipRect);
                #endif
                #ifdef UNITY_UI_ALPHACLIP
                    clip(color.a - .001);
                #endif
                return color;
            }
            ENDCG
        }
    }
}
