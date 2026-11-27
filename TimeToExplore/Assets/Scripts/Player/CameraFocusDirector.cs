using System;
using UnityEngine;
using TimeToExplore.Core;

namespace TimeToExplore.Player
{
    /// <summary>
    /// Drives scripted debug "jumps" to a body: an eased ship move to a point
    /// beside the target, while the camera rig frames the target. The
    /// destination is recomputed every frame from the target's current
    /// position, so a planet keeps moving under time warp while the ship
    /// arrives. Manual input is disabled for the duration and restored on
    /// arrival or cancellation.
    /// </summary>
    public class CameraFocusDirector : MonoBehaviour
    {
        public float DurationSeconds = FocusTransition.DefaultDurationSeconds;
        public ShipController Ship;
        [System.NonSerialized] public SolarSystemModel Model;
        public ShipCameraRig Rig;

        public FocusTransition Transition { get; } = new FocusTransition();
        public BodyRuntime Target { get; private set; }
        public bool IsTransitioning => Transition.IsActive;

        public event Action<BodyRuntime> Completed;

        public void Initialise(SolarSystemModel model, ShipController ship, ShipCameraRig rig)
        {
            Model = model;
            Ship = ship;
            Rig = rig;
        }

        public void Focus(BodyRuntime target)
        {
            if (target == null || Ship == null) return;

            Transition.DurationSeconds = Mathf.Max(0.01f, DurationSeconds);
            Transition.Begin(Ship.PositionM);
            Target = target;
            Ship.InputEnabled = false;
            Rig?.Frame(target);
        }

        public void Cancel()
        {
            if (!Transition.IsActive) return;

            Transition.Cancel();
            Target = null;
            if (Ship != null) Ship.InputEnabled = true;
        }

        private void Update()
        {
            if (!Transition.IsActive || Target == null || Ship == null) return;

            Vector3d destination = EndPositionFor(Target);
            bool finished = Transition.Advance(Time.deltaTime);

            Ship.MoveTo(Transition.PositionAt(destination));

            if (!finished) return;

            // Snap to the exact pose; interpolation can leave a rounding gap.
            Ship.MoveTo(destination);
            Ship.InputEnabled = true;

            BodyRuntime arrived = Target;
            Target = null;
            Rig?.Frame(arrived);
            Completed?.Invoke(arrived);
        }

        /// <summary>Where the ship stops for a body: eight radii away, and
        /// never closer than 5,000 km, off to one side of the Sun line.</summary>
        public Vector3d EndPositionFor(BodyRuntime target) =>
            target.PositionM + ApproachOffset(target, Model != null ? Model.Sun : target);

        public static Vector3d ApproachOffset(BodyRuntime target, BodyRuntime sun)
        {
            Vector3d toSun = target == sun || sun == null
                ? new Vector3d(1, 0, 0)
                : (sun.PositionM - target.PositionM).Normalized;

            Vector3d side = Vector3d.Cross(Vector3d.Up, toSun);
            if (side.SqrMagnitude < 1e-12)
                side = new Vector3d(0, 1, 0);
            side = side.Normalized;

            Vector3d direction = (toSun * 0.55 + side * 0.65 + Vector3d.Up * 0.35).Normalized;
            double distance = Math.Max(target.RadiusM * 8.0, 5.0e6);
            return direction * distance;
        }
    }
}
