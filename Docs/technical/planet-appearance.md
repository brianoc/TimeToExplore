---
type: Reference
title: Planet appearance
description: The albedo maps, their import settings and attribution, the detail gates that swap a flat marker for the map and the rim-glow atmosphere, and the appearance fields the body shaders consume.
tags: [technical, rendering, textures, planets, atmosphere, attribution]
status: active
generated: { by: opencode/deepseek-v4.1-flash, at: 2026-11-26T20:10:00Z }
---

# What exists now

A body has two appearance regimes. Far away it is a **marker**: the flat
`_Color` from the catalogue, no texture fetch, no atmosphere geometry. Once it
is close enough that its disc is readable, a **detail gate** opens and the body
shader samples its albedo map; a second, higher gate opens the atmosphere shell
for a body that has one. Both gates read the body's **true** angular radius.

Keeping the data first meant the catalogue, the validator and the runtime shape
settled once, before the shaders depend on them.

# The albedo detail gate

`TimeToExplore/Assets/Scripts/Rendering/BodyAppearance.cs` owns the rule. It is
a plain static class with no Unity state, so the EditMode suite tests it
directly:

```text
AlbedoMinAngularRadius = 0.025 rad
UsesAlbedo(angularRadius) => angularRadius >= 0.025
```

The input is the body's **true angular radius**, `radius / distance` in the
render frame, before the minimum-apparent-size aid. The aid inflates a distant
body to a click target; it must not earn that body a texture. A planet scaled up
a hundredfold by the aid therefore stays a flat marker, and only real proximity
opens the gate.

The threshold is deliberately low. At 1600x900 with the 60° field of view,
0.025 rad is about a **31-pixel diameter**, so the map is already sampling by
the time the disc becomes legible. A higher threshold would make the hand-over
from marker to textured sphere visible as a pop.

`BodyView` applies the gate every `Refresh` from the `angularSize` it already
computes, and writes `_HasAlbedo` only when the state changes, so a marker frame
costs no material churn. It does **not** gate on `_scaleFactor`: that value
carries the aid.

## Lazy loading

`BodyView.Initialise` never calls `Resources.Load`. On the first frame the gate
opens for a body, it loads the catalogue's `albedoTexture` key once, caches the
`Texture2D`, and sets `_MainTex`. A body the player never approaches therefore
never pays for its 2K map. A key that is present but resolves to nothing logs one
warning and leaves the body a flat marker for the rest of the session.

# The body shader

`TimeToExplore/BodyLit` gained `_MainTex` (the map) and `_HasAlbedo` (the
switch, 0 or 1):

```hlsl
half3 albedo = lerp(_Color.rgb, SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv).rgb,
                    _HasAlbedo);
half3 colour = albedo * (ambient + (1.0h - ambient) * ndl);
```

`_Color` remains the catalogue **marker tint**, and `_Ambient` is unchanged. The
map replaces the tint at arrival; it is not tinted by it and is not pre-mixed
with an average colour. Lighting is the same ambient plus N·L term in both
regimes, so a textured body and its marker share one terminator.

The texture and sampler are declared outside the `UnityPerMaterial` buffer and
`_MainTex_ST` inside it. The maps import sRGB
([ADR-021](/decisions/adr-021-linear-colour-space.md)), so the sampler delivers
linear light and the shader does not convert the sample again. The pass keeps its
`RenderPipeline` tag, `UniversalForward` light mode and target 2.0.

# Catalogue fields


Both fields are optional and are emitted for every body, `null` where absent
([Data schema](/technical/data-schema.md#appearance)):

* `albedoTexture`: a Resources key of the form `Planets/<id>_albedo`.
* `atmosphere`: `heightM`, an sRGB `color` of three numbers, and an `intensity`.

The eight planets and Earth's Moon carry a map. The Sun, the other 21 moons, the
100 asteroids and the six stations carry `null`. Mercury is the one planet with
no atmosphere; the other seven carry real visible-atmosphere heights.

The catalogue `color` remains the flat marker tint. It is not replaced by an
average of the texture, and the texture is not tinted by it.

`JsonUtility` deserialises a `null` nested object as a default instance, so the
runtime `atmosphere` is never null; `BodyData.HasAtmosphere` is the test the
renderer should use. See [Data schema](/technical/data-schema.md#appearance).

# Textures

Nine 2048x1024 equirectangular JPEG albedo maps live in
`Assets/Resources/Planets/`:

| Body | File | Catalogue key |
|---|---|---|
| Mercury | `mercury_albedo.jpg` | `Planets/mercury_albedo` |
| Venus | `venus_albedo.jpg` | `Planets/venus_albedo` |
| Earth | `earth_albedo.jpg` | `Planets/earth_albedo` |
| Mars | `mars_albedo.jpg` | `Planets/mars_albedo` |
| Jupiter | `jupiter_albedo.jpg` | `Planets/jupiter_albedo` |
| Saturn | `saturn_albedo.jpg` | `Planets/saturn_albedo` |
| Uranus | `uranus_albedo.jpg` | `Planets/uranus_albedo` |
| Neptune | `neptune_albedo.jpg` | `Planets/neptune_albedo` |
| Moon | `moon_albedo.jpg` | `Planets/moon_albedo` |

The maps come from [Solar System Scope](https://www.solarsystemscope.com/textures/)
under [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/), and are based
on NASA data. The attribution travels with the files in
`Assets/Resources/Planets/ATTRIBUTION.md`; keep it if the maps are copied or
republished.

Venus uses the atmosphere map rather than a surface map: the surface is hidden,
and the visible planet is the cloud deck.

## Fetching and checking

`tools/fetch_planet_textures.py` owns the manifest and does the download:

```bash
python3 tools/fetch_planet_textures.py            # fetch anything missing
python3 tools/fetch_planet_textures.py --force    # re-download all nine
python3 tools/fetch_planet_textures.py --check    # no network; verify the files
```

Downloads use a browser user agent, three attempts with a short backoff, and an
atomic temp-file rename. `--check` reads each JPEG's frame header to prove the
file exists, is a JPEG, is 2048x1024 and is at least 50 KB, so it needs no
imaging library and no network. `tools/check.sh --fast` runs it right after the
catalogue validation.

The committed files are the only copy the game uses; a build never downloads.

## Import settings

`Assets/Editor/PlanetTextureImporter.cs` is an `AssetPostprocessor` keyed on any
path containing `/Resources/Planets/`. It pins:

| Setting | Value | Why |
|---|---|---|
| Texture type | Default | A colour map, not a sprite or normal map |
| sRGB | on | Albedo is colour; the shader needs linear light |
| Wrap | Repeat | The equirectangular map closes at the longitude seam |
| Mipmaps | on | A distant body is a few pixels across |
| Max size | 2048 | The source resolution; no upscale, no silent halving |
| Compression | CompressedHQ | Keeps memory down without smearing the bands |

The sRGB rule is [ADR-021](/decisions/adr-021-linear-colour-space.md)'s: every
authored colour stays sRGB and converts once, at the boundary where it enters a
shader. For a texture that boundary is the import flag. A mask, normal map or
the bright-star map is data and must stay linear; these maps are colour.

Do not convert the texture's colour in C# or in the shader a second time. An
sRGB import plus Unity's material and sampler path already delivers linear
values.

# The atmosphere shell

The atmosphere is **tier 1**: a second sphere and an additive rim. It is not
scattering. The shell is a child of the body's visual, so it inherits the
body's spin and scale; only its local scale is the height ratio.

## Shell scale

`BodyAppearance.AtmosphereScale(bodyRadiusM, heightM)` returns
`1 + heightM / bodyRadiusM`, both in metres, clamped to
`[1.001, 1.08]`. The heights are the catalogue's **real** visible-atmosphere
heights, not an art-inflated shell:

| Body | Height | Shell scale |
|---|---:|---:|
| Venus | 250 km | 1.0413 |
| Earth | 100 km | 1.0157 |
| Mars | 120 km | 1.0354 |
| Jupiter | 1,000 km | 1.0143 |
| Saturn | 1,000 km | 1.0172 |
| Uranus | 800 km | 1.0315 |
| Neptune | 800 km | 1.0325 |

The clamp is only a guard against a bad catalogue record. Earth's shell is
therefore about **100 km** above the surface: the rim is thin because the real
atmosphere is thin.

## The shader

`TimeToExplore/BodyAtmosphere` is additive:
`Blend SrcAlpha One`, `ZWrite Off`, `Cull Back`, in the `Transparent` queue. It
is a URP forward pass like the body shader, with a `UniversalForward` light
mode and target 2.0.

```hlsl
half rim = pow(saturate(1 - saturate(dot(n, view))), _RimPower);
half sun = smoothstep(-0.2, 0.35, dot(n, toSun));
half alpha = saturate(rim * sun * _Intensity * _Color.a);
return half4(_Color.rgb, alpha);
```

* The rim is brightest where the shell turns away from the camera, so the glow
  is a limb rather than a disc. `_RimPower` (default 3) narrows it.
* The soft sun term fades the glow round the terminator: the day side glows and
  the night side does not get a bright outline. It is a smoothstep, not a
  hard hemisphere cut, so the join is not a visible edge.
* `_Intensity` and `_Color` come from the catalogue's `atmosphere` record.
  `_Color` is authored sRGB and Unity converts it when it submits the material;
  the shader does not convert it again.
* `_WorldSpaceCameraPos` is URP's; `_SunPosition` is the global float4
  `SolarSystemRenderer` already sets for the body shader.

## The visibility gate

The shell opens at `AtmosphereMinAngularRadius = 0.06 rad`, the body's **true**
angular radius: about a 90-pixel diameter at 1600x900 with a 60° field of view.
That is higher than the albedo gate because a thin rim reads as a smudge on a
small disc, and it is far above every minimum apparent size, so the marker aid
is already off when the shell appears.

`BodyView.Refresh` also hides the shell when the ship is inside it
(`distance > drawnRadius × shellScale`, with a small epsilon): an additive rim
seen from within would fill the view. A body with no `atmosphere` record gets no
shell object at all, and `BodyView.AtmosphereRenderer` stays null. The test is
always `Body.Data.HasAtmosphere`, never a null check on `atmosphere`.

The shell is deliberately outside `VisualRadiusUnits`, so mouse picking stays
against the body itself.

# What is deferred

**Single scattering.** A ray-marched Rayleigh/Mie shell is tier 2: it needs a
per-pixel optical depth through the atmosphere and a phase function, and it
belongs with rings and clouds in a later pass. The additive rim is the cheap
stand-in until then, and it keeps the real height so the hand-over to a proper
model does not move the silhouette.

Rings for Saturn and Uranus are separate work with their own geometry.
