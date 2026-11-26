Shader "TimeToExplore/BodyLit"
{
    Properties
    {
        _Color ("Colour", Color) = (1,1,1,1)
        _Ambient ("Ambient", Range(0,1)) = 0.055
    }
    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
        }
        LOD 100

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"

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

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half _Ambient;
            CBUFFER_END

            float4 _SunPosition; // Set globally, in render-world units.

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
                float3 toSun = normalize(_SunPosition.xyz - input.positionWS);
                half ndl = saturate(dot(n, toSun));

                // _Ambient is authored as an sRGB grey, the way it was in the
                // gamma pipeline, so the night side keeps the same perceived
                // floor. The light itself is linear.
                half ambient = SRGBToLinear(_Ambient);
                half3 colour = _Color.rgb * (ambient + (1.0h - ambient) * ndl);
                return half4(colour, 1.0h);
            }
            ENDHLSL
        }
    }
    Fallback "Universal Render Pipeline/Unlit"
}
