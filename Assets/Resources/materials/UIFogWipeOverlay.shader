Shader "UI/FogWipeOverlay"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

        _FogAlpha ("Fog Alpha", Range(0,1)) = 0
        _MaskTex  ("Fog Mask", 2D) = "white" {}

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
        Tags
        {
            "Queue"="Overlay"
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
            Name "Fog"
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0

            #pragma multi_compile __ UNITY_UI_CLIP_RECT
            #pragma multi_compile __ UNITY_UI_ALPHACLIP

            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            sampler2D _MainTex;
            sampler2D _MaskTex;
            float4 _Color;
            float _FogAlpha;

            // ★ RectMask2D が設定する値。宣言がないと _ClipRect 未定義で落ちる
            float4 _ClipRect;

            struct appdata_t
            {
                float4 vertex   : POSITION;
                float4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex   : SV_POSITION;
                float4 color    : COLOR;
                float2 uv       : TEXCOORD0;
                float4 localPos : TEXCOORD1;   // UIデフォルトと同じくローカル座標を保持
            };

            v2f vert(appdata_t v)
            {
                v2f o;
                o.localPos = v.vertex;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.texcoord;
                o.color = v.color * _Color;
                return o;
            }

            // ★ UnityGet2DClipping を使わず、硬い矩形クリップを自前実装
            inline float FogRectClip(float2 pos, float4 clipRect)
            {
                // clipRect: (xMin, yMin, xMax, yMax)
                float2 inside = step(clipRect.xy, pos) * step(pos, clipRect.zw);
                return inside.x * inside.y;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 baseCol = tex2D(_MainTex, i.uv) * i.color;

                fixed mask = tex2D(_MaskTex, i.uv).a;
                fixed a = saturate(_FogAlpha * mask);

                fixed4 col = fixed4(1,1,1, a);

                #ifdef UNITY_UI_CLIP_RECT
                col.a *= FogRectClip(i.localPos.xy, _ClipRect);
                #endif

                #ifdef UNITY_UI_ALPHACLIP
                clip(col.a - 0.001);
                #endif

                return col;
            }
            ENDHLSL
        }
    }
}
