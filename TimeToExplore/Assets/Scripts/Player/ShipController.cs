using System;
using UnityEngine;
using TimeToExplore.Core;

namespace TimeToExplore.Player
{
    /// <summary>
    /// Debug ship movement. The controller no longer owns the ship's position:
    /// it comes from the flight session through <see cref="PositionSource"/>,
    /// and movement is reported through <see cref="Teleported"/>. That keeps one
    /// writer for the ship state ([ADR-007](/decisions/adr-007-flight-director.md)).
    ///
    /// The view belongs to <see cref="ShipCameraRig"/>, so this class only moves
    /// the ship in debug free flight. The camera follows it, because the camera
    /// orbits the ship.
    /// </summary>
    public class ShipController : MonoBehaviour
    {
        public double FlySpeedMps = 1_000_000.0; // 1,000 km/s, a convenient inspection speed

        /// <summary>False while a scripted move or a flight owns the ship.</summary>
        public bool InputEnabled = true;

        /// <summary>Debug movement only. A flight owns the position and turns this off.</summary>
        public bool MovementEnabled = true;

        /// <summary>Reads the authoritative ship position.</summary>
        public Func<Vector3d> PositionSource;

        /// <summary>Reports a debug teleport or free-flight step.</summary>
        public Action<Vector3d> Teleported;

        /// <summary>The authoritative ship position.</summary>
        public Vector3d PositionM =>
            PositionSource != null ? PositionSource() : Vector3d.Zero;

        private void Update()
        {
            if (!InputEnabled || !MovementEnabled) return;
            HandleMovement();
        }

        private void HandleMovement()
        {
            Vector3 move = PlayerControls.MoveAxes;
            if (move != Vector3.zero)
            {
                Vector3 local = move.normalized;
                Vector3 direction = Camera.main != null
                    ? Camera.main.transform.TransformDirection(local)
                    : local;

                double multiplier = PlayerControls.SpeedMultiplier;

                MoveTo(PositionM + Vector3d.FromUnity(direction) * (FlySpeedMps * multiplier * Time.deltaTime));
            }

            // The wheel belongs to the camera orbit. Free-flight speed uses
            // comma and period so the two controls never fight.
            float speedSteps = PlayerControls.SpeedSteps;
            if (speedSteps != 0f)
            {
                FlySpeedMps = System.Math.Clamp(
                    FlySpeedMps * System.Math.Pow(1.45, speedSteps * Time.deltaTime * 6f),
                    10.0,
                    2.0e9);
            }
        }

        /// <summary>Report a new position to the owner. The owner decides
        /// whether it is a flight state or a debug teleport.</summary>
        public void MoveTo(Vector3d positionM)
        {
            Teleported?.Invoke(positionM);
        }

        /// <summary>Debug teleport. Kept as a separate name for call sites that
        /// are explicitly not a trajectory.</summary>
        public void Teleport(Vector3d positionM) => MoveTo(positionM);
    }
}
