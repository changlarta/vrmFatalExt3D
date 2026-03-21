Shader "Custom/LightningBeamBuiltin"
{
    Properties
    {
        _Color ("Color", Color) = (0.8, 0.95, 1, 1)
        _Intensity ("Intensity", Float) = 2.5
        _SoftEdge ("Soft Edge", Range(0,1)) = 0.35
        _Band ("Band", Range(0,4)) = 1.6
        _Speed ("Scroll Speed", Float) = 18.0
        _Flicker ("Flicker", Float) = 1.0
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        LOD 100

        Cull Off
        ZWrite Off
        Blend One One

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            fixed4 _Color;
            float _Intensity;
            float _SoftEdge;
            float _Band;
            float _Speed;
            float _Flicker;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 localPos : TEXCOORD0;   // ローカル座標（スケール前の-0.5..0.5想定）
                float3 localNrm : TEXCOORD1;   // ローカル法線
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.localPos = v.vertex.xyz;
                o.localNrm = v.normal;
                return o;
            }

            float hash(float2 p)
            {
                return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453123);
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // ローカルZを「長手方向」(0..1)にする（Cubeの-0.5..0.5 → 0..1）
                float v = i.localPos.z + 0.5;

                // “横方向”は面の法線から自動決定
                // 左右面(±X面)なら横=Y、上下面(±Y面)なら横=X、前後面(±Z面)なら横=X
                float3 an = abs(normalize(i.localNrm));
                float across;
                if (an.x > an.y && an.x > an.z)      across = i.localPos.y; // ±X面
                else                                  across = i.localPos.x; // ±Y面 or ±Z面

                // across は -0.5..0.5 想定、中心ほど明るい
                float x = abs(across);
                float edge = smoothstep(0.5, 0.5 - _SoftEdge, x);

                // 縦方向（長手）のバンド/揺らぎ：v を使う（面に依存しない）
                float t = _Time.y * _Speed;
                float n = hash(float2(v * 12.0 + t, t));
                float band = sin((v * 30.0 + t) * _Band) * 0.5 + 0.5;

                float core = saturate(edge * (0.55 + 0.45 * band) * (0.6 + 0.4 * n));

                // フリッカー
                float flick = 1.0;
                if (_Flicker > 0.5)
                {
                    float f = sin(_Time.y * 50.0) * 0.5 + 0.5;
                    flick = lerp(0.65, 1.25, f);
                }

                float a = core * flick;
                fixed3 rgb = _Color.rgb * (_Intensity * a);
                return fixed4(rgb, a);
            }
            ENDCG
        }
    }
}