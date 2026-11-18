using System;
using System.Collections.Generic;
using UnityEngine;

namespace TimeToExplore.Core
{
    /// <summary>
    /// Owns the body hierarchy and advances every body to a Julian date.
    /// Bodies are updated parent-first, so a moon sees its planet's current
    /// position and a station sees both of its Lagrange partners.
    /// </summary>
    public class SolarSystemModel
    {
        public SolarSystemFile Data { get; private set; }
        public readonly Dictionary<string, BodyRuntime> Bodies = new Dictionary<string, BodyRuntime>();
        public readonly List<BodyRuntime> Ordered = new List<BodyRuntime>();
        public BodyRuntime Sun { get; private set; }
        public double CurrentJulianDate { get; private set; }

        public void Load(string json)
        {
            Data = JsonUtility.FromJson<SolarSystemFile>(json);
            if (Data == null || Data.bodies == null || Data.bodies.Length == 0)
                throw new InvalidOperationException("solar_system.json did not contain any bodies.");

            Bodies.Clear();
            Ordered.Clear();

            foreach (BodyData bodyData in Data.bodies)
            {
                var body = new BodyRuntime(bodyData);
                Bodies.Add(body.Id, body);
                if (bodyData.kind == "star") Sun = body;
            }

            foreach (BodyRuntime body in Bodies.Values)
            {
                if (string.IsNullOrEmpty(body.Data.parent)) continue;
                if (!Bodies.TryGetValue(body.Data.parent, out BodyRuntime parent))
                {
                    Debug.LogWarning($"Body '{body.Id}' has unknown parent '{body.Data.parent}'.");
                    continue;
                }
                body.Parent = parent;
                parent.Children.Add(body);
            }

            var depth = new Dictionary<string, int>();
            foreach (BodyRuntime body in Bodies.Values)
                depth[body.Id] = DepthOf(body, depth);

            Ordered.AddRange(Bodies.Values);
            Ordered.Sort((a, b) =>
            {
                int cmp = depth[a.Id].CompareTo(depth[b.Id]);
                return cmp != 0 ? cmp : string.Compare(a.Id, b.Id, StringComparison.Ordinal);
            });

            foreach (BodyRuntime body in Ordered)
                body.InitialiseElements();

            Update(Data.startJulianDate);
        }

        private int DepthOf(BodyRuntime body, Dictionary<string, int> cache)
        {
            if (cache.TryGetValue(body.Id, out int known)) return known;
            int value = body.Parent == null ? 0 : DepthOf(body.Parent, cache) + 1;
            cache[body.Id] = value;
            return value;
        }

        public void Update(double julianDate)
        {
            CurrentJulianDate = julianDate;

            foreach (BodyRuntime body in Ordered)
            {
                // JsonUtility turns a JSON null object into a default instance,
                // so a non-null `lagrange` does not reliably mean "station".
                // The kind is the authoritative flag.
                if (body.Data.IsStation)
                {
                    UpdateLagrangeStation(body, julianDate);
                }
                else if (body.HasOrbit)
                {
                    body.UpdateOrbit(julianDate);
                }
                else
                {
                    body.PositionM = Vector3d.Zero;
                    body.VelocityMps = Vector3d.Zero;
                }
            }
        }

        private void UpdateLagrangeStation(BodyRuntime station, double julianDate)
        {
            LagrangeData configuration = station.Data.lagrange;
            if (configuration == null ||
                string.IsNullOrEmpty(configuration.primary) ||
                string.IsNullOrEmpty(configuration.secondary))
            {
                Debug.LogWarning($"Station '{station.Id}' has no Lagrange pair in the catalogue.");
                return;
            }

            if (!Bodies.TryGetValue(configuration.primary, out BodyRuntime primary) ||
                !Bodies.TryGetValue(configuration.secondary, out BodyRuntime secondary))
            {
                Debug.LogWarning($"Station '{station.Id}' has an unresolved Lagrange pair.");
                return;
            }

            Vector3d position = OrbitMath.ComputeLagrangePoint(
                primary.PositionM, primary.Gm,
                secondary.PositionM, secondary.Gm,
                secondary.VelocityMps,
                configuration.Point);

            if (!double.IsNaN(station.LastJulianDate))
            {
                double dt = (julianDate - station.LastJulianDate) * JulianDate.SecondsPerDay;
                if (dt > 1e-3)
                    station.VelocityMps = (position - station.LastPositionM) / dt;
            }

            station.PositionM = position;
            station.LastPositionM = position;
            station.LastJulianDate = julianDate;
        }

        public BodyRuntime Get(string id) =>
            Bodies.TryGetValue(id, out BodyRuntime body) ? body : null;

        /// <summary>
        /// State of a body at an arbitrary Julian date, without mutating the
        /// model. The frame loop keeps using <see cref="Update"/>; this overload
        /// exists so the ship's integrator and trajectory planner can evaluate
        /// the world at RK4 stage times without disturbing the render frame.
        /// A station is positioned from its Lagrange pair, and its velocity is a
        /// central difference of that position because a Lagrange point has no
        /// closed-form velocity here.
        /// </summary>
        public void ComputeState(BodyRuntime body, double julianDate,
            out Vector3d positionM, out Vector3d velocityMps)
        {
            if (body == null) throw new ArgumentNullException(nameof(body));

            if (body.Data.IsStation)
            {
                LagrangeData configuration = body.Data.lagrange;
                if (configuration == null ||
                    !Bodies.TryGetValue(configuration.primary, out BodyRuntime primary) ||
                    !Bodies.TryGetValue(configuration.secondary, out BodyRuntime secondary))
                {
                    positionM = Vector3d.Zero;
                    velocityMps = Vector3d.Zero;
                    return;
                }

                positionM = LagrangePosition(primary, secondary, configuration.Point, julianDate);

                const double halfStepSeconds = 30.0;
                double h = halfStepSeconds / JulianDate.SecondsPerDay;
                Vector3d ahead = LagrangePosition(primary, secondary, configuration.Point, julianDate + h);
                Vector3d behind = LagrangePosition(primary, secondary, configuration.Point, julianDate - h);
                velocityMps = (ahead - behind) / (2.0 * halfStepSeconds);
                return;
            }

            if (!body.HasOrbit)
            {
                // The Sun is fixed at rest at the origin.
                positionM = Vector3d.Zero;
                velocityMps = Vector3d.Zero;
                return;
            }

            body.Elements.GetState(julianDate, out Vector3d localPosition, out Vector3d localVelocity);
            if (body.Elements.parentEquatorFrame && body.Parent != null)
            {
                localPosition = body.Parent.FromEquator(localPosition);
                localVelocity = body.Parent.FromEquator(localVelocity);
            }

            if (body.Parent == null)
            {
                positionM = localPosition;
                velocityMps = localVelocity;
                return;
            }

            ComputeState(body.Parent, julianDate, out Vector3d parentPosition, out Vector3d parentVelocity);
            positionM = parentPosition + localPosition;
            velocityMps = parentVelocity + localVelocity;
        }

        /// <summary>
        /// Heliocentric acceleration of a body on its Keplerian rail, without
        /// mutating the model. Each body accelerates under its parent's gravity,
        /// and the parent's own acceleration is added recursively; this is the
        /// second derivative of the same ephemeris <see cref="ComputeState"/>
        /// returns. A conic coast uses it to make the reference acceleration
        /// continuous across the sphere-of-influence seam.
        /// </summary>
        public Vector3d ComputeAcceleration(BodyRuntime body, double julianDate)
        {
            if (body == null) throw new ArgumentNullException(nameof(body));
            if (!body.HasOrbit || body.Parent == null) return Vector3d.Zero;

            body.Elements.GetState(julianDate, out Vector3d localPosition, out _);
            if (body.Elements.parentEquatorFrame)
                localPosition = body.Parent.FromEquator(localPosition);

            double radius = localPosition.Magnitude;
            Vector3d localAcceleration = radius > 0.0
                ? localPosition * (-body.Parent.Gm / (radius * radius * radius))
                : Vector3d.Zero;

            return ComputeAcceleration(body.Parent, julianDate) + localAcceleration;
        }

        private Vector3d LagrangePosition(BodyRuntime primary, BodyRuntime secondary,
            LagrangePoint point, double julianDate)
        {
            ComputeState(primary, julianDate, out Vector3d primaryPosition, out _);
            ComputeState(secondary, julianDate, out Vector3d secondaryPosition, out Vector3d secondaryVelocity);
            return OrbitMath.ComputeLagrangePoint(
                primaryPosition, primary.Gm,
                secondaryPosition, secondary.Gm,
                secondaryVelocity, point);
        }

        public BodyRuntime FindNearest(Vector3d point, Func<BodyRuntime, bool> filter = null)
        {
            BodyRuntime best = null;
            double bestDistance = double.PositiveInfinity;
            foreach (BodyRuntime body in Ordered)
            {
                if (filter != null && !filter(body)) continue;
                double distance = Vector3d.Distance(body.PositionM, point);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = body;
                }
            }
            return best;
        }

        /// <summary>Fill a buffer with the body's orbit in heliocentric metres.
        /// The last point equals the first, closing the ellipse.</summary>
        public bool SampleOrbitWorld(BodyRuntime body, int samples, Vector3d[] buffer)
        {
            if (!body.HasOrbit || body.Parent == null) return false;

            body.Elements.SampleEllipse(samples, buffer);
            Vector3d origin = body.Parent.PositionM;
            if (body.Elements.parentEquatorFrame)
            {
                for (int i = 0; i <= samples; i++)
                    buffer[i] = origin + body.Parent.FromEquator(buffer[i]);
            }
            else
            {
                for (int i = 0; i <= samples; i++)
                    buffer[i] = origin + buffer[i];
            }
            return true;
        }
    }
}
