using System.Diagnostics;

namespace TimeToExplore.Simulation
{
    /// <summary>Which quality a burn–coast–burn build ran at. The planner
    /// reports only <see cref="Certified"/>; the other two are search
    /// inputs.</summary>
    public enum PlanBuildQuality
    {
        /// <summary>The full-field build whose plan is returned.</summary>
        Certified,

        /// <summary>A floor probe on the two-body search model.</summary>
        FastProbe,

        /// <summary>A floor probe that integrates the summed field.</summary>
        FullFieldProbe
    }

    /// <summary>
    /// Counters and coarse phase timings for one planner call. Attach one to
    /// <see cref="TripPlanner.Metrics"/> and reset it before the call to
    /// measure; the planner only writes to it and never reads it, so attaching
    /// one changes no plan, no tolerance, and no pass cap.
    ///
    /// The counts are exact. The timings are <see cref="Stopwatch"/> timestamps
    /// taken at phase boundaries: they split the wall time, they are not
    /// microsecond truth. Each timestamp costs a few tens of nanoseconds, and a
    /// build takes hundreds, so the instrumentation itself is below the
    /// run-to-run spread. A phase that is not bracketed (the coast skeleton, the
    /// capture chain, the seam arithmetic) falls into the remainder between the
    /// measured phases and the call's own wall time.
    /// </summary>
    public sealed class PlanMetrics
    {
        private long _pinLoopTicks;
        private long _pinBrakeTicks;
        private long _pinSolveTicks;
        private long _settleLoopTicks;
        private long _burnOnlySeamTicks;
        private long _certifiedBurnCreateTicks;
        private long _certifiedBurnMeasureTicks;
        private long _probeBurnCreateTicks;
        private long _probeBurnMeasureTicks;
        private long _fastestCoastTicks;
        private long _fastestBurnOnlyTicks;

        /// <summary>Certified burn–coast–burn builds (the ones that can be
        /// reported).</summary>
        public int BuildsCertified { get; private set; }

        /// <summary>Floor probes on the two-body search model.</summary>
        public int BuildsFastProbe { get; private set; }

        /// <summary>Floor probes that keep the summed field.</summary>
        public int BuildsFullFieldProbe { get; private set; }

        /// <summary>Near-floor pin retries: a certified build rebuilt once with
        /// <see cref="TripPlanner"/>'s longer pass cap.</summary>
        public int PinRetries { get; private set; }

        /// <summary>Outer brake/departure passes, all build qualities.</summary>
        public int PinPasses { get; private set; }

        public int CertifiedPinPasses { get; private set; }
        public int FastPinPasses { get; private set; }
        public int FullFieldProbePinPasses { get; private set; }

        /// <summary>Settle passes after the pin loop, certified builds only.</summary>
        public int SettlePasses { get; private set; }

        /// <summary>Fixed-date burn-only builds, one per moving-target
        /// pass.</summary>
        public int BurnOnlyBuilds { get; private set; }

        /// <summary>Calls to <see cref="TripPlanner.PlanFastest"/>.</summary>
        public int FastestCalls { get; private set; }

        /// <summary>Finite-burn arcs built by a certified plan: the departure
        /// burn and the brake, each integrated and tabulated at its sample
        /// count. The two reported arcs are 512 samples each at the epoch.</summary>
        public int CertifiedBurnArcs { get; private set; }

        public int CertifiedBurnSamples { get; private set; }

        /// <summary>Finite-burn arcs built by a search probe.</summary>
        public int ProbeBurnArcs { get; private set; }

        public int ProbeBurnSamples { get; private set; }

        /// <summary>The coast floor's own flight time from the last
        /// <see cref="FastestCalls"/>, or NaN. A recorded plan value, not a
        /// timing: it proves the floor the measurement ran at.</summary>
        public double FastestCoastPlanSeconds { get; private set; } = double.NaN;

        /// <summary>The burn-only crossing's own flight time from the last
        /// <see cref="FastestCalls"/>, or NaN when it was not built.</summary>
        public double FastestBurnOnlyPlanSeconds { get; private set; } = double.NaN;

        /// <summary>Wall time inside the outer brake/departure loop, all
        /// passes and all build qualities.</summary>
        public double PinLoopSeconds => FromTicks(_pinLoopTicks);

        /// <summary>The part of <see cref="PinLoopSeconds"/> spent integrating
        /// the brake backwards through its samples.</summary>
        public double PinBrakeSeconds => FromTicks(_pinBrakeTicks);

        /// <summary>The part of <see cref="PinLoopSeconds"/> spent in the
        /// pinned departure solve, including its coarse-to-certified
        /// fallback.</summary>
        public double PinSolveSeconds => FromTicks(_pinSolveTicks);

        public double SettleLoopSeconds => FromTicks(_settleLoopTicks);

        /// <summary>The burn-only seam solve, across its attempted seeds.</summary>
        public double BurnOnlySeamSeconds => FromTicks(_burnOnlySeamTicks);

        /// <summary>Integrating and tabulating the certified finite-burn
        /// arcs.</summary>
        public double CertifiedBurnCreateSeconds => FromTicks(_certifiedBurnCreateTicks);

        /// <summary>Measuring the certified finite-burn arcs (peak, delta-v,
        /// clearance).</summary>
        public double CertifiedBurnMeasureSeconds =>
            FromTicks(_certifiedBurnMeasureTicks);

        public double ProbeBurnCreateSeconds => FromTicks(_probeBurnCreateTicks);
        public double ProbeBurnMeasureSeconds => FromTicks(_probeBurnMeasureTicks);

        /// <summary>Wall time in the coast floor inside the last
        /// <see cref="TripPlanner.PlanFastest"/>.</summary>
        public double FastestCoastSeconds => FromTicks(_fastestCoastTicks);

        /// <summary>Wall time in the burn-only crossing inside the last
        /// <see cref="TripPlanner.PlanFastest"/>.</summary>
        public double FastestBurnOnlySeconds => FromTicks(_fastestBurnOnlyTicks);

        /// <summary>Drop every count and timing. Call before the measured
        /// call.</summary>
        public void Reset()
        {
            _pinLoopTicks = 0;
            _pinBrakeTicks = 0;
            _pinSolveTicks = 0;
            _settleLoopTicks = 0;
            _burnOnlySeamTicks = 0;
            _certifiedBurnCreateTicks = 0;
            _certifiedBurnMeasureTicks = 0;
            _probeBurnCreateTicks = 0;
            _probeBurnMeasureTicks = 0;
            _fastestCoastTicks = 0;
            _fastestBurnOnlyTicks = 0;

            BuildsCertified = 0;
            BuildsFastProbe = 0;
            BuildsFullFieldProbe = 0;
            PinRetries = 0;
            PinPasses = 0;
            CertifiedPinPasses = 0;
            FastPinPasses = 0;
            FullFieldProbePinPasses = 0;
            SettlePasses = 0;
            BurnOnlyBuilds = 0;
            FastestCalls = 0;
            CertifiedBurnArcs = 0;
            CertifiedBurnSamples = 0;
            ProbeBurnArcs = 0;
            ProbeBurnSamples = 0;
            FastestCoastPlanSeconds = double.NaN;
            FastestBurnOnlyPlanSeconds = double.NaN;
        }

        internal static long Timestamp() => Stopwatch.GetTimestamp();

        private static double FromTicks(long ticks) =>
            ticks <= 0 ? 0.0 : (double)ticks / Stopwatch.Frequency;

        internal void NoteBuild(PlanBuildQuality quality)
        {
            switch (quality)
            {
                case PlanBuildQuality.Certified:
                    BuildsCertified++;
                    break;
                case PlanBuildQuality.FastProbe:
                    BuildsFastProbe++;
                    break;
                default:
                    BuildsFullFieldProbe++;
                    break;
            }
        }

        internal void NotePinPass(PlanBuildQuality quality)
        {
            PinPasses++;
            switch (quality)
            {
                case PlanBuildQuality.Certified:
                    CertifiedPinPasses++;
                    break;
                case PlanBuildQuality.FastProbe:
                    FastPinPasses++;
                    break;
                default:
                    FullFieldProbePinPasses++;
                    break;
            }
        }

        internal void AddPinLoop(long ticks) => _pinLoopTicks += ticks;
        internal void AddPinBrake(long ticks) => _pinBrakeTicks += ticks;
        internal void AddPinSolve(long ticks) => _pinSolveTicks += ticks;
        internal void AddSettleLoop(long ticks) => _settleLoopTicks += ticks;
        internal void AddBurnOnlySeam(long ticks) => _burnOnlySeamTicks += ticks;
        internal void NoteSettlePass() => SettlePasses++;
        internal void NotePinRetry() => PinRetries++;
        internal void NoteBurnOnlyBuild() => BurnOnlyBuilds++;

        internal void AddFastestCoast(long ticks, double planSeconds)
        {
            FastestCalls++;
            _fastestCoastTicks += ticks;
            FastestCoastPlanSeconds = planSeconds;
        }

        internal void AddFastestBurnOnly(long ticks, double planSeconds)
        {
            _fastestBurnOnlyTicks += ticks;
            FastestBurnOnlyPlanSeconds = planSeconds;
        }

        internal void AddBurn(int samples, long createTicks, long measureTicks,
            bool certified)
        {
            if (certified)
            {
                CertifiedBurnArcs++;
                CertifiedBurnSamples += samples;
                _certifiedBurnCreateTicks += createTicks;
                _certifiedBurnMeasureTicks += measureTicks;
            }
            else
            {
                ProbeBurnArcs++;
                ProbeBurnSamples += samples;
                _probeBurnCreateTicks += createTicks;
                _probeBurnMeasureTicks += measureTicks;
            }
        }
    }
}
