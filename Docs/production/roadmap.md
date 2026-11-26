---
type: Roadmap
title: Roadmap
description: What exists in the prototype, what comes next, and the risks that could change the plan.
tags: [production, roadmap, milestones]
status: draft
generated: { by: opencode/deepseek-v4.1-flash, at: 2026-11-26T12:00:00Z }
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
* Linear colour space, with every authored colour kept in sRGB and converted
  once at the shader boundary
  ([ADR-021](../decisions/adr-021-linear-colour-space.md)).
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
  ([ADR-014](../decisions/adr-014-keplerian-coast.md)). The quintic capture was
  the default at the time; the conic capture is the profile's default now.
* A **hybrid fixed-thrust arrival** as the alternative capture: one long burn at
  a player-set level from 0 to 1 g, an impulse trim onto the exact transfer
  ellipse, and the conic parking radius, with the throttle stepping instead of
  blending ([ADR-015](../decisions/adr-015-fixed-thrust-burns.md)).
* The **burn–coast–burn** profile as the planner's only crossing: a zero-rev
  prograde `LambertSolver`, a heliocentric `ConicArc` coast, a short prograde
  escape break and a ballistic climb to the `1.02 × SOI` ceiling, a forward
  departure burn from the conic's ceiling state, and a brake
  reverse-placed from the capture chain's entry state. The trip-time slider is
  the profile's speed control
  ([Burn–coast–burn trips](/simulation/burn-coast-burn.md)). Planning it is
  two-tier: a fast, search-only central-body model chooses the duration, the
  returned plan and the flight stay full-field, a 5% screen certifies near-limit
  probes, the floor and window are cached, and the slider retimes on a
  latest-wins background worker
  ([Planning performance](/technical/planning-performance.md)).
* **Station calls**: a station is a point-target Go To whose plan ends on a
  500 m hold point with the velocity matched, with no sphere, parking orbit or
  capture chain. Four stations are heliocentric rendezvous and Gateway and Titan
  Reach start inside a planet's sphere; docking is a later phase
  ([ADR-019](../decisions/adr-019-station-calls-and-departure.md)).
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
  one type — with burn markers and an arrival marker.
* The flight panel is a phase table — departure, cruise, capture with dates and
  durations, arrival date, flight time, peak thrust and throttle, delta-v, and
  the fuel readout — plus the live phase, next-event countdown, throttle, and
  warp.
* `WarpPolicy` drops the warp by clearance, local orbital timescale, and the next
  plan event, and the status panel says when it is holding the clock.
* The frame has a 2,000-substep budget; if it runs out the clock is clamped to
  the date the ship reached, so the world never runs ahead of it.
* After arrival the drive goes off, the ship coasts, and the ship comes back
  under the player: an orbit camera (right-drag, wheel zoom) that frames the
  destination automatically until they take it ([ADR-008](../decisions/adr-008-orbit-camera.md)).

**Acceptance met at the time:** Gateway → Mars, 4.44 days, arrives 0.0 m and
0.0000 m/s from
the parking orbit, holds 3,898 km at e = 3.07 × 10⁻⁶ for three orbits, and does
it in 65.7 real seconds over 3,939 frames at the suggested 10,000×. Peak 63
substeps in a frame; warp auto-drops to 10× for the last second. No teleport.
(The 4.44-day plan was the pre-physical-departure profile; the departure change
on 2026-11-23 re-baselined the trip times and totals — see
[Done: station calls and the departure fix](#done-station-calls-and-the-departure-fix-2026-11-24).)

**Known gaps:** the warp drop is in power-of-ten steps; the ship is a marker
rather than a model; the camera can still be orbited inside a planet (it is
pulled out, but as a rescue); there is no manual thrust; and a second Go To
replans from scratch rather than correcting the existing plan.

# Milestone 2.5 — the route preview and the camera (done)

**Goal: one continuous zoom from the ship to the whole system, and a route the
player can see before they fly it.**

The route preview is built and stays: **Plot route** draws the curve and the
target-arrival ghost, the trip-time slider and the arrival-style buttons retime
it, and **Begin flight** replans from the current state and flies it. The
planner now has one crossing — **burn–coast–burn** — with a **Conic** or
**`X g burn`** capture; the powered quintic cruise and the plain coast are
retired. The details are in
[The flight session](/technical/flight-session.md).

The top-down **system map** that this milestone also built was removed on
**2026-11-21** ([ADR-018](../decisions/adr-018-one-orbit-camera.md)). It was a
comparison layer, not a gameplay mode: one wheel can reach the whole system
without leaving the ordinary orbit pose. `MapView`, `SystemMap.unity`,
`EnableMapView`, the map pan, and the map-only marker, label, and line rules are
gone. What remains from the camera work is the single 2,000 km–12 billion km
range, the cursor-anchored wheel zoom, `Space` home, and one orbit pose at every
distance.

**The camera after the removal:**

* one range and one pose: the wheel never tilts or slerps the view;
* the framing distance is captured once when a plan, jump, or reset starts and
  re-captured on arrival, so a moving target does not re-target the zoom every
  frame;
* body avoidance stops the camera at a body's clearance sphere continuously, so
  a body drifting past the orbit slides the camera instead of stepping it;
* `Space` still eases home in one second; the 60 AU look-at cap still bounds an
  off-centre zoom.

**Acceptance met at the time:** one wheel gesture climbs from the Gateway
neighbourhood to
the 12-billion-km ceiling with the pose unchanged, and the reverse gesture
returns to the exact remembered yaw, pitch, and rotation. Mars can be selected
and plotted; the curve and the arrival ghost draw without flying, and Begin
replans and arrives on the 3,898 km parking orbit in 4.44 days with 0.0 m and
0.0000 m/s, 3,939 frames and 65.7 real seconds — the same numbers as the scene
flight. (The plan's duration and totals have since been re-baselined by the
physical departure.) Importance-ordered picking lets a planet win an overlapping station and
repeated clicks cycle the stack.

**Known gaps:** the zoom tween's feel and real pointer hits still need a
PlayMode pass, the camera keeps the player's yaw rather than offering a
north-up option, and the 60 AU look-at cap and 12-billion-km ceiling are fixed
constants. The automatic framing now settles on the parking orbit with a
visible second zoom at arrival; its duration should be judged in play.

# Milestone 3 — fuel and the delta-v economy

**First pass done (2026-11-21):** the nav computer prints what a journey would
burn in a `Fuel` row — the rocket equation at 10,000 km/s and the 1,000 t dry
mass, as tonnes and a share of the departure mass. It is a readout, not a
budget: the tank is unlimited, mass is constant, and no plan is refused for
fuel ([ADR-016](../decisions/adr-016-fuel-readout.md)). Remaining:

* Charge `PlanArc.DeltaVMps` against a finite budget.
* Make acceleration mass-dependent, or hold it constant by throttling.
* Refuse a plan the budget cannot pay, and offer the cheap coasting alternative.
* Add the tank level to the fuel readout and the end-of-fuel failure state.

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
* The quintic capture was the default at the time.
  `TripPlanner.ArrivalStyle` was the opt-in switch, and the trip-time slider
  re-solved the torch plan. [ADR-014](../decisions/adr-014-keplerian-coast.md)
  records the decision; the conic capture became the default when the powered
  cruise retired.

**Follow-up done:** the nav computer exposed the style as **Quintic capture** /
**Conic coast** buttons beside the trip-time slider (now **Conic** / **X g
burn**). Switching rebuilds the
preview against the chosen style from the pinned departure state, keeps the
chosen flight time when it fits the new window, and redraws the coast, burns,
and lower parking orbit. A live flight keeps the plan it was begun with. The
finite-burn arc and the rings/atmosphere arrival model are later fidelity work.

# Done: hybrid fixed-thrust capture (2026-11-18)

The conic capture is flyable as one long finite burn at a player-set level, with
an impulse trim for exactness. The arrival choice is now **Conic / X g burn** in
the nav computer, with a burn-level slider from **0.05 g** to 1 g; the old
Quintic arrival and the 0 g off switch were retired with the powered cruise.

* `FiniteBurnArc` integrates constant thrust along the instantaneous velocity
  through the two-body field and tabulates the local state; position and velocity
  are continuous at a seam and acceleration steps by the burn level.
  `SimulateUntilRadius` finds the injection crossing.
* `HybridCapture` ends the burn at an injection radius near the conic periapsis,
  applies the terminal impulse that lands on the exact transfer ellipse, and
  keeps the conic circularisation. The solver searches the start offset and
  keeps the smallest trim; the parking radius is the conic
  `max(1.15 R, R + 300 km)` at every level.
* The default level is 0.3 g. The level's floor is now 0.05 g: the profile is two
  real burns around a coast, so there is no zero-thrust version.
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
* A reference coast replaces the powered middle with a heliocentric
  `ConicArc` chosen by the Lambert solve, with the quintic departure retargeted
  to `v₁` and the conic/hybrid capture chain at the far end. It is now private:
  the planner never returns it as a plan.
* The profile adds a prograde departure finite burn and a brake,
  both about the Sun. At the time the quintic break ended on the departure
  clearance sphere (`1.02 × SOI`) at the local escape speed along the escape ray,
  and the departure burn started there and held that ray; the physical departure
  later replaced that with a short prograde break and a ballistic climb (see
  [Done: station calls and the departure fix](#done-station-calls-and-the-departure-fix-2026-11-24)).
  The brake is still built backwards from
  the capture chain's entry state with `FiniteBurnArc.SimulateBackward`, so every
  seam is continuous and the arrival is exact. The brake spends the handover the
  coast skeleton would have made an impulse, so there is no impulse at the
  target's sphere of influence; its thrust direction is the change the chain
  needs, because a burn along the velocity can only change the speed.
* The profile is now the only crossing. The trip-time slider is its speed
  control; its right-hand end
  is the slowest duration the planner accepts, probed because a long short-way
  transfer sweeps past 180° and a slow coast would need an acceleration rather
  than a brake. `Plan()` searches for the true burn–coast–burn floor and gates on
  the chain closing. At the time the epoch's Mars coast window was **6.73–20.19
  days** and the Mercury fast end was 3.21610 days at 1 g; Jupiter's was
  13.07–39.22 and Uranus's 33.60–100.81 at 0.3 g and 33.65–100.96 at 1 g. The
  physical departure re-baselined every one of those to a Mars **16.84–50.53
  days** window, and the departure climb split re-baselined them again: Mars is
  now **7.17–21.50 days** on the coast path. The burn-only crossing keeps the
  parabolic climb and is the **longer** plan everywhere it builds, so the
  short-coast gap is empty and the nav minimum is the split-aware coast floor
  ([ADR-022](../decisions/adr-022-departure-split.md)). A 90-day Mars plan is a
  direct `PlanAtDuration` build outside that window. The floor plan and window are
  cached per date, target, level, cruise and arrival style, and moving the
  slider retimes on a latest-wins background worker; every published preview is
  the full-field build, never the fast search estimate
  ([Planning performance](/technical/planning-performance.md)).
* Measured at the epoch on the pre-physical-departure profile, from Gateway to
  Mars at 90 days and 0.3 g: 44.4 km/s in
  total (a 4.84-day break at 8.8 km/s, a 13.4 km/s departure burn in 76 minutes,
  an 85.0-day coast, an 18.5 km/s brake in 1.7 hours, and the 3.5 km/s conic
  capture into a 6,033 km entry), against the cheap coast's 14 km/s at 200 days
  and the torch plan's 2,345 km/s at 4.4 days. The re-measured profile is
  **38.5411 km/s over nine arcs** at the same 90 days, with a 7.405-day climb;
  seams close inside the profile's gate. The split-aware floors are
  **3.67616 days** to Mercury at 1 g (1,649.4 km/s) and **7.16751 days** to Mars
  at 0.3 g (1,070.1 km/s), both at the 16 km/s excess cap, and an over-thrust
  plan carries an `InfeasibleReason` naming the arc and the peak it needs.

# Done: PlayMode suite (2026-11-22)

The PlayMode suite drives the runtime-created `SolarSystemBootstrap` inside
Unity's own player loop: no lifecycle method is called by reflection, the HUD
and renderer are the shipped `MonoBehaviour`s, and the assertions read the real
`LineRenderer` and `Text` objects. It covers the frame order (model and clock in
`Update`, body views and trajectory in `LateUpdate`), the HUD's frame pump
publishing an async preview and a slider retime into the trajectory line, the
route legend and flight panel, and Begin and Cancel through the real buttons.
`tools/check.sh` now runs EditMode and PlayMode as separate Unity invocations;
the PlayMode suite is about half a minute on the warm project. It runs under
`-nographics`, so rendered pixels and a real pointer through the EventSystem
remain human checks ([Testing and verification](/technical/testing.md#unity-playmode-suite)).

# Done: station calls and the departure fix (2026-11-24)

Stations are destinations, and the departure is a short escape instead of a
multi-day hover. The decisions are
[ADR-019](../decisions/adr-019-station-calls-and-departure.md); the profile is
[Burn–coast–burn trips](/simulation/burn-coast-burn.md), the rendezvous is
[Navigation and arrival](/simulation/navigation-and-arrival.md), and the
stations are [Lagrange stations](/world/lagrange-stations.md).

* **A station call is a point-target rendezvous.** The arrival is the station's
  own state at the arrival date offset **500 m astern along its velocity**
  (`TripPlanner.StationHoldDistanceM`), with the ship's velocity matched. There
  is no sphere of influence, handover band, parking orbit, planetocentric
  approach or capture chain. Four stations are outside every planet's sphere and
  are heliocentric rendezvous; Gateway is inside the Moon's and the Earth's, and
  Titan Reach is inside Saturn's, but they end on the same point target. Docking
  is not implemented: the plan ends at the hold point.
* **A station plots, flies and arrives.** The bootstrap and the HUD let
  `BodyData.IsStation` through the massless-body guard, so Go To works from the
  details screen. Ares Junction → Gateway at 160 days is **12.239 km/s** over
  three arcs; the fastest is **54.16210 days / 69.541 km/s**. The flown arrival
  tracks the hold point to **0.0254 m and 7.68 × 10⁻⁵ m/s**.
* **The departure is a short prograde escape burn and a ballistic climb.** The
  break ends at the parking radius on the ship's own tangent at the planetary
  escape speed; a planetocentric `ConicArc` carries it to the `1.02 × SOI`
  ceiling (parabolic when the trip allows, hyperbolic when it does not), and the
  departure burn starts at the conic's ceiling state. The break's duration is its
  own commanded-level solve, never a share of the trip: **2.854 km/s from a Mars
  300 km orbit** (2.02× the impulsive `v_esc − v_circ`) and **1.404 km/s from
  Gateway** (2.98×). A 0.3 g departure from a 400 km Earth orbit is refused
  because the break cannot lift it.
* **The trip totals are re-baselined, not preserved.** Gateway → Mars at 90 days
  is **38.9678 km/s over nine arcs**, not the old 44.35. Mars 300 km → Earth is
  **81.045 / 49.940 / 35.823 km/s** at 60/90/120 days and → the Moon **83.166 /
  49.644 / 34.520**; the floors moved to **16.84485 days** at Mars 0.3 g,
  **14.06070** at Mercury 1 g, **23.73299** at Jupiter 0.3 g, and **57.02223 /
  92.13088** at Uranus 1 g / 0.3 g.
* **The station is paced into its hold.** Two warp caps apply while a station
  plan is live — clearance to the station's catalogue surface at the current
  relative speed, and 5% of the time to the hold point — and are released when
  the plan completes. Measured: **99.8 s per frame** at 200 km astern closing at
  100 m/s, where the gravity field alone allows 3,709 s; the **1 s floor** at
  the hold point; no effect 2 × 10⁹ m away.
* **The panel and the HUD say what the arrival is.** A station's terminal phase
  and drive row read **Match** (never Capture or Brake), the next event reads
  `Match T− …`, and the status reads **Arrived, at the hold point**; every
  planet label is unchanged.

# Done: the first-pass trade model (2026-11-25)

The six stations, Earth and Mars trade a fixed set of commodities with a parked
ship. The decision is [ADR-020](../decisions/adr-020-trade-catalogue.md) and the
model is [Trade](/world/trade.md).

* **Commodities and markets are data.** `Assets/Resources/trade_catalogue.json`
  holds two flat arrays — four commodities and eight markets — loaded through
  `TradeCatalogueLoader` behind a seam. No runtime C# names a commodity or a
  price, and the file is separate from the generated `solar_system.json`.
* **Cargo is integer units in one 40 m³ hold.** `CargoHold` (`Simulation`, plain
  C#) limits a load by volume and derives mass for display; flight ignores it.
  The station's stock is unlimited and cargo does not persist.
* **Prices are labels.** `buyPrice` and `sellPrice` exist so the panel and a
  later currency step have the right shape; a transaction moves units only and
  nothing is charged.
* **The trade gate is arrival.** A market needs the flight status **Arrived** at
  the selected body, with no second distance or speed test: any accepted planet
  orbit or a station's hold point counts.
* **The panel is a second centred screen.** The details footer gained a **Trade**
  button, hidden without a market row and live only on arrival, opening a
  700×300 panel of offer rows with `+1`, `+max`, `-1` and `sell all`. The rows
  and the hold line are built by `TradeReport` in `Presentation`.

Deferred: currency and a balance, finite stock and restocking, price movement,
cargo mass in flight, docking, persistence, and ship selection.

# Immediate next tasks

1. Give the trade loop an economy: a balance so prices charge and pay, finite
   stock, and a cargo mass the fuel readout has to carry, so a delivery can be
   paid for from the propellant it spends.
2. Implement **docking** as the phase after the hold point: a short relative
   approach with RCS authority, a closing-rate limit, and the hard mate the
   docking section of [Navigation and arrival](/simulation/navigation-and-arrival.md#docking)
   describes.
3. Add the porkchop search now that a Lambert solve exists: sample departure and
   arrival dates, draw the time/cost heat map, and let the coast profiles choose
   a window instead of planning "leave now".
4. Add a mid-course correction that keeps the current plan instead of replanning
   from the current state. Begin's replan is the first place this matters: a
   correction would keep the plotted curve instead of solving a new one.
5. Give warp a continuous scale so the drop near a body is smooth rather than a
   step down the power-of-ten ladder.
6. Label a handful of constellations, or draw faint asterism outlines, now that
   the real stars are in place
   ([The sky](/technical/sky.md#the-real-stars)).

# Follow-ups from the departure climb split (2026-11-26)

The split is [ADR-022](../decisions/adr-022-departure-split.md). These three were
found while landing it and are deliberately left out of that plan.

* **The station departure split.** A station plan's terminal velocity match has
  no capture chain to price, so the chooser has nothing to cost a candidate
  against and a station departure keeps the unforced minimum-energy climb.
  Extending the split to stations needs a cost of its own and its own
  re-baselined anchors.
* **The reverse-route plot cost.** Venus → Earth now plots in about **10.2 s**
  and Uranus → Earth in about **39.0 s**, against the **0.4–0.5 s** refusals
  they returned before the split. Both are new capability, not regressions — the
  routes build where they used to be refused — but the first plot is slow and
  needs its own investigation before the reverse routes feel usable.
* **The full-blast minimum-time route.** The burn-only crossing is the
  planner's no-coast shape, but it keeps the minimum-energy parabolic climb
  because its duration is an output, so it measures 16.49 d against the split
  coast floor's 7.17 d. Give it the same climb choice with **time** as the
  objective — a small `v∞` grid and the shortest feasible plan — so the slider's
  left end is a full-blast route with no coast. At Gateway → Mars 0.3 g expect
  about **6–6.5 d** (the kinematic no-coast bound is 6.0 d) at roughly
  **1,500 km/s**, against the coast floor's 1,070. The burn-only seam solve is
  the delicate part, so "nearly" is the target. Keep the 3× window anchored to
  the **coast** floor (21.5 d) so only the left end moves; the time-optimal
  `v∞` may also sit above the fuel grid's 16 km/s cap.


# Risks

| Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|
| The 1 g torch is physically implausible | Certain | Medium | Treat it as the one admitted lie: a mature compact fusion torch whose waste heat leaves with the exhaust; fuel is the balance lever |
| Time warp makes arrival trivial or chaotic | Medium | High | Implemented: the warp policy caps a frame at 25% of the time to the nearest surface, 2% of the local orbital timescale, and 5% of the time to the next plan event — plus, at a station, 25% of the time to its catalogue surface and 5% of the time to the hold point — and clamps the clock to the ship when the substep budget runs out |
| True scale is unreadable without UI | High | High | Labels, markers, trajectory lines, target-relative HUD; the flight panel carries the phase table and countdown |
| Float/depth artefacts on a target platform | Medium | Medium | Ship-relative rendering; reversed-Z; two-camera fallback |
| Autopilot removes the gameplay | Medium | High | Autopilot handles long burns; the current build is plan-and-watch, and manual override and fuel are the milestone-3 answer |
| The smooth plan is 35–75% slower than the design's tables | Certain | Medium | Show the plan's real time, not the ideal, in the HUD; close the gap with a bang-bang planner once fuel makes time and delta-v compete |
| The gravity ceiling moves arrival orbits out on the big bodies | Certain | Medium | Accepted for the retired all-torch plan: the alternative was an unreachable destination. The profile uses the conic or hybrid capture and the lower conic radius, so it does not pay the ceiling; only a trip inside the target's sphere still uses the quintic capture |
| The profile's feasible trip times are discontinuous | Certain | Medium | The planner reports the rejected geometry, and the trip-time slider's right-hand end is probed from what the planner accepts rather than assumed, so no slider position asks for a refused plan |
| Data accuracy drifts with epoch | Low | Low | Regenerate from JPL; document epoch and accuracy |
| Scope grows beyond a solo project | High | High | Milestone gates; one ship, one system, no combat |
