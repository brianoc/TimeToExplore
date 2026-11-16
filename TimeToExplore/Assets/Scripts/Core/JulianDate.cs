using System;

namespace TimeToExplore.Core
{
    public static class JulianDate
    {
        public const double J2000 = 2451545.0;
        public const double SecondsPerDay = 86400.0;

        /// <summary>Julian Date (UTC) for a DateTime. The few tens of seconds
        /// between UTC and TT do not matter at this game's fidelity.</summary>
        public static double FromUtc(DateTime utc) => utc.ToOADate() + 2415018.5;

        public static DateTime ToUtc(double julianDate) => DateTime.FromOADate(julianDate - 2415018.5);

        public static string Format(double julianDate) =>
            ToUtc(julianDate).ToString("yyyy-MM-dd HH:mm:ss") + " UTC";

        public static string FormatShort(double julianDate) =>
            ToUtc(julianDate).ToString("yyyy-MM-dd HH:mm");

        public static double DaysSince(double julianDate, double epoch) => julianDate - epoch;
    }
}
