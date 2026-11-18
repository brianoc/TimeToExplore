using System;
using System.Collections.Generic;
using TimeToExplore.Core;

namespace TimeToExplore.Simulation
{
    /// <summary>
    /// Turns an arc path into a polyline whose chords follow the curve to a
    /// fixed <em>relative</em> tolerance. Uniform sampling in the arc parameter
    /// spends the same number of points on the nearly straight cruise as on the
    /// tight departure and capture turns, which is why a long plan used to show
    /// as a handful of long chords at each end. The split test is the sagitta
    /// (midpoint deviation) over the chord length: because both scale with the
    /// zoom, the same tolerance holds when a chord covers the screen and when
    /// it covers a pixel.
    ///
    /// Pure C# with no UnityEngine dependency, so it lives in the simulation
    /// assembly and is verified in EditMode against the real plan curves.
    /// </summary>
    public static class PathTessellation
    {
        /// <summary>
        /// Largest midpoint deviation allowed, as a fraction of the chord
        /// length. 5 × 10⁻⁴ puts the error below about half a pixel on a chord
        /// that spans the 1080-pixel viewport.
        /// </summary>
        public const double MaxSagittaRatio = 5e-4;

        /// <summary>
        /// Hard ceiling on an arc's segments, so a pathological plan cannot
        /// stall a frame. A normal capture arc lands well below it.
        /// </summary>
        public const int MaxSegmentsPerArc = 4096;

        private const int MaxDepth = 18;

        /// <summary>
        /// Append the arc's polyline to <paramref name="output"/>. The start
        /// point is written when <paramref name="includeStart"/> is true; pass
        /// false for a seam whose previous arc already wrote it.
        /// </summary>
        public static void Append(IArcPath path, List<Vector3d> output, bool includeStart = true)
        {
            Append(path, output, null, includeStart);
        }

        /// <summary>
        /// Append the arc and, when <paramref name="parameters"/> is not null,
        /// the arc parameter of each point. The parameters make the flatness
        /// guarantee directly testable: every chord's midpoint can be compared
        /// with the true curve at the same parameter.
        /// </summary>
        public static void Append(IArcPath path, List<Vector3d> output,
            List<double> parameters, bool includeStart = true)
        {
            if (output == null) throw new ArgumentNullException(nameof(output));

            Vector3d start = path.PositionAt(0.0);
            Vector3d end = path.PositionAt(1.0);
            if (includeStart)
            {
                output.Add(start);
                parameters?.Add(0.0);
            }

            int segments = 0;
            Subdivide(path, 0.0, 1.0, start, end, output, parameters,
                ref segments, 0);
        }

        private static void Subdivide(IArcPath path, double u0, double u1,
            Vector3d p0, Vector3d p1, List<Vector3d> output, List<double> parameters,
            ref int segments, int depth)
        {
            double chord = Vector3d.Distance(p0, p1);

            if (depth >= MaxDepth || segments >= MaxSegmentsPerArc || chord <= 0.0)
            {
                AddLeaf(u1, p1, output, parameters, ref segments);
                return;
            }

            double um = 0.5 * (u0 + u1);
            Vector3d mid = path.PositionAt(um);
            Vector3d chordMid = (p0 + p1) * 0.5;
            double sagitta = Vector3d.Distance(mid, chordMid);

            if (sagitta <= MaxSagittaRatio * chord)
            {
                AddLeaf(u1, p1, output, parameters, ref segments);
                return;
            }

            Subdivide(path, u0, um, p0, mid, output, parameters, ref segments, depth + 1);
            Subdivide(path, um, u1, mid, p1, output, parameters, ref segments, depth + 1);
        }

        private static void AddLeaf(double u, Vector3d point, List<Vector3d> output,
            List<double> parameters, ref int segments)
        {
            output.Add(point);
            parameters?.Add(u);
            segments++;
        }
    }
}
