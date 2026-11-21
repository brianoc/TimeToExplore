using UnityEngine;
using TimeToExplore.Core;

namespace TimeToExplore.Player
{
    /// <summary>
    /// A timed move from the player's current view to the pose that frames a
    /// plotted route. Yaw takes the shortest turn, pitch and pan interpolate
    /// linearly, and distance interpolates in log space so a change of several
    /// orders of magnitude reads as an even zoom rather than a jump. One
    /// smoothstep drives all four, so the move starts and ends at rest.
    ///
    /// Plain C#, so the wrap, the log distance, the exact endpoints, and the
    /// cancellation are verified in EditMode without a scene.
    /// </summary>
    public sealed class RouteViewTransition
    {
        public const float DefaultDurationSeconds = 1.5f;

        public float DurationSeconds = DefaultDurationSeconds;
        public bool IsActive { get; private set; }
        public float Progress { get; private set; }
        public float EasedProgress { get; private set; }

        public float StartYawDegrees { get; private set; }
        public float TargetYawDegrees { get; private set; }
        public float StartPitchDegrees { get; private set; }
        public float TargetPitchDegrees { get; private set; }
        public float StartDistanceUnits { get; private set; }
        public float TargetDistanceUnits { get; private set; }
        public Vector3d StartPanM { get; private set; }
        public Vector3d TargetPanM { get; private set; }

        private float _elapsedSeconds;

        public void Begin(float startYawDegrees, float startPitchDegrees,
            float startDistanceUnits, Vector3d startPanM, float targetYawDegrees,
            float targetPitchDegrees, float targetDistanceUnits, Vector3d targetPanM)
        {
            StartYawDegrees = startYawDegrees;
            StartPitchDegrees = startPitchDegrees;
            StartDistanceUnits = startDistanceUnits;
            StartPanM = startPanM;
            TargetYawDegrees = targetYawDegrees;
            TargetPitchDegrees = targetPitchDegrees;
            TargetDistanceUnits = targetDistanceUnits;
            TargetPanM = targetPanM;
            _elapsedSeconds = 0f;
            Progress = 0f;
            EasedProgress = 0f;
            IsActive = true;
        }

        /// <summary>
        /// Advance the clock. Returns true on the step that reaches the end,
        /// at which point <see cref="IsActive"/> is already false and the
        /// caller should snap to the exact targets.
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

        /// <summary>The yaw on this step, taking the shortest turn.</summary>
        public float YawDegreesAt =>
            OrbitCamera.WrapDegrees(StartYawDegrees +
                                    Mathf.DeltaAngle(StartYawDegrees, TargetYawDegrees) *
                                    EasedProgress);

        public float PitchDegreesAt =>
            Mathf.Lerp(StartPitchDegrees, TargetPitchDegrees, EasedProgress);

        /// <summary>The distance on this step, geometric like a zoom.</summary>
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

        public Vector3d PanMAt =>
            StartPanM + (TargetPanM - StartPanM) * EasedProgress;

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
