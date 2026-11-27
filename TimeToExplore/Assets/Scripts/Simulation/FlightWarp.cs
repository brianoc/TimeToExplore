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
    /// At each end the scale holds near 1x for a long stretch and then climbs
    /// along a quartic. The ends are where the ship is near a body and the
    /// departure, capture, and parking are the parts worth watching; a long,
    /// slow climb keeps them on screen instead of flashing past in the first
    /// second. Each end covers <see cref="EndFraction"/> of the target duration,
    /// and a quartic ramp covers a fifth of its span, so the cruise rate that
    /// makes the whole trip last the target time is
    /// <c>(flight - target) / (target - 2·ramp + 2·ramp/5) + 1</c>. The curve is
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

        /// <summary>Fraction of the target duration spent easing in and out.</summary>
        public const double EndFraction = 0.4;

        /// <summary>
        /// Power of the end ease. A quartic holds close to 1x far longer than a
        /// smoothstep, then climbs steeply, so the local departure and capture
        /// have time to read while the middle still crosses quickly.
        /// </summary>
        public const double EndExponent = 4.0;

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
            // The plateau is target - 2·ramp long; each ramp adds 1/(k+1) of
            // its own length instead of all of it.
            double holding = target - 2.0 * ramp + 2.0 * ramp / (EndExponent + 1.0);
            double cruise = (flightSeconds - target) / holding + 1.0;
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

            double leaving = EndEase(realElapsedSeconds / ramp);
            double arriving = EndEase((target - realElapsedSeconds) / ramp);
            return 1.0 + (cruise - 1.0) * Math.Min(leaving, arriving);
        }

        /// <summary>The length of one end's slow climb.</summary>
        private static double Ramp(double flightSeconds) =>
            EndFraction * TargetRealSeconds(flightSeconds);

        private static double EndEase(double t)
        {
            t = Math.Clamp(t, 0.0, 1.0);
            return Math.Pow(t, EndExponent);
        }
    }
}
