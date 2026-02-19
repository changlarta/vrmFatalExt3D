Shader "Custom/URP/InfiniteDualGridBackground_Fixed"
{
    SubShader
    {
        Tags
        {
            "RenderPipeline"="UniversalPipeline"
            "RenderType"="Opaque"
            "Queue"="Geometry"
        }

        Pass
        {
            Name "UniversalForward"
            Tags { "LightMode"="UniversalForward" }

            ZWrite On
            ZTest LEqual
            Cull Off
            Blend Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float3 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv          : TEXCOORD0;
            };

            Varyings vert (Attributes v)
            {
                Varyings o;
                o.positionHCS = TransformObjectToHClip(v.positionOS);
                o.uv = v.uv;
                return o;
            }

            float GridLines(float2 uvScaled)
            {
                const float lineWidth = 0.001;

                float2 f = frac(uvScaled);
                float2 distToEdge = min(f, 1.0 - f);
                float d = min(distToEdge.x, distToEdge.y);

                float aa = max(fwidth(uvScaled.x), fwidth(uvScaled.y));
                aa = max(aa, 1e-5);

                return 1.0 - smoothstep(lineWidth, lineWidth + aa, d);
            }

            half4 frag (Varyings i) : SV_Target
            {
                // 参照コードの調整値をそのまま代入
                const float3 bgColor     = float3(0.0, 0.0, 0.0);
                const float3 frontColor  = float3(0.0, 1.0, 1.0);
                const float3 backColor   = float3(0.2, 0.0, 0.3);

                const float  frontScale  = 8.0;
                const float  backScale   = 14.0;

                const float2 frontSpeed  = float2(-0.04, 0.08);
                const float2 backSpeed   = float2( 0.16, -0.04);

                const float  fadePower   = 1.5;

                float t = _Time.y;

                float2 p = i.uv * 2.0 - 1.0;
                float r = saturate(1.0 - dot(p, p));
                float edgeFade = pow(r, fadePower);

                float2 uvFront = i.uv * frontScale + frontSpeed * t;
                float2 uvBack  = i.uv * backScale  + backSpeed  * t;

                float front = GridLines(uvFront);
                float back  = GridLines(uvBack);

                float3 col = bgColor;
                col += backColor  * back  * edgeFade;
                col += frontColor * front * edgeFade;

                return half4((half3)col, 1.0h);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
