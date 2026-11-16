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
    }

    /// <summary>
    /// Screen-space body picking. Raycasts are not used because body colliders
    /// are removed and the marker aid can draw a body many times its physical
    /// size; picking against the drawn marker is what the player expects.
    /// </summary>
    public static class BodyPicker
    {
        public const float MinimumHitRadiusPixels = 9f;

        public static BodyRuntime Pick(IReadOnlyList<ScreenTarget> targets, Vector2 pointer,
            float minimumHitRadiusPixels = MinimumHitRadiusPixels)
        {
            if (targets == null) return null;

            BodyRuntime best = null;
            float bestDepth = float.PositiveInfinity;

            for (int i = 0; i < targets.Count; i++)
            {
                ScreenTarget target = targets[i];
                if (target.Body == null) continue;

                float radius = Mathf.Max(target.RadiusPixels, minimumHitRadiusPixels);
                float dx = pointer.x - target.Centre.x;
                float dy = pointer.y - target.Centre.y;
                if (dx * dx + dy * dy > radius * radius) continue;

                if (target.Depth < bestDepth)
                {
                    bestDepth = target.Depth;
                    best = target.Body;
                }
            }

            return best;
        }
    }
}
