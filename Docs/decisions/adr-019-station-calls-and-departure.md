---
type: Decision Record
title: "ADR-019: station calls and the departure break"
description: A station call is a point-target rendezvous that ends on a hold point 500 m astern, with no sphere, capture chain or docking, and a go-to departure is a short prograde escape break followed by a ballistic climb, never a share of the trip time.
tags: [decision, simulation, navigation, stations, departure, trajectory]
status: accepted
generated: { by: opencode/deepseek-v4.1-flash, at: 2026-11-24T04:15:13Z }
---

# Context

Trade and cargo need stations as destinations, not only as starting points.
Every station in the catalogue is a real Lagrange point with `Gm == 0`, and the
planner's guard treated a massless body as a data error, so every station was
unplottable. The arrival pipeline assumed a planet at the far end as well: a
target sphere of influence, an arrival handover band, a parking orbit, and a
conic or hybrid capture chain. None of those has a meaning at a point in space,
and a station has no gravity well to capture into.

The departure had the same planet-shaped assumption in a worse form. The break
was a multi-day powered quintic that pinned the ship to a **radial** escape ray
chosen by the coast solver. A ship in a low Mars orbit is moving almost
perpendicular to that ray — the pin sat 99–118° from the ship's own motion at a
Mars 300 km orbit and 166.6° at Gateway — so the break flew a huge out-and-back
hover. At a fixed trip time the break alone spent **267–355 km/s** on the
Mars→Earth and Mars→Moon crossings at 60, 90 and 120 days, which both dwarfed
the rest of the trip and inverted the time/fuel trade: adding time made the
profile spend more, because the hover was stretched to fill it.

# Decision

1. **A station call is a point-target rendezvous with a hold point.** The
   planner dispatches a `BodyData.IsStation` target before any sphere, parking
   orbit or handover-band code, and the terminal condition is the **hold
   state**: `SolarSystemModel.ComputeState(station, arrivalJd)` offset
   **500 m astern along the station's own velocity**
   (`TripPlanner.StationHoldDistanceM`), with the ship's velocity matched to the
   station's. There is **no sphere of influence, no handover band, no parking
   orbit, no planetocentric approach and no capture chain** in a station plan.
   The final arc is a finite velocity match that may add or shed speed, because
   a rendezvous is not a retrograde brake.

2. **An L-point station is a heliocentric rendezvous.** Four of the six
   stations lie outside every planet's sphere: Helios Watch, Nyx Anchor, Ares
   Junction and the Jovian Transfer Yard are approached from the Sun's frame
   directly. **Gateway** is the exception — inside both the Moon's and the
   Earth's sphere, and the only station inside its catalogue parent's — and
   **Titan Reach** is inside Saturn's; they are local trips from the ship's state
   but still arrive at the same point target. A station
   plan therefore starts from a local climb when the ship is inside a planet's
   sphere and from the ship's own heliocentric state when it is not.

3. **Docking is not implemented.** The plan ends at the hold point; the last few
   hundred metres are a future docking phase, not part of the flight. A
   station's `gm` stays 0 and its catalogue radius (1.0–1.75 km) is a visual
   placeholder. The radius is used by the warp policy's clearance cap and by
   nothing else — never as physics, and never as a parking orbit.

4. **The departure break is a short escape, not a trip-time share.** A go-to
   departure is a **short prograde quintic break** at the parking radius to the
   planetocentric escape speed `√(2μ/r₀ + v∞²)`, then a **ballistic
   planetocentric `ConicArc`** from that state to the `1.02 × SOI` handover
   ceiling — the minimum-energy **parabolic** climb when the trip allows it, a
   solved **hyperbolic** excess speed when it does not — and then the departure
   fixed burn at the conic's ceiling **state**. The break's duration is its own
   commanded-level solve (peak 9.7000 m/s² at 1 g, 2.9467 at 0.3 g), never a
   share of the trip time. The coast absorbs the residual, so a faster trip
   costs more. The departure burn's free variables are its thrust direction and
   duration; its start position is fixed by the conic.

# Consequences

**Positive**

* A station is a destination. Route plotting, the arrival ghost, the flight
  session and the HUD all work for a station without faking a mass or an orbit.
* A Mars 300 km escape now costs **2.854 km/s** in a 1,832-second break, which
  is **2.02×** the impulsive `v_esc − v_circ`; Gateway's 1.404 km/s in 928 s is
  **2.98×**. The profile's total is set by the coast and the two fixed burns,
  not by the departure.
* The trade points the right way again: more trip time spends less, and the
  fixed-duration anchors are re-baselined rather than preserved.
* A station flight arrives on its contract. Ares Junction → Gateway through
  `FlightSession.Advance` tracks the hold point to **0.0254 m and
  7.68 × 10⁻⁵ m/s**.
* The station warp caps pace the last approach and the hold without pinning the
  clock after arrival: **99.8 s/frame** at 200 km astern closing at 100 m/s
  (the gravity field alone would allow 3,709 s), the **1 s** floor at the hold
  point, and no effect 2 × 10⁹ m away, where the event cap's 130,059 s wins.
* The departure is a state at every seam. The break, the escape conic, the
  departure burn, the coast, the approach and the brake hand over position and
  velocity, so the autopilot never flies an impulse the plan did not model.
* The escape conic is exactly parabolic on a long trip, which caught a **√2**
  error in `ConicElements`' parabolic Barker branch: both directions now use the
  standard `½ √(p³/μ)` (17.002 days became 12.022 from a Mars 300 km orbit), and
  `KeplerPropagatorTests` pins the branch against universal-variable
  propagation.

**Negative**

* **Gateway → Mars at 90 days is 38.9678 km/s across nine arcs**, not the old
  44.35: the climb takes 10.74066 days where the old break took 4.84, so the
  fixed 90-day trip leaves the coast 5.9 days shorter and the departure
  burn/brake grow from 13.4/18.5 to 14.0/19.7 km/s. The old anchor is
  deliberately **not** preserved.
* Every planet floor and window moved. Mars 0.3 g is **16.84485 days**
  (16.84–50.53), Mercury 1 g **14.06070** (14.06–34.27), Jupiter 0.3 g
  **23.73299** (23.73–71.20), Uranus 1 g **57.02223** (57.02–171.07) and 0.3 g
  **92.13088** (92.13–276.39). The old Mercury 8 d, Mars 10 d and Uranus 35 d
  anchors are below the floor and are now **refusals**.
* A drive weaker than the local gravity cannot fly the short break at all: a
  **0.3 g departure from a 400 km Earth orbit is refused** (8.7 m/s² of local
  gravity against 2.94 m/s² of thrust), while the same orbit at 1 g builds.
* Mars 300 km → Earth is **81.045 / 49.940 / 35.823 km/s** at 60/90/120 days and
  → the Moon **83.166 / 49.644 / 34.520**; the faster trips are expensive
  because the short coast forces large fixed burns.
* The fixed-duration references were re-measured: Mars 0.3 g 90 d 38.9678,
  Uranus 1 g 400 d 183.9153, Mars 0.3 g 20 d 495.9818, Mars 0.3 g 18 d
  706.0906, Mercury 1 g 16 d 869.0568, Uranus 1 g 60 d 1,329.6508 km/s.
* The station is still a dot in the world. Without docking, the player stops at
  the hold point with no port to approach and no service to use yet.

# Alternatives considered

**Keep the powered quintic departure.** Rejected: it is a multi-day hover that
no ship would fly, it cost 267–355 km/s at the Mars→Earth and Mars→Moon trip
times, and it made the trip-time control trade the wrong way.

**Give a station a fake mass, radius and parking orbit.** Rejected: there is no
gravity to hold an orbit, no capture chain to fly, and a fake `Gm` would leak
into the catalogue, the gravity field and every "nearest body" reading.

**Burn at the parking radius to exploit the Oberth effect.** The impulsive
capture arithmetic says a deep, fast burn is cheapest, but the heliocentric
coast model relies on the `1.02 × SOI` handover for its reference frame. Burning
deep would need the full-field coast and a different patched-conic handover; it
is out of scope for this step.

**Model the escape as an impulse only.** It would be cheaper to solve and just
as exact, but the plan would no longer command the same acceleration level as
every other burn, and the seam would stop being a state. The finite break keeps
the whole plan flyable.

# Acceptance

`StationTripPlannerTests` covers the hold point (arrival state, the last arc
inside 1 m and 0.001 m/s), the absence of approach, parking-orbit and
capture-chain arcs and of any arc about the station, the heliocentric starts, a
station-to-station trip, the fastest station route, the unchanged planet anchor,
and the station-accepted/massless-refused pair. `BurnCoastBurnTripPlannerTests`
covers the physical departure: the Mars-orbit climb to Earth and the Moon, the
break ending at the parking radius on the prograde tangent at escape speed, the
parabolic and hyperbolic conics, the impulsive-escape sanity (2.02× from Mars,
2.98× from Gateway), and the weak-drive refusal from low Earth orbit.
`KeplerPropagatorTests` pins the parabolic Barker time with the standard
`½ √(p³/μ)` factor. `FlightSessionTests` flies Ares Junction → Gateway to the
tracked hold point; `WarpPolicyTests` pins the station clearance, the hold floor
and the non-binding far case; `FlightReportTests` and `SolarSystemHudTests` pin
the **hold point** and **match** wording. `tools/check.sh` is the gate.

The reasoning for the point target is in
[Navigation and arrival](/simulation/navigation-and-arrival.md), the profile in
[Burn–coast–burn trips](/simulation/burn-coast-burn.md), the stations and their
spheres in [Lagrange stations](/world/lagrange-stations.md); the conic and
fixed-thrust ancestry is [ADR-014](/decisions/adr-014-keplerian-coast.md) and
[ADR-015](/decisions/adr-015-fixed-thrust-burns.md).
