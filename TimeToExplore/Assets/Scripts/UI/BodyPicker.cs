using System.Collections.Generic;
using UnityEngine;
using TimeToExplore.Core;

namespace TimeToExplore.UI
{
    /// <summary>A body projected onto the screen for mouse picking.</summary>
    public struct ScreenTarget
    {
        public BodyRuntime Body;
        public Vector2 Centre;
        public float RadiusPixels;
        /// <summary>Distance from the camera; the nearest hit wins.</summary>
        public float Depth;
        /// <summary>
        /// Map class rank. On the map a higher rank wins an overlapping hit
        /// before depth is considered; 0 is the default for the local picker.
        /// </summary>
        public int Priority;
    }

    /// <summary>
    /// Screen-space body picking. Raycasts are not used because body colliders
    /// are removed and the marker aid can draw a body many times its physical
    /// size; picking against the drawn marker is what the player expects.
    /// </summary>
    public static class BodyPicker
    {
        public const float MinimumHitRadiusPixels = 9f;

        /// <summary>Sun, planet, or station: wins an overlapping hit on the map.</summary>
        public const int MajorBodyPriority = 1;

        /// <summary>Moon or asteroid: selectable when it is not covered.</summary>
        public const int MinorBodyPriority = 0;

        /// <summary>
        /// Class rank for map picking. The Sun, planets, and stations share the
        /// top rank; moons and asteroids are below them.
        /// </summary>
        public static int PriorityOf(BodyRuntime body)
        {
            if (body == null) return MinorBodyPriority;
            switch (body.Kind)
            {
                case "star":
                case "planet":
                case "station":
                    return MajorBodyPriority;
                default:
                    return MinorBodyPriority;
            }
        }

        /// <summary>
        /// Pick the target under the pointer. Locally the nearest hit wins. On
        /// the map, when <paramref name="classPriority"/> is set, the highest
        /// class among the hits wins and distance only breaks a tie within the
        /// same class — so a planet is not stolen by a moon drawn behind it.
        /// A moon separated from its planet by more than the hit radius is the
        /// only hit and remains selectable.
        /// </summary>
        public static BodyRuntime Pick(IReadOnlyList<ScreenTarget> targets, Vector2 pointer,
            float minimumHitRadiusPixels = MinimumHitRadiusPixels, bool classPriority = false)
        {
            if (targets == null) return null;

            BodyRuntime best = null;
            int bestPriority = int.MinValue;
            float bestDepth = float.PositiveInfinity;

            for (int i = 0; i < targets.Count; i++)
            {
                ScreenTarget target = targets[i];
                if (target.Body == null) continue;

                float radius = Mathf.Max(target.RadiusPixels, minimumHitRadiusPixels);
                float dx = pointer.x - target.Centre.x;
                float dy = pointer.y - target.Centre.y;
                if (dx * dx + dy * dy > radius * radius) continue;

                bool better = classPriority
                    ? target.Priority > bestPriority ||
                      (target.Priority == bestPriority && target.Depth < bestDepth)
                    : target.Depth < bestDepth;

                if (!better) continue;

                best = target.Body;
                bestPriority = target.Priority;
                bestDepth = target.Depth;
            }

            return best;
        }
    }
}
