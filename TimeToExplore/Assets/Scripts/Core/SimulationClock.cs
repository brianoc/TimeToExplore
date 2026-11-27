using System;

namespace TimeToExplore.Core
{
    /// <summary>
    /// Game time. The simulation runs at 1 real second = <see cref="Scale"/>
    /// simulated seconds; positions advance by Julian date rather than by
    /// Unity's Time.timeScale, so physics and rendering stay independent.
    ///
    /// The scale is continuous, so a flight can ramp from real time up to a
    /// cruise warp and back without stepping through a ladder. The manual
    /// scales are only what the number keys select, and they are the whole
    /// ladder the prototype exposes to the player.
    /// </summary>
    public class SimulationClock
    {
        /// <summary>The fixed scales the number keys select, 1x to 1,000,000x.</summary>
        public static readonly double[] ManualScales =
            { 1, 10, 100, 1_000, 10_000, 100_000, 1_000_000 };

        public double JulianDate;
        public bool Paused;

        /// <summary>Simulated seconds per real second. Continuous and never
        /// negative; pausing makes the effective scale zero without losing the
        /// value the frame loop wrote.</summary>
        public double Scale { get; set; } = 1.0;

        public double TimeScale => Paused ? 0.0 : Scale;

        public void Advance(float realDeltaSeconds)
        {
            if (Paused || !(Scale > 0.0)) return;
            JulianDate += realDeltaSeconds * Scale / Core.JulianDate.SecondsPerDay;
        }

        /// <summary>Set a fixed scale from the manual ladder, and resume.</summary>
        public void SetScaleIndex(int index)
        {
            Scale = ManualScales[Math.Clamp(index, 0, ManualScales.Length - 1)];
            Paused = false;
        }

        /// <summary>Set any scale; used by the flight's warp profile.</summary>
        public void SetScale(double scale)
        {
            Scale = Math.Max(0.0, scale);
        }

        public void TogglePause() => Paused = !Paused;

        public string Label => Paused ? "PAUSED" : FormatScale(Scale);

        /// <summary>
        /// A scale for the HUD. Whole numbers above ten, one decimal below it,
        /// so the profile's ramp reads as a smooth count rather than a jump
        /// between powers of ten.
        /// </summary>
        public static string FormatScale(double scale)
        {
            if (!(scale > 0.0)) return "0x";
            if (scale < 10.0) return scale.ToString("0.#") + "x";
            return "x" + scale.ToString("N0");
        }
    }
}
