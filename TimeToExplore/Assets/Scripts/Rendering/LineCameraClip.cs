using System.Collections.Generic;
using UnityEngine;

namespace TimeToExplore.Rendering
{
    /// <summary>
    /// Clips a world-space polyline to the part the camera can see. A
    /// <see cref="LineRenderer"/> billboards its strip toward the view, so a
    /// segment with one end behind the camera has no consistent screen-space
    /// width: Unity still emits its quad, and the projected result can be a
    /// huge triangle that fills the frame. That is what a route did when the
    /// player zoomed in past its near end and the ship, the route's first
    /// vertex, fell behind the camera.
    ///
    /// The clip keeps the longest contiguous run in front of the near plane and
    /// interpolates the crossing point, so the drawn polyline never has a
    /// vertex behind the camera. It is a geometry correction, separate from the
    /// apparent-width rule in <see cref="LineWidth"/>.
    /// </summary>
    public static class LineCameraClip
    {
        /// <summary>
        /// Write the visible part of <paramref name="points"/> into
        /// <paramref name="visible"/> and return its count. The two scratch
        /// lists let the caller reuse storage per frame; the longer of the
        /// visible runs wins, because a line that leaves the view and re-enters
        /// it cannot be drawn as one <see cref="LineRenderer"/> polyline.
        /// </summary>
        public static int Clip(IReadOnlyList<Vector3> points, Camera camera,
            List<Vector3> run, List<Vector3> visible)
        {
            if (visible == null)
            {
                if (run != null) run.Clear();
                return 0;
            }

            visible.Clear();
            if (run != null) run.Clear();
            if (points == null || points.Count == 0) return 0;

            if (camera == null)
            {
                for (int i = 0; i < points.Count; i++) visible.Add(points[i]);
                return visible.Count;
            }

            Vector3 origin = camera.transform.position;
            Vector3 forward = camera.transform.forward;
            float near = Mathf.Max(camera.nearClipPlane, 1e-4f);
            List<Vector3> current = run ?? new List<Vector3>();

            for (int i = 0; i < points.Count; i++)
            {
                Vector3 point = points[i];
                float depth = Vector3.Dot(point - origin, forward);
                bool inside = depth >= near;

                if (i == 0)
                {
                    if (inside) current.Add(point);
                    continue;
                }

                Vector3 previous = points[i - 1];
                float previousDepth = Vector3.Dot(previous - origin, forward);
                bool previousInside = previousDepth >= near;

                if (inside && previousInside)
                {
                    current.Add(point);
                }
                else if (inside && !previousInside)
                {
                    current.Add(Crossing(previous, point, previousDepth, depth, near, forward, origin));
                    current.Add(point);
                }
                else if (!inside && previousInside)
                {
                    current.Add(Crossing(previous, point, previousDepth, depth, near, forward, origin));
                    KeepLongest(current, visible);
                    current.Clear();
                }
            }

            KeepLongest(current, visible);
            return visible.Count;
        }

        private static Vector3 Crossing(Vector3 from, Vector3 to, float fromDepth,
            float toDepth, float near, Vector3 forward, Vector3 origin)
        {
            float span = fromDepth - toDepth;
            float t = Mathf.Abs(span) > 1e-9f
                ? Mathf.Clamp01((fromDepth - near) / span)
                : 0f;
            Vector3 point = Vector3.Lerp(from, to, t);

            // Put the crossing exactly on the plane; the interpolation and the
            // dot product disagree by float noise at these depths.
            float depth = Vector3.Dot(point - origin, forward);
            return point + forward * (near - depth);
        }

        private static void KeepLongest(List<Vector3> candidate, List<Vector3> best)
        {
            if (candidate == null || best == null || candidate == best) return;
            if (candidate.Count <= best.Count) return;

            best.Clear();
            best.AddRange(candidate);
        }
    }
}
