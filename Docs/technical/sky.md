---
type: Architecture
title: The sky
description: The baked Milky Way, the procedural stars, and the galactic frame behind the orrery.
tags: [technical, rendering, skybox, stars, milky-way]
status: draft
generated: { by: opencode/deepseek-v4.1-flash, at: 2026-11-17T00:00:00Z }
---

# What the player sees

Behind every body, marker, and line there is a sky: the broad glow of the Milky
Way rising through the frame, a warm central bulge, dark dust lanes cutting the
band, and a sparse field of stars with a handful of bright ones. It is not
decoration bolted on at the end; it is the first thing that tells the player
they are somewhere real.

The sky is a normal Unity skybox drawn by
`TimeToExplore/Skybox`, assembled at runtime by `SkyView` from

* a **generated equirectangular texture** holding only the diffuse glow
  (`Assets/Resources/Sky/milky_way.png`), and
* **procedural stars** drawn per pixel in the shader.

# Why it is generated, not painted

The band is produced by `tools/generate_skybox.py`, which sits beside the
catalogue generator and works the same way: a seed goes in, a committed asset
comes out, and anyone can regenerate it:

```bash
.venv/bin/python tools/generate_skybox.py              # 2048 x 1024, seed 20261115
.venv/bin/python tools/generate_skybox.py --width 4096
.venv/bin/python tools/generate_skybox.py --check      # stats only, writes nothing
```

GIMP or Blender could produce a prettier single frame, but neither is needed
here and both would be worse for this project:

* A hand-painted sky cannot be regenerated, diffed, or tuned by a number. A
  six-hour painting is also a much larger repository artefact than the 1.1 MiB
  texture the generator writes.
* Blender is the right tool for a modelled nebula or a planet, not for a
  direction-only background. There is no geometry to author: every pixel of a
  sky is a direction, which is exactly what a noise field can supply.
* The generator is where the astronomy lives. The galactic frame, the tilt to
  the ecliptic, and the position of the bulge are constants in code, not
  decisions locked into brush strokes.

The texture is committed because the game must load without Python. The script
is committed so the texture is never a mystery, and `tools/check.sh` regenerates
the image and compares every pixel, so a stale or hand-edited texture fails the
suite.

# Why the stars are not baked

A star in a texture is exactly one texel wide. The equirectangular texture is
2048 px around 360°, so one texel spans about 0.18°. At the prototype's 60°
vertical field of view on a 1080-pixel screen one screen pixel is 0.056°, which
makes every baked star at least three pixels across — a field of grey blobs, all
the same size, no matter how they are drawn. Pushing the texel down to a pixel
would need a 12k-wide texture, about 300 MB of video memory for a background.

So the texture carries only what is genuinely low-frequency (the glow, the star
clouds, the dust), which hides its upscaling, and the stars are drawn in the
shader: one star per lattice cell of the view direction, sized in radians. Each
star is a pixel or two at any resolution, and a faint star can be fainter
without also being bigger.

# The galactic frame

The generator uses the real sky's orientation, so the band is in the right place
relative to the ecliptic rather than at a convenient angle:

| Constant | Value |
|---|---|
| Galactic north pole | RA 192.85948°, Dec +27.12825° |
| Galactic centre | RA 266.40499°, Dec −28.93617° |
| Obliquity of the ecliptic | 23.4392911° |

Equatorial vectors are rotated into the game's ecliptic frame, whose +Y is the
ecliptic north pole, and the galactic axes are built from those two directions.
The band therefore sits about 60° from the ecliptic plane, as it does in the sky,
and the galactic centre is about 5.5° south of it.

The whole sky is then spun about the ecliptic pole by
`BULGE_AZIMUTH_DEGREES`, which is derived so the bulge lands at a chosen
azimuth. The prototype has no sidereal time — the ship is not on a rotating
Earth — so that azimuth is a composition choice: the opening camera looks along
azimuth −90°, and the bulge is placed at −86° so it is in frame when the player
presses Play. The tilt is astronomy; the azimuth is art direction, and the code
says so.

Two consequences are deliberate:

* **No parallax.** Stars are at infinity, so moving from Earth to Neptune does
  not shift them. The parallax of even the nearest star is a fraction of an
  arcsecond, far below anything this renderer resolves.
* **No proper motion or precession.** The sky is fixed for the whole game. The
  Milky Way does not rotate over a 4-day flight, and a time warp to the year
  2100 should not drag it around.

# What is baked

`render()` builds a periodic, isotropic Gaussian random field once per
frequency band (a white-noise cube filtered in the Fourier domain to a
$k^{-\beta}$ power spectrum) and samples it on the sphere, which is seamless
because it is a function of the 3D direction, not of longitude and latitude.
That avoids a visible seam at the map's wrap.

The band is then composed from:

| Term | Shape | Purpose |
|---|---|---|
| Core and halo | Gaussians in galactic latitude, σ 0.105 rad and 0.34 rad | The narrow bright lane inside a wide glow |
| Bulge | Gaussian around the galactic centre, σ 0.42 rad | The central glow |
| Longitude weighting | Bulge plus the Cygnus and Carina star clouds | The band is not equally bright all the way round |
| Clouds | `exp(0.62 × structure)` | Multiplicative, so it never clamps into a flat patch |
| Grain | An FFT field plus per-pixel speckle at texture resolution | Unresolved star clouds |
| Dust | Soft filaments where a noise field crosses zero, confined to the plane | The dark lanes, which dim the band rather than cut holes in it |

The luminance is normalised so its 99.9th percentile equals `BAND_PEAK`, then
written as an 8-bit sRGB PNG. Normalising means the peak does not drift when the
seed changes; the look stays the same while the clouds move.

The image is imported as an **uncompressed**, non-mipmapped, sRGB texture with
repeat wrapping, and the shader samples it at **LOD 0**. Block compression would
put 4×4 artefacts in the dark gradients, and mip selection would pick the
smallest mip at the wrap seam, drawing a bright vertical line down the sky.

# What the shader draws

`StarLayer` treats the view direction as a point in a cubic lattice of side
`1 / scale`, and gives each cell one candidate star:

1. hash the cell to three pseudo-random numbers;
2. most cells fail the existence test and return immediately;
3. the survivor's direction is the cell centre jittered inside the cell, so the
   field is irregular;
4. its magnitude is a power of one hash component, its colour a blend from
   blue-white to amber, and its angular radius grows slightly with magnitude;
5. the drawn brightness is a tight core plus a faint halo.

An existing star is more likely inside the bright band: the shader samples the
baked texture at the star's direction and raises the existence threshold by the
local luminance. That is the one place the two halves of the sky talk to each
other, and it costs one texture sample only on cells that survive step 2.

Three layers are summed:

| Layer | Lattice scale | Existence | Radius (rad) | Gain | Look |
|---|---:|---:|---:|---:|---|
| Dust | 300 | 0.35% | 5.0 × 10⁻⁴ | 0.60 | ~1 px, barely visible |
| Field | 90 | 1.0% | 9.0 × 10⁻⁴ | 1.10 | ~2 px, the common star |
| Beacons | 26 | 5.0% | 1.8 × 10⁻³ | 2.20 | ~4 px, the few that draw the eye |

The lattice is far below one pixel at these scales, so the pattern reads as
random. `_BandGain` and `_StarGain` on the material scale the two halves without
regenerating anything.

# Cost and appearance

One skybox pass, one texture fetch for the band, and at most one fetch per layer
where a star exists. The texture is 2048 × 1024, about 6–8 MiB in memory
uncompressed. The pipeline already renders with HDR off, so a bright star clips
to white instead of blooming; that is acceptable for the prototype and is the
reason the beacons stay small. [Post-processing](/technical/rendering-scale.md)
can add bloom later, and the sky will take it well.

The band's peak is deliberately below the brightness of the HUD panels and the
markers. If a label ever gets lost over the bulge, lower `BAND_PEAK` in the
generator or set `_BandGain` below 1 on the material; do not repaint the sky.

# Verification

`SkyboxTests` checks that the committed texture is present at 2048 × 1024 with
no mip chain, that it is dark overall but has a bright band covering part of the
sky, that the shader compiles, that `SkyView` puts the camera on `Skybox` and
points `RenderSettings.skybox` at the material, that destroying the view restores
what was there, and that a missing texture or shader is refused rather than
half-applied. `BootstrapSmokeTests` asserts the real composition root ends up
with the sky applied.

The look itself is judged from the generator's statistics and a perspective
preview, not from a test. The camera is still the authority: if the band is in
the wrong place when Play is pressed, `BULGE_AZIMUTH_DEGREES` is the dial.

# What is not here yet

* **Real bright stars.** The field is procedural, so Sirius and Vega are not
  where Sirius and Vega are. A small catalogue of the brightest few hundred
  stars, drawn as a fourth layer from real RA/Dec/magnitude, would make the sky
  navigable and is the natural next step.
* **Milky Way structure from a survey.** The band is a plausible model, not the
  Gaia map. A real equirectangular survey image would slot into the same
  texture path.
* **Twinkle and exposure.** No atmosphere, no camera exposure model, and no
  time-varying scintillation. All are local-view effects; none belong at solar
  scale.

The load-bearing rules — double simulation, ship-relative rendering, minimum
apparent size ([Rendering at solar scale](/technical/rendering-scale.md)) — are
untouched. The sky is a direction, not a position, and every ship-relative
transform leaves it alone.
