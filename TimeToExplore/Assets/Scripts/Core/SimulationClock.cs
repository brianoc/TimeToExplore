using System;

namespace TimeToExplore.Core
{
    /// <summary>
    /// Game time. The simulation runs at 1 real second = TimeScale simulated
    /// seconds; positions advance by Julian date rather than by Unity's
    /// Time.timeScale, so physics and rendering stay independent.
    /// </summary>
    public class SimulationClock
    {
        public static readonly double[] TimeScales = { 1, 10, 100, 1_000, 10_000, 100_000, 1_000_000 };

        public double JulianDate;
        public int ScaleIndex;
        public bool Paused;

        public double TimeScale => Paused ? 0.0 : TimeScales[ScaleIndex];

        public void Advance(float realDeltaSeconds)
        {
            if (Paused || TimeScale <= 0.0) return;
            JulianDate += realDeltaSeconds * TimeScale / Core.JulianDate.SecondsPerDay;
        }

        public void SetScaleIndex(int index)
        {
            ScaleIndex = Math.Clamp(index, 0, TimeScales.Length - 1);
            Paused = false;
        }

        public void TogglePause() => Paused = !Paused;

        public string Label
        {
            get
            {
                if (Paused) return "PAUSED";
                double scale = TimeScales[ScaleIndex];
                if (scale == 1) return "1x";
                return "x" + scale.ToString("N0");
            }
        }
    }
}
