using System;
using TimeToExplore.Core;

namespace TimeToExplore.Simulation
{
    /// <summary>
    /// The automatic warp curve for a flight. Time starts at real time while
    /// the camera settles, rises to a cruise rate that paces the trip to a
    /// watchable length, and falls back to real time for the final approach.
    /// The curve is a function of the real seconds since the flight began.
    ///
    /// The pacing is a function of the plan's own duration, not a table. A
    /// reference trip of <see cref="ReferenceFlightDays"/> takes
    /// <see cref="ReferenceRealSeconds"/>; each e-fold of extra flight time adds
    /// <see cref="SecondsPerLogDay"/> real seconds, so a three-week crossing is
    /// twice the one-week crossing and a four-month one is not four times as
    /// long. The result is clamped to <see cref="MinRealSeconds"/> and
    /// <see cref="MaxRealSeconds"/>.
    ///
    /// At each end the scale blends between 1x and the cruise rate over
    /// <see cref="RampSeconds"/> real seconds. Each ramp covers half the area of
    /// its span, so the cruise rate that makes the whole trip last the target
    /// time is <c>(flight - target) / (target - ramp) + 1</c>. The curve is
    /// therefore exact: integrating it over the target duration advances the
    /// flight by its whole duration, and the clock is at 1x at both ends.
    ///
    /// Plain C#, no Unity dependency, so the curve is verified in EditMode.
    /// </summary>
    public static class FlightWarp
    {
        /// <summary>Flight time that takes <see cref="ReferenceRealSeconds"/>.</summary>
        public const double ReferenceFlightDays = 6.7;

        /// <summary>Real seconds the reference flight should take.</summary>
        public const double ReferenceRealSeconds = 20.0;

        /// <summary>Extra real seconds per e-fold of flight time.</summary>
        public const double SecondsPerLogDay = 17.0;

        /// <summary>Shortest real duration, for a local or very fast trip.</summary>
        public const double MinRealSeconds = 12.0;

        /// <summary>Longest real duration, so an outer-system crossing still ends.</summary>
        public const double MaxRealSeconds = 60.0;

        /// <summary>Real seconds over which the scale leaves and rejoins 1x.</summary>
        public const double RampSeconds = 3.0;

        /// <summary>Ceiling on the cruise rate, well above any planned trip.</summary>
        public const double MaxScale = 1.0e7;

        /// <summary>
        /// Real seconds a trip of this duration should take. The formula is
        /// logarithmic so the outer system grows slowly: about 20 s for a
        /// one-week crossing, 40 s for three weeks, and 55 s for two months.
        /// </summary>
        public static double TargetRealSeconds(double flightSeconds)
        {
            if (!(flightSeconds > 0.0)) return MinRealSeconds;

            double days = flightSeconds / JulianDate.SecondsPerDay;
            double target = ReferenceRealSeconds
                            + SecondsPerLogDay * Math.Log(days / ReferenceFlightDays);
            return Math.Clamp(target, MinRealSeconds, MaxRealSeconds);
        }

        /// <summary>
        /// The scale held through the middle of the trip, chosen so the ramps at
        /// either end still leave the whole trip at its target duration.
        /// </summary>
        public static double CruiseScale(double flightSeconds)
        {
            if (!(flightSeconds > 0.0)) return 1.0;

            double target = TargetRealSeconds(flightSeconds);
            double ramp = Ramp(flightSeconds);
            double cruise = (flightSeconds - target) / (target - ramp) + 1.0;
            return Math.Clamp(cruise, 1.0, MaxScale);
        }

        /// <summary>
        /// The scale for one frame, given the plan's whole duration and the real
        /// seconds since the flight began. Starts and ends at 1x, and never
        /// exceeds the cruise rate.
        /// </summary>
        public static double ScaleAt(double flightSeconds, double realElapsedSeconds)
        {
            if (!(flightSeconds > 0.0)) return 1.0;

            double target = TargetRealSeconds(flightSeconds);
            double ramp = Ramp(flightSeconds);
            double cruise = CruiseScale(flightSeconds);

            double leaving = SmoothStep(realElapsedSeconds / ramp);
            double arriving = SmoothStep((target - realElapsedSeconds) / ramp);
            return 1.0 + (cruise - 1.0) * Math.Min(leaving, arriving);
        }

        /// <summary>
        /// The ramp length, shortened for a target so short that the two ramps
        /// would overlap.
        /// </summary>
        private static double Ramp(double flightSeconds) =>
            Math.Min(RampSeconds, 0.5 * TargetRealSeconds(flightSeconds));

        private static double SmoothStep(double t)
        {
            t = Math.Clamp(t, 0.0, 1.0);
            return t * t * t * (t * (6.0 * t - 15.0) + 10.0);
        }
    }
}
