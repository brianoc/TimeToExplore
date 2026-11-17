Shader "TimeToExplore/Skybox"
{
    // A two-part sky: a baked equirectangular Milky Way (the diffuse glow) plus
    // procedural stars. Baking the stars into the same texture would make every
    // star the size of a texel — several screen pixels at 60 degrees of field of
    // view — so the resolved stars are drawn here instead, one cell at a time.
    //
    // The mapping matches tools/generate_skybox.py:
    //   u = atan2(z, x) / 2pi + 0.5,  v = asin(y) / pi + 0.5
    // with +y the ecliptic north pole. The texture is sampled at LOD 0 so the
    // wrap seam at u = 0 cannot pick the smallest mip.

    Properties
    {
        _MainTex ("Milky Way (equirectangular)", 2D) = "black" {}
        _BandGain ("Band gain", Range(0, 2)) = 1
        _StarGain ("Star gain", Range(0, 3)) = 1
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Background"
            "RenderType" = "Background"
            "PreviewType" = "Skybox"
        }
        Cull Off
        ZWrite Off

        Pass
        {
            Name "Skybox"

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                half _BandGain;
                half _StarGain;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 direction : TEXCOORD0;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.direction = input.positionOS.xyz;
                return output;
            }

            float2 SkyUv(float3 direction)
            {
                float u = atan2(direction.z, direction.x) * 0.15915494309 + 0.5;
                float v = asin(clamp(direction.y, -1.0, 1.0)) * 0.31830988618 + 0.5;
                return float2(u, v);
            }

            half BandLuminance(float3 direction)
            {
                half3 band = SAMPLE_TEXTURE2D_LOD(
                    _MainTex, sampler_MainTex, SkyUv(direction), 0).rgb;
                return dot(band, half3(0.2126, 0.7152, 0.0722));
            }

            // Dave Hoskins' cheap 3D hash. Stable across platforms because it
            // only uses frac, multiply, and dot.
            float3 Hash33(float3 p)
            {
                p = frac(p * float3(0.1031, 0.1030, 0.0973));
                p += dot(p, p.yxz + 33.33);
                return frac((p.xxy + p.yxx) * p.zyx);
            }

            // One star per lattice cell, drawn only where the cell's existence
            // hash passes. The star's direction is dithered inside the cell, so
            // the field is irregular; the lattice itself is far below one pixel.
            half3 StarLayer(float3 direction, float scale, float density,
                            float radius, float gain, float power, float seed)
            {
                float3 cell = floor(direction * scale);
                float3 h = Hash33(cell + seed);
                if (h.x > density * 5.0) return half3(0, 0, 0);

                float3 centre = normalize(cell + 0.5 + (h - 0.5) * 0.7);

                // The galaxy is denser in stars than the rest of the sky.
                float threshold = density * (1.0 + 4.0 * BandLuminance(centre));
                if (h.x > threshold) return half3(0, 0, 0);

                float sinAngle = length(cross(direction, centre));
                half magnitude = pow(abs(h.y), power);
                half localRadius = radius * (0.8 + 0.5 * magnitude);
                half core = saturate(1.0h - sinAngle / localRadius);
                core *= core * core;
                half halo = saturate(1.0h - sinAngle / (localRadius * 3.0h));

                half3 tint = lerp(half3(0.70, 0.80, 1.00),
                                  half3(1.00, 0.80, 0.55), h.z * h.z);
                return tint * (gain * (0.04h + 2.4h * magnitude) * (core + 0.06h * halo));
            }

            half4 frag(Varyings input) : SV_Target
            {
                float3 direction = normalize(input.direction);

                half3 colour = SAMPLE_TEXTURE2D_LOD(
                    _MainTex, sampler_MainTex, SkyUv(direction), 0).rgb * _BandGain;

                half3 stars = StarLayer(direction, 300.0, 0.0035, 0.00050, 0.60, 8.0, 11.0)
                            + StarLayer(direction, 90.0, 0.0100, 0.00090, 1.10, 6.0, 37.0)
                            + StarLayer(direction, 26.0, 0.0500, 0.00180, 2.20, 4.0, 71.0);

                return half4(colour + stars * _StarGain, 1.0h);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
