---
type: Glossary
title: Glossary
description: The words the design bundle and the code share, starting with the two camera views.
tags: [reference, glossary, camera, terminology]
status: active
generated: { by: opencode/deepseek-v4.1-flash, at: 2026-11-17T00:00:00Z }
---

# Glossary

Terms used across the design bundle and the code. When a document and the code
disagree, the concept that owns the term wins; this page only points at it.

## Local view

The close camera around the ship, below the dominant body's sphere of influence.
The player's yaw and pitch are the angle, the ship sits at the render origin
([ADR-002](/decisions/adr-002-double-precision.md)), and the view is always
ship-centred. Bodies outside the local neighbourhood fade to their true-scale
dots; the **T** toggle forces true scale everywhere.

The local view is one end of the single zoom, not a mode: it is the camera when
`w = 0` in [the system map](/technical/system-map.md#one-zoom-two-readings-of-the-same-camera).

Related: **orbit camera** ([ADR-008](/decisions/adr-008-orbit-camera.md)) is the
mechanism; **local view** is what the player sees when it is close.

## Solar system view

The top-down reading of the same camera: as the zoom crosses the local sphere of
influence, the pose blends to looking straight down the ecliptic pole, left-drag
pans, and the range opens to 12 billion km. It is also called the **map view**
or **the map** in code and in the system-map concept. Markers keep a 6 px
minimum so every planet and station stays clickable, the ship is labelled
"YOU ARE HERE", and the planet orbit lines thicken from their local 3 px to a
10 px coloured stroke without changing hue.

It is not a second camera and not a coordinate space: the world stays
ship-relative and only the pose changes ([ADR-009](/decisions/adr-009-map-camera-pan.md)).
The solar system view is `w = 1`.

## Zoom blend (`w`)

The continuous weight between the two views, from the camera distance `d` and
the dominant body's sphere-of-influence radius `r`:

```text
x = clamp(ln(d / r) / ln(3), 0, 1)
w = x²(3 − 2x)
```

`w = 0` is the local view, `w = 1` the solar system view. The pitch and the
rotation are read through `w`, so no mode switch and no saved pose are needed.

## Dominant body and neighbourhood

The **dominant body** is the smallest sphere of influence that contains the ship
(the Moon inside the Earth, for example). Its SOI radius is the local scale `r`,
also called the **neighbourhood**. Deep space, outside every SOI, falls back to
1 million km.

## Zoom tween

A wheel notch **requests** a camera distance; the camera eases to it over a
short exponential tween (`OrbitCamera.EaseDistance`, 7/s by default) rather
than jumping there. Because the blend weight is derived from the camera
distance, the solar system view's top-down tilt and the rotation sweep with the
same tween instead of snapping. Directly assigning a distance (a reset, a
snap-to, a test) places the camera immediately.

## See also

* [Selection and camera focus](/technical/selection-and-camera.md) — the camera as built, and the debug jump.
* [The system map and route preview](/technical/system-map.md) — the blend, pan, and route preview.
* [ADR-008](/decisions/adr-008-orbit-camera.md) and [ADR-009](/decisions/adr-009-map-camera-pan.md) — why the camera is an orbit and how the map bends it.
