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

        /// <summary>
        /// Farthest orbit: 12 billion km, about 80 AU. It frames Neptune's
        /// 4.5-billion-km orbit at 60° FOV with margin, so one gesture reaches
        /// the whole catalogue.
        /// </summary>
        public const float MaxDistanceUnits = 12_000_000f;

        /// <summary>Starting orbit: 250,000 km, the Earth–Moon neighbourhood.</summary>
        public const float DefaultDistanceUnits = 0.25f;

        /// <summary>Starting pitch: a little above the ecliptic.</summary>
        public const float DefaultPitchDegrees = 14f;

        /// <summary>Near plane as a fraction of the orbit distance.</summary>
        public const float NearClipFraction = 0.02f;

        /// <summary>Nearest near plane: 200 m (the closest zoom inside a parking orbit).</summary>
        public const float MinNearClipUnits = 0.0002f;

        /// <summary>Farthest near plane: 50 km.</summary>
        public const float MaxNearClipUnits = 0.05f;

        /// <summary>
        /// Far plane: 20 billion km (about 133 AU). A 12-billion-km camera
        /// plus a 60 AU look-at offset and the catalogue's 60 AU width still
        /// fits, and the near:far ratio stays inside a float depth range.
        /// </summary>
        public const float FarClipUnits = 20_000_000f;

        public float YawDegrees;
        public float PitchDegrees = DefaultPitchDegrees;

        private float _distanceUnits = DefaultDistanceUnits;

        /// <summary>
        /// The current orbit distance, in render units. Assigning it places the
        /// camera immediately and cancels any pending request; a wheel zoom
        /// instead calls <see cref="RequestDistance"/> and lets the rig ease
        /// toward it with <see cref="EaseDistance"/>.
        ///
        /// A wheel zoom instead calls <see cref="RequestDistance"/> and lets
        /// the rig ease toward it; assigning the distance places the camera
        /// immediately and cancels any pending request.
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
        /// pitch. Kept as a pure function so the route fit can test a candidate
        /// pose without moving the camera.
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
        /// while <see cref="EaseDistance"/> runs.
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
        /// The zoom curve as a pure function, clamped to the orbit range.
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

        /// <summary>
        /// The near plane for an orbit distance: 2% of the distance, clamped
        /// to 200 m–50 km. Scaling with the orbit stops a moon being cut open
        /// at the closest zoom, while the floor and ceiling keep the depth range
        /// sane at both ends.
        /// </summary>
        public static float NearClipUnits(float distanceUnits)
        {
            return Mathf.Clamp(distanceUnits * NearClipFraction,
                MinNearClipUnits, MaxNearClipUnits);
        }
    }
}
