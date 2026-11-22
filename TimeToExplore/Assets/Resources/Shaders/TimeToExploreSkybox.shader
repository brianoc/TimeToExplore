Shader "TimeToExplore/Skybox"
{
    // A two-part sky: a baked equirectangular Milky Way (the diffuse glow) plus
    // procedural stars. Baking the stars into the same texture would make every
    // star the size of a texel — several screen pixels at 60 degrees of field of
    // view — so the resolved stars are drawn here instead, one cell at a time.
    // The brightest real stars come from a catalogue map, so Orion is where
    // Orion is; see StarCatalogue.cs for its layout and SkyAngle for the lookup.
    //
    // The mapping matches tools/generate_skybox.py:
    //   u = atan2(z, x) / 2pi + 0.5,  v = asin(y) / pi + 0.5
    // with +y the ecliptic north pole. The texture is sampled at LOD 0 so the
    // wrap seam at u = 0 cannot pick the smallest mip.

    Properties
    {
        _MainTex ("Milky Way (equirectangular)", 2D) = "black" {}
        _StarMap ("Bright star map", 2D) = "black" {}
        _BandGain ("Band gain", Range(0, 2)) = 1
        _StarGain ("Star gain", Range(0, 3)) = 1
        _CatalogueGain ("Bright star gain", Range(0, 3)) = 1
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
            TEXTURE2D(_StarMap);
            SAMPLER(sampler_StarMap);

            CBUFFER_START(UnityPerMaterial)
                half _BandGain;
                half _StarGain;
                half _CatalogueGain;
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

            // --- Real stars -------------------------------------------------
            //
            // The catalogue map holds one star per texel: the fractional
            // position inside its sky cell, its magnitude, and its colour. A
            // cell is about a third of a degree, so at most nine cells can hold
            // a star close enough to a pixel to matter, and a star is only drawn
            // by pixels within one cell of it. The layout is written by
            // StarCatalogue.cs; both sides must agree on it.
            static const float StarMapColumns = 1024.0;
            static const float StarMapRows = 512.0;
            static const float TwoPi = 6.28318530718;
            static const float Pi = 3.14159265359;

            // B-V through the eye's reading of colour temperature at low light:
            // blue-white, white, amber. The channel arrives normalised to 0..1
            // over the catalogue's -0.4 to 2.0 range.
            half3 StarTint(half bv)
            {
                half3 cool = half3(0.72, 0.80, 1.00);
                half3 white = half3(1.00, 1.00, 1.00);
                half3 warm = half3(1.00, 0.72, 0.48);
                return bv < 0.42h
                    ? lerp(cool, white, saturate(bv / 0.42h))
                    : lerp(white, warm, saturate((bv - 0.42h) / 0.58h));
            }

            // Distance on the sky between a pixel's uv and a star's, in radians.
            // Longitude shrinks with the cosine of latitude, so a degree of
            // right ascension is a shorter step near the poles.
            float SkyAngle(float2 uv, float2 star, float cosLat)
            {
                float du = star.x - uv.x;
                du -= floor(du + 0.5);              // wrap to [-0.5, 0.5]
                float dv = star.y - uv.y;
                float duRad = du * TwoPi * cosLat;
                float dvRad = dv * Pi;
                return sqrt(max(0.0, duRad * duRad + dvRad * dvRad));
            }

            half3 CatalogueStars(float2 uv, float cosLat)
            {
                float2 cells = float2(StarMapColumns, StarMapRows);
                float2 index = floor(uv * cells);
                half3 sum = half3(0, 0, 0);

                [unroll]
                for (int dy = -1; dy <= 1; dy++)
                {
                    float row = index.y + dy;
                    if (row < 0.0 || row > StarMapRows - 1.0) continue;

                    [unroll]
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        float col = index.x + dx;
                        if (col < 0.0) col += StarMapColumns;
                        else if (col > StarMapColumns - 1.0) col -= StarMapColumns;

                        half4 texel = SAMPLE_TEXTURE2D_LOD(
                            _StarMap, sampler_StarMap,
                            (float2(col, row) + 0.5) / cells, 0);
                        if (texel.b <= 0.0) continue;   // no star in this cell

                        float2 star = float2(
                            (col + texel.r) / StarMapColumns,
                            (row + texel.g) / StarMapRows);
                        float angle = SkyAngle(uv, star, cosLat);

                        // The blue channel is the magnitude rebased so 1.0 is the
                        // faintest catalogued star. Flux falls 2.512x per
                        // magnitude, so brightness is 10^(0.4 * 6 * b) and both
                        // size and gain follow from it with an exp2, not a log.
                        // The widest star stays inside one cell, which is what
                        // makes the nine-cell search sufficient.
                        half radius = 0.0013h * exp2(1.3h * texel.b);
                        half gain = 0.42h * exp2(3.8h * texel.b);
                        half a = angle / radius;
                        // A flat-topped core, not a peaked one: a star rarely
                        // lands on a pixel centre, and a peaked core would lose
                        // most of its brightness to the four pixels that share
                        // it. This reads as one point whatever its subpixel
                        // position.
                        half core = saturate(1.0h - a * a);
                        half halo = saturate(1.0h - angle / (radius * 3.0h));
                        sum += StarTint(texel.a) * (gain * (core + 0.10h * halo));
                    }
                }

                return sum;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float3 direction = normalize(input.direction);
                float2 uv = SkyUv(direction);

                half3 colour = SAMPLE_TEXTURE2D_LOD(
                    _MainTex, sampler_MainTex, uv, 0).rgb * _BandGain;

                half3 stars = StarLayer(direction, 300.0, 0.0035, 0.00050, 0.60, 8.0, 11.0)
                            + StarLayer(direction, 90.0, 0.0100, 0.00090, 1.10, 6.0, 37.0)
                            + StarLayer(direction, 26.0, 0.0300, 0.00180, 1.30, 4.0, 71.0);

                // The two gains are separate dials: thinning the procedural
                // field must not take Sirius with it.
                stars *= _StarGain;
                stars += CatalogueStars(uv, length(direction.xz)) * _CatalogueGain;

                return half4(colour + stars, 1.0h);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
