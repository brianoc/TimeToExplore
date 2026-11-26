---
type: Reference
title: Planet appearance
description: The albedo maps, their import settings and attribution, and the appearance fields the body shader and atmosphere shell will consume.
tags: [technical, rendering, textures, planets, atmosphere, attribution]
status: active
generated: { by: opencode/deepseek-v4.1-flash, at: 2026-11-26T17:00:00Z }
---

# What exists now

The catalogue carries the appearance data and the project ships the texture
files. Nothing draws them yet: `TimeToExplore/BodyLit` is still a flat
`_Color` shader, and no atmosphere geometry exists. The renderer's detail gate
— the rule that swaps the flat marker for the albedo map only when the true
angular radius is large enough — and the additive rim shell are the next steps.

Keeping the data first means the catalogue, the validator and the runtime shape
settle once, before two shaders depend on them.

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

# What the next steps add

* **Detail gate.** The body shader gains `_MainTex` and `_HasAlbedo`, and
  `BodyView` loads the map lazily on the first close pass. The gate opens on the
  body's **true angular radius**, not the inflated minimum marker size, so
  distant bodies stay flat and cheap.
* **Atmosphere shell.** A second sphere at `1 + heightM / radiusM`, additive,
  depth-write off, rim-weighted by `pow(1 - dot(N, V), k)` and multiplied by a
  soft sun term. Mercury gets none; every planet with an `atmosphere` record
  gets one.

Neither exists yet, and this page should not claim otherwise until they land.
