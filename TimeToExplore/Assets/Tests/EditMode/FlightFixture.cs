using TimeToExplore.Core;
using TimeToExplore.Simulation;

namespace TimeToExplore.Tests.EditMode
{
    /// <summary>Helpers for the flight-model suites: ship states placed on the
    /// shipped catalogue, and the standard gravity field.</summary>
    internal static class FlightFixture
    {
        public static ShipState ShipAt(SolarSystemModel model, string bodyId)
        {
            BodyRuntime body = model.Get(bodyId);
            model.ComputeState(body, model.CurrentJulianDate,
                out Vector3d positionM, out Vector3d velocityMps);
            return new ShipState(model.CurrentJulianDate, positionM, velocityMps);
        }

        /// <summary>A ship in a circular orbit at the given altitude around a
        /// body, used to exercise the departure leg.</summary>
        public static ShipState ShipInEquatorialOrbit(SolarSystemModel model, string bodyId, double altitudeM)
        {
            BodyRuntime body = model.Get(bodyId);
            model.ComputeState(body, model.CurrentJulianDate,
                out Vector3d bodyPosition, out Vector3d bodyVelocity);

            double radius = body.RadiusM + altitudeM;
            double speed = TwoBody.CircularSpeedMps(body.Gm, radius);
            Vector3d radial = body.EquatorX;
            Vector3d tangential = body.EquatorY;

            return new ShipState(
                model.CurrentJulianDate,
                bodyPosition + radial * radius,
                bodyVelocity + tangential * speed);
        }

        public static GravityField FullField(SolarSystemModel model) => new GravityField(model);

        public static GravityField SunOnlyField(SolarSystemModel model) =>
            new GravityField(model, body => body.Id == "sun");
    }
}
