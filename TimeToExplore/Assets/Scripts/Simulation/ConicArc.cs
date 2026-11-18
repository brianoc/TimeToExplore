using System;
using TimeToExplore.Core;

namespace TimeToExplore.Simulation
{
    /// <summary>
    /// A Keplerian coast around one body, expressed in heliocentric coordinates
    /// by adding the body's own ephemeris to a planetocentric conic. On the
    /// two-body reference the path's acceleration is the body's rail
    /// acceleration plus the body's local gravity, so the commanded thrust is
    /// zero; in the summed field the autopilot supplies the small third-body
    /// correction ([ADR-005](/decisions/adr-005-continuous-gravity.md),
    /// [ADR-014](/decisions/adr-014-keplerian-coast.md)).
    ///
    /// The parameter u is linear in <em>true anomaly</em>, not in time. A
    /// transfer hyperbola spends most of its time far from the body and turns
    /// hardest at periapsis, so a time-uniform parameter would cut the corner
    /// exactly where the path matters. True anomaly gives the sampler dense
    /// points at periapsis and sparse points on the long approach.
    /// </summary>
    public sealed class ConicArc : IArcPath
    {
        private readonly SolarSystemModel _model;
        private readonly BodyRuntime _centre;
        private readonly ConicElements _elements;
        private readonly double _startJulianDate;
        private readonly double _durationSeconds;
        private readonly double _trueAnomalyStartRad;
        private readonly double _trueAnomalyEndRad;
        private readonly double _timeSincePeriapsisStartSeconds;

        private ConicArc(SolarSystemModel model, BodyRuntime centre, double startJulianDate,
            double durationSeconds, ConicElements elements)
        {
            _model = model;
            _centre = centre;
            _startJulianDate = startJulianDate;
            _durationSeconds = durationSeconds;
            _elements = elements;

            _trueAnomalyStartRad = elements.TrueAnomalyRad;
            _timeSincePeriapsisStartSeconds = elements.TimeSincePeriapsisSeconds;

            double endTime = _timeSincePeriapsisStartSeconds + durationSeconds;
            double endAnomaly = elements.TrueAnomalyAtTime(endTime);
            while (endAnomaly <= _trueAnomalyStartRad)
                endAnomaly += OrbitMath.TwoPi;
            _trueAnomalyEndRad = endAnomaly;
        }

        public double StartJulianDate => _startJulianDate;
        public double DurationSeconds => _durationSeconds;
        public BodyRuntime Centre => _centre;
        public ConicElements Elements => _elements;

        /// <summary>Build a coast from a body-relative state. The model is used
        /// only to place the centre at each queried date, never mutated.</summary>
        public static ConicArc Create(SolarSystemModel model, BodyRuntime centre,
            double startJulianDate, double durationSeconds,
            Vector3d localStartPositionM, Vector3d localStartVelocityMps)
        {
            if (model == null) throw new ArgumentNullException(nameof(model));
            if (centre == null) throw new ArgumentNullException(nameof(centre));
            if (!(durationSeconds > 0.0))
                throw new ArgumentOutOfRangeException(nameof(durationSeconds));

            ConicElements elements = ConicElements.FromState(
                localStartPositionM, localStartVelocityMps, centre.Gm);
            return new ConicArc(model, centre, startJulianDate, durationSeconds, elements);
        }

        public Vector3d PositionAt(double u)
        {
            JulianDateFor(u, out double julianDate);
            _model.ComputeState(_centre, julianDate, out Vector3d centrePosition, out _);
            LocalStateAt(u, out Vector3d localPosition, out _);
            return centrePosition + localPosition;
        }

        public Vector3d VelocityAt(double u)
        {
            JulianDateFor(u, out double julianDate);
            _model.ComputeState(_centre, julianDate, out _, out Vector3d centreVelocity);
            LocalStateAt(u, out _, out Vector3d localVelocity);
            return centreVelocity + localVelocity;
        }

        public Vector3d AccelerationAt(double u)
        {
            JulianDateFor(u, out double julianDate);
            LocalStateAt(u, out Vector3d localPosition, out _);

            double radius = localPosition.Magnitude;
            Vector3d localAcceleration = radius > 0.0
                ? localPosition * (-_centre.Gm / (radius * radius * radius))
                : Vector3d.Zero;

            return _model.ComputeAcceleration(_centre, julianDate) + localAcceleration;
        }

        public double SecondsAt(double u)
        {
            u = Math.Clamp(u, 0.0, 1.0);
            if (u <= 0.0) return 0.0;
            if (u >= 1.0) return _durationSeconds;

            double anomaly = TrueAnomalyAt(u);
            double time = _elements.TimeSincePeriapsis(anomaly)
                          - _timeSincePeriapsisStartSeconds;
            return Math.Clamp(time, 0.0, _durationSeconds);
        }

        public double ParameterAtSeconds(double seconds)
        {
            if (!(seconds > 0.0)) return 0.0;
            if (seconds >= _durationSeconds) return 1.0;

            double time = _timeSincePeriapsisStartSeconds + seconds;
            double anomaly = _elements.TrueAnomalyAtTime(time);

            while (anomaly < _trueAnomalyStartRad - 1e-12)
                anomaly += OrbitMath.TwoPi;
            while (anomaly > _trueAnomalyEndRad + 1e-12)
                anomaly -= OrbitMath.TwoPi;

            double span = _trueAnomalyEndRad - _trueAnomalyStartRad;
            return span > 0.0
                ? Math.Clamp((anomaly - _trueAnomalyStartRad) / span, 0.0, 1.0)
                : 0.0;
        }

        /// <summary>Position and velocity relative to the centre at parameter u.</summary>
        public void LocalStateAt(double u, out Vector3d localPositionM,
            out Vector3d localVelocityMps)
        {
            _elements.StateAtTrueAnomaly(TrueAnomalyAt(u),
                out localPositionM, out localVelocityMps);
        }

        private double TrueAnomalyAt(double u)
        {
            u = Math.Clamp(u, 0.0, 1.0);
            return _trueAnomalyStartRad
                   + u * (_trueAnomalyEndRad - _trueAnomalyStartRad);
        }

        private void JulianDateFor(double u, out double julianDate)
        {
            u = Math.Clamp(u, 0.0, 1.0);
            julianDate = _startJulianDate + SecondsAt(u) / JulianDate.SecondsPerDay;
        }

        public override string ToString()
        {
            string kind = _elements.IsHyperbolic ? "hyperbola"
                : _elements.IsElliptic ? "ellipse" : "parabola";
            return $"{kind} about {_centre.Id}, e={_elements.Eccentricity:F4}, " +
                   $"{_durationSeconds / 3600.0:F2} h";
        }
    }
}
