---
type: Design Document
title: Full-field burns and the powered-leg floor
description: Fixed burns integrate the summed gravity field and hand over to the shaped ends inside a band, so a capture can enter hot and deep instead of falling in from the sphere of influence at a planted 5 km/s.
tags: [simulation, navigation, trajectory, propulsion, planning]
status: accepted
generated: { by: opencode/deepseek-v4.1-flash, at: 2026-11-19T00:00:00Z }
---

# The problem

A burn–coast–burn trip to Uranus reads as almost all coast at any setting of the
trip-time slider. Two things cause it: the capture always starts at the planted
entry speed, and the slider only re-times the heliocentric leg.

The capture chain always enters the target's sphere of influence at the planted
`TripPlanner.EntrySpeedMps = 5000`. That is a **global input**: every conic and
hybrid capture starts on the SOI sphere with 5 km/s inbound, and the drive-off
fall from there to periapsis is set by the target's gravity, not by the trip
time. At Uranus the SOI is **51.8 million km**, so the fall takes about
**104 days** — of order `r_SOI / v∞`, shortened by the fall's own acceleration.
At Earth, whose SOI is **0.92 million km**, the same entry speed gives about
**2 days**.

| Body | SOI radius | Drive-off fall at 5 km/s |
|---|---:|---:|
| Earth | 0.92 million km | about 2 days |
| Uranus | 51.8 million km | about 104 days |

The slider re-times the heliocentric leg only. It moves the coast, the
departure burn, and the brake, but it cannot move the capture's fall: the entry
state is the same at every duration. On a Uranus trip the fixed fall therefore
dominates every duration the slider can ask for, and the profile reads as coast
at every setting.

This is a **speed** problem, not an arc-boundary problem. The boundary is not in
the wrong place, and braking earlier does not help: braking early leaves less
speed for the fall and makes the coast longer. The win is a **hotter entry and a
deeper brake** — arrive at the target with more speed, at a radius closer in, and
let the brake and the capture spend it where the Oberth effect makes it cheap.

# Full-field burns

A fixed burn is planned today in the two-body field of one body.
`FiniteBurnArc` integrates constant thrust through `centre.Gm` alone and adds the
centre's own ephemeris; the departure burn and the brake are "about the Sun",
the hybrid capture burn is about the target. The ship in flight does not switch
frames: it feels the summed gravity of every body at once
([ADR-005](/decisions/adr-005-continuous-gravity.md)). A burn that begins near a
body and ends well outside it is therefore planned in a field that is wrong at
one end, and a level or trip time that would make a burn cross a sphere of
influence is refused rather than flown.

**A fixed burn integrates the summed gravity field.** The reference body becomes
a **label**: it names the phase, the frame the arc is drawn and reported in, and
the ephemeris used to decompose the state — not the physics. One burn arc may
cross a sphere of influence.

Do **not** split a burn at the SOI. Splitting it is the patched option, and it
puts the seam exactly where each two-body model is about 50% wrong: at the
boundary each body's term is comparable to the other's, so neither approximation
is the field. A single summed-field arc has no seam to get wrong. The coast and
the shaped ends are unaffected — a coast is still a conic about one body, and a
quintic still takes its endpoint accelerations from the full field.

The cost is real: a full-field burn is no longer a function of one body's `μ` and
one initial state, so every sample needs the catalogue's ephemeris. It is slower,
and the floor search's near-minimum basin is already noisy. The established
pattern — search with the fast model, rebuild and certify the reported plan with
the full field — is what keeps this affordable, and the plan that is reported and
flown remains the full-field one.

# The handover band

The burn hands over to the shaped ends between a hard **floor** and a hard
**ceiling**:

* the **floor** is `sqrt(μ / (κ a_drive))` with `κ = 0.4`, the parking ceiling's
  constant. `a_drive` is the acceleration the powered leg commands, so the floor
  sits where local gravity is 40% of that acceleration and moves outward as the
  level falls. It is the same rule that sets the quintic parking ceiling
  ([navigation and arrival](/simulation/navigation-and-arrival.md#the-parking-orbit)),
  used here as a floor on the handover;
* the **ceiling** is `1.02 × SOI`, the current clearance sphere. Outside it the
  handover is no longer local to the body, and the shaped end has nothing to
  shape against.

The floor is a **policy**, not a law of physics, in the same sense as the parking
ceiling: it keeps the handover in a region where the ship's own thrust still
dominates the field it is steering through, so the shaped end and the burn's
attitude law remain describable. Inside the band the radius is chosen by a rule;
solving for the best radius inside the band is deferred until measurement shows
the rule costs something (see the open questions).

The band is not the same radius at both ends, and it is **asymmetric between
departure and arrival**:

* at **departure** the handover is where the quintic break ends at the local
  escape speed along the escape ray, and the departure burn starts there. The
  break's job is escape, so its natural end is the outer part of the band;
* at **arrival** the handover is where the brake ends and the capture chain is
  handed its entry state. The fall shortens as that radius comes in, so the
  arrival wants the inner part of the band.

How far each end sits, and whether the same `κ` and ceiling apply to both, is
open below. What is settled is that a single fixed clearance sphere no longer
defines the handover.

# The shaped ends stay

The break and the conic/hybrid capture keep their jobs. Only the handover radius
and the field the burn is integrated in change.

The break is still the quintic that leaves the parking orbit and ends on the
handover at the local escape speed along the escape ray; the capture is still the
conic or hybrid chain the profile already uses. The break's duration solve, the
pinned fixed point, the escape-ray attitude, the brake's direction, the trim, the
transfer ellipse, and the circularisation are unchanged. No new arc type is
introduced, and `0 g` still selects `CruiseStyle.Coast` exactly.

# Entry speed is an output

Today the entry speed is an **input**. `EntrySpeedMps = 5000` is global and shapes
every capture; changing it moves every conic plan, including the inner-system
ones where the current entry works.

The capture chain gains an **entry radius** — the handover radius — and the brake
solve sets the **residual entry speed** at that radius instead of accepting the
planted 5 km/s. The entry speed becomes an **output** of the trip: the brake
solve chooses how much of the arrival velocity it removes, and the state it
leaves — radius and residual speed — is what the capture chain is handed.

This is what makes a short fall representable. The fall time is set by the entry
radius and the entry speed, not by the SOI. A brake that ends near the floor with
a hot residual hands the capture a short, fast inbound leg: the ship arrives with
more speed at a radius closer in, and the capture spends it where the Oberth
effect is strongest. The 104-day fall at Uranus is a statement about entering at
51.8 million km at exactly 5 km/s, not a property of Uranus.

Keeping the global constant as an option is allowed — a plan may still plant
5 km/s at the SOI — but a per-plan residual is what the profile needs. Changing
the global value is not the lever: it moves every plan at once.

# Steering starts simple

The burns need an attitude law, because a full-field arc is no longer "prograde
about one body". The first law is a **fixed inertial direction**, which is the
honest model of a real burn: the ship holds one attitude instead of tracking its
velocity. A long burn may be subdivided into a few fixed-direction segments if
one is too crude; each segment is still a fixed inertial direction, not a control
law.

The alternative is a **local-prograde law** — thrust along the instantaneous
velocity or the local frame's prograde — which is natural on a two-body arc but
not obviously cheaper when the field is summed and the burn crosses a boundary.
The penalty of the fixed direction must be measured against the local-prograde
law before any more expensive law is considered. A shooting solve is the last
resort, not the first.

The texture already exists: the brake holds a fixed direction along the change
the chain needs, and the departure burn holds the escape ray. Starting simple
means keeping that and measuring it, not replacing it.

# What this changes

* Burning across a sphere of influence is a supported case, not a rejected one.
  This supersedes the exclusion in
  [burn–coast–burn trips](/simulation/burn-coast-burn.md#not-in-the-first-cut).
* The `1.02 × SOI` clearance sphere becomes the **ceiling** of the handover band,
  not the definition of the handover radius.
* The global `EntrySpeedMps` stops being the profile's entry condition; the entry
  radius and the residual entry speed are per plan.
* The coast, the quintic, the capture chain, and the `0 g` identity are
  unchanged.

# Open questions

* **The steering law.** Fixed direction, a few fixed segments, or local prograde?
  The delta-v penalty of the simple law is unmeasured; the choice waits on a
  measurement at the epoch at 0.3 g and 1 g.
* **Band versus solved radius.** The band says where the handover may sit; a rule
  picks a radius inside it. Solving for the radius — as a search or a shooting
  variable — is deferred unless measurement shows the rule costs real delta-v or
  time.
* **The departure/arrival asymmetry.** The two ends want different radii; how far
  each sits, whether the same `κ` and ceiling apply at both, and whether the
  departure should also move inward for the Oberth benefit are not settled.
* **Search input or certified build.** Is the handover radius a free variable the
  floor search explores, or a value computed from the band and then certified
  with the full field like a chosen duration? The floor search is already noisy
  near its minimum, so this is a cost question as well as a design one.

# See also

* [Burn–coast–burn trips](/simulation/burn-coast-burn.md) — the profile, the
  pinned break, and the measured floors this change moves.
* [Navigation and arrival](/simulation/navigation-and-arrival.md) — the capture
  chain, the parking ceiling, and the sphere-of-influence table.
* [The flight model](/simulation/flight-model.md) — the drive, the burn levels,
  and the delta-v economy.
* [ADR-005](/decisions/adr-005-continuous-gravity.md),
  [ADR-014](/decisions/adr-014-keplerian-coast.md), and
  [ADR-015](/decisions/adr-015-fixed-thrust-burns.md) — the summed field, the
  capture chain, and the fixed-thrust burn.
