Shader "Sunbearer/Sun Corona"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        [HideInInspector] _Charge ("Charge", Range(0, 1)) = 0
        [HideInInspector] _Extent ("Corona Extent", Float) = 2.1
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "IgnoreProjector" = "True"
            "CanUseSpriteAtlas" = "False"
        }

        Pass
        {
            Cull Off
            ZWrite Off
            Blend One One
            ColorMask RGB

            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct Attributes
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 position : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            float _Charge;
            float _Extent;

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.position = UnityObjectToClipPos(input.vertex);
                output.uv = input.uv;
                return output;
            }

            float Hash(float value)
            {
                return frac(sin(value * 127.1 + 311.7) * 43758.5453);
            }

            float RayField(float turn, float radius, float time, float charge,
                float sectors, float density, float lengthScale, float seed)
            {
                // Stable angles let new rays appear between the existing ones as the ball grows.
                float angular = frac(turn) * sectors;
                float sector = floor(angular);
                float lane = frac(angular);
                float lengthVariation = Hash(sector + seed);
                float shapeVariation = Hash(sector + seed + 19.0);
                float visible = smoothstep(lengthVariation - 0.06, lengthVariation + 0.06, density);
                float flickerRate = 4.0 + 7.0 * lengthVariation + 9.0 * charge;
                float flicker = 0.5 + 0.5 * sin(time * flickerRate + shapeVariation * 6.2831853);

                float reach = lerp(0.14, 0.60, charge) * lengthScale *
                    lerp(0.66, 1.36, lengthVariation) * lerp(0.73, 1.0, flicker);
                float along = max(0.0, (radius - 0.97) / reach);
                float taper = max(0.04, 1.0 - along);
                float halfWidth = lerp(0.085, 0.155, shapeVariation) * taper;
                float lateral = abs(lane - lerp(0.31, 0.69, shapeVariation));
                float edge = clamp(fwidth(lane) * 0.65, 0.006, 0.045);
                float blade = 1.0 - smoothstep(max(0.0, halfWidth - edge), halfWidth + edge, lateral);
                float glow = 1.0 - smoothstep(halfWidth, 2.1 * halfWidth + edge, lateral);
                float envelope = smoothstep(0.93, 1.015, radius) * (1.0 - smoothstep(0.10, 1.0, along));

                return visible * envelope * (blade + 0.23 * glow) * lerp(0.57, 1.0, flicker);
            }

            half4 frag(Varyings input) : SV_Target
            {
                float2 p = (input.uv - 0.5) * (2.0 * _Extent);
                float radius = length(p);
                float turn = atan2(p.y, p.x) * 0.15915494;
                float charge = saturate(_Charge);
                float time = _Time.y;

                float haloWidth = lerp(0.23, 0.74, charge);
                float halo = 1.0 - smoothstep(0.0, haloWidth, max(0.0, radius - 1.0));
                halo = halo * halo * smoothstep(0.92, 1.015, radius) * lerp(0.15, 0.50, charge);
                float rim = 1.0 - smoothstep(0.0, lerp(0.05, 0.14, charge), abs(radius - 0.99));
                rim *= lerp(0.19, 0.74, charge);
                float pulse = 0.88 + 0.12 * sin(time * lerp(3.0, 8.0, charge));

                float lances = RayField(turn + time * 0.025, radius, time, charge,
                    9.0, lerp(0.43, 1.06, charge), 1.12, 31.0);
                float threads = RayField(turn - time * 0.041, radius, time, charge,
                    23.0, lerp(-0.09, 1.06, charge), 0.65, 71.0);

                half3 light = pulse * (half3(1.0, 0.47, 0.10) * halo + half3(1.0, 0.79, 0.36) * rim);
                light += half3(1.0, 0.96, 0.69) * lances * lerp(0.44, 1.27, charge);
                light += half3(1.0, 0.74, 0.27) * threads * lerp(0.31, 0.76, charge);
                return half4(light, 0.0);
            }
            ENDCG
        }
    }
}
