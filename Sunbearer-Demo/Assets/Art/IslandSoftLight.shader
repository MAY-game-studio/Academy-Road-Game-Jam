Shader "Hidden/Sunbearer/Island Soft Light"
{
    Properties
    {
        _MainTex ("Scene", 2D) = "white" {}
        _GlowTex ("Glow", 2D) = "black" {}
        _Tint ("Tint", Color) = (1, 1, 1, 1)
        _Strength ("Strength", Range(0, 1)) = 0
        _Threshold ("Threshold", Range(0, 1)) = 0.55
        _BlurDirection ("Blur Direction", Vector) = (1, 0, 0, 0)
    }

    SubShader
    {
        Cull Off
        ZWrite Off
        ZTest Always

        CGINCLUDE
        #include "UnityCG.cginc"

        sampler2D _MainTex;
        sampler2D _GlowTex;
        float4 _MainTex_TexelSize;
        float4 _BlurDirection;
        half4 _Tint;
        half _Strength;
        half _Threshold;

        half4 ExtractHighlights(v2f_img input) : SV_Target
        {
            float2 texel = abs(_MainTex_TexelSize.xy);
            half3 color = (
                tex2D(_MainTex, input.uv + texel).rgb +
                tex2D(_MainTex, input.uv - texel).rgb +
                tex2D(_MainTex, input.uv + texel * float2(1, -1)).rgb +
                tex2D(_MainTex, input.uv + texel * float2(-1, 1)).rgb) * 0.25;
            half brightness = max(color.r, max(color.g, color.b));
            half highlight = smoothstep(max(0, _Threshold - 0.12), _Threshold + 0.12, brightness);
            return half4(color * highlight, 1);
        }

        half4 BlurGlow(v2f_img input) : SV_Target
        {
            float2 offset = _MainTex_TexelSize.xy * _BlurDirection.xy;
            half3 color = tex2D(_MainTex, input.uv).rgb * 0.227027;
            color += (tex2D(_MainTex, input.uv + offset * 1.384615).rgb +
                      tex2D(_MainTex, input.uv - offset * 1.384615).rgb) * 0.316216;
            color += (tex2D(_MainTex, input.uv + offset * 3.230769).rgb +
                      tex2D(_MainTex, input.uv - offset * 3.230769).rgb) * 0.070270;
            return half4(color, 1);
        }

        half4 Composite(v2f_img input) : SV_Target
        {
            float2 glowUv = input.uv;
            #if UNITY_UV_STARTS_AT_TOP
            if (_MainTex_TexelSize.y < 0)
                glowUv.y = 1 - glowUv.y;
            #endif

            half4 scene = tex2D(_MainTex, input.uv);
            half3 glow = min(tex2D(_GlowTex, glowUv).rgb, 1.6);
            half3 ambientTint = lerp(half3(1, 1, 1), _Tint.rgb, _Strength * 0.20);
            scene.rgb = scene.rgb * ambientTint + glow * _Tint.rgb * _Strength;
            return scene;
        }
        ENDCG

        Pass
        {
            Name "Highlights"
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert_img
            #pragma fragment ExtractHighlights
            ENDCG
        }

        Pass
        {
            Name "Blur"
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert_img
            #pragma fragment BlurGlow
            ENDCG
        }

        Pass
        {
            Name "Composite"
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert_img
            #pragma fragment Composite
            ENDCG
        }
    }
}
