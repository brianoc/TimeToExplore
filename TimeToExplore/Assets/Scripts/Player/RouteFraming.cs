using System;
using System.Collections.Generic;
using UnityEngine;
using TimeToExplore.Core;

namespace TimeToExplore.Player
{
    /// <summary>
    /// Chooses a camera pose that shows a whole plotted route.
    ///
    /// The frame is the route's bounding box: the look-at sits at its centre,
    /// and the distance is the smallest one that keeps the projected route
    /// inside the tighter of the camera's vertical and horizontal fields of
    /// view. Measuring the projection rather than a bounding sphere means a
    /// long, thin transfer fills the picture instead of leaving half of it
    /// empty. The view direction is taken from the plane the route actually
    /// lies in, using Newell's method over the sampled path, so an inclined
    /// transfer is seen across its plane rather than edge-on. A small fixed
    /// tilt off that normal keeps the picture from looking flat without
    /// foreshortening the curve.
    ///
    /// Plain C# over double-precision ecliptic metres, so the fit and the angle
    /// are verified in EditMode without a scene. Positions stay in metres until
    /// the final unit vector, because converting solar-scale coordinates to
    /// float first would lose the precision that makes the fit meaningful
    /// ([ADR-002](/decisions/adr-002-double-precision.md)).
    /// </summary>
    public static class RouteFraming
    {
        /// <summary>How many segments each plan arc contributes to the fit.</summary>
        public const int SamplesPerArc = 48;

        /// <summary>How far off the route plane's normal the camera sits.</summary>
        public const float ViewTiltDegrees = 18f;

        /// <summary>Fraction of the fitted view kept as breathing room.</summary>
        public const float FitMargin = 1.12f;

        /// <summary>
        /// A camera pose that contains the route: where to look, from what
        /// angle, and how far back. Invalid values mean the caller should leave
        /// the camera alone.
        /// </summary>
        public readonly struct Frame
        {
            public Frame(Vector3d centreM, double radiusM, Vector3d panM,
                float yawDegrees, float pitchDegrees, float distanceUnits)
            {
                CentreM = centreM;
                RadiusM = radiusM;
                PanM = panM;
                YawDegrees = yawDegrees;
                PitchDegrees = pitchDegrees;
                DistanceUnits = distanceUnits;
            }

            /// <summary>The centre of the route, in ecliptic metres.</summary>
            public Vector3d CentreM { get; }

            /// <summary>The radius that contains every fitted point, in metres.</summary>
            public double RadiusM { get; }

            /// <summary>The look-at offset from the ship, in ecliptic metres.</summary>
            public Vector3d PanM { get; }

            public float YawDegrees { get; }
            public float PitchDegrees { get; }
            public float DistanceUnits { get; }

            public bool IsValid =>
                RadiusM > 0.0
                && double.IsFinite(RadiusM)
                && DistanceUnits > 0f
                && float.IsFinite(DistanceUnits)
                && float.IsFinite(YawDegrees)
                && float.IsFinite(PitchDegrees)
                && double.IsFinite(PanM.x)
                && double.IsFinite(PanM.y)
                && double.IsFinite(PanM.z);
        }

        /// <summary>
        /// Build the frame from a sampled route. The optional target-arrival
        /// point is the planet's position on the arrival date, so the "where it
        /// will be" ghost is inside the shot as well as the ship's path.
        /// </summary>
        public static Frame Compute(IReadOnlyList<Vector3d> routePointsM,
            Vector3d shipPositionM, Vector3d? targetArrivalM, float unitsPerMeter,
            float fieldOfViewDegrees, float aspect, float currentYawDegrees,
            float currentPitchDegrees, bool mapEnabled, float maxDistanceUnits)
        {
            if (routePointsM == null || routePointsM.Count < 2) return default;
            if (!(unitsPerMeter > 0f) || !(fieldOfViewDegrees > 0f) ||
                !(maxDistanceUnits > 0f))
                return default;

            if (!TryBounds(routePointsM, targetArrivalM, out Vector3d min, out Vector3d max))
                return default;

            Vector3d centre = (min + max) * 0.5;
            double radiusM = RadiusAround(routePointsM, targetArrivalM, centre);
            if (!(radiusM > 0.0) || !double.IsFinite(radiusM)) return default;

            Vector3d normal = PlaneNormal(routePointsM, centre, radiusM, out bool straight);

            Vector3 currentOffset = OrbitCamera.Direction(currentYawDegrees, currentPitchDegrees);
            Vector3d currentOffsetEcliptic = Vector3d.FromUnity(currentOffset);
            double side = Vector3d.Dot(normal, currentOffsetEcliptic);
            if (side < 0.0) normal = -normal;
            else if (side == 0.0 && normal.z < 0.0) normal = -normal;

            Vector3d tilt = straight
                ? StraightTilt(normal, FirstFinite(routePointsM), LastFinite(routePointsM),
                    currentOffsetEcliptic)
                : TiltDirection(normal, currentOffsetEcliptic);
            double tiltRadians = ViewTiltDegrees * Math.PI / 180.0;
            Vector3d offsetEcliptic = normal * Math.Cos(tiltRadians)
                                      + tilt * Math.Sin(tiltRadians);

            Vector3 offset = offsetEcliptic.ToUnity().normalized;
            float pitch = Mathf.Asin(Mathf.Clamp(offset.y, -1f, 1f)) * Mathf.Rad2Deg;
            pitch = Mathf.Clamp(pitch, OrbitCamera.MinPitchDegrees,
                OrbitCamera.MaxPitchDegrees);
            float yaw = Mathf.Atan2(offset.x, offset.z) * Mathf.Rad2Deg;

            double distanceM = ProjectedDistance(routePointsM, targetArrivalM, centre,
                yaw, pitch, mapEnabled, fieldOfViewDegrees, aspect);
            if (!(distanceM > 0.0) || !double.IsFinite(distanceM)) return default;

            double distanceUnits = distanceM * unitsPerMeter;
            float clampedDistance = (float)Math.Min(
                Math.Max(distanceUnits, OrbitCamera.MinDistanceUnits), maxDistanceUnits);

            return new Frame(centre, radiusM, centre - shipPositionM, yaw, pitch,
                clampedDistance);
        }

        /// <summary>
        /// The distance that keeps every route point inside the tighter field of
        /// view with <see cref="FitMargin"/> to spare. Unlike a bounding-sphere
        /// fit, this measures the route projected onto the screen, so a long
        /// thin transfer fills the frame instead of leaving half of it empty.
        /// All the rotations the map blend can place the camera in are tested and
        /// the largest distance wins, so the fit does not depend on whether the
        /// blend has already tilted the pose.
        /// </summary>
        private static double ProjectedDistance(IReadOnlyList<Vector3d> points,
            Vector3d? extra, Vector3d centre, float yawDegrees, float pitchDegrees,
            bool mapEnabled, float fieldOfViewDegrees, float aspect)
        {
            double halfVertical = 0.5 * fieldOfViewDegrees * Math.PI / 180.0;
            double tanVertical = Math.Tan(halfVertical);
            double tanHorizontal = tanVertical * Math.Max(aspect, 0f);
            if (!(tanVertical > 0.0) || !(tanHorizontal > 0.0)) return 0.0;

            double limit = 1.0 / FitMargin;
            Quaternion localRotation = Quaternion.LookRotation(
                -OrbitCamera.Direction(yawDegrees, pitchDegrees), Vector3.up);

            int steps = mapEnabled ? 4 : 0;
            double distanceM = 0.0;

            for (int step = 0; step <= steps; step++)
            {
                float weight = steps == 0 ? 0f : (float)step / steps;
                float effectivePitch = Mathf.Lerp(pitchDegrees,
                    MapView.TopDownPitchDegrees, weight);
                Quaternion rotation = steps == 0
                    ? localRotation
                    : MapView.BlendRotation(localRotation, yawDegrees, weight);

                Vector3d offset = Vector3d.FromUnity(
                    OrbitCamera.Direction(yawDegrees, effectivePitch));
                Vector3d right = Vector3d.FromUnity(rotation * Vector3.right);
                Vector3d up = Vector3d.FromUnity(rotation * Vector3.up);

                distanceM = Math.Max(distanceM, DistanceForBasis(points, extra, centre,
                    offset, right, up, limit, tanHorizontal, tanVertical));
            }

            return distanceM;
        }

        private static double DistanceForBasis(IReadOnlyList<Vector3d> points,
            Vector3d? extra, Vector3d centre, Vector3d offset, Vector3d right,
            Vector3d up, double limit, double tanHorizontal, double tanVertical)
        {
            double distanceM = 0.0;

            for (int i = 0; i < points.Count; i++)
                distanceM = Math.Max(distanceM, NeededDistance(points[i] - centre,
                    offset, right, up, limit, tanHorizontal, tanVertical));

            if (extra.HasValue && IsFinite(extra.Value))
                distanceM = Math.Max(distanceM, NeededDistance(extra.Value - centre,
                    offset, right, up, limit, tanHorizontal, tanVertical));

            return distanceM;
        }

        /// <summary>
        /// The nearest camera distance that puts one point inside both fields of
        /// view. The depth at distance <c>d</c> is <c>d − s</c>, where
        /// <c>s</c> is how far the point leans toward the camera, so the required
        /// distance is <c>|axis| / (limit × tan) + s</c>.
        /// </summary>
        private static double NeededDistance(Vector3d offsetFromCentre, Vector3d offset,
            Vector3d right, Vector3d up, double limit, double tanHorizontal,
            double tanVertical)
        {
            if (!IsFinite(offsetFromCentre)) return 0.0;

            double towardCamera = Vector3d.Dot(offsetFromCentre, offset);
            double horizontal = Math.Abs(Vector3d.Dot(offsetFromCentre, right));
            double vertical = Math.Abs(Vector3d.Dot(offsetFromCentre, up));

            return Math.Max(
                horizontal / (limit * tanHorizontal) + towardCamera,
                vertical / (limit * tanVertical) + towardCamera);
        }

        private static bool TryBounds(IReadOnlyList<Vector3d> points, Vector3d? extra,
            out Vector3d min, out Vector3d max)
        {
            min = default;
            max = default;
            bool any = false;

            for (int i = 0; i < points.Count; i++)
            {
                Vector3d point = points[i];
                if (!IsFinite(point)) continue;
                Include(point, ref any, ref min, ref max);
            }

            if (extra.HasValue && IsFinite(extra.Value))
                Include(extra.Value, ref any, ref min, ref max);

            return any;
        }

        private static void Include(Vector3d point, ref bool any, ref Vector3d min,
            ref Vector3d max)
        {
            if (!any)
            {
                min = point;
                max = point;
                any = true;
                return;
            }

            if (point.x < min.x) min.x = point.x;
            if (point.y < min.y) min.y = point.y;
            if (point.z < min.z) min.z = point.z;
            if (point.x > max.x) max.x = point.x;
            if (point.y > max.y) max.y = point.y;
            if (point.z > max.z) max.z = point.z;
        }

        private static double RadiusAround(IReadOnlyList<Vector3d> points, Vector3d? extra,
            Vector3d centre)
        {
            double radiusSq = 0.0;
            for (int i = 0; i < points.Count; i++)
            {
                Vector3d point = points[i];
                if (!IsFinite(point)) continue;
                radiusSq = Math.Max(radiusSq, (point - centre).SqrMagnitude);
            }

            if (extra.HasValue && IsFinite(extra.Value))
                radiusSq = Math.Max(radiusSq, (extra.Value - centre).SqrMagnitude);

            return Math.Sqrt(radiusSq);
        }

        /// <summary>
        /// The route plane's normal from Newell's method over the ordered path.
        /// A route that is too straight to define a plane falls back to a view
        /// perpendicular to its chord from the ecliptic-north side, which still
        /// shows the whole line without looking along it.
        /// </summary>
        private static Vector3d PlaneNormal(IReadOnlyList<Vector3d> points,
            Vector3d centre, double radiusM, out bool straight)
        {
            Vector3d normal = Vector3d.Zero;
            for (int i = 0; i < points.Count; i++)
            {
                Vector3d a = points[i] - centre;
                Vector3d b = points[(i + 1) % points.Count] - centre;
                if (!IsFinite(a) || !IsFinite(b)) continue;

                normal.x += (a.y - b.y) * (a.z + b.z);
                normal.y += (a.z - b.z) * (a.x + b.x);
                normal.z += (a.x - b.x) * (a.y + b.y);
            }

            // Straight routes have almost no enclosed area. The relative test
            // keeps the plane's direction for a shallow but real curve.
            if (normal.Magnitude <= 1e-6 * radiusM * radiusM)
            {
                straight = true;
                Vector3d chord = LastFinite(points) - FirstFinite(points);
                if (chord.SqrMagnitude <= 0.0) return Vector3d.Up;

                Vector3d north = new Vector3d(0.0, 0.0, 1.0);
                Vector3d candidate = north - chord * (Vector3d.Dot(north, chord) /
                                                      chord.SqrMagnitude);
                if (candidate.SqrMagnitude > 1e-12) return candidate.Normalized;

                Vector3d east = new Vector3d(1.0, 0.0, 0.0);
                candidate = east - chord * (Vector3d.Dot(east, chord) / chord.SqrMagnitude);
                return candidate.SqrMagnitude > 1e-12 ? candidate.Normalized : Vector3d.Up;
            }

            straight = false;
            return normal.Normalized;
        }

        /// <summary>
        /// For a straight route the only feature is the chord, so the tilt must
        /// stay perpendicular to it: tilting toward one end would foreshorten
        /// the line. The side follows the player's current view.
        /// </summary>
        private static Vector3d StraightTilt(Vector3d normal, Vector3d first, Vector3d last,
            Vector3d currentOffset)
        {
            Vector3d chord = (last - first).Normalized;
            Vector3d tilt = Vector3d.Cross(normal, chord);
            if (tilt.SqrMagnitude <= 1e-12) return TiltDirection(normal, currentOffset);

            tilt = tilt.Normalized;
            if (Vector3d.Dot(tilt, currentOffset) < 0.0) tilt = -tilt;
            return tilt;
        }

        /// <summary>
        /// A direction in the route plane to tilt toward: the part of the
        /// current view direction that already lies in the plane, so the camera
        /// does not flip sides. Falls back to the ecliptic-north side and then
        /// to the east side when the current view is exactly along the normal.
        /// </summary>
        private static Vector3d TiltDirection(Vector3d normal, Vector3d currentOffset)
        {
            Vector3d tilt = currentOffset - normal * Vector3d.Dot(currentOffset, normal);
            if (tilt.Magnitude >= 0.25) return tilt.Normalized;

            Vector3d north = new Vector3d(0.0, 0.0, 1.0);
            tilt = north - normal * Vector3d.Dot(north, normal);
            if (tilt.Magnitude >= 0.25) return tilt.Normalized;

            Vector3d east = new Vector3d(1.0, 0.0, 0.0);
            tilt = east - normal * Vector3d.Dot(east, normal);
            return tilt.Magnitude > 1e-12 ? tilt.Normalized : Vector3d.Up;
        }

        private static Vector3d FirstFinite(IReadOnlyList<Vector3d> points)
        {
            for (int i = 0; i < points.Count; i++)
                if (IsFinite(points[i])) return points[i];
            return Vector3d.Zero;
        }

        private static Vector3d LastFinite(IReadOnlyList<Vector3d> points)
        {
            for (int i = points.Count - 1; i >= 0; i--)
                if (IsFinite(points[i])) return points[i];
            return Vector3d.Zero;
        }

        private static bool IsFinite(Vector3d value) =>
            double.IsFinite(value.x) && double.IsFinite(value.y) && double.IsFinite(value.z);
    }
}
