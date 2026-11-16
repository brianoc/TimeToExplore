using System;
using System.Collections.Generic;
using TimeToExplore.Core;

namespace TimeToExplore.Simulation
{
    /// <summary>
    /// Continuous gravitational field summed over the catalogue bodies. The
    /// bodies stay on their Keplerian rails; this class only evaluates their
    /// state at a Julian date. It never mutates <see cref="SolarSystemModel"/>,
    /// so an integration can evaluate the field at any time without disturbing
    /// the frame the renderer is using.
    ///
    /// There is no sphere-of-influence switch and no dominant-body choice: the
    /// acceleration near Mars simply is Mars plus the Sun plus the moons. The
    /// field is prepared once per substep, which selects the bodies whose
    /// acceleration is above <see cref="FloorMps2"/>; the selected set is then
    /// evaluated at every intermediate RK4 stage.
    /// </summary>
    public sealed class GravityField : IGravityField
    {
        /// <summary>Bodies below this acceleration are dropped. The error is
        /// below the navigation budget: 1e-11 m/s² over two weeks is 1 mm/s.</summary>
        public const double DefaultFloorMps2 = 1e-11;

        private readonly SolarSystemModel _model;
        private readonly BodyRuntime[] _sources;
        private readonly Dictionary<BodyRuntime, int> _slot;
        private readonly Vector3d[] _sourcePosition;
        private readonly Vector3d[] _sourceVelocity;
        private readonly double[] _sourceAcceleration;
        private readonly bool[] _used;

        private readonly List<int> _active = new List<int>();
        private readonly Vector3d[] _stagePosition;
        private double _stageJulianDate = double.NaN;
        private bool _prepared;

        public GravityField(SolarSystemModel model, Func<BodyRuntime, bool> filter = null)
        {
            _model = model ?? throw new ArgumentNullException(nameof(model));

            // A body cannot be evaluated without its ancestors, so a filter that
            // selects a moon implicitly selects its planet and star.
            var included = new HashSet<BodyRuntime>();
            foreach (BodyRuntime body in model.Ordered)
            {
                if (body.Gm <= 0.0) continue;
                if (filter != null && !filter(body)) continue;
                included.Add(body);
            }

            foreach (BodyRuntime body in model.Ordered)
            {
                if (!included.Contains(body)) continue;
                for (BodyRuntime ancestor = body.Parent; ancestor != null; ancestor = ancestor.Parent)
                    if (ancestor.Gm > 0.0) included.Add(ancestor);
            }

            var sources = new List<BodyRuntime>();
            foreach (BodyRuntime body in model.Ordered)
                if (included.Contains(body)) sources.Add(body);

            _sources = sources.ToArray();
            _slot = new Dictionary<BodyRuntime, int>(_sources.Length);
            for (int i = 0; i < _sources.Length; i++) _slot[_sources[i]] = i;

            _sourcePosition = new Vector3d[_sources.Length];
            _sourceVelocity = new Vector3d[_sources.Length];
            _sourceAcceleration = new double[_sources.Length];
            _used = new bool[_sources.Length];
            _stagePosition = new Vector3d[_sources.Length];

            NearestClearanceM = double.PositiveInfinity;
        }

        /// <summary>Smallest acceleration that still contributes to the sum.</summary>
        public double FloorMps2 { get; set; } = DefaultFloorMps2;

        /// <summary>Upper bound on the bodies evaluated per query.</summary>
        public int MaxActiveBodies { get; set; } = 8;

        public Vector3d PreparedPositionM { get; private set; }
        public double PreparedJulianDate { get; private set; }
        public int SourceCount => _sources.Length;
        public int ActiveBodyCount => _active.Count;

        /// <summary>Distance from the prepared position to the nearest body's
        /// surface. Negative means inside a body.</summary>
        public double NearestClearanceM { get; private set; } = double.PositiveInfinity;

        public string NearestBodyId { get; private set; }

        public BodyRuntime ActiveBodyAt(int index) => _sources[_active[index]];

        /// <summary>Position of an active body at the prepared Julian date.</summary>
        public Vector3d ActiveBodyPositionM(int index) => _sourcePosition[_active[index]];

        /// <summary>Velocity of an active body at the prepared Julian date.</summary>
        public Vector3d ActiveBodyVelocityMps(int index) => _sourceVelocity[_active[index]];

        public bool IsActive(string bodyId)
        {
            for (int i = 0; i < _active.Count; i++)
                if (_sources[_active[i]].Id == bodyId) return true;
            return false;
        }

        /// <summary>Body whose surface the prepared position is inside, if any.</summary>
        public BodyRuntime FindCollidingBody(Vector3d positionM, double julianDate)
        {
            Prepare(positionM, julianDate);
            if (NearestClearanceM >= 0.0 || NearestBodyId == null) return null;
            return _model.Get(NearestBodyId);
        }

        public void Prepare(Vector3d positionM, double julianDate)
        {
            PreparedPositionM = positionM;
            PreparedJulianDate = julianDate;
            NearestClearanceM = double.PositiveInfinity;
            NearestBodyId = null;

            for (int i = 0; i < _sources.Length; i++)
            {
                BodyRuntime body = _sources[i];

                if (body.Parent == null)
                {
                    // The Sun is fixed at the origin by the model.
                    _sourcePosition[i] = Vector3d.Zero;
                    _sourceVelocity[i] = Vector3d.Zero;
                }
                else
                {
                    body.Elements.GetState(julianDate, out Vector3d localPosition, out Vector3d localVelocity);
                    if (body.Elements.parentEquatorFrame)
                    {
                        localPosition = body.Parent.FromEquator(localPosition);
                        localVelocity = body.Parent.FromEquator(localVelocity);
                    }

                    int parentSlot = _slot[body.Parent];
                    _sourcePosition[i] = _sourcePosition[parentSlot] + localPosition;
                    _sourceVelocity[i] = _sourceVelocity[parentSlot] + localVelocity;
                }

                Vector3d delta = _sourcePosition[i] - positionM;
                double distance = delta.Magnitude;
                double clearance = distance - body.RadiusM;
                if (clearance < NearestClearanceM)
                {
                    NearestClearanceM = clearance;
                    NearestBodyId = body.Id;
                }

                double effectiveRadius = Math.Max(distance, body.RadiusM);
                _sourceAcceleration[i] = body.Gm / (effectiveRadius * effectiveRadius);
            }

            SelectActiveBodies();
            _prepared = true;
            _stageJulianDate = double.NaN;
        }

        private void SelectActiveBodies()
        {
            for (int i = 0; i < _active.Count; i++) _used[_active[i]] = false;
            _active.Clear();

            for (int k = 0; k < MaxActiveBodies; k++)
            {
                int best = -1;
                double bestAcceleration = FloorMps2;
                for (int i = 0; i < _sources.Length; i++)
                {
                    if (_used[i]) continue;
                    if (_sourceAcceleration[i] > bestAcceleration)
                    {
                        bestAcceleration = _sourceAcceleration[i];
                        best = i;
                    }
                }

                if (best < 0) break;
                _used[best] = true;
                _active.Add(best);
            }
        }

        public Vector3d AccelerationAt(Vector3d positionM, double julianDate)
        {
            if (!_prepared) Prepare(positionM, julianDate);

            if (julianDate != _stageJulianDate)
            {
                for (int k = 0; k < _active.Count; k++)
                {
                    _model.ComputeState(_sources[_active[k]], julianDate,
                        out Vector3d stagePosition, out _);
                    _stagePosition[k] = stagePosition;
                }

                _stageJulianDate = julianDate;
            }

            double ax = 0.0;
            double ay = 0.0;
            double az = 0.0;

            for (int k = 0; k < _active.Count; k++)
            {
                BodyRuntime body = _sources[_active[k]];
                Vector3d delta = _stagePosition[k] - positionM;
                double r2 = delta.SqrMagnitude;
                double radius = body.RadiusM;
                double r2Effective = Math.Max(r2, radius * radius);
                double inverse = 1.0 / Math.Sqrt(r2Effective);
                double scale = body.Gm * inverse * inverse * inverse;
                ax += delta.x * scale;
                ay += delta.y * scale;
                az += delta.z * scale;
            }

            return new Vector3d(ax, ay, az);
        }

        /// <summary>Full sum over every source, ignoring the floor and the
        /// active-set cap. Used by tests to prove that pruning is harmless;
        /// not on the integrator's path.</summary>
        public Vector3d AccelerationAtFull(Vector3d positionM, double julianDate)
        {
            double ax = 0.0;
            double ay = 0.0;
            double az = 0.0;

            foreach (BodyRuntime body in _sources)
            {
                _model.ComputeState(body, julianDate, out Vector3d bodyPosition, out _);
                Vector3d delta = bodyPosition - positionM;
                double r2 = delta.SqrMagnitude;
                double radius = body.RadiusM;
                double r2Effective = Math.Max(r2, radius * radius);
                double inverse = 1.0 / Math.Sqrt(r2Effective);
                double scale = body.Gm * inverse * inverse * inverse;
                ax += delta.x * scale;
                ay += delta.y * scale;
                az += delta.z * scale;
            }

            return new Vector3d(ax, ay, az);
        }
    }
}
