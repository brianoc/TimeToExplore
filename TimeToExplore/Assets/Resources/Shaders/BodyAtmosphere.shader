Shader "TimeToExplore/BodyAtmosphere"
{
    Properties
    {
        _Color ("Colour", Color) = (0.35,0.56,1,1)
        _Intensity ("Intensity", Range(0,2)) = 1
        _RimPower ("Rim Power", Range(1,8)) = 3
    }
    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
        }

        Pass
        {
            Name "ForwardAtmosphere"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha One
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half _Intensity;
                half _RimPower;
            CBUFFER_END

            float4 _SunPosition; // Set globally, in render-world units.

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs positions = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normals = GetVertexNormalInputs(input.normalOS);
                output.positionCS = positions.positionCS;
                output.positionWS = positions.positionWS;
                output.normalWS = normals.normalWS;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float3 n = normalize(input.normalWS);
                float3 view = normalize(_WorldSpaceCameraPos.xyz - input.positionWS);

                // Brightest where the shell turns away from the camera, so the
                // glow is a limb rather than a disc.
                half rim = pow(saturate(1.0h - saturate(dot(n, view))), _RimPower);

                // Fade the rim round the terminator, softly, so the day side
                // glows and the night side does not get a bright outline.
                float3 toSun = normalize(_SunPosition.xyz - input.positionWS);
                half sun = smoothstep(-0.2h, 0.35h, dot(n, toSun));

                // Additive, so alpha is the amount of glow to add. _Color is an
                // authored sRGB colour, converted once when Unity submits the
                // material; do not convert it again here.
                half alpha = saturate(rim * sun * _Intensity * _Color.a);
                return half4(_Color.rgb, alpha);
            }
            ENDHLSL
        }
    }
    Fallback "Universal Render Pipeline/Unlit"
}
