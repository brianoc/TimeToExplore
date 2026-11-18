using System;
using System.Collections.Generic;
using UnityEngine;
using TimeToExplore.Simulation;

namespace TimeToExplore.Rendering
{
    /// <summary>
    /// The path type an arc carries. These are the only implementations of
    /// <see cref="IArcPath"/>: a continuous powered quintic, a Keplerian conic
    /// coast, and a fixed-thrust burn arc.
    /// </summary>
    public enum RouteArcKind
    {
        /// <summary>A continuous powered quintic: departure, cruise, or a
        /// continuous capture.</summary>
        Quintic,
        /// <summary>A Keplerian conic with no thrust on the reference path.</summary>
        Conic,
        /// <summary>A fixed-thrust burn arc at the player's chosen level.</summary>
        FiniteBurn
    }

    /// <summary>How one plan arc is drawn: its colour, whether it is dashed,
    /// and a short label for diagnostics and tests.</summary>
    public readonly struct RouteArcStyle
    {
        public RouteArcStyle(Color colour, bool dashed, string label)
        {
            Colour = colour;
            Dashed = dashed;
            Label = label;
        }

        public Color Colour { get; }
        public bool Dashed { get; }
        public string Label { get; }
    }

    /// <summary>
    /// One colour per arc <em>type</em> on a route, so the player reads the
    /// physics at a glance: gold for a continuous quintic, blue for a Keplerian
    /// conic coast, red for a fixed-thrust burn. Line style carries the same
    /// split — solid when the drive is on, dashed when it is off.
    ///
    /// A plan often runs two or three arcs of the same type back to back: the
    /// default quintic arrival is three quintics, the conic arrival is three
    /// conics, and the hybrid ends with a transfer conic and a parking conic.
    /// Those runs are separated by a deliberate shade change within the same
    /// hue, so consecutive arcs stay one family but are individually legible.
    /// The first arc of a run takes the base colour, the second a darker shade,
    /// the third a paler one, and any further arc repeats the cycle.
    ///
    /// Pure C#, so the mapping is tested without a scene.
    /// </summary>
    public static class RouteColours
    {
        /// <summary>A continuous powered quintic: warm gold.</summary>
        public static readonly Color Quintic = new Color(1f, 0.84f, 0.38f, 0.92f);

        /// <summary>A Keplerian conic coast: sky blue.</summary>
        public static readonly Color Conic = new Color(0.44f, 0.83f, 1f, 0.95f);

        /// <summary>A fixed-thrust burn arc: red.</summary>
        public static readonly Color FixedBurn = new Color(1f, 0.32f, 0.32f, 0.95f);

        /// <summary>How much darker the second arc of a same-type run is.</summary>
        public const float DarkShadeFactor = 0.62f;

        /// <summary>How far the third arc of a same-type run moves toward white.</summary>
        public const float LightShadeMix = 0.45f;

        private static readonly RouteArcStyle QuinticStyle =
            new RouteArcStyle(Quintic, false, "Quintic arc");
        private static readonly RouteArcStyle ConicStyle =
            new RouteArcStyle(Conic, true, "Conic arc");
        private static readonly RouteArcStyle FixedBurnStyle =
            new RouteArcStyle(FixedBurn, false, "Fixed burn");

        /// <summary>The path type an arc carries.</summary>
        public static RouteArcKind KindOf(PlanArc arc)
        {
            if (arc == null) throw new ArgumentNullException(nameof(arc));
            if (arc.IsFiniteBurn) return RouteArcKind.FiniteBurn;
            return arc.IsCoast ? RouteArcKind.Conic : RouteArcKind.Quintic;
        }

        /// <summary>The base style for a real plan arc, ignoring its neighbours.</summary>
        public static RouteArcStyle For(PlanArc arc) => For(KindOf(arc), 0);

        /// <summary>The style for a real plan arc at a position in the plan,
        /// shaded against the run of same-type arcs it belongs to.</summary>
        public static RouteArcStyle For(IReadOnlyList<PlanArc> arcs, int index)
        {
            if (arcs == null) throw new ArgumentNullException(nameof(arcs));
            if (index < 0 || index >= arcs.Count)
                throw new ArgumentOutOfRangeException(nameof(index));

            return For(KindOf(arcs[index]), RunIndex(arcs, index));
        }

        /// <summary>
        /// The style for an arc type at a position within a run of the same
        /// type: 0 is the base colour, 1 a darker shade, 2 a paler shade, then
        /// the cycle repeats.
        /// </summary>
        public static RouteArcStyle For(RouteArcKind kind, int runIndex)
        {
            Color colour = Shade(BaseColour(kind), runIndex);
            return new RouteArcStyle(colour, kind == RouteArcKind.Conic, Label(kind));
        }

        /// <summary>How many arcs of the same type immediately precede
        /// <paramref name="index"/> in the plan.</summary>
        public static int RunIndex(IReadOnlyList<PlanArc> arcs, int index)
        {
            if (arcs == null) throw new ArgumentNullException(nameof(arcs));
            if (index < 0 || index >= arcs.Count)
                throw new ArgumentOutOfRangeException(nameof(index));

            RouteArcKind kind = KindOf(arcs[index]);
            int run = 0;
            for (int i = index - 1; i >= 0 && KindOf(arcs[i]) == kind; i--) run++;
            return run;
        }

        /// <summary>The base colour of an arc type.</summary>
        public static Color BaseColour(RouteArcKind kind)
        {
            switch (kind)
            {
                case RouteArcKind.Conic: return Conic;
                case RouteArcKind.FiniteBurn: return FixedBurn;
                default: return Quintic;
            }
        }

        /// <summary>The legend label of an arc type.</summary>
        public static string Label(RouteArcKind kind)
        {
            switch (kind)
            {
                case RouteArcKind.Conic: return "Conic arc";
                case RouteArcKind.FiniteBurn: return "Fixed burn";
                default: return "Quintic arc";
            }
        }

        /// <summary>
        /// The shaded variant at a run position. Scaling the RGB keeps the hue;
        /// mixing toward white pales it. Three variants are enough for the
        /// longest run the planner emits (three quintics or three conics).
        /// </summary>
        public static Color Shade(Color colour, int runIndex)
        {
            switch (((runIndex % 3) + 3) % 3)
            {
                case 1:
                    return new Color(colour.r * DarkShadeFactor,
                        colour.g * DarkShadeFactor,
                        colour.b * DarkShadeFactor,
                        colour.a);
                case 2:
                    return new Color(
                        Mathf.Lerp(colour.r, 1f, LightShadeMix),
                        Mathf.Lerp(colour.g, 1f, LightShadeMix),
                        Mathf.Lerp(colour.b, 1f, LightShadeMix),
                        colour.a);
                default:
                    return colour;
            }
        }
    }
}
