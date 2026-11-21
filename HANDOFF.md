# Handoff

**Active plan:** remove the solar system view and stabilise the camera.

## Done

- Design decisions settled with the user:
  - one orbit camera, one zoom range (2,000 km to 12 billion km);
  - the top-down map pose, map scene, map switch, map pan, marker/label map
    rules and map tests are removed;
  - cursor-anchored zoom and `Space` home stay;
  - flight/route/jump framing stays, but auto-distance is captured once instead
    of re-targeted every frame, and body avoidance becomes continuous.
- Step 1 (code): the map pose, scene, switch, pan, map marker/label/line rules
  and map tests are removed. `MapView` split into `CursorZoom` (pointer anchor)
  and `LocalScale` (dominant body for `Space`). `OrbitCamera` owns the single
  12-billion-km range and near/far clipping. `presentation` and `smoke` suites
  pass.

## Next

1. Step 2: smooth the framing and body avoidance; add continuity tests.
2. Step 3: docs, log, verification, screenshots.

## Verify

```bash
tools/check.sh --fast
tools/check.sh --tests presentation
tools/check.sh
```

Last result: not run yet.

## Traps

- The open editor in the main checkout must not be touched; `check.sh` uses
  `.cache/test-project`.
- `MapView` also owns the cursor-anchor maths and the dominant-body choice.
  Delete the map, but move the live cursor-anchor maths and dominant-body
  choice into their own small classes rather than deleting them.
- `Tools > Create Scenes` rebuilds `SystemMap.unity`; remove that path too.
