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
        /// <summary>Distance from the camera; it breaks a tie within a rank.</summary>
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

        // The importance ladder. A higher rank wins an overlapping hit before
        // distance is considered, so the big, durable object under the pointer
        // is what a click selects even when a smaller marker sits in front.
        public const int StarBodyPriority = 5;
        public const int PlanetBodyPriority = 4;
        public const int StationBodyPriority = 3;
        public const int MoonBodyPriority = 2;
        public const int AsteroidBodyPriority = 1;
        public const int UnknownBodyPriority = 0;

        /// <summary>
        /// Importance of a catalogue body: star above planet, planet above
        /// station, station above moon, moon above asteroid. A null or unknown
        /// body ranks last but stays selectable.
        /// </summary>
        public static int PriorityOf(BodyRuntime body)
        {
            if (body == null) return UnknownBodyPriority;
            switch (body.Kind)
            {
                case "star": return StarBodyPriority;
                case "planet": return PlanetBodyPriority;
                case "station": return StationBodyPriority;
                case "moon": return MoonBodyPriority;
                case "asteroid": return AsteroidBodyPriority;
                default: return UnknownBodyPriority;
            }
        }

        /// <summary>
        /// The most important target under the pointer. Rank wins first; depth
        /// only breaks a tie within one rank, so a nearer moon cannot steal a
        /// planet. Clicking again cycles through the rest of the stack.
        /// </summary>
        public static BodyRuntime Pick(IReadOnlyList<ScreenTarget> targets, Vector2 pointer,
            float minimumHitRadiusPixels = MinimumHitRadiusPixels) =>
            Pick(targets, pointer, out _, minimumHitRadiusPixels);

        /// <summary>
        /// <see cref="Pick(IReadOnlyList{ScreenTarget},Vector2,float)"/> plus the
        /// number of bodies whose drawn marker contains the pointer.
        /// </summary>
        public static BodyRuntime Pick(IReadOnlyList<ScreenTarget> targets, Vector2 pointer,
            out int hitCount, float minimumHitRadiusPixels = MinimumHitRadiusPixels)
        {
            hitCount = 0;
            if (targets == null) return null;

            BodyRuntime best = null;
            int bestPriority = int.MinValue;
            float bestDepth = float.PositiveInfinity;

            for (int i = 0; i < targets.Count; i++)
            {
                ScreenTarget target = targets[i];
                if (target.Body == null) continue;
                if (!Contains(target, pointer, minimumHitRadiusPixels)) continue;

                hitCount++;
                int priority = PriorityOf(target.Body);
                bool better = priority > bestPriority ||
                              (priority == bestPriority && target.Depth < bestDepth);
                if (!better) continue;

                best = target.Body;
                bestPriority = priority;
                bestDepth = target.Depth;
            }

            return best;
        }

        /// <summary>
        /// Every target under the pointer, most important first and the nearest
        /// first within one rank. The list is cleared and refilled, so a caller
        /// can reuse it. This is the click list that the repeated-click cycle
        /// walks through.
        /// </summary>
        public static void OrderedHits(IReadOnlyList<ScreenTarget> targets, Vector2 pointer,
            List<ScreenTarget> hits,
            float minimumHitRadiusPixels = MinimumHitRadiusPixels)
        {
            if (hits == null) return;
            hits.Clear();
            if (targets == null) return;

            for (int i = 0; i < targets.Count; i++)
            {
                ScreenTarget target = targets[i];
                if (target.Body == null) continue;
                if (Contains(target, pointer, minimumHitRadiusPixels)) hits.Add(target);
            }

            hits.Sort(CompareHits);
        }

        private static bool Contains(ScreenTarget target, Vector2 pointer,
            float minimumHitRadiusPixels)
        {
            float radius = Mathf.Max(target.RadiusPixels, minimumHitRadiusPixels);
            float dx = pointer.x - target.Centre.x;
            float dy = pointer.y - target.Centre.y;
            return dx * dx + dy * dy <= radius * radius;
        }

        private static int CompareHits(ScreenTarget a, ScreenTarget b)
        {
            int priority = PriorityOf(b.Body).CompareTo(PriorityOf(a.Body));
            if (priority != 0) return priority;

            int depth = a.Depth.CompareTo(b.Depth);
            if (depth != 0) return depth;

            // A stable order keeps the cycle predictable when two markers sit
            // at exactly the same depth.
            return string.CompareOrdinal(a.Body.Id, b.Body.Id);
        }
    }

    /// <summary>
    /// The repeated-click cycle over one stack of overlapping markers. A click
    /// at the same pointer starts at the most important target and each further
    /// click at that spot advances one step, wrapping at the end. Moving the
    /// pointer, or the stack changing, starts again from the top.
    /// </summary>
    public sealed class PickCycle
    {
        /// <summary>How far the pointer may move between clicks and still count
        /// as the same spot. It matches the click-versus-drag slop.</summary>
        public const float PointerSlopPixels = 4f;

        private readonly List<ScreenTarget> _last = new List<ScreenTarget>();
        private Vector2 _pointer;
        private int _index = -1;

        public void Reset()
        {
            _index = -1;
            _last.Clear();
        }

        /// <summary>
        /// The target the next click at this pointer would select, without
        /// advancing the cycle. Returns -1 when nothing is under the pointer.
        /// </summary>
        public int Peek(IReadOnlyList<ScreenTarget> hits, Vector2 pointer)
        {
            if (hits == null || hits.Count == 0) return -1;
            return Matches(hits, pointer) ? (_index + 1) % hits.Count : 0;
        }

        /// <summary>
        /// Advance the cycle for a completed click and return the index to
        /// select, or -1 when nothing is under the pointer. A click on a new
        /// spot or a changed stack starts again at the most important target.
        /// </summary>
        public int Advance(IReadOnlyList<ScreenTarget> hits, Vector2 pointer)
        {
            if (hits == null || hits.Count == 0)
            {
                Reset();
                return -1;
            }

            bool same = Matches(hits, pointer);
            _index = same ? (_index + 1) % hits.Count : 0;
            _pointer = pointer;

            _last.Clear();
            for (int i = 0; i < hits.Count; i++) _last.Add(hits[i]);

            return _index;
        }

        private bool Matches(IReadOnlyList<ScreenTarget> hits, Vector2 pointer)
        {
            if (_index < 0 || _last.Count != hits.Count) return false;

            float dx = pointer.x - _pointer.x;
            float dy = pointer.y - _pointer.y;
            if (dx * dx + dy * dy > PointerSlopPixels * PointerSlopPixels) return false;

            for (int i = 0; i < hits.Count; i++)
            {
                if (!ReferenceEquals(_last[i].Body, hits[i].Body)) return false;
            }

            return true;
        }
    }
}
