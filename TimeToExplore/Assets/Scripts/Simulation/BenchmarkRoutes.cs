using System;
using TimeToExplore.Core;

namespace TimeToExplore.Simulation
{
    /// <summary>Where a benchmark route starts. A station leaves from its own
    /// state; a planet leaves from the parking orbit a conic capture parks in.</summary>
    public enum BenchmarkDepartureKind
    {
        Station,
        ParkingOrbit
    }

    /// <summary>One fixed route the benchmark times. It is a measurement input,
    /// never a planner setting: the level is the player's default unless the
    /// planner refuses the route below it.</summary>
    public sealed class BenchmarkRoute
    {
        public BenchmarkRoute(string name, string displayName, string departureBodyId,
            BenchmarkDepartureKind kind, string targetId, double burnLevelG, string why)
        {
            Name = name;
            DisplayName = displayName;
            DepartureBodyId = departureBodyId;
            Kind = kind;
            TargetId = targetId;
            BurnLevelG = burnLevelG;
            Why = why;
        }

        /// <summary>The stable id the baseline keys on.</summary>
        public string Name { get; }

        public string DisplayName { get; }

        public string DepartureBodyId { get; }

        public BenchmarkDepartureKind Kind { get; }

        public string TargetId { get; }

        public double BurnLevelG { get; }

        /// <summary>Why this level, and where the departure point comes from.</summary>
        public string Why { get; }

        /// <summary>The ship's state at the route's departure point. A station is
        /// its catalogue state; a parking orbit is a circular equatorial orbit at
        /// the radius a conic capture parks in.</summary>
        public ShipState DepartureState(SolarSystemModel model, double julianDate)
        {
            BodyRuntime body = model.Get(DepartureBodyId)
                ?? throw new ArgumentException(
                    $"No body '{DepartureBodyId}' in the catalogue.");
            model.ComputeState(body, julianDate, out Vector3d bodyPosition,
                out Vector3d bodyVelocity);

            if (Kind == BenchmarkDepartureKind.Station)
                return new ShipState(julianDate, bodyPosition, bodyVelocity);

            double radius = ParkingOrbit.ConicRadiusM(body);
            double speed = TwoBody.CircularSpeedMps(body.Gm, radius);

            // Put the ship on the part of the parking orbit whose tangent is
            // prograde relative to the body's own flight. The catalogue's
            // equatorial basis picks an arbitrary phase; a ship about to leave
            // for another planet burns along its motion, and a retrograde phase
            // would test a departure the player does not fly.
            Vector3d planeVelocity = bodyVelocity
                - body.PoleEcliptic * Vector3d.Dot(bodyVelocity, body.PoleEcliptic);
            Vector3d tangent = planeVelocity.SqrMagnitude > 1.0
                ? planeVelocity.Normalized
                : body.EquatorY;
            Vector3d radial = Vector3d.Cross(tangent, body.PoleEcliptic).Normalized;

            return new ShipState(julianDate,
                bodyPosition + radial * radius,
                bodyVelocity + tangent * speed);
        }
    }

    /// <summary>The six routes and their levels. The Earth departures use the
    /// Gateway station, the anchor every existing measurement uses. The reverses
    /// start from the planet's own parking orbit, because that is where a ship
    /// going home actually is.</summary>
    public static class BenchmarkRoutes
    {
        /// <summary>The player's default level, and the level of every route
        /// unless a measured refusal forced a raise.</summary>
        public const double DefaultBurnLevelG = 0.3;

        public const double EpochJulianDate = 2461359.5;

        /// <summary>How far the clock is advanced for the Begin phase. A few
        /// hours is enough to stale the preview without changing the route.</summary>
        public const double BeginAdvanceSeconds = 3.0 * 3600.0;

        public static readonly BenchmarkRoute[] All =
        {
            new BenchmarkRoute("earth-mars", "Earth → Mars", "gateway",
                BenchmarkDepartureKind.Station, "mars", DefaultBurnLevelG,
                "Gateway station, Earth's anchor departure point; 0.3 g is the default level"),
            new BenchmarkRoute("earth-venus", "Earth → Venus", "gateway",
                BenchmarkDepartureKind.Station, "venus", DefaultBurnLevelG,
                "Gateway station; 0.3 g is the default level"),
            new BenchmarkRoute("earth-uranus", "Earth → Uranus", "gateway",
                BenchmarkDepartureKind.Station, "uranus", DefaultBurnLevelG,
                "Gateway station; 0.3 g is the default level"),
            new BenchmarkRoute("mars-earth", "Mars → Earth", "mars",
                BenchmarkDepartureKind.ParkingOrbit, "earth", DefaultBurnLevelG,
                "Mars conic parking orbit; 0.3 g departs a 300 km Mars orbit"),
            new BenchmarkRoute("venus-earth", "Venus → Earth", "venus",
                BenchmarkDepartureKind.ParkingOrbit, "earth", 1.0,
                "Venus conic parking orbit; 0.3 g cannot lift out of Venus's well, so 1 g"),
            new BenchmarkRoute("uranus-earth", "Uranus → Earth", "uranus",
                BenchmarkDepartureKind.ParkingOrbit, "earth", 1.0,
                "Uranus conic parking orbit; 0.3 g cannot lift out of Uranus's well, so 1 g")
        };

        /// <summary>The display name for a route id, falling back to the id so a
        /// baseline recorded by a different build still prints.</summary>
        public static string DisplayNameFor(string route)
        {
            foreach (BenchmarkRoute candidate in All)
            {
                if (string.Equals(candidate.Name, route, StringComparison.Ordinal))
                    return candidate.DisplayName;
            }

            return route;
        }
    }
}
