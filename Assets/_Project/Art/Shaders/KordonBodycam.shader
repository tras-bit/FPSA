// KORDON bodycam post process. Built-in render pipeline, single fullscreen pass.
// Barrel distortion + chromatic aberration + sensor grain + vignette + exposure
// flutter driven by suppression and breathing. Always on: the material is created
// by BodycamRig at runtime and nothing in the settings can disable it.
Shader "Hidden/Kordon/Bodycam"
{
    Properties
    {
        _MainTex ("Frame", 2D) = "white" {}
        _Distortion ("Barrel distortion", Range(0.0, 0.25)) = 0.075
        _Aberration ("Chromatic aberration", Range(0.0, 0.02)) = 0.0045
        _Vignette ("Vignette strength", Range(0.0, 2.0)) = 0.85
        _Grain ("Sensor grain", Range(0.0, 0.35)) = 0.11
        _Time2 ("Seconds since boot", Float) = 0
        _Suppression ("Suppression shake 0..1", Float) = 0
        _Breath ("Breath intensity 0..1", Float) = 0
        _Sprint ("Sprinting 0/1", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        ZTest Always Cull Off ZWrite Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            float _Distortion;
            float _Aberration;
            float _Vignette;
            float _Grain;
            float _Time2;
            float _Suppression;
            float _Breath;
            float _Sprint;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            float2 barrel(float2 uv, float amount)
            {
                float2 c = uv - 0.5;
                float r2 = dot(c, c);
                return 0.5 + c * (1.0 + amount * r2);
            }

            float hash(float2 p)
            {
                return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453);
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // Handheld micro-shake: the cam never sits perfectly still.
                float t = _Time2;
                float2 jitter = float2(
                    sin(t * 1.7) * 0.35 + sin(t * 4.3) * 0.15,
                    cos(t * 1.3) * 0.35 + cos(t * 3.7) * 0.15) * 0.0016
                    * (1.0 + _Suppression * 2.0 + _Breath * 0.8 + _Sprint * 1.2);

                float2 uv = barrel(i.uv + jitter, _Distortion + _Sprint * 0.012);

                // Chromatic aberration grows towards the frame edge.
                float2 c = uv - 0.5;
                float r2 = dot(c, c);
                float2 ab = c * (_Aberration * (0.35 + r2 * 2.0));
                fixed r = tex2D(_MainTex, uv + ab).r;
                fixed g = tex2D(_MainTex, uv).g;
                fixed b = tex2D(_MainTex, uv - ab).b;
                fixed3 col = fixed3(r, g, b);

                // Exposure flutter: cheap auto-gain hunting like a real sensor.
                float flutter = 1.0 + 0.035 * sin(t * 9.1) + 0.02 * sin(t * 23.7);
                col *= flutter;

                // Vignette + slight corner desaturation.
                float vig = smoothstep(0.95, 0.28, r2 * _Vignette * 1.9);
                col *= vig;

                // Sensor grain, animated, stronger in shadows.
                float grain = hash(i.uv * _MainTex_TexelSize.zw * 0.25 + frac(t) * 91.7);
                float lum = dot(col, fixed3(0.299, 0.587, 0.114));
                col += (grain - 0.5) * _Grain * (1.25 - lum);

                // Suppression wash: near-misses bleach the frame briefly.
                col = lerp(col, fixed3(0.86, 0.88, 0.9), _Suppression * 0.16);

                return fixed4(col, 1.0);
            }
            ENDCG
        }
    }

    Fallback Off
}
