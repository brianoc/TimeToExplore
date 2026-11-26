---
type: Decision Record
title: "ADR-021: Linear colour space, and one conversion boundary"
description: Move the project from Gamma to Linear, keep every authored colour in sRGB, convert once where it enters a shader, and re-tune only the maths that was nonlinear.
tags: [decision, rendering, colour, shaders, urp, technical]
status: accepted
generated: { by: opencode/deepseek-v4.1-flash, at: 2026-11-25T23:40:00Z }
---

# Context

[ADR-010](/decisions/adr-010-urp-rendering.md) left the project in **Gamma**
colour space on purpose: the URP migration was a pipeline change, not an art
pass, and a colour-space change would have muddied the before-and-after. Its
consequences section still says so.

Three things have landed since, and each one assumes linear light:

* HDR and Bloom. The default volume profile carries Bloom at a threshold of 1.0
  and Neutral Tonemapping. Both were written for linear values and were being
  fed gamma-encoded ones.
* The real bright-star catalogue writes up to about 2.4 in HDR units. In Gamma
  that number means one thing; in Linear it is a different amount of light.
* The next rendering work is atmosphere and ring shading. Single-scattering
  formulas, and the terminator they sit on, are physical models of light. Run
  in Gamma, the Lambert term darkens midtones and every scattering integral is
  wrong by a variable factor.

The cost of waiting is that each of those gets tuned against a wrong space and
then re-tuned. The cost of switching now is four shaders, about forty colour
literals, and one afternoon of look-tuning.

# Decision

1. **Set the project to Linear** (`m_ActiveColorSpace: 1`). This is a
   ProjectSettings change and reimports every texture once.
2. **Author every colour in sRGB, and convert once, at the boundary where the
   value enters a shader.** The catalogue JSON, the C# palette constants, and
   shader literals all stay as they were written. Where that boundary sits
   depends on the consumer:
   * **A material colour property.** Unity converts `_Color` from sRGB to linear
     when it submits the material, in a linear project. `BodyView`,
     `ShipView`, and the `TrajectoryLine` markers therefore pass the authored
     value through untouched; converting again would leave a body dark and
     over-saturated. This was measured, not assumed: a 0.5 grey assigned to an
     unlit material reaches the shader as 0.212.
   * **A `LineRenderer` vertex colour.** Unity does not convert these, so
     `OrbitLine` and `TrajectoryLine` apply `.linear` as they hand the palette
     over. The palette itself stays sRGB, which is also what the HUD legend
     needs.
   * **A shader literal or a float.** `BodyLit` converts its authored `_Ambient`
     with `SRGBToLinear`, and the skybox shader converts its star tints the same
     way. Floats and literals never pass through the material conversion.
3. **Leave the uGUI HUD alone.** Unity's `UI/Default` and `UI/Default Font`
   shaders call `UIGammaToLinear` on the vertex colour when the project is
   linear. Converting the palette in C# would convert it twice.
4. **Keep data textures linear and colour textures sRGB.** The bright-star map
   is already created `linear: true`; the Milky Way band is already
   `sRGBTexture: 1`. Neither changes.
5. **Re-check the maths that is genuinely nonlinear**, rather than the colours:
   the bloom threshold, the sky's star and band gains, and the alpha blends.
   Measured against before-and-after captures, the sky needed no change at all.
   A 50% orbit line now blends in linear light and so reads brighter at the
   same alpha; that is the physical result, and the alphas were left alone.

# Consequences

**Positive**

* Lighting, tonemapping, and bloom now operate on the values they were
  designed for. The terminator is a physical falloff instead of a gamma-curved
  one.
* Atmosphere and ring work can be written against standard scattering
  references instead of a corrected-for-gamma variant.
* Colour values read the same on the page and on the screen, so an sRGB hex
  from a design tool is the value that goes in.
* `tools/validate_catalogue.py` keeps its [0, 1] rule and keeps meaning it.

**Negative**

* Every texture reimports once, and any new colour texture must be imported as
  sRGB while masks and normal maps must not be. That is a rule to remember, not
  a thing the compiler checks.
* There are **three** different answers to "does this colour convert?", and
  getting one wrong is silent: a material property converts for you, a line
  vertex colour does not, and a uGUI tint converts inside the UI shader. A
  double conversion darkens and over-saturates; a missing one washes out. The
  proof for the material case is in [Rendering at solar
  scale](/technical/rendering-scale.md#colour-space).
* Semi-transparent blends (the 50% orbit lines, the dashed arcs, the HUD
  panels) mix in a different space, so some alphas read differently and are
  judged on screen.
* The sky's HDR values are the same numbers with a new meaning; its gains are
  re-tuned rather than preserved.

# Alternatives considered

**Stay in Gamma.** Cheapest today, and it keeps the existing look exactly. It
makes every later lighting feature a special case, and the first scattering pass
would have to be rewritten.

**Convert in the shaders only.** Multiply every incoming colour by a gamma
correction inside each shader. It hides the boundary, duplicates the rule four
times, and still leaves the blend and post-process maths in the wrong space.

**Move to Linear and keep the authored values as linear numbers.** This is the
"just flip the switch" path. It brightens every body colour, every orbit line,
and the HUD, and it silently changes the meaning of every stored colour. It was
rejected because the values would then mean different things in different
places.

# Revisit when

* the night side's black level needs to read above Neutral tonemapping's toe,
  which compresses the deep shadows and leaves the authored 5.5% ambient almost
  invisible,
* a platform without sRGB framebuffer support becomes a target,
* the atmosphere pass needs a wider HDR range than the R11G11B10 buffer holds,
* or a colour-management pass (a proper LUT and exposure control) is added,
  which would supersede the tonemapping half of this record.
