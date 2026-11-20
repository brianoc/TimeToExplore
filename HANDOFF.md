# Handoff

**Live plan: make the first burn–coast–burn plot fast.** Branch
`work/first-plot-latency`, worktree
`~/.local/share/opencode/worktree/4ad4f7/first-plot-latency`.

The goal agreed with the user is the **calculation**, not the UI: a certified
first route in roughly **3 s in the editor**, at the default 0.3 g as well as
1 g. The UI (an async plot, a prefetch on selection, an estimate) is explicitly
deferred. Iterate in the throwaway .NET harness, not the editor.

## The problem, measured (2026-11-20, epoch 2026-11-15, Gateway, conic capture)

First `Plot()` is synchronous: `FlightSession.WindowFor` calls
`TripPlanner.Plan()` on the main thread.

| Case | .NET Release | Editor (warm) | Editor floor |
|---|---:|---:|---:|
| Uranus 0.3 g (default) | 0.53 s | **27.3 s** | 42.54216 d |
| Uranus 1 g | 3.38 s | 2.57 s | 31.25256 d |
| Jupiter 0.3 g | ~6 s | 33.6 s | 13.46153 d |
| Mars 0.3 g | 2.47 s | 13.5 s | 6.73715 d |
| Mercury 1 g | 1.02 s | 4.2 s | 3.21610 d |

- The editor is ~5.5× .NET on Mars and Mercury, whose floors match the docs
  exactly. Uranus's floor **differs between runtimes** (31.25 vs 34.16 d at 1 g,
  42.54 vs 40.71 d at 0.3 g) because the near-floor basin is a scatter and a
  last-bit libm difference picks a different needle. The editor number is the
  one that ships.
- One full-field build is **0.37–0.44 s .NET / ~2 s editor**, of which
  **65–75% is the pinned-departure Newton solve** (512-sample burn integrations
  across up to 48 iterations × a 6-eval Jacobian). The brake is 0.10–0.31 s,
  arc measurement ~20 ms, coast skeleton ~7–26 ms.
- The rest of `Plan()` is **probe count**: ~14 full-field builds at 1 g and 13
  in the editor at 0.3 g. Feasibility versus duration is a scatter, not a
  boundary (`31.0 ✗ 31.5 ✗ 32.0 ✗ 32.5 ✓ 33.0 ✗ 33.5 ✓ 34.0 ✓ 34.5 ✗ …`),
  because the departure Newton's stall decides it. The search bisects that
  noise to `FloorRelativeTolerance = 5e-4`.

## Measured levers (prototyped, then reverted)

A build with the departure solve at **128 samples**, the reported burn still at
512, the coast re-solved from the reported burn's end, and the brake at **512**
samples is **3.7× faster** with **every fixed-duration Δv anchor identical to
four decimals** and seams of metres / sub-m/s against the 1 km / 1 m/s gate:

| Plan | Δv | Build before → after | Worst seam |
|---|---|---|---|
| Mars 90 d 0.3 g | 44.3564 | 0.38 → 0.14 s | 0.009 m / 1.7e-5 m/s |
| Mercury 8 d 1 g | 557.0380 | 0.43 → 0.10 s | 9.9 m / 4.1e-3 m/s |
| Uranus 400 d 1 g | 182.3393 | 0.40 → 0.12 s | 0.07 m / 2.2e-5 m/s |
| Mars 20 d 0.3 g | 270.8500 | 0.38 → 0.07 s | 0.02 m / 3.5e-5 m/s |
| Mars 10 d 0.3 g | 607.8259 | 0.42 → 0.09 s | 2.2 m / 1.3e-3 m/s |
| Uranus 35 d 1 g | 1984.4479 | 0.63 → 0.12 s | 2.2 m / 3.7e-4 m/s |

Solve at 64 samples with brake 512 is 0.07–0.09 s (~5×). In the 1 g sweep a
cheap **probe was never optimistic** against the certified build (probe ✓ ⇒
cert ✓), which is what makes "search cheap, certify the answer" safe; a refused
certificate must continue the search upward, not be reported.

## Step 1 result (2026-11-20)

Done on this branch. The departure solve's trial burns integrate at **128
samples** (`PinnedDepartureSamples`), the reported departure burn stays at
**512**, the certified brake is **512** (`BrakeSamples`), a fast probe keeps its
own 2,048-sample brake, and the coast is re-solved from the reported burn's own
end. A coarse solve that stalls retries once at 512 (needed by Mono at Uranus
35 d; .NET converges first).

Warm Release .NET harness, medians of five after two warm-up builds:

| Plan | Δv (both) | Build before → after | Worst seam |
|---|---|---:|---|
| Mars 90 d 0.3 g | 44.3564 | 0.16 → **0.05 s** | 0.009 m / 1.7 × 10⁻⁵ m/s |
| Mercury 8 d 1 g | 557.0380 | 0.28 → **0.09 s** | 9.9 m / 4.1 × 10⁻³ m/s |
| Uranus 400 d 1 g | 182.3393 | 0.18 → **0.06 s** | 0.07 m / 2.2 × 10⁻⁵ m/s |
| Mars 20 d 0.3 g | 270.8500 | 0.23 → **0.06 s** | 0.02 m / 3.5 × 10⁻⁵ m/s |
| Mars 10 d 0.3 g | 607.8259 | 0.26 → **0.08 s** | 2.2 m / 1.3 × 10⁻³ m/s |
| Uranus 35 d 1 g | 1984.4479 | 0.40 → **0.11 s** | 2.2 m / 3.7 × 10⁻⁴ m/s |

`Plan()` (warm, median of three): Uranus 1 g **3.11 → 0.22 s**, Uranus 0.3 g
**0.48 → 0.21 s**, Mars 0.3 g **1.97 → 0.60 s**, Mercury 1 g **0.56 → 0.25 s**,
Jupiter 0.3 g **0.59 → 0.22 s**. Floors re-measured: Mars 6.73715 d, Mercury
3.21610, Uranus 0.3 g 40.71332, and Jupiter 13.33520 are unchanged;
**Uranus 1 g moves from 34.16041 to 30.55886 d** (2,299.29 km/s) because the
certified 128-sample solve converges where 512 stalled at ~1.1 × 10⁻⁵ m/s. It is
certified (`IsFeasibleAt`, worst seam 6.7 m / 8.4 × 10⁻⁴ m/s, cold rebuild at
0.4 m / 1.0 × 10⁻³ m/s), and the 1 g window becomes **30.56–91.68 d**. The six
fixed-duration anchors did not move; the details are in
[planning performance](Docs/technical/planning-performance.md#step-1-the-builds-own-cost-2026-11-20).

## Steps

- [x] **Step 0** — plan recorded, handoff live.
- [x] **Step 1 — cheap certified build.** Done: solve at 128 samples, reported
      burn at 512, coast re-solved from the reported end, certified brake at 512
      (probes untouched), fallback retry for the Mono stall. Six Δv anchors
      identical to four decimals; builds 2.9–3.9× faster; seams metres / sub-m/s.
      `tools/check.sh --tests flight` passed.
- [ ] **Step 2 — cheap search probes.** Probe quality (solve 64, fast peak) for
      full-field floor probes; the answer is rebuilt at certified quality, and a
      refused certificate continues upward. **This is next**: the probe path is
      now the untouched one, and it is the remaining `Plan()` cost.
- [ ] **Step 3 — a bracket for the scatter.** Replace the 15%-walk +
      0.05%-bisection with coarse growth and a 1% bisection (~7 probes). Floors
      and windows are **re-measured and become the new anchors**; this is the
      only place the answer legitimately moves (up to ~0.4 d at Uranus), and it
      removes the .NET-vs-Mono floor divergence.
- [ ] **Step 4 — editor measurement and close-out.** Timed first plots for the
      five cases in the warm test project, docs (planning performance,
      floors/windows, testing anchors), log, handoff cleared.

## Verification

Last result on this branch: `tools/check.sh --tests flight` passed the flight
category in **125 s**, 0 failed; `.venv/bin/python tools/validate_okf.py` clean
(41 concepts, 0 warnings, 0 errors, the same check `check.sh` runs first).

```bash
tools/check.sh --tests flight        # any C# change
tools/check.sh --tests presentation,smoke   # UI/behaviour changes
tools/check.sh --fast                # docs and tools
.venv/bin/python tools/validate_okf.py
```

Editor timing, warm test project (`.cache/test-project`), a throwaway test that
calls `planner.Plan` and logs `Stopwatch` times via `-testFilter` — do not add
a timing assertion to the suite.

The .NET harness lives at `.cache/plan-harness` (gitignored, **not committed**):
`dotnet build -c Release` then `dotnet bin/Release/net10.0/harness.dll …`. It
compiles `Assets/Scripts/Core/**` and `Assets/Scripts/Simulation/**` against a
small `UnityEngine` shim and loads the shipped catalogue; `Program.cs` has
`plan`/`duration`/`session` modes and `Probe.cs` has `sweep`/`build`/`knobs`/
`compare`/`seam` modes. Adapt it when the API changes.

## Traps

- **Do not loosen `DepartureSolveToleranceMps` (1e-5) or the 1 km / 1 m/s seam
  gate**, and do not revive the `best × coast ≤ 250 m` stall acceptance: combined
  with a cheaper build it collapsed the Uranus floor to 16.21 d with 5,087 km/s
  plans. Floors may be re-measured; accuracy may not move outside the gate.
- The brake pin's 1e-6 m/s stop is inconsistent with the departure solve's
  1e-5; a near-floor build whose brake oscillates at ~1.1e-5 runs its 12-pass
  cap. Step 1 may stop the pin when it stops improving, but it must not change
  a converged plan.
- A coarse departure solve leaves the reported burn and the coast from the same
  start but different sample counts; re-solve the coast from the **reported**
  burn's end or the position seam breaks. The 128-sample solve also stalls just
  over the tolerance in Mono at Uranus 35 d (1.274 × 10⁻⁵ m/s); the one retry at
  512 (`TrySolvePinnedDepartureWithFallback`) is what keeps that anchor a plan.
  Do not remove it, and do not give fast probes a retry — that would change the
  search.
- Step 1 moved only the Uranus 1 g floor (34.16041 → 30.55886 d) because the
  certified 128-sample solve converges where 512 stalled; Uranus 0.3 g stays
  40.71332 d and is still conservative (direct certified builds close at 40.5,
  40.0, and 39.0 d, but the fast probes refuse them). Step 3 owns the bracket and
  the windows; do not treat the step-7 floor table as current.
- `FlightSession.Plot` stays synchronous this plan. Do not add async, a
  prefetch, or UI state; that is a later plan and the user asked for it to wait.
- The user's Unity editor is open on `main`: never force-close it, never run
  batch Unity against the main checkout.
