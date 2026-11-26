# Handoff

**Linear colour space — finished, waiting on the check (2026-11-26).**
[ADR-021](Docs/decisions/adr-021-linear-colour-space.md) carries the reasoning;
the branch is `work/linear-colour-space`.

## Done

- `ProjectSettings.asset`: `m_ActiveColorSpace: 1`.
- The three conversion boundaries, each proven rather than assumed:
  * material `_Color` — Unity converts it; the C# passes the authored value
    through (`BodyView.ShaderColourFrom`, `ShipView`, the trajectory markers).
  * `LineRenderer` vertex colours — not converted; `OrbitLine` and
    `TrajectoryLine` apply `.linear`, and the palettes stay sRGB for the HUD.
  * uGUI — `UI/Default` converts the vertex tint; the HUD palette is untouched.
- Shaders: `BodyLit` converts `_Ambient` with `SRGBToLinear`; the skybox converts
  its star tints. Both include core `Color.hlsl`.
- `ColourSpaceTests` (EditMode, `presentation`) and `BodyCaptureTests`
  (PlayMode, `capture`).

## Next

1. `tools/check.sh` must be green. Then merge to `main`, remove this worktree and
   the temporary baseline worktree, and clear this file.
2. Show the before/after captures in the chat and ask for confirmation before
   any push.

## Traps

- **Converting a material colour twice is silent.** `.linear` on a value passed
  to `material.SetColor` darkens and over-saturates. A scratch probe measured a
  0.5 grey arriving as 0.2122 with no `.linear` and 0.0389 with it.
- **The capture path is not the screen.** URP's HDR blit into a RenderTexture
  applies no sRGB write: the capture uses a linear half-float target and encodes
  in C#, or the PNG reads dark and bands.
- **The marker-size aid eases at 8/s.** A teleport needs about two seconds of
  frames before a body's scale settles, or the camera ends up inside it.
- **The Sun portrait is missing.** `BodyCaptureTests` covers Earth, Mars, and
  Jupiter; a Sun framed at 1,741 units did not appear through the capture
  camera although culling reported it visible. The game's own Sun is untouched by
  this branch; give the Sun its own framing if a portrait is wanted.

## Verify

```bash
tools/check.sh                    # the gate for the C# change
tools/check.sh --tests presentation
```

Captures: run `BodyCaptureTests` without `-nographics` through
`tools/unity_guard.py` with `BODY_CAPTURE_DIR` set, as its class comment says.
