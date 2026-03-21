Shader "Custom/DashboardGradientFlow_Clean"
{
    Properties
    {
        _BackColor   ("Back (Deep Red)", Color) = (0.85, 0.08, 0.05, 1)
        _FrontColor  ("Front (Hot Orange)", Color) = (1.00, 0.45, 0.05, 1)

        _Intensity   ("Intensity", Range(0, 3)) = 1.2
        _Alpha       ("Alpha", Range(0, 1)) = 0.9

        _EdgeSoftness ("Edge Softness", Range(0, 1)) = 0.35

        // Flow (highlight bands moving forward: back -> front)
        _FlowSpeed    ("Flow Speed (Forward)", Range(0, 10)) = 2.0
        _FlowScale    ("Flow Density", Range(0.5, 30)) = 8.0
        _FlowStrength ("Flow Strength", Range(0, 1)) = 0.18
        _FlowWidth    ("Flow Band Width", Range(0.05, 1)) = 0.35
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            fixed4 _BackColor;
            fixed4 _FrontColor;
            float _Intensity;
            float _Alpha;
            float _EdgeSoftness;

            float _FlowSpeed;
            float _FlowScale;
            float _FlowStrength;
            float _FlowWidth;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv     : TEXCOORD0; // uv.y : back(0) -> front(1)
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv  : TEXCOORD0;
            };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            // 0..1 の帯を作る（中心ほど1、外側ほど0）
            float Band(float x, float width)
            {
                // width が小さいほど細い帯
                // x は 0..1 の saw を想定
                float d = abs(x - 0.5) * 2.0;         // 0 center -> 1 edges
                return saturate((width - d) / max(1e-5, width)); // center=1, edges=0
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // ---- Base forward gradient (Deep Red -> Orange) ----
                float t = saturate(i.uv.y);                 // back(0) -> front(1)
                fixed3 baseRgb = lerp(_BackColor.rgb, _FrontColor.rgb, t);
                baseRgb *= _Intensity;

                // ---- Edge soft alpha (stronger at center, softer at sides) ----
                float side = abs(i.uv.x - 0.5) * 2.0;        // 0 center -> 1 side
                float centerMask = saturate(1.0 - side);     // 1 center -> 0 side
                // EdgeSoftness: 0 = hard, 1 = soft
                float aSide = lerp(1.0, centerMask, _EdgeSoftness);
                float alpha = saturate(_Alpha * aSide);

                // ---- Flow: moving highlight bands forward (back -> front) ----
                // Forward motion means the band phase increases with time along +uv.y.
                // If it ever looks backwards, flip the sign before _Time.y.
                float phase = i.uv.y * _FlowScale - _Time.y * _FlowSpeed;

                // Make repeating 0..1 pattern
                float saw = frac(phase);

                // Build a single band in each repeat
                float band = Band(saw, _FlowWidth);

                // Keep the flow subtle, and strongest near the center so gradient isn't destroyed
                float flowMask = band * _FlowStrength * (0.35 + 0.65 * centerMask);

                // Add a warm highlight (more orange) rather than pink/salmon
                fixed3 highlight = fixed3(1.0, 0.55, 0.12) * flowMask;

                fixed3 rgb = saturate(baseRgb + highlight);

                return fixed4(rgb, alpha);
            }
            ENDCG
        }
    }
}