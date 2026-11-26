# Handoff

**Active plan (2026-11-26): planet appearance — albedo textures and rim-shell
atmospheres.** Branch `work/planet-look`. The project is now in Linear colour
space ([ADR-021](Docs/decisions/adr-021-linear-colour-space.md)), so this is the
first art pass written for linear light. Scope agreed with the person testing:
**eight planets plus the Moon, 2K/1K albedo maps, and the cheap rim-glow
atmosphere shell. Ray-marched single scattering and maps for the other moons are
follow-ups.**

The earlier discussion is in the session "Planet Atmosphere and Sun Glow"
(2026-11-26). Its key points, kept here so the plan does not depend on session
history:

* A body has two regimes. Below a few tens of pixels it is a marker: flat
  catalogue colour, no texture fetch, no shell. At arrival it earns albedo and
  an atmosphere. Gate on the **true angular radius**, not the inflated marker.
* Textures are equirectangular albedo maps, loaded through a key in the
  catalogue so the loader stays data-driven. Do not bake colour into the map;
  the catalogue colour remains the marker tint.
* Atmosphere tier 1 is a second sphere at `1 + height/R`, additive, rim
  `pow(1 - dot(N,V), k)`, multiplied by a soft sun term. Tier 2 is a
  ray-marched Rayleigh/Mie shell; defer it.
* Earth's visible atmosphere is 100 km on a 6,371 km sphere; keep real heights,
  do not inflate for looks.
* The Sun's own bloom halo is separate work, not in this plan.

## Steps

### Step 1 — appearance data and textures (done, committed)

* Catalogue gains optional `albedoTexture` and `atmosphere { heightM, color,
  intensity }`; the generator emits them, the validator checks them, and
  `BodyData` reads them. Nine 2K maps are committed under
  `Assets/Resources/Planets/` with the Solar System Scope CC BY 4.0
  attribution.
* `tools/fetch_planet_textures.py` downloads the maps and has a no-network
  `--check` used by `tools/check.sh --fast`.
* `Assets/Editor/PlanetTextureImporter.cs` pins sRGB, Repeat, mipmaps, 2048 px
  and high-quality compression.
* Tests: `PlanetAppearanceTests` (presentation), `test_validate_catalogue.py`,
  `test_fetch_planet_textures.py`.
* Design: [Planet appearance](Docs/technical/planet-appearance.md) and the
  appearance section of [Data schema](Docs/technical/data-schema.md).

Step 1 left no renderer change: `BodyLit` is still flat, and nothing builds an
atmosphere shell.

### Step 2 — textured body shader and detail gate (done, committed)

* `BodyAppearance` holds `AlbedoMinAngularRadius = 0.025 rad` and
  `UsesAlbedo`, measured on the body's **true** angular radius, not the aid.
* `BodyLit` gained `_MainTex` and `_HasAlbedo`; the fragment lerps the marker
  `_Color` to the sampled map and lights the result with the same ambient +
  N·L term. The sRGB sample is not converted again.
* `BodyView` loads the map lazily on the first close pass, warns once on a
  missing key, and writes `_HasAlbedo` only on a state change.
* Tests: three `PlanetAppearanceTests` cases (gate, shader properties, shader
  source) and `PlanetDetailPlayModeTests` (0.4 rad Earth is textured, a distant
  Earth is flat).
* Design: the detail-gate and shader sections of
  [Planet appearance](Docs/technical/planet-appearance.md), and the materials
  section of [Rendering at solar scale](Docs/technical/rendering-scale.md).

### Step 3 — atmosphere shell (done, committed)

* `BodyAppearance` gained `AtmosphereMinAngularRadius = 0.06 rad`,
  `UsesAtmosphere`, `MinAtmosphereScale`/`MaxAtmosphereScale` and
  `AtmosphereScale(radiusM, heightM)`. The height is the catalogue's real one,
  and the clamp is only a guard: Earth 1.0157, Venus 1.0413, Jupiter 1.0143.
* `TimeToExplore/BodyAtmosphere` is an additive rim: transparent queue,
  `Blend SrcAlpha One`, `ZWrite Off`, `Cull Back`, `pow(1 - N·V, k)` times a
  soft sun smoothstep, no scattering integral.
* `BodyView` builds one shell per atmospheric body as a child of the visual,
  scaled by the height ratio, and shows it above 0.06 rad when the ship is
  outside it. `AtmosphereRenderer` is null for an airless body.
* ADR-023 records the pass, with scattering and rings deferred.
* Tests: `PlanetAppearanceTests` (gate, scale, shipped planets, shader source and
  properties), `RenderingPipelineTests` (URP tag and compile),
  `PlanetDetailPlayModeTests` (Earth close, Earth far, Mercury none).

### Step 4 — verification and evidence (next)

* Full `tools/check.sh` (EditMode + PlayMode) green.
* Capture Earth, Mars and Jupiter close up with the PlayMode capture suite,
  write them under `Screenshots/after/`, and report the paths and measurements.
  Earth and Mars should show a rim; Jupiter's is faint (1.0143) and Mercury has
  none.
* Clear this handoff to the no-active-handoff stub in the final commit, then
  merge `work/planet-look` into `main`; do not push until the person testing
  confirms the look.

## Verification commands

```bash
tools/check.sh --fast                       # docs, data, generators, tooling tests
tools/check.sh --tests presentation,playmode
tools/check.sh                              # full gate before the merge
```

Captures need a graphics device; follow
[Testing and verification](Docs/technical/testing.md) for the PlayMode capture
command (no `-nographics`, `BODY_CAPTURE_DIR` set).

## Traps

* The worktree has no `.venv` of its own; it is symlinked to the main checkout.
* Unity writes `.meta` files for new textures, scripts and shaders on its first
  import. Run an EditMode gate before committing so the metas are in the tree;
  `BodyAtmosphere.shader.meta` is new in this step.
* `tools/check.sh` compares a fresh offline catalogue byte for byte. Regenerate
  `TimeToExplore/Assets/Resources/solar_system.json` after touching the
  generator; never hand-edit it.
* Do not convert catalogue colours or texture colours in C#. Unity converts
  material colours; imported sRGB textures arrive linear. The shader has exactly
  one `SRGBToLinear`, on `_Ambient`, and `PlanetAppearanceTests` counts it. The
  atmosphere shader has **none**: its `_Color` goes through the material.
* **`JsonUtility` does not keep a null nested object.** `"atmosphere": null`
  deserialises as a default `AtmosphereData` with `heightM` 0, so a runtime null
  test never fires. Use `BodyData.HasAtmosphere`.
* The nine maps are committed, not fetched at build time.
  `fetch_planet_textures.py --check` reads JPEG frame headers only.
* The capture camera eases marker size at 8/s; wait two seconds after a
  teleport before shooting.
* **A PlayMode test needs `[Category("playmode")]` on its class.** `check.sh`
  runs the PlayMode suite with `-testCategory playmode` even on a bare run.
* `_HasAlbedo` is written only when the albedo gate's state changes, and the
  material's initial value is the shader property's 0.
* Both gates read the true `angularSize`, not `_scaleFactor`. The 0.06 rad
  atmosphere gate is above every minimum apparent size, so the aid is off by the
  time a shell appears; do not lower it or a marker will gain a shell.
* The shell is a child of `_visual`, not of the body root, so it inherits the
  body's spin and scale. Its local scale is the ratio only; do not set the
  world scale.
* `SolarSystemRenderer` sets the `_SunPosition` global **after** it refreshes
  the views, so the shell's first enabled frame lights from the previous
  frame's sun position. That is one frame of a stale direction, and it only
  matters if the sun moves a long way in one step.
* `AtmosphereRenderer` is null for Mercury and every airless body, so any new
  call site must null-test it rather than the `atmosphere` object.
