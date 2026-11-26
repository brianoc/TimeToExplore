Shader "TimeToExplore/BodyLit"
{
    Properties
    {
        _Color ("Colour", Color) = (1,1,1,1)
        _MainTex ("Albedo", 2D) = "white" {}
        _HasAlbedo ("Has Albedo", Float) = 0
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
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
            };

            // A texture and its sampler cannot live in the per-material
            // buffer, but the tiling/offset can.
            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4 _Color;
                half _Ambient;
                half _HasAlbedo;
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
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float3 n = normalize(input.normalWS);
                float3 toSun = normalize(_SunPosition.xyz - input.positionWS);
                half ndl = saturate(dot(n, toSun));

                // _HasAlbedo is 0 for a marker and 1 once the detail gate opens.
                // The map is imported sRGB, so the sampler has already decoded
                // it to linear; do not convert it again here. _Color is the
                // authored marker tint, which Unity converts on submit.
                half3 albedo = lerp(_Color.rgb,
                    SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv).rgb,
                    _HasAlbedo);

                // _Ambient is authored as an sRGB grey, the way it was in the
                // gamma pipeline, so the night side keeps the same perceived
                // floor. The light itself is linear.
                half ambient = SRGBToLinear(_Ambient);
                half3 colour = albedo * (ambient + (1.0h - ambient) * ndl);
                return half4(colour, 1.0h);
            }
            ENDHLSL
        }
    }
    Fallback "Universal Render Pipeline/Unlit"
}
