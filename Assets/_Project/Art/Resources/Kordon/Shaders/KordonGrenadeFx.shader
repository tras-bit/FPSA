// KORDON - grenade effect volume shader.
//
// Unlit, alpha blended, soft-edged sprites-in-3D used by the explosion flash,
// the dust ring and the smoke cloud. It is deliberately dependency free (no
// render pipeline package, no imported texture): the colour and the soft edge
// are computed from the mesh normal and the view direction, so every effect
// stays original and redistributable.
Shader "Kordon/GrenadeFx"
{
    Properties
    {
        _Color ("Colour", Color) = (0.55, 0.52, 0.46, 0.55)
        _Softness ("Soft edge", Range(0.05, 4.0)) = 1.6
        _AlphaPower ("Alpha power", Range(0.25, 4.0)) = 1.0
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "IgnoreProjector" = "True"
            "PreviewType" = "Sphere"
        }

        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off
        Lighting Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 position : SV_POSITION;
                float3 normal : TEXCOORD0;
                float3 viewDir : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            fixed4 _Color;
            float _Softness;
            float _AlphaPower;

            v2f vert(appdata input)
            {
                v2f output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_OUTPUT(v2f, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                output.position = UnityObjectToClipPos(input.vertex);
                float3 worldNormal = UnityObjectToWorldNormal(input.normal);
                output.normal = worldNormal;
                output.viewDir = WorldSpaceViewDir(input.vertex);
                return output;
            }

            fixed4 frag(v2f input) : SV_Target
            {
                float3 normal = normalize(input.normal);
                float3 view = normalize(input.viewDir);
                float facing = saturate(abs(dot(normal, view)));
                float edge = pow(facing, _Softness);
                float alpha = _Color.a * pow(edge, _AlphaPower);
                return fixed4(_Color.rgb, saturate(alpha));
            }
            ENDCG
        }
    }

    Fallback Off
}
