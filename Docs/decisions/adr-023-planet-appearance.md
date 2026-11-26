---
type: Decision Record
title: "ADR-023: planet appearance, gated detail and a rim-shell atmosphere"
description: Draw detail only when the true angular radius earns it; replace the flat tint with an albedo map at arrival; add a cheap additive atmosphere shell at the real height; leave ray-marched scattering and rings for later.
tags: [decision, rendering, planets, atmosphere, textures, shaders]
status: accepted
generated: { by: opencode/deepseek-v4.1-flash, at: 2026-11-26T20:20:00Z }
---

# Context

A planet at true scale is a few pixels across for most of a session. The
catalogue's flat `_Color` is what makes it legible, but it is also all the
player ever sees unless the renderer spends more when the body is close. The
project had already made two related choices:

* [ADR-003](/decisions/adr-003-render-scale.md) gives every body a
  minimum-apparent-size **aid**, so a distant body is inflated to a click
  target. The aid is not a physical size.
* [ADR-021](/decisions/adr-021-linear-colour-space.md) moved the project to
  linear light and fixed where an authored colour converts. Any new appearance
  work has to respect that boundary.

The discussion in the session "Planet Atmosphere and Sun Glow" (2026-11-26)
settled the scope: the eight planets plus the Moon, albedo maps, and a cheap
atmosphere. A body should look like a marker until the player actually
approaches, and should not pay for detail it cannot show.

Two constraints shaped the atmosphere. Real visible-atmosphere heights are
**thin** — Earth's is 100 km on a 6,371 km radius, about 1.6% — and an
art-inflated shell would make every planet look like a cartoon. A ray-marched
single-scattering shell is the honest model, but it needs per-pixel optical
depth, a phase function, and a reason to spend the fill rate; it is not the
first pass.

# Decision

1. **Detail is gated on the true angular radius.** `BodyAppearance` owns two
   thresholds, both measured as `radius / distance` in the render frame,
   **before** the minimum-apparent-size aid. Albedo opens at **0.025 rad**
   (about a 31-pixel diameter at 1600x900 and 60°), atmosphere at **0.06 rad**
   (about 90 pixels). Gating on the aid's inflated size would give a distant
   marker a texture and a shell, which is exactly what the aid must not buy.
2. **Albedo maps replace the catalogue tint only at arrival.** The catalogue
   `color` stays the flat marker tint. `TimeToExplore/BodyLit` lerps `_Color` to
   the sampled map on `_HasAlbedo`, and both regimes share one ambient + N·L
   terminator. The maps load lazily on the first close pass, so a body the
   player never approaches never costs a 2K fetch. The maps import sRGB
   ([ADR-021](/decisions/adr-021-linear-colour-space.md)), so the shader does
   not convert the sample again.
3. **Atmosphere tier 1 is an additive rim shell at the real height.**
   `BodyView` builds a second sphere as a child of the body's visual, scaled by
   `1 + heightM / radiusM` from the catalogue's real heights. Earth's shell is
   therefore 1.0157, Venus 1.0413, Jupiter 1.0143. The shell is hidden below the
   atmosphere gate and when the ship is inside it, and an airless body gets no
   shell object. `TimeToExplore/BodyAtmosphere` draws it with
   `Blend SrcAlpha One`, depth write off, back faces culled; alpha is
   `pow(1 - N·V, k)` times a smoothstep sun term, so the glow is a limb that
   fades round the terminator. There is no scattering integral.
4. **Ray-marched single scattering and rings are deferred.** Tier 2 is a
   Rayleigh/Mie shell; rings for Saturn and Uranus are separate geometry with
   their own draw order and shadowing questions. Both belong to a later
   rendering pass, and the rim shell is cheap enough to ship first and to
   measure against.
5. **The nine albedo maps are CC BY 4.0 with attribution.** They come from
   Solar System Scope, are based on NASA data, and their attribution travels
   with the files in `Assets/Resources/Planets/ATTRIBUTION.md`. Venus uses its
   cloud-deck map rather than a surface map, because the surface is hidden.

# Consequences

**Positive**

* A marker is still one flat draw with no texture fetch, and the expensive
  appearance only appears when the disc is large enough to show it.
* The atmosphere is one extra sphere and one forward pass. The shell's real
  height keeps the silhouette honest, so a later scattering pass can replace it
  without changing the shape.
* Both gates live in a pure static class with no Unity state, so the thresholds
  and the height maths are unit-tested in edit mode.
* A planet's colour is authored once in the catalogue and read once by each
  shader, so the marker and the arrival view agree on their lighting.

**Negative**

* The detail gates are thresholds, so a body crossing one changes appearance in
  a frame. The albedo threshold is low enough that the map is already sampling
  when the disc is legible; the atmosphere threshold is high enough that the rim
  arrives on a disc that can carry it.
* The rim shell is a visual approximation. It has no real optical depth, no
  sunset colour shift, and no forward-scattering lobe, and it will not match a
  physical model at the limb.
* The shell inherits the body's spin, which is harmless for a sphere but means a
  non-spherical atmosphere would need its own transform.
* Nine 2K maps and one more shader add import time and build size.

# Alternatives considered

**Gate on the inflated marker size.** Cheapest code, because `_scaleFactor` is
already computed. Rejected: the aid exists to make a distant body clickable, and
spending a texture and a shell on a marker that is 4.5 pixels wide inverts its
purpose.

**Inflate the atmosphere height for looks.** Rejected: it makes every planet
look thicker than it is and destroys the one number the catalogue carries. The
clamp at 1.08 is a data guard, not an art control.

**Alpha-blend the shell instead of adding it.** Rejected: a rim glow is light
scattered toward the camera, which adds to the body behind it. An
alpha-over blend would darken the limb rather than brighten it.

**Ship ray-marched scattering first.** Rejected for this pass: it costs a
per-pixel march and a phase function before the cheaper look has been judged on
screen. The gates and the real height are the parts a later model inherits.

# Revisit when

* the rim shell's look has been judged on captures and the fill-rate budget for
  a per-pixel march is known,
* rings need the atmosphere's draw order or depth behaviour,
* or the marker aid changes shape, which would move the point at which the two
  gates open.
