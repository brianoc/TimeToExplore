using System;
using UnityEngine;
using TimeToExplore.Core;

namespace TimeToExplore.Player
{
    /// <summary>
    /// An orbit camera around the ship: yaw, pitch, and a distance in render
    /// units. The ship stays at the render origin
    /// ([ADR-002](/decisions/adr-002-double-precision.md)), so the camera's
    /// position *is* the offset from the ship; nothing has to move the world.
    ///
    /// Plain C# with no scene dependency, so the limits, the zoom curve, and the
    /// auto-framing slew are verified in EditMode.
    /// </summary>
    public sealed class OrbitCamera
    {
        public const float MinPitchDegrees = -85f;
        public const float MaxPitchDegrees = 85f;

        /// <summary>Closest orbit: 2,000 km, enough to inspect a parking orbit.</summary>
        public const float MinDistanceUnits = 0.002f;

        /// <summary>Farthest orbit: 5 billion km, about 33 AU.</summary>
        public const float MaxDistanceUnits = 5_000_000f;

        /// <summary>Starting orbit: 250,000 km, the Earth–Moon neighbourhood.</summary>
        public const float DefaultDistanceUnits = 0.25f;

        /// <summary>Starting pitch: a little above the ecliptic.</summary>
        public const float DefaultPitchDegrees = 14f;

        public float YawDegrees;
        public float PitchDegrees = DefaultPitchDegrees;
        public float DistanceUnits = DefaultDistanceUnits;

        /// <summary>Unit vector from the ship to the camera, in Unity space.</summary>
        public Vector3 OffsetDirection
        {
            get
            {
                float yaw = YawDegrees * Mathf.Deg2Rad;
                float pitch = PitchDegrees * Mathf.Deg2Rad;
                float cos = Mathf.Cos(pitch);
                return new Vector3(cos * Mathf.Sin(yaw), Mathf.Sin(pitch), cos * Mathf.Cos(yaw));
            }
        }

        /// <summary>Camera position relative to the ship, in render units.</summary>
        public Vector3 OffsetUnits => OffsetDirection * DistanceUnits;

        /// <summary>Rotation that looks from the offset back at the ship.</summary>
        public Quaternion Rotation => Quaternion.LookRotation(-OffsetDirection, Vector3.up);

        /// <summary>Orbit by a mouse delta, in degrees.</summary>
        public void Turn(float deltaYawDegrees, float deltaPitchDegrees)
        {
            YawDegrees = WrapDegrees(YawDegrees + deltaYawDegrees);
            PitchDegrees = Mathf.Clamp(PitchDegrees + deltaPitchDegrees,
                MinPitchDegrees, MaxPitchDegrees);
        }

        /// <summary>Multiplicative zoom, so each notch feels the same at any scale.</summary>
        public void Zoom(float steps, float factorPerStep = 1.25f)
        {
            if (steps == 0f) return;
            if (factorPerStep <= 0f) throw new ArgumentOutOfRangeException(nameof(factorPerStep));

            double scale = Math.Pow(factorPerStep, steps);
            DistanceUnits = Mathf.Clamp((float)(DistanceUnits * scale),
                MinDistanceUnits, MaxDistanceUnits);
        }

        /// <summary>Point the camera so it looks along a direction from the ship.</summary>
        public void AimAlong(Vector3d lookDirection)
        {
            if (lookDirection.SqrMagnitude < 1e-12) return;

            Vector3d offset = -lookDirection.Normalized;
            PitchDegrees = Mathf.Clamp(
                (float)(Math.Asin(Math.Clamp(offset.y, -1.0, 1.0)) * Mathf.Rad2Deg),
                MinPitchDegrees, MaxPitchDegrees);
            YawDegrees = WrapDegrees((float)(Math.Atan2(offset.x, offset.z) * Mathf.Rad2Deg));
        }

        /// <summary>
        /// Turn toward a look direction at a limited rate. Returns the angle left
        /// to travel, in degrees, so a caller can tell when the aim has settled.
        /// </summary>
        public float SlewToLookDirection(Vector3d lookDirection, float degreesPerSecond,
            float deltaTime)
        {
            if (lookDirection.SqrMagnitude < 1e-12) return 0f;

            Vector3d offset = -lookDirection.Normalized;
            float targetPitch = Mathf.Clamp(
                (float)(Math.Asin(Math.Clamp(offset.y, -1.0, 1.0)) * Mathf.Rad2Deg),
                MinPitchDegrees, MaxPitchDegrees);
            float targetYaw = WrapDegrees(
                (float)(Math.Atan2(offset.x, offset.z) * Mathf.Rad2Deg));

            return SlewTo(targetYaw, targetPitch, degreesPerSecond, deltaTime);
        }

        /// <summary>Turn toward an absolute yaw and pitch at a limited rate.</summary>
        public float SlewTo(float targetYawDegrees, float targetPitchDegrees,
            float degreesPerSecond, float deltaTime)
        {
            float yawDelta = Mathf.DeltaAngle(YawDegrees, targetYawDegrees);
            float pitchDelta = targetPitchDegrees - PitchDegrees;
            float remaining = Mathf.Sqrt(yawDelta * yawDelta + pitchDelta * pitchDelta);

            float budget = Mathf.Max(0f, degreesPerSecond) * Mathf.Max(0f, deltaTime);
            if (budget <= 0f) return remaining;

            if (remaining <= budget || remaining < 1e-4f)
            {
                YawDegrees = WrapDegrees(targetYawDegrees);
                PitchDegrees = Mathf.Clamp(targetPitchDegrees, MinPitchDegrees, MaxPitchDegrees);
                return 0f;
            }

            float step = budget / remaining;
            YawDegrees = WrapDegrees(YawDegrees + yawDelta * step);
            PitchDegrees = Mathf.Clamp(PitchDegrees + pitchDelta * step,
                MinPitchDegrees, MaxPitchDegrees);
            return remaining - budget;
        }

        /// <summary>Ease the distance toward a target, exponentially in time.</summary>
        public void SmoothDistance(float targetUnits, float deltaTime, float ratePerSecond = 2.5f)
        {
            targetUnits = Mathf.Clamp(targetUnits, MinDistanceUnits, MaxDistanceUnits);
            if (!(deltaTime > 0f) || !(ratePerSecond > 0f))
            {
                DistanceUnits = targetUnits;
                return;
            }

            float t = 1f - Mathf.Exp(-ratePerSecond * deltaTime);
            DistanceUnits = Mathf.Clamp(Mathf.Lerp(DistanceUnits, targetUnits, t),
                MinDistanceUnits, MaxDistanceUnits);
        }

        /// <summary>Look at a point from a given ship position, keeping the distance.</summary>
        public void Frame(Vector3d shipPositionM, Vector3d targetPositionM)
        {
            AimAlong(targetPositionM - shipPositionM);
        }

        public static float WrapDegrees(float degrees)
        {
            degrees %= 360f;
            if (degrees < 0f) degrees += 360f;
            return degrees;
        }
    }
}
