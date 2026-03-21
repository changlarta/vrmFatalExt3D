Shader "Custom/Unlit_Scroll_BottomRight_ST_DoubleSidedFix"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _SpeedX ("Speed X (Right)", Float) = 0.20
        _SpeedY ("Speed Y (Down)", Float) = 0.20
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" }
        Cull Off
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            float4 _Color;
            float _SpeedX, _SpeedY;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv     : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv  : TEXCOORD0;
                float  faceSign : TEXCOORD1; // +1: front, -1: back
            };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);

                float2 uv = TRANSFORM_TEX(v.uv, _MainTex);

                float t = _Time.y;
                uv += float2(_SpeedX * t, -_SpeedY * t);

                o.uv = uv;

                // Unityは背面だと VFACE が -1 になる（fragment側で使う）
                o.faceSign = 1.0;
                return o;
            }

            fixed4 frag (v2f i, fixed face : VFACE) : SV_Target
            {
                float2 uv = i.uv;

                // 背面なら左右反転を打ち消す（Xを反転）
                if (face < 0) uv.x = 1.0 - uv.x;

                return tex2D(_MainTex, uv) * _Color;
            }
            ENDCG
        }
    }
}