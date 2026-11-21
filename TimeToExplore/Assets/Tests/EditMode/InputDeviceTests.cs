using NUnit.Framework;
using TimeToExplore.Player;
using UnityEngine;
using UnityEngine.InputSystem;

namespace TimeToExplore.Tests.EditMode
{
    /// <summary>
    /// The seam against real (virtual) devices. The fixture keeps device state
    /// isolated, so these tests do not depend on the machine's keyboard or mouse.
    /// </summary>
    [Category("core")]
    public class InputDeviceTests : InputTestFixture
    {
        [Test]
        public void WithNoDevicesEveryIntentIsNeutral()
        {
            Assert.That(Keyboard.current, Is.Null);
            Assert.That(Mouse.current, Is.Null);

            Assert.That(PlayerControls.ReadHotkeys().ScaleIndex, Is.EqualTo(-1));
            Assert.That(PlayerControls.ReadHotkeys().Home, Is.False);
            Assert.That(PlayerControls.ReadHotkeys().Pause, Is.False);
            Assert.That(PlayerControls.PointerPosition, Is.EqualTo(Vector2.zero));
            Assert.That(PlayerControls.LeftClickPressed, Is.False);
            Assert.That(PlayerControls.LeftClickHeld, Is.False);
            Assert.That(PlayerControls.LeftClickReleased, Is.False);
            Assert.That(PlayerControls.DragDelta, Is.EqualTo(Vector2.zero));
            Assert.That(PlayerControls.WheelSteps, Is.EqualTo(0f));
            Assert.That(PlayerControls.MoveAxes, Is.EqualTo(Vector3.zero));
            Assert.That(PlayerControls.SpeedMultiplier, Is.EqualTo(1.0));
        }

        [Test]
        public void KeyboardEdgesFeedTheHotkeyIntents()
        {
            var keyboard = InputSystem.AddDevice<Keyboard>();

            Press(keyboard[Key.Space]);
            Assert.That(PlayerControls.ReadHotkeys().Home, Is.True);
            Release(keyboard[Key.Space]);
            Assert.That(PlayerControls.ReadHotkeys().Home, Is.False);

            Press(keyboard[Key.P]);
            Assert.That(PlayerControls.ReadHotkeys().Pause, Is.True);
            Release(keyboard[Key.P]);
            Assert.That(PlayerControls.ReadHotkeys().Pause, Is.False);

            Press(keyboard[Key.Digit4]);
            Assert.That(PlayerControls.ReadHotkeys().ScaleIndex, Is.EqualTo(3));
            Release(keyboard[Key.Digit4]);

            Press(keyboard[Key.Tab]);
            Assert.That(PlayerControls.ReadHotkeys().CycleTarget, Is.True);
            Release(keyboard[Key.Tab]);

            Press(keyboard[Key.Escape]);
            Assert.That(PlayerControls.CancelPressed, Is.True);
            Release(keyboard[Key.Escape]);
            Assert.That(PlayerControls.CancelPressed, Is.False);
        }

        [Test]
        public void HeldKeysFeedTheMovementAndSpeedIntents()
        {
            var keyboard = InputSystem.AddDevice<Keyboard>();

            Press(keyboard[Key.W]);
            Press(keyboard[Key.D]);
            Assert.That(PlayerControls.MoveAxes, Is.EqualTo(new Vector3(1f, 0f, 1f)));

            Press(keyboard[Key.LeftShift]);
            Assert.That(PlayerControls.SpeedMultiplier, Is.EqualTo(10.0));

            Press(keyboard[Key.Period]);
            Assert.That(PlayerControls.SpeedSteps, Is.EqualTo(1f));

            Release(keyboard[Key.Period]);
            Release(keyboard[Key.LeftShift]);
            Press(keyboard[Key.RightCtrl]);
            Assert.That(PlayerControls.SpeedMultiplier, Is.EqualTo(0.1));

            Release(keyboard[Key.RightCtrl]);
            Release(keyboard[Key.D]);
            Release(keyboard[Key.W]);
            Assert.That(PlayerControls.MoveAxes, Is.EqualTo(Vector3.zero));
        }

        [Test]
        public void MouseButtonsFeedTheClickAndDragIntents()
        {
            var mouse = InputSystem.AddDevice<Mouse>();

            Press(mouse.leftButton);
            Assert.That(PlayerControls.LeftClickPressed, Is.True);
            Assert.That(PlayerControls.LeftClickHeld, Is.True);
            Assert.That(PlayerControls.LeftClickReleased, Is.False);
            Release(mouse.leftButton);
            Assert.That(PlayerControls.LeftClickPressed, Is.False);
            Assert.That(PlayerControls.LeftClickHeld, Is.False);
            Assert.That(PlayerControls.LeftClickReleased, Is.True);

            Press(mouse.rightButton);
            Assert.That(PlayerControls.RightDragStarted, Is.True);
            Release(mouse.rightButton);
            Assert.That(PlayerControls.RightDragEnded, Is.True);
        }

        [Test]
        public void MouseDeltasAndScrollPassThroughTheLegacyRules()
        {
            var mouse = InputSystem.AddDevice<Mouse>();

            Move(mouse.position, new Vector2(100f, 100f), new Vector2(10f, -20f));
            Assert.That(PlayerControls.DragDelta, Is.EqualTo(new Vector2(1f, -2f)),
                "raw pixels times the old 0.1 axis sensitivity");

            Set(mouse.scroll, new Vector2(0f, 1f));
            Assert.That(PlayerControls.WheelSteps,
                Is.EqualTo(PlayerControls.NormaliseScroll(1f, Application.platform)).Within(1e-5f),
                "one notch on this platform is one zoom step");
        }
    }
}
