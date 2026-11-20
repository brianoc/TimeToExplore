---
type: Roadmap
title: Roadmap
description: What exists in the prototype, what comes next, and the risks that could change the plan.
tags: [production, roadmap, milestones]
status: draft
generated: { by: opencode/deepseek-v4.1-flash, at: 2026-11-19T00:00:00Z }
---

# Current state — milestone 0

**Done: a true-scale, moving orrery with a debug camera.**

* Generated catalogue: 1 star, 8 planets, 22 major moons, 100 main-belt
  asteroids, 6 Lagrange stations.
* JPL-derived planet and asteroid elements, IAU physical data.
* Keplerian propagation in double-precision SI units.
* Lagrange-point station positions computed from the moving pair.
* Ship-relative rendering at 1 unit = 1,000 km.
* Universal Render Pipeline 17.3.0, with a project pipeline asset, a minimal
  forward renderer, and four small runtime shaders (lit bodies, unlit markers,
  vertex-coloured lines, and the Milky Way skybox).
* Input System 1.20.1 behind one `PlayerControls` gameplay seam and one
  `UiInput` uGUI module, with Active Input Handling **New** only; no runtime code
  reads the legacy backend.
* Minimum-apparent-size markers and a true-scale toggle.
* Time warp from 1× to 1,000,000× and pause.
* Planet and moon orbit lines.
* A generated Milky Way sky in place of the black clear: a baked band texture
  from `tools/generate_skybox.py`, procedural stars in the shader, and `SkyView`
  owning and restoring the camera's clear flags
  ([The sky](/technical/sky.md)).
* Free-flight debug camera and a runtime-built uGUI HUD with labels, panels,
  and the details screen.
* HUD and input migration done: uGUI replaced IMGUI
  ([ADR-013](../decisions/adr-013-ugui-hud.md)), Active Input Handling moved to
  **New** only, and `InputSeamTests` allows the Input System in exactly the seam
  and the UI module.
* Left-click body selection with a live details screen (physical, orbital, and
  current data) and a three-second eased debug camera move to the selected body.
* A **transfer-conic arrival** as an opt-in planner style: a universal-variable
  state-vector propagator, an `IArcPath` contract, a coasted hyperbola, an
  impulsive Oberth capture, a transfer ellipse, and a circularisation burn
  ([ADR-014](../decisions/adr-014-keplerian-coast.md)). The quintic capture stays
  the default.
* A **hybrid fixed-thrust arrival** as the third style: one long capture burn at
  a player-set level from 0 to 1 g, an impulse trim onto the exact transfer
  ellipse, and the conic parking radius, with the throttle stepping instead of
  blending ([ADR-015](../decisions/adr-015-fixed-thrust-burns.md)). At 0 g it is
  the conic plan.
* A **Lambert coast cruise** and the **burn–coast–burn** profile: a zero-rev
  prograde `LambertSolver`, a heliocentric `ConicArc` coast, a pinned departure
  break at `1.02 × SOI` at escape speed, a forward departure burn, and a brake
  reverse-placed from the capture chain's entry state. The nav computer's
  `PROFILE` row selects Powered (the default), Coast, or Burn-coast-burn, with
  the trip-time slider as that profile's speed control
  ([Burn–coast–burn trips](/simulation/burn-coast-burn.md)). Planning it is
  two-tier: a fast, search-only central-body model chooses the duration, the
  returned plan and the flight stay full-field, a 5% screen certifies near-limit
  probes, the floor and window are cached, and the slider retimes on a
  latest-wins background worker
  ([Planning performance](/technical/planning-performance.md)).
* Shipped scene at `Assets/Scenes/SolarSystem.unity`, an editor menu to rebuild
  it, and a bootstrap that also self-installs in an empty scene.
* Automated verification: `tools/check.sh` runs the OKF validator, the catalogue
  validator, a byte-for-byte catalogue check, a pixel-for-pixel sky check, and
  the Unity EditMode suite.

**Acceptance:** open the project, press Play, see Earth and the Sun, warp time,
watch the planets move, click a planet for its details, and jump to it in debug
flight to see the scale. Gateway Station sits at the Earth–Moon L1 point.

# Milestone 1 — the flight core (done)

**Goal: fly a real 10 m/s² burn from Gateway to Mars and arrive in a stable
orbit, with no scene.**

Done in `Assets/Scripts/Simulation/`, verified headlessly by the EditMode suite:

* `ShipState` in double precision, with mass carried as bookkeeping.
* Continuous gravity from every catalogue body, evaluated without mutating the
  model ([ADR-005](../decisions/adr-005-continuous-gravity.md)).
* Adaptive RK4 that advances to a Julian date, so frame rate and time warp do
  not change the result.
* A Go To planner: escape, cruise, and capture arcs, throttleable thrust, exact
  arrival on the parking orbit
  ([ADR-006](../decisions/adr-006-quintic-plan.md)).
* An autopilot that tracks the plan with light position and velocity feedback.
* A circular equatorial parking orbit per body, with a gravity ceiling that
  lifts the arrival orbit until local gravity is 40% of the drive.
* The field, integrator, quintic, parking orbit and its ceiling, SOI, planner,
  and end-to-end flight to Mars, Jupiter, and back to Earth are covered by the
  new tests.

**Acceptance met:** plan Gateway→Mars, fly it, and arrive within 0.01 m and
0.0000 m/s of the parking orbit, then hold the orbit for three orbits at an
eccentricity of about 3 × 10⁻⁶. Venus, Mars, Saturn, Jupiter, the Moon, and a
departure from low Earth orbit are all feasible. Jupiter arrives in 10.3 days
at 177,965 km, where the ceiling puts gravity at 4 m/s² instead of the 19.6
m/s² of a surface-radius orbit.

**Known gap:** the plan is 35–75% slower than the ideal bang-bang profile. Mars
takes 4.4 days for 1.32 AU instead of 3.3.

# Milestone 2 — fly it in the scene (done)

**Goal: press Go To on Mars and watch the ship do it.**

Done in `Assets/Scripts/` and verified through the scene's frame loop:

* `FlightSession` owns the `ShipState`, the active plan, the autopilot, and the
  integrator ([ADR-007](../decisions/adr-007-flight-director.md)). The bootstrap
  advances the clock and asks the session to reach the same date.
* The ship starts at Gateway Station, so the default trip is the tested one.
* **Go To** on the details screen calls `TripPlanner.Plan`, raises the warp to a
  ~30-second trip, and flies it.
* `TrajectoryLine` draws the sampled path as one coloured line per arc by path
  type — gold quintic, blue dashed conic, red fixed burn, shaded within a run of
  one type — with a flip marker at the thrust reversal and an arrival marker.
* The flight panel is a phase table — departure, cruise, capture with dates and
  durations, arrival date, flight time, peak thrust and throttle, delta-v — plus
  the live phase, next-event countdown, throttle, and warp.
* `WarpPolicy` drops the warp by clearance, local orbital timescale, and the next
  plan event, and the status panel says when it is holding the clock.
* The frame has a 2,000-substep budget; if it runs out the clock is clamped to
  the date the ship reached, so the world never runs ahead of it.
* After arrival the drive goes off, the ship coasts, and the ship comes back
  under the player: an orbit camera (right-drag, wheel zoom) that frames the
  destination automatically until they take it ([ADR-008](../decisions/adr-008-orbit-camera.md)).

**Acceptance met:** Gateway → Mars, 4.44 days, arrives 0.0 m and 0.0000 m/s from
the parking orbit, holds 3,898 km at e = 3.07 × 10⁻⁶ for three orbits, and does
it in 65.7 real seconds over 3,939 frames at the suggested 10,000×. Peak 63
substeps in a frame; warp auto-drops to 10× for the last second. No teleport.

**Known gaps:** the warp drop is in power-of-ten steps; the ship is a marker
rather than a model; the camera can still be orbited inside a planet (it is
pulled out, but as a rescue); there is no manual thrust; and a second Go To
replans from scratch rather than correcting the existing plan.

# Milestone 2.5 — the system map and route preview (done)

**Goal: one continuous zoom from the ship to the whole system, a top-down map
the player can pan and click, and a route they can see before they fly it.**

Done in `Assets/Scripts/Player/`, `Assets/Scripts/UI/`, and the rendering layer.
The design is [The system map and route preview](/technical/system-map.md); the
camera decision is [ADR-009](../decisions/adr-009-map-camera-pan.md). In order:

1. The zoom-driven blend from the local orbit view to a top-down map at the
   local sphere of influence, with the remembered angle restored on the way
   back in, and panning on the map. **Steps 1–2 are done:** the plain-C#
   `MapView` holds the blend, the dominant-body choice, the 12-billion-km
   range, the map pose and clipping, and the 60 AU pan cap; `ShipCameraRig`
   blends the pose, keeps right-drag as orbit, routes left-drag to pan on the
   map, scales the pan from the camera distance, eases it home on re-entry,
   suspends auto-framing on the map, and routes the wheel through the extended
   range. `SystemMap.unity` and
   the `EnableMapView` switch provide the side-by-side comparison.
2. Map reading rules: bigger markers, label density by zoom, class-priority
   picking, the map-only ship label, and the camera-distance orbit width. **Step
   3 is done:** `MarkerSizing` eases markers from 4.5 to 6 px, `BodyLabels`
   applies the 5× parent-SOI moon window and the hover/selection-only
   asteroids, `BodyPicker` ranks Sun/planet/station above moons and asteroids
   on the map, and `OrbitLine` stops a distant orbit becoming a ribbon at close
   map zoom.
3. The local view stops inflating distant bodies; the marker aid becomes
   neighbourhood-aware. **Step 4 is done:** `MarkerSizing.AidStrength` keeps
   the aid full inside the dominant SOI, fades it to true scale by 3× along the
   camera blend's log-distance band, lets the camera distance widen the
   neighbourhood, and restores the full aid on the map.
   `SolarSystemRenderer` applies the per-body strength, and `EnableMapView`
   gates the fade so the shipped scene keeps its old look during the
   comparison.
4. **Plot route** draws the plan as a smooth preview curve with an arrival
   ghost; **Begin** replans from the current state and flies it. The preview
   is a promise of shape, not a contract: time passes between Plot and Begin.
   **Step 5 is done:** `FlightSession.Plot` holds a preview on a copy of the
   state without moving the ship; a 1% staleness window ages the panel and
   **Replot** refreshes it; `TrajectoryLine` draws a ghost at the target's
   arrival position; the details footer splits **Plot route** and **Begin
   flight**, and the flight panel shows the PREVIEW rows. **Step 6 is done:**
   `BootstrapSmokeTests` drives the real bootstrap from the map ceiling through
   select, plot, Begin, and arrival in 3,939 frames, reproducing the scene
   flight exactly (2026-11-19 10:29:02, 0.0 m, 0.0000 m/s, 3,898 km, e = 4.3 ×
   10⁻⁹). **Step 7 is done:** [ADR-009](../decisions/adr-009-map-camera-pan.md)
   records the blended map and the `ship + pan` look-at, and the camera,
   flight-session, testing, and index concepts carry the finished layer.
   **Post-2.5:** the preview carries a **trip-time slider**, so the player can
   trade a longer trip for a gentler burn and less delta-v before pressing
   Begin. It is a duration control over the existing torch plan. Beside it, the
   **arrival-style** buttons switch between the fast quintic capture and the
   cheap conic coast, so the time/delta-v choice is now in the nav computer. The
   **`PROFILE`** row later added the unpowered `Coast` and the
   `Burn-coast-burn` crossing, which closed the deferred coast-in-the-middle
   item below.

**Acceptance met:** one wheel gesture climbs from the Gateway neighbourhood to
the 12-billion-km map ceiling with the pose top-down (the distance and the tilt
are eased, not snapped), and the reverse gesture returns to the exact remembered
yaw, pitch, and rotation. From the map, Mars can
be selected and plotted; the curve and the arrival ghost draw without flying,
and Begin replans and arrives on the 3,898 km parking orbit in 4.44 days with
0.0 m and 0.0000 m/s, 3,939 frames and 65.7 real seconds — the same numbers as
the scene flight. The map's pan cap allows a browse to Jupiter's neighbourhood,
class-priority picking lets the planet win an overlapping hit, and the local view
fades distant bodies to true scale outside the Moon's sphere of influence; the
first two are unit-tested, and a real pointer click still needs a human frame.

**Known gaps:** the `EnableMapView` comparison switch still gates the map (off in
the shipped scene), the zoom tween is unit-tested but its feel and real pointer
hits still need a PlayMode pass, the map keeps the player's yaw rather than
offering north-up, and the 60 AU pan cap and 12-billion-km ceiling are fixed
constants.

# Milestone 3 — fuel and the delta-v economy

* Charge `PlanArc.DeltaVMps` against a finite budget.
* Make acceleration mass-dependent, or hold it constant by throttling.
* Refuse a plan the budget cannot pay, and offer the cheap coasting alternative.
* Add the fuel UI and the end-of-fuel failure state.

**Open design choice:** constant acceleration with decreasing mass (more honest)
versus constant acceleration with throttling (easier to fly). The current
preference is constant acceleration with throttle, showing the mass flow and
heat cost in the HUD.

# Done: transfer-conic capture (2026-11-18)

The transfer-conic arrival is implemented as a second planner style. It was
recorded as deferred because the gravity ceiling already made every body
reachable; it is now the cheap, slow alternative that parks lower.

* `KeplerPropagator` is a universal-variable state-vector propagator, and
  `ConicElements` derives the conic and converts between true anomaly and time.
  `IArcPath` generalised the arc contract; `Quintic` and `ConicArc` implement it.
* The conic capture chain is a planetocentric hyperbola from the sphere of
  influence to `max(1.05 R, R + 100 km)`, an impulsive capture burn, a transfer
  ellipse to `max(1.15 R, R + 300 km)`, and a circularisation burn. The arrival
  is exact and the final orbit is a real free-fall orbit.
* The burns are impulses at plan boundaries. `PlanArc` carries an entry impulse
  vector, the flight session applies it at the seam, and the plan's delta-v is
  the powered arcs' thrust plus the burn magnitudes.
* Measured at the epoch: captures of 3.53 km/s at Mars against 84.8, 17.40 at
  Jupiter against 1,088.6, and 10.66 at Saturn against 1,159.5. Parking radii
  fall from 9,983 to 7,327 km at Earth, from 177,965 to 80,398 at Jupiter, and
  from 97,380 to 66,967 at Saturn. The outer arrivals take months (99.5 days to
  Jupiter, 131.5 to Saturn), which is the burn-and-coast time cost.
* The quintic capture stays the default. `TripPlanner.ArrivalStyle` is the
  opt-in switch, and the trip-time slider still re-solves the torch plan.
  [ADR-014](../decisions/adr-014-keplerian-coast.md) records the decision.

**Follow-up done:** the nav computer exposes the style as **Quintic capture** /
**Conic coast** buttons beside the trip-time slider. Switching rebuilds the
preview against the chosen style from the pinned departure state, keeps the
chosen flight time when it fits the new window, and redraws the coast, burns,
and lower parking orbit. A live flight keeps the plan it was begun with. The
finite-burn arc and the rings/atmosphere arrival model are later fidelity work.

# Done: hybrid fixed-thrust capture (2026-11-18)

The conic arrival's capture is now flyable as one long finite burn at a
player-set level, with an impulse trim for exactness. The third arrival style is
**Quintic / Conic / X g burn** in the nav computer, with a burn-level slider from
0 g to 1 g.

* `FiniteBurnArc` integrates constant thrust along the instantaneous velocity
  through the two-body field and tabulates the local state; position and velocity
  are continuous at a seam and acceleration steps by the burn level.
  `SimulateUntilRadius` finds the injection crossing.
* `HybridCapture` ends the burn at an injection radius near the conic periapsis,
  applies the terminal impulse that lands on the exact transfer ellipse, and
  keeps the conic circularisation. The solver searches the start offset and
  keeps the smallest trim; the parking radius is the conic
  `max(1.15 R, R + 300 km)` at every level.
* At 0 g the style is exactly the conic plan. The default is 0.3 g.
* Measured captures at 0.3 g: Mars 4.57 km/s (2.70 fixed + 1.79 trim + 0.08
  circularisation), Venus 4.80, Earth 4.92, Jupiter 18.44, Saturn 12.06, the
  Moon 5.07. The flights arrive 0.0–0.8 m and 0.0000–0.0031 m/s from the
  reference state.
* The old single-level style is replaced. It flew both impulses at 0.3 g and
  parked at `1.4 R`; the hybrid parks at the conic radius and, at Jupiter,
  spends 18.44 km/s instead of 19.68 while parking 17,477 km lower.
* [ADR-015](../decisions/adr-015-fixed-thrust-burns.md) records the decision.
  The departure and cruise were powered quintics at the time; the fixed-thrust
  cruise landed later as the burn–coast–burn profile below.

# Done: burn–coast–burn trips (2026-11-19)

The heliocentric middle can now be a real Keplerian coast, and the profile most
torch-ship fiction uses is flyable. The design is
[Burn–coast–burn trips](/simulation/burn-coast-burn.md).

* `LambertSolver.SolveShortWay` in Core answers the zero-rev, prograde, short-way
  branch in universal variables and reports a rejected geometry as
  `HasSolution == false`. The long way, the retrograde branch, and multi-rev
  coasts are deliberately absent.
* `CruiseStyle.Coast` replaces the powered cruise with a heliocentric
  `ConicArc` chosen by the Lambert solve, with the quintic departure retargeted
  to `v₁` and the conic/hybrid capture chain at the far end.
* `CruiseStyle.BurnCoastBurn` adds a prograde departure finite burn and a brake,
  both about the Sun. The quintic break ends on the departure clearance sphere
  (`1.02 × SOI`) at the local escape speed along the escape ray, and the
  departure burn starts there and holds that ray; its angles, its duration, and
  the coast's start are solved forward. The brake is still built backwards from
  the capture chain's entry state with `FiniteBurnArc.SimulateBackward`, so every
  seam is continuous and the arrival is exact. The brake spends the handover the
  coast skeleton would have made an impulse, so there is no impulse at the
  target's sphere of influence; its thrust direction is the change the chain
  needs, because a burn along the velocity can only change the speed. `0 g` is
  exactly the coast plan.
* The nav computer's `PROFILE` row selects Powered (the default), Coast, or
  Burn-coast-burn. The trip-time slider is the profile's speed control: the
  powered window stays `3 × minimum`, while a coasting profile's right-hand end
  is the slowest duration the planner accepts, probed because a long short-way
  transfer sweeps past 180° and a slow coast would need an acceleration rather
  than a brake. `Plan()` searches for the true burn–coast–burn floor and gates on
  the chain closing, so at the epoch the Mars window is **6.73–20.19 days** and
  the Mercury fast end is 3.21610 days at 1 g; Jupiter's is 13.07–39.22 and
  Uranus's 33.60–100.81 at 0.3 g and 31.25–93.76 at 1 g. A 90-day Mars plan is a
  direct `PlanAtDuration` build outside that window. The floor plan and window are
  cached per date, target, level, cruise and arrival style, and moving the
  slider retimes on a latest-wins background worker; every published preview is
  the full-field build, never the fast search estimate
  ([Planning performance](/technical/planning-performance.md)).
* Measured at the epoch from Gateway to Mars at 90 days and 0.3 g: 44.4 km/s in
  total (a 4.84-day break at 8.8 km/s, a 13.4 km/s departure burn in 76 minutes,
  an 85.0-day coast, an 18.5 km/s brake in 1.7 hours, and the 3.5 km/s conic
  capture into a 6,033 km entry), against the cheap coast's 14 km/s at 200 days
  and the torch plan's 2,345 km/s at 4.4 days. Seams close under a metre and
  3.7 × 10⁻⁴ m/s; the flight arrives 0.1 m and 0.0001 m/s from the reference
  state. The true floors are 3.21610 days to Mercury at 1 g (1,766 km/s) and
  6.73042 days to Mars at 0.3 g (1,171.6 km/s), and an over-thrust plan carries an
  `InfeasibleReason` naming the arc and the peak it needs.

# Immediate next tasks

1. Default `EnableMapView` on (or remove the switch) now that the comparison is
   done, so the shipped scene gets the map and the neighbourhood aid fade. This
   is the first small follow-up to milestone 2.5.
2. Add the porkchop search now that a Lambert solve exists: sample departure and
   arrival dates, draw the time/cost heat map, and let the coast profiles choose
   a window instead of planning "leave now".
3. Add a mid-course correction that keeps the current plan instead of replanning
   from the current state. Begin's replan is the first place this matters: a
   correction would keep the plotted curve instead of solving a new one.
4. Give warp a continuous scale so the drop near a body is smooth rather than a
   step down the power-of-ten ladder.
5. Add a PlayMode suite that drives the real bootstrap and the real renderer, so
   the frame order, the trajectory line, and the HUD are covered end to end.
6. Add a fourth sky layer from a small catalogue of real bright stars
   (RA/Dec/magnitude for the brightest few hundred), so the procedural field
   gains the navigable constellations it is missing
   ([The sky](/technical/sky.md)).

# Risks

| Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|
| The 1 g torch is physically implausible | Certain | Medium | Treat it as the one admitted lie: a mature compact fusion torch whose waste heat leaves with the exhaust; fuel is the balance lever |
| Time warp makes arrival trivial or chaotic | Medium | High | Implemented: the warp policy caps a frame at 25% of the time to the nearest surface, 2% of the local orbital timescale, and 5% of the time to the next plan event, and clamps the clock to the ship when the substep budget runs out |
| True scale is unreadable without UI | High | High | Labels, markers, trajectory lines, target-relative HUD; the flight panel carries the phase table and countdown |
| Float/depth artefacts on a target platform | Medium | Medium | Ship-relative rendering; reversed-Z; two-camera fallback |
| Autopilot removes the gameplay | Medium | High | Autopilot handles long burns; the current build is plan-and-watch, and manual override and fuel are the milestone-3 answer |
| The smooth plan is 35–75% slower than the design's tables | Certain | Medium | Show the plan's real time, not the ideal, in the HUD; close the gap with a bang-bang planner once fuel makes time and delta-v compete |
| The gravity ceiling moves arrival orbits out on the big bodies | Certain | Medium | Accepted for the default quintic capture: the alternative is an unreachable destination. The conic arrival style restores lower, cheaper orbits at the cost of months of coasting, and the nav computer's arrival-style buttons expose the choice; a coasting profile always uses the conic or hybrid capture, so it never pays the ceiling |
| A coasting profile's feasible trip times are discontinuous | Certain | Medium | The planner reports the rejected geometry, and the trip-time slider's right-hand end is probed from what the planner accepts rather than assumed, so no slider position asks for a refused plan |
| Data accuracy drifts with epoch | Low | Low | Regenerate from JPL; document epoch and accuracy |
| Scope grows beyond a solo project | High | High | Milestone gates; one ship, one system, no combat |
