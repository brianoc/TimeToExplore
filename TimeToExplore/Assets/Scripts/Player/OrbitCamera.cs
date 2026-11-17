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

        private float _distanceUnits = DefaultDistanceUnits;

        /// <summary>
        /// The current orbit distance, in render units. Assigning it places the
        /// camera immediately and cancels any pending request; a wheel zoom
        /// instead calls <see cref="RequestDistance"/> and lets the rig ease
        /// toward it with <see cref="EaseDistance"/>.
        ///
        /// The caller owns the range: the rig routes a wheel through
        /// <see cref="ZoomDistanceUnits"/> (local) or
        /// <see cref="MapView.ZoomDistanceUnits"/> (map), so the map's higher
        /// ceiling is not clamped away here.
        /// </summary>
        public float DistanceUnits
        {
            get => _distanceUnits;
            set
            {
                _distanceUnits = value;
                TargetDistanceUnits = _distanceUnits;
            }
        }

        /// <summary>
        /// The distance a zoom is heading for. Equal to
        /// <see cref="DistanceUnits"/> whenever no tween is pending.
        /// </summary>
        public float TargetDistanceUnits { get; private set; } = DefaultDistanceUnits;

        /// <summary>True when the camera has arrived and there is no zoom left to ease.</summary>
        public bool IsDistanceSettled => _distanceUnits == TargetDistanceUnits;

        /// <summary>Unit vector from the ship to the camera, in Unity space.</summary>
        public Vector3 OffsetDirection => Direction(YawDegrees, PitchDegrees);

        /// <summary>
        /// Unit vector from the ship to the camera for an arbitrary yaw and
        /// pitch. The rig uses this with the map's effective pitch, while the
        /// player's stored pitch is left alone.
        /// </summary>
        public static Vector3 Direction(float yawDegrees, float pitchDegrees)
        {
            float yaw = yawDegrees * Mathf.Deg2Rad;
            float pitch = pitchDegrees * Mathf.Deg2Rad;
            float cos = Mathf.Cos(pitch);
            return new Vector3(cos * Mathf.Sin(yaw), Mathf.Sin(pitch), cos * Mathf.Cos(yaw));
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

        /// <summary>
        /// Request a zoom without moving the camera: the distance eases there
        /// while <see cref="EaseDistance"/> runs. The caller routes the range
        /// through <see cref="ZoomDistanceUnits"/> or
        /// <see cref="MapView.ZoomDistanceUnits"/>, so the map's higher ceiling
        /// survives.
        /// </summary>
        public void RequestDistance(float distanceUnits)
        {
            TargetDistanceUnits = distanceUnits;
        }

        /// <summary>Place the camera immediately, for a reset or a start.</summary>
        public void SnapDistance(float distanceUnits)
        {
            DistanceUnits = distanceUnits;
        }

        /// <summary>
        /// Multiplicative zoom, requested from the pending target so rapid
        /// notches compound. The camera moves on the next
        /// <see cref="EaseDistance"/>, not in this call.
        /// </summary>
        public void Zoom(float steps, float factorPerStep = 1.25f)
        {
            RequestDistance(ZoomDistanceUnits(TargetDistanceUnits, steps, factorPerStep));
        }

        /// <summary>
        /// The local zoom curve as a pure function, clamped to the local range.
        /// The rig routes map-side zoom through <see cref="MapView"/> instead,
        /// whose ceiling is higher.
        /// </summary>
        public static float ZoomDistanceUnits(float distanceUnits, float steps,
            float factorPerStep = 1.25f)
        {
            if (steps == 0f) return Mathf.Clamp(distanceUnits, MinDistanceUnits, MaxDistanceUnits);
            if (factorPerStep <= 0f) throw new ArgumentOutOfRangeException(nameof(factorPerStep));

            double scale = Math.Pow(factorPerStep, steps);
            return Mathf.Clamp((float)(distanceUnits * scale),
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

        /// <summary>
        /// Ease the distance toward the requested target, exponentially in time.
        /// Returns true while the camera is still travelling and false once it
        /// has arrived (or when the frame is too short to move), so a caller can
        /// stop work when the zoom settles.
        /// </summary>
        public bool EaseDistance(float deltaTime, float ratePerSecond = 2.5f)
        {
            float target = TargetDistanceUnits;

            if (!(deltaTime > 0f) || !(ratePerSecond > 0f))
            {
                _distanceUnits = target;
                return false;
            }

            if (_distanceUnits == target) return false;

            float t = 1f - Mathf.Exp(-ratePerSecond * deltaTime);
            float next = Mathf.Lerp(_distanceUnits, target, t);

            // Snap the last float noise so the pose settles exactly and the
            // remembered distance round-trips without a permanent crawl.
            if (Mathf.Abs(next - target) <= Mathf.Max(target * 1e-6f, 1e-9f))
            {
                _distanceUnits = target;
                return false;
            }

            _distanceUnits = next;
            return true;
        }

        /// <summary>
        /// Request a target and ease one frame toward it. A caller that
        /// recomputes its target every frame can use this instead of the two
        /// separate calls.
        /// </summary>
        public void SmoothDistance(float targetUnits, float deltaTime, float ratePerSecond = 2.5f)
        {
            RequestDistance(targetUnits);
            EaseDistance(deltaTime, ratePerSecond);
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
