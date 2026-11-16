using UnityEngine;
using TimeToExplore.Core;

namespace TimeToExplore.Player
{
    /// <summary>
    /// A timed move of the ship from a recorded position to a moving
    /// destination. Position follows a smoothstep curve, so the move starts and
    /// ends at rest. The camera follows the ship, so there is no rotation to
    /// ease. The class is plain C# so the timing and easing can be tested
    /// without a scene.
    /// </summary>
    public class FocusTransition
    {
        public const float DefaultDurationSeconds = 3f;

        public float DurationSeconds = DefaultDurationSeconds;
        public bool IsActive { get; private set; }
        public float Progress { get; private set; }
        public float EasedProgress { get; private set; }
        public Vector3d StartPositionM { get; private set; }

        private float _elapsedSeconds;

        public void Begin(Vector3d startPositionM)
        {
            StartPositionM = startPositionM;
            _elapsedSeconds = 0f;
            Progress = 0f;
            EasedProgress = 0f;
            IsActive = true;
        }

        /// <summary>
        /// Advance the clock. Returns true on the step that reaches the end,
        /// at which point <see cref="IsActive"/> is already false and the
        /// caller should snap to the exact destination.
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

        public Vector3d PositionAt(Vector3d destinationM) =>
            StartPositionM + (destinationM - StartPositionM) * EasedProgress;

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
