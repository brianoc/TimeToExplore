using UnityEngine;
using TimeToExplore.Core;

namespace TimeToExplore.Player
{
    /// <summary>
    /// A timed return to the ship: the orbit distance eases geometrically to a
    /// local value while the look-at offset unwinds to zero. The distance is
    /// interpolated in log space, so a descent from the outer zoom ceiling to
    /// the local band changes the view at an even rate instead of doing nothing
    /// and then collapsing at the end. Both follow one smoothstep, so the zoom
    /// and the unwind start and end at rest. Plain C#, so the timing and the
    /// endpoints are verified without a scene.
    /// </summary>
    public sealed class HomeTransition
    {
        public const float DefaultDurationSeconds = 1f;

        public float DurationSeconds = DefaultDurationSeconds;
        public bool IsActive { get; private set; }
        public float Progress { get; private set; }
        public float EasedProgress { get; private set; }
        public float StartDistanceUnits { get; private set; }
        public float TargetDistanceUnits { get; private set; }
        public Vector3d StartLookAtOffsetM { get; private set; }

        private float _elapsedSeconds;

        public void Begin(float startDistanceUnits, float targetDistanceUnits,
            Vector3d startLookAtOffsetM)
        {
            StartDistanceUnits = startDistanceUnits;
            TargetDistanceUnits = targetDistanceUnits;
            StartLookAtOffsetM = startLookAtOffsetM;
            _elapsedSeconds = 0f;
            Progress = 0f;
            EasedProgress = 0f;
            IsActive = true;
        }

        /// <summary>
        /// Advance the clock. Returns true on the step that reaches the end,
        /// at which point <see cref="IsActive"/> is already false and the
        /// caller should snap to the exact endpoints.
        /// </summary>
        public bool Advance(float deltaTime)
        {
            if (!IsActive) return false;

            _elapsedSeconds += Mathf.Max(0f, deltaTime);
            float duration = Mathf.Max(DurationSeconds, 0f);
            Progress = duration <= 0f ? 1f : Mathf.Clamp01(_elapsedSeconds / duration);
            EasedProgress = Ease(Progress);

            if (Progress < 1f) return false;
            IsActive = false;
            return true;
        }

        /// <summary>
        /// The orbit distance on this step, between the two endpoints. Log-space
        /// so the perceived zoom rate is even across a long descent.
        /// </summary>
        public float DistanceUnitsAt
        {
            get
            {
                if (StartDistanceUnits > 0f && TargetDistanceUnits > 0f)
                {
                    return Mathf.Exp(Mathf.Lerp(Mathf.Log(StartDistanceUnits),
                        Mathf.Log(TargetDistanceUnits), EasedProgress));
                }

                return Mathf.Lerp(StartDistanceUnits, TargetDistanceUnits, EasedProgress);
            }
        }

        /// <summary>The look-at offset on this step, unwinding from the start offset.</summary>
        public Vector3d LookAtOffsetMAt => StartLookAtOffsetM * (1.0 - EasedProgress);

        /// <summary>Smoothstep: zero velocity at both ends, no overshoot.</summary>
        public static float Ease(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3f - 2f * t);
        }

        public void Cancel()
        {
            IsActive = false;
        }
    }
}
