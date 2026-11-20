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

## Step 2 result (2026-11-20)

Done on this branch. The floor search now has a **full-field probe** quality
between the fast two-body probe and the certified build: burns integrate the
summed field, the pinned departure solve uses **64 samples** with no 512
fallback, the brake stays at **512**, and the measurement is the cheap
`MeasureFast` (fixed burns at their commanded acceleration, conics at zero, a
break against its own body). It keeps the certified fixed-point stops and skips
the settle passes. The fast probe is untouched.

`PlanBurnCoastBurnMinimum` certifies every candidate with the default build and
returns it only when `IsFeasibleAt` holds; a refused certificate continues the
search **above** it, seeded at `candidate × 1.2` with the refusal as the free
`[cached]` low end. The near-floor 0.2% retry runs before the wide bisection, and
at most **three** certificates are spent before the last refusal is reported.
No tolerance and no seam gate moved.

Warm Release .NET harness, `Plan()` (median of three; probe counts from the
trace):

| Case | `Plan()` before → after | Floor | Search |
|---|---:|---:|---|
| Uranus 0.3 g | 0.213 → **0.230 s** | 40.71332 d (both) | certificate accepted |
| Uranus 1 g | 0.222 → **0.239 s** | 30.55886 d (both) | certificate accepted |
| Mars 0.3 g | 0.612 → **0.778 s** | 6.73715 d (both) | 5 probes, 2 certificates |
| Mercury 1 g | 0.246 → **0.267 s** | 3.21610 d (both) | certificate accepted |
| Jupiter 0.3 g | 0.224 → **0.240 s** | 13.33520 d (both) | certificate accepted |

Editor first `Plot()` in the warm test project (planner plus window):

| Case | Before → after | Floor before → after |
|---|---:|---|
| Uranus 0.3 g | 1.806 → **1.713 s** | 33.60438 d (both) |
| Uranus 1 g | 1.230 → **1.232 s** | 31.25256 d (both) |
| Mars 0.3 g | 4.009 → **4.558 s** | 6.73715 d (both) |
| Mercury 1 g | 2.288 → **2.356 s** | 3.21610 d (both) |
| Jupiter 0.3 g | **14.555 → 9.552 s** | **13.27662 → 13.34149 d**, window 13.341–40.024 |

Jupiter is the win the step was for (the old full search paid ~14 certified
builds). Mars is the one regression: the refused certificate costs a second
certificate plus the five-probe continuation, and the bisection is still 0.05%;
step 3's coarse growth and 1% bisection is the lever that removes it. The
Jupiter editor floor moved +0.49% because Mono's scatter refuses the fast high;
it is re-measured and certified, every arc has finite clearance and measured
delta-v, and every seam is inside the 1 km / 1 m/s gate. The six fixed-duration
Δv anchors are unchanged to four decimals.

## Step 3 result (2026-11-20)

Done on this branch. The user's question was why the editor's Jupiter was slow.
The answer is that the editor ran a **different search**, not the same work
slower: its fast high was **13.07211 d** and the certificate **refused** it at
the chain gate (*the brake does not close on the capture entry*, 28,069.5 m /
1.0 m/s), so the full-field continuation spent **14 probes (+1 cached)** and a
second certificate before accepting 13.34149 d — 34 builds, 9.55 s. .NET's fast
high is 13.33520 d and certifies at once in 0.24 s.

The refusal was the outer brake/departure fixed point running its **twelve-pass
cap** with the mismatch still falling ~3× a pass (0.425 → **0.137 m/s at pass
11**), which is the 28 km the gate reads. The 128-sample departure solve had
converged (no 512 fallback), the peak was 4.6 m/s², and a duration nudge does not
help (+0.2% / +0.5% / 13.2 d still miss by 2.4 / 2.0 / 1.5 km at twelve passes).

The fix is one **solver retry inside the certified build**: `Build`'s certified
burn–coast–burn path rebuilds once at `RetryOuterPasses = 24` when the plan
refused at the chain gate after its pin ran `DefaultOuterPasses = 12` without
converging (`BuildResult.PinStalled`). No tolerance, gate, screen, or search
rule moved; a converged build never pays it; a probe never retries. The same
duration then certifies (peak 9.700 m/s², 2,270.99 km/s, worst seam 93.35 m /
8.0 × 10⁻³ m/s).

.NET `Plan()` (median of three) and the editor's first `Plot()`:

| Case | .NET before → after | Editor before → after | Editor floor after |
|---|---:|---:|---:|
| Uranus 0.3 g | 0.230 → **0.226 s** | 1.713 → **1.87 s** | 33.60438 d |
| Uranus 1 g | 0.239 → **0.233 s** | 1.232 → **1.25 s** | 31.25256 d |
| Mars 0.3 g | 0.778 → **0.464 s** | 4.558 → **3.25 s** | **6.73042 d**, window 6.730–20.191 |
| Mercury 1 g | 0.267 → **0.261 s** | 2.356 → **2.42 s** | 3.21610 d |
| Jupiter 0.3 g | 0.240 → **0.231 s** | **9.552 → 3.07 s** | **13.07211 d**, window 13.072–39.216 |

Uranus and Mercury take the identical path to step 2, so their spread is noise.
The six fixed-duration Δv anchors are unchanged to four decimals in both
runtimes. Step 3's floor/window table is now the current one; step 4 re-brackets
the search and re-measures the floors again.

## Steps

- [x] **Step 0** — plan recorded, handoff live.
- [x] **Step 1 — cheap certified build.** Done: solve at 128 samples, reported
      burn at 512, coast re-solved from the reported end, certified brake at 512
      (probes untouched), fallback retry for the Mono stall. Six Δv anchors
      identical to four decimals; builds 2.9–3.9× faster; seams metres / sub-m/s.
      `tools/check.sh --tests flight` passed.
- [x] **Step 2 — cheap search probes.** Done: a full-field probe quality
      (summed-field burns, solve 64, no fallback, brake 512, cheap measurement),
      the answer rebuilt and certified before it leaves `Plan()`, a refused
      certificate continuing upward at `candidate × 1.2` with a free `[cached]`
      low, and a test proving the returned floor is certified. `tools/check.sh
      --tests flight` passed.
- [x] **Step 3 — why the editor's Jupiter is slow.** Done: traced the editor's
      Jupiter 0.3 g plan — 18 fast probes at 13.07211 d, a chain-gate refusal
      after the **twelve-pass pin cap** (0.137 m/s at pass 11 → 28 km), 14
      full-field probes and a second certificate, 34 builds. Fixed with **one
      certified-build solver retry at 24 passes** when the pin stalled and the
      chain refused; no tolerance or gate moved. Jupiter editor **9.55 → 3.07 s**
      (floor 13.07211 d, window 13.072–39.216), Mars **4.56 → 3.25 s** (floor
      6.73042 d); .NET Mars floor 6.73042 d. Six Δv anchors unchanged.
      `tools/check.sh --tests flight` passed.
- [ ] **Step 4 — a bracket for the scatter.** Replace the 15%-walk +
      0.05%-bisection with coarse growth and a 1% bisection (~7 probes). Floors
      and windows are **re-measured and become the new anchors**; this is the
      only place the answer legitimately moves (up to ~0.4 d at Uranus), and it
      removes the .NET-vs-Mono floor divergence. **This is next.**
- [ ] **Step 5 — editor measurement and close-out.** Timed first plots for the
      five cases in the warm test project, docs (planning performance,
      floors/windows, testing anchors), log, handoff cleared.

## Verification

Last result on this branch (step 3): `tools/check.sh --tests flight` passed the
flight category in **133 s**, 0 failed; `.venv/bin/python tools/validate_okf.py`
clean (41 concepts, 0 warnings, 0 errors, the same check `check.sh` runs first).

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
  1e-5; a near-floor build whose brake plateaus above it runs its pass cap. That
  cap is now `DefaultOuterPasses = 12`, and a **chain-gate refusal after a stalled
  pin** retries once at `RetryOuterPasses = 24` (`BuildResult.PinStalled`, in
  `Build`'s certified burn–coast–burn branch). This is not a loosened tolerance:
  the same fixed point gets more passes, and the gate still decides. Do not make
  the retry unconditional — that cost Uranus 0.3 g ~1.1 s and Mercury ~0.7 s in
  the editor, for no floor move — and do not give a probe a retry.
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
  40.0, and 39.0 d, but the fast probes refuse them). Step 2 moved the **editor's**
  Jupiter floor to 13.34149 d; step 3's pin retry certifies the editor's fast high
  at **13.07211 d** (window 13.072–39.216) and moves Mars 0.3 g to **6.73042 d**
  (window 6.730–20.191) in both runtimes, while the .NET Jupiter floor stays
  13.33520 d. Step 4 owns the bracket and re-measures the floors again; do not
  treat the step-7, step-1, or step-2 floor tables as current.
- **The certificate is the authority.** `PlanBurnCoastBurnMinimum` must rebuild
  the selected duration with the default (certified) build before returning; a
  probe plan has an infinite clearance and zero coast delta-v and is never an
  answer. The continuation is bounded at `MaxFloorCertificates = 3`; after that
  the last refusal is reported with its reason, not the last probe. After step 3
  no measured case reaches the continuation: Mars and the editor's Jupiter are
  certified by the pin retry. Do not delete the continuation — Mars 1 g still
  uses it in .NET — but do not re-add a test that requires it in Mono.
- A full-field probe is **not** the fast probe: it keeps `burnGravity = _gravity`,
  solves at 64 samples, has no fallback, keeps the 512-sample brake, and skips
  the settle passes. Do not give it the fast probe's loose stops to chase time
  (the measured cost is the pin passes) and do not trim the brake: step 4 owns
  the probe count.
- The probe's 64-sample solve can stall in Mono at a duration close to the floor
  (Uranus 0.3 g at 1.1 × its floor refused at 1.877 × 10⁻⁵ m/s), so a test that
  wants a probe plan to inspect should use a comfortable duration (the new test
  uses 1.5 × the floor), not the floor itself.
- The Unity guard can report a shutdown hang after a completed measurement run;
  the throwaway timing test writes its own `MEASURE` lines to a file, so read
  that even if the guard reports 124. A plain batch run exits cleanly and is
  acceptable for a throwaway measurement; the suite always goes through
  `check.sh`.
- `FlightSession.Plot` stays synchronous this plan. Do not add async, a
  prefetch, or UI state; that is a later plan and the user asked for it to wait.
- The user's Unity editor is open on `main`: never force-close it, never run
  batch Unity against the main checkout.
