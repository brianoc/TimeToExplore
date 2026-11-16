using System;
using TimeToExplore.Core;

namespace TimeToExplore.Simulation
{
    /// <summary>
    /// Caps time warp so the simulation cannot skip the parts a player needs to
    /// see. The world runs ahead of the ship otherwise: at 1,000,000× a 60 fps
    /// frame spans 4.6 simulated hours, which crosses a low parking orbit more
    /// than twice and reaches a burn boundary without showing it.
    ///
    /// Three physical limits set the frame's simulated span, and the smallest
    /// wins:
    ///
    /// * <b>Clearance</b> — a quarter of the time to reach the nearest body's
    ///   surface at the current relative speed.
    /// * <b>Local dynamics</b> — 2% of the nearest body's orbital timescale
    ///   `sqrt(r³/GM)`, so an orbit stays a curve rather than a jump.
    /// * <b>Plan events</b> — 5% of the time remaining to the next arc boundary
    ///   or the arrival, so the countdown paces itself down to the event.
    ///
    /// The result is expressed in the clock's existing power-of-ten scales, so
    /// the HUD keeps showing an honest scale. The player's requested scale is
    /// always the ceiling; the policy only ever drops below it.
    ///
    /// Plain C#, no Unity dependency, so the policy is verified in EditMode.
    /// </summary>
    public static class WarpPolicy
    {
        /// <summary>Fraction of the time-to-surface that may pass in one frame.</summary>
        public const double ClearanceFraction = 0.25;

        /// <summary>Fraction of the local orbital timescale that may pass in one frame.</summary>
        public const double LocalTimescaleFraction = 0.02;

        /// <summary>Fraction of the time remaining to the next event that may pass in one frame.</summary>
        public const double EventFraction = 0.05;

        /// <summary>Floor on the allowed frame span, so the drop never stalls the clock.</summary>
        public const double MinFrameSeconds = 1.0;

        /// <summary>Real seconds a trip should take when Go To chooses a warp.</summary>
        public const double DefaultRealSecondsPerTrip = 30.0;

        /// <summary>
        /// Simulated seconds one frame may span at this state. The gravity field
        /// is prepared here, so calling this from the HUD is safe.
        /// </summary>
        public static double MaxFrameSeconds(ShipState state, double julianDate,
            SolarSystemModel model, GravityField gravity, TrajectoryPlan plan)
        {
            if (gravity == null) throw new ArgumentNullException(nameof(gravity));

            double maxSeconds = double.PositiveInfinity;

            gravity.Prepare(state.PositionM, julianDate);
            BodyRuntime nearest = model != null && gravity.NearestBodyId != null
                ? model.Get(gravity.NearestBodyId)
                : null;

            if (nearest != null)
            {
                model.ComputeState(nearest, julianDate,
                    out Vector3d bodyPosition, out Vector3d bodyVelocity);

                double distance = Math.Max(Vector3d.Distance(state.PositionM, bodyPosition),
                    Math.Max(nearest.RadiusM, 1.0));
                double relativeSpeed = (state.VelocityMps - bodyVelocity).Magnitude;
                double clearance = Math.Max(gravity.NearestClearanceM, 0.0);
                double crossing = clearance / Math.Max(relativeSpeed, 1.0);
                maxSeconds = Math.Min(maxSeconds, AtLeastOneFrame(crossing * ClearanceFraction));

                if (nearest.Gm > 0.0)
                {
                    double timescale = Math.Sqrt(distance * distance * distance / nearest.Gm);
                    maxSeconds = Math.Min(maxSeconds,
                        AtLeastOneFrame(timescale * LocalTimescaleFraction));
                }
            }

            if (plan != null)
            {
                double boundary = plan.NextBoundaryJulianDate(julianDate);
                if (double.IsFinite(boundary))
                {
                    double remaining = (boundary - julianDate) * JulianDate.SecondsPerDay;
                    maxSeconds = Math.Min(maxSeconds, AtLeastOneFrame(remaining * EventFraction));
                }
            }

            return maxSeconds;
        }

        private static double AtLeastOneFrame(double seconds) =>
            double.IsNaN(seconds) ? MinFrameSeconds : Math.Max(seconds, MinFrameSeconds);

        /// <summary>
        /// Largest clock scale index that keeps one frame inside the allowed
        /// span. Never returns an index above the one the player asked for.
        /// </summary>
        public static int ClampScaleIndex(double maxFrameSeconds, double realDeltaSeconds,
            int requestedIndex)
        {
            requestedIndex = Math.Clamp(requestedIndex, 0, SimulationClock.TimeScales.Length - 1);
            if (!double.IsFinite(maxFrameSeconds)) return requestedIndex;

            double deltaSeconds = Math.Max(realDeltaSeconds, 1e-4);
            int index = requestedIndex;
            while (index > 0 && SimulationClock.TimeScales[index] * deltaSeconds > maxFrameSeconds)
                index--;
            return index;
        }

        /// <summary>
        /// The scale Go To should raise the clock to, so a multi-day trip is
        /// watchable rather than stalled. The player can still override it.
        /// </summary>
        public static int SuggestInitialScaleIndex(double flightTimeSeconds,
            double realSeconds = DefaultRealSecondsPerTrip)
        {
            if (!(flightTimeSeconds > 0.0)) return 0;

            double wanted = realSeconds > 0.0 ? flightTimeSeconds / realSeconds : 1.0;
            int index = 0;
            for (int i = 1; i < SimulationClock.TimeScales.Length; i++)
                if (SimulationClock.TimeScales[i] <= wanted) index = i;
            return index;
        }

        public static int SuggestInitialScaleIndex(TrajectoryPlan plan,
            double realSeconds = DefaultRealSecondsPerTrip) =>
            plan == null ? 0 : SuggestInitialScaleIndex(plan.FlightTimeSeconds, realSeconds);
    }
}
