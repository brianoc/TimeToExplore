using System.Collections.Generic;
using UnityEngine;
using TimeToExplore.Core;

namespace TimeToExplore.UI
{
    /// <summary>One line in the body details panel.</summary>
    public readonly struct DetailRow
    {
        public readonly string Label;
        public readonly string Value;
        public readonly bool IsHeading;

        private DetailRow(string label, string value, bool isHeading)
        {
            Label = label;
            Value = value;
            IsHeading = isHeading;
        }

        public static DetailRow Heading(string text) => new DetailRow(text, string.Empty, true);
        public static DetailRow Of(string label, string value) => new DetailRow(label, value, false);
    }

    /// <summary>
    /// Builds the text of the planet details screen. Pure data, so the panel's
    /// content can be tested without opening a scene. Values are formatted for
    /// a person, not for a data dump: radii in kilometres, masses in kilograms,
    /// and light time in seconds.
    /// </summary>
    public static class BodyDetails
    {
        public const double GravitationalConstant = 6.67430e-11;
        public const double EarthMassKg = 5.9722e24;
        public const double StandardGravityMps2 = 9.80665;
        public const double SpeedOfLightMps = 299_792_458.0;

        public static string KindLabel(BodyRuntime body)
        {
            if (body == null) return "Unknown";
            switch (body.Kind)
            {
                case "star": return "Star";
                case "planet": return "Planet";
                case "moon": return "Moon";
                case "asteroid": return "Asteroid";
                case "station": return "Station";
                case "barycentre": return "Barycentre";
                default: return string.IsNullOrEmpty(body.Kind) ? "Body" : body.Kind;
            }
        }

        public static string Subtitle(BodyRuntime body, SolarSystemModel model)
        {
            if (body == null) return string.Empty;

            if (body.Kind == "station")
            {
                string location = StationLocation(body, model);
                return string.IsNullOrEmpty(location) ? "Station" : $"Station · {location}";
            }

            if (body.Kind == "star" || body.Parent == null)
                return $"{KindLabel(body)} · centre of the system";

            return $"{KindLabel(body)} · orbits {body.Parent.Name}";
        }

        public static string StationLocation(BodyRuntime body, SolarSystemModel model)
        {
            LagrangeData configuration = body?.Data?.lagrange;
            if (configuration == null ||
                string.IsNullOrEmpty(configuration.primary) ||
                string.IsNullOrEmpty(configuration.secondary))
                return string.Empty;

            string primary = model?.Get(configuration.primary)?.Name ?? configuration.primary;
            string secondary = model?.Get(configuration.secondary)?.Name ?? configuration.secondary;
            return $"{primary}–{secondary} {configuration.point}";
        }

        public static List<DetailRow> Build(BodyRuntime body, SolarSystemModel model, Vector3d shipPositionM)
        {
            var rows = new List<DetailRow>();
            if (body == null) return rows;

            Vector3d sunPosition = model?.Sun?.PositionM ?? Vector3d.Zero;
            double shipDistance = Vector3d.Distance(body.PositionM, shipPositionM);
            double sunDistance = Vector3d.Distance(body.PositionM, sunPosition);

            rows.Add(DetailRow.Heading("NOW"));
            rows.Add(DetailRow.Of("Distance from ship", SolarSystemHud.FormatDistance(shipDistance)));
            rows.Add(DetailRow.Of("Heliocentric distance", SolarSystemHud.FormatDistance(sunDistance)));
            rows.Add(DetailRow.Of("Light time", FormatLightTime(LightTimeSeconds(shipDistance))));
            rows.Add(DetailRow.Of("Heliocentric speed", SolarSystemHud.FormatSpeed(body.VelocityMps.Magnitude)));

            rows.Add(DetailRow.Heading("PHYSICAL"));
            rows.Add(DetailRow.Of("Radius", SolarSystemHud.FormatDistance(body.RadiusM)));
            rows.Add(DetailRow.Of("Mass", FormatMass(MassKg(body.Gm))));
            rows.Add(DetailRow.Of("Surface gravity", FormatGravity(SurfaceGravityMps2(body))));
            rows.Add(DetailRow.Of("Escape velocity", FormatSpeedKm(SurfaceGravityMps2(body) > 0
                ? EscapeVelocityMps(body)
                : 0.0)));
            rows.Add(DetailRow.Of("Rotation", FormatRotation(body.Data.rotationPeriodS)));
            rows.Add(DetailRow.Of("Axial tilt", $"{body.Data.axialTiltDeg:0.00}°"));

            if (body.HasOrbit && body.Parent != null)
            {
                OrbitalElements elements = body.Elements;
                rows.Add(DetailRow.Heading("ORBIT"));
                rows.Add(DetailRow.Of("Parent", body.Parent.Name));
                rows.Add(DetailRow.Of("Semi-major axis", SolarSystemHud.FormatDistance(elements.semiMajorAxisM)));
                rows.Add(DetailRow.Of("Eccentricity", $"{elements.eccentricity:0.0000}"));
                rows.Add(DetailRow.Of("Inclination", $"{elements.inclinationRad * OrbitMath.Rad2Deg:0.00}°"));
                rows.Add(DetailRow.Of("Orbital period", SolarSystemHud.FormatDuration(elements.PeriodSeconds)));
                rows.Add(DetailRow.Of("Distance from parent",
                    SolarSystemHud.FormatDistance(Vector3d.Distance(body.PositionM, body.Parent.PositionM))));
            }
            else if (body.Kind == "station")
            {
                string location = StationLocation(body, model);
                if (!string.IsNullOrEmpty(location))
                {
                    rows.Add(DetailRow.Heading("LOCATION"));
                    rows.Add(DetailRow.Of("Lagrange point", location));
                }
            }

            return rows;
        }

        public static double MassKg(double gravitationalParameter) =>
            gravitationalParameter > 0.0 ? gravitationalParameter / GravitationalConstant : 0.0;

        public static double SurfaceGravityMps2(BodyRuntime body) =>
            body != null && body.RadiusM > 0.0 ? body.Gm / (body.RadiusM * body.RadiusM) : 0.0;

        public static double EscapeVelocityMps(BodyRuntime body) =>
            body != null && body.RadiusM > 0.0 && body.Gm > 0.0
                ? System.Math.Sqrt(2.0 * body.Gm / body.RadiusM)
                : 0.0;

        public static double LightTimeSeconds(double distanceM) => distanceM / SpeedOfLightMps;

        public static string FormatMass(double kilograms)
        {
            if (kilograms <= 0.0 || double.IsNaN(kilograms)) return "—";
            double earths = kilograms / EarthMassKg;
            string earthText = earths >= 10_000.0
                ? $"{earths:N0} Earths"
                : $"{earths:0.00} Earths";
            return $"{FormatScientific(kilograms)} kg  ({earthText})";
        }

        public static string FormatGravity(double gravityMps2)
        {
            if (gravityMps2 <= 0.0) return "—";
            return $"{gravityMps2:0.00} m/s²  ({gravityMps2 / StandardGravityMps2:0.00} g)";
        }

        public static string FormatRotation(double periodSeconds)
        {
            if (periodSeconds == 0.0) return "—";
            string prefix = periodSeconds < 0.0 ? "retrograde, " : string.Empty;
            return prefix + SolarSystemHud.FormatDuration(System.Math.Abs(periodSeconds));
        }

        public static string FormatLightTime(double seconds)
        {
            if (seconds < 60.0) return $"{seconds:0.00} s";
            if (seconds < 3600.0) return $"{seconds / 60.0:0.0} min";
            if (seconds < 86400.0) return $"{seconds / 3600.0:0.0} h";
            return $"{seconds / 86400.0:0.0} d";
        }

        private static string FormatSpeedKm(double metresPerSecond) =>
            metresPerSecond > 0.0 ? $"{metresPerSecond / 1000.0:0.00} km/s" : "—";

        private static string FormatScientific(double value)
        {
            int exponent = (int)System.Math.Floor(System.Math.Log10(value));
            double mantissa = value / System.Math.Pow(10, exponent);
            return $"{mantissa:0.00}e{exponent}";
        }
    }
}
