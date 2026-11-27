using UnityEngine;
using TimeToExplore.Core;
using TimeToExplore.Rendering;

namespace TimeToExplore.Tests.PlayMode
{
    /// <summary>
    /// Observes one frame from inside Unity's own player loop. It runs after
    /// <see cref="SolarSystemBootstrap"/> in both Update and LateUpdate, so it
    /// can tell whether the renderer wrote the body views during Update or
    /// during LateUpdate, and whether the clock held still between the two
    /// phases.
    /// </summary>
    internal sealed class FrameOrderProbe : MonoBehaviour
    {
        public SolarSystemBootstrap Bootstrap;
        [System.NonSerialized] public BodyRuntime Body;

        public int Frames { get; private set; }
        public bool ClockHeldBetweenPhases { get; private set; }
        public double LastAdvanceDays { get; private set; }

        public double ClockAtUpdate { get; private set; }
        public Vector3 ExpectedAtUpdate { get; private set; }
        public Vector3 DrawnAtUpdate { get; private set; }

        public double ClockAtLateUpdate { get; private set; }
        public Vector3 ExpectedAtLateUpdate { get; private set; }
        public Vector3 DrawnAtLateUpdate { get; private set; }

        private bool _sawUpdateThisFrame;
        private double _previousLateClock;

        private void Update()
        {
            Capture(out double clock, out Vector3 expected, out Vector3 drawn);
            ClockAtUpdate = clock;
            ExpectedAtUpdate = expected;
            DrawnAtUpdate = drawn;
            _sawUpdateThisFrame = true;
        }

        private void LateUpdate()
        {
            Capture(out double clock, out Vector3 expected, out Vector3 drawn);

            // A test coroutine resumes in the Update phase, so the clock read
            // here must be the same value this frame's Update captured.
            ClockHeldBetweenPhases = _sawUpdateThisFrame &&
                                     System.Math.Abs(clock - ClockAtUpdate) < 1e-12;

            if (Frames > 0) LastAdvanceDays = clock - _previousLateClock;
            _previousLateClock = clock;

            ClockAtLateUpdate = clock;
            ExpectedAtLateUpdate = expected;
            DrawnAtLateUpdate = drawn;
            _sawUpdateThisFrame = false;
            Frames++;
        }

        private void Capture(out double clock, out Vector3 expected, out Vector3 drawn)
        {
            clock = Bootstrap != null ? Bootstrap.Clock.JulianDate : 0.0;

            if (Bootstrap == null || Body == null)
            {
                expected = Vector3.zero;
                drawn = Vector3.zero;
                return;
            }

            expected = ((Body.PositionM - Bootstrap.ShipPositionM) *
                        Bootstrap.UnitsPerMeter).ToUnity();

            BodyView view = Bootstrap.Renderer != null
                ? Bootstrap.Renderer.GetView(Body)
                : null;
            drawn = view != null ? view.transform.localPosition : Vector3.zero;
        }
    }
}
