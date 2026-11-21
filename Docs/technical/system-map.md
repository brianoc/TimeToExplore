---
type: Design Document
title: The system map (removed)
description: A historical record of the top-down system map and its comparison scene, removed on 2026-11-21. The current camera is one orbit camera documented in Selection and camera focus.
tags: [technical, camera, navigation, historical]
status: superseded
generated: { by: opencode/deepseek-v4.1-flash, at: 2026-11-21T22:00:00Z }
---

# This feature was removed

The top-down system map was a development comparison layer. It blended the
orbit camera to a fixed 90° pose above the ecliptic as the zoom crossed the
local sphere of influence, added a map-only pan, a 12-billion-km ceiling, a
separate comparison scene, a per-view marker and label density, and a map-only
orbit-line width. It did not ship as a gameplay mode, and on **2026-11-21** it
was removed along with `MapView`, `SystemMap.unity`,
`SolarSystemBootstrap.EnableMapView`, the map tests, and every map-only drawing
rule. The reasons and alternatives are recorded in
[ADR-018](/decisions/adr-018-one-orbit-camera.md).

The camera is now one orbit camera with one range and one pose. Its behaviour is
documented in
[Selection and camera focus](/technical/selection-and-camera.md#the-camera);
the route preview that grew out of this work stays and is documented in the same
place and in [The flight session](/technical/flight-session.md).

This page is kept only so the earlier log entries and decisions still resolve.
It is not a description of the current design.
