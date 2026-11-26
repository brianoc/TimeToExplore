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

### Step 3 — atmosphere shell (next)

* `TimeToExplore/BodyAtmosphere`: additive, front-face, depth-write-off rim
  glow with a soft sun term. `BodyView` builds one shell per atmospheric body,
  scaled by `1 + height/R`, hidden below the atmosphere gate and when the
  camera is inside it.
* Gate with `Body.Data.HasAtmosphere`, never a null test. The shell's scale
  comes from `atmosphere.heightM / body.radiusM`, both in metres, then into
  render units; keep the real height, do not inflate it.
* ADR-023 records rim-shell now, scattering later, and the detail gate.
* Tests: atmosphere scale and visibility maths, shader compile/property tests,
  `tools/check.sh --tests presentation`, `--fast`.

### Step 4 — verification and evidence

* Full `tools/check.sh` (EditMode + PlayMode) green.
* Capture Earth, Mars and Jupiter close up with the PlayMode capture suite,
  write them under `Screenshots/after/`, and report the paths and measurements.
* Clear this handoff to the no-active-handoff stub in the final commit, then
  merge `work/planet-look` into `main`; do not push until the person testing
  confirms the look.

## Verification commands

```bash
tools/check.sh --fast                 # docs, data, generators, tooling tests
tools/check.sh --tests core,presentation
tools/check.sh --tests presentation   # once the shader work lands
tools/check.sh                        # full gate before the merge
```

Captures need a graphics device; follow
[Testing and verification](Docs/technical/testing.md) for the PlayMode capture
command (no `-nographics`, `BODY_CAPTURE_DIR` set).

## Traps

* The worktree has no `.venv` of its own; it is symlinked to the main checkout.
* Unity writes `.meta` files for new textures and scripts on its first import.
  Run an EditMode gate before committing so the metas are in the tree.
* `tools/check.sh` compares a fresh offline catalogue byte for byte. Regenerate
  `TimeToExplore/Assets/Resources/solar_system.json` after touching the
  generator; never hand-edit it.
* Do not convert catalogue colours or texture colours in C#. Unity converts
  material colours; imported sRGB textures arrive linear. The albedo maps import
  sRGB through `PlanetTextureImporter`, so a second conversion darkens them. The
  shader has exactly one `SRGBToLinear`, on `_Ambient`, and
  `PlanetAppearanceTests` counts it.
* **`JsonUtility` does not keep a null nested object.** `"atmosphere": null`
  deserialises as a default `AtmosphereData` with `heightM` 0, so a runtime null
  test never fires. Use `BodyData.HasAtmosphere`. The validator checks the
  catalogue's nulls; the C# test checks the height.
* The nine maps are committed, not fetched at build time. `fetch_planet_textures.py
  --check` reads JPEG frame headers only, so it is fast and needs no network.
* The capture camera eases marker size at 8/s; wait two seconds after a
  teleport before shooting.
* **A PlayMode test needs `[Category("playmode")]` on its class.** `check.sh`
  runs the PlayMode suite with `-testCategory playmode` even on a bare run, so a
  test with no category never starts. `PlanetDetailPlayModeTests` carries it.
* `_HasAlbedo` is written only when the gate's state changes, and the material's
  initial value is the shader property's 0. If step 3 changes that default,
  `BodyView._albedoApplied` starts wrong and the first marker frame will not
  write 0.
* The detail gate reads the true `angularSize`, not `_scaleFactor`. Step 3's
  atmosphere gate should do the same; gating on the aid would give a distant
  marker a shell.
