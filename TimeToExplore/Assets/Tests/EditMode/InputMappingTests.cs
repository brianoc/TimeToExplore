using System;
using NUnit.Framework;
using TimeToExplore.Player;
using UnityEngine;
using UnityEngine.InputSystem;

namespace TimeToExplore.Tests.EditMode
{
    /// <summary>
    /// The input seam's mapping. Every case feeds a key predicate or a raw device
    /// value, so the mapping is tested without a keyboard, a mouse, or a scene.
    /// </summary>
    [Category("core")]
    public class InputMappingTests
    {
        private static Func<Key, bool> Held(params Key[] keys) =>
            key => Array.IndexOf(keys, key) >= 0;

        [Test]
        public void TheDigitTableMapsOneToNineOntoTheClockIndices()
        {
            Assert.That(PlayerControls.ScaleIndexForDigit(Key.Digit1), Is.EqualTo(0));
            Assert.That(PlayerControls.ScaleIndexForDigit(Key.Digit2), Is.EqualTo(1));
            Assert.That(PlayerControls.ScaleIndexForDigit(Key.Digit3), Is.EqualTo(2));
            Assert.That(PlayerControls.ScaleIndexForDigit(Key.Digit4), Is.EqualTo(3));
            Assert.That(PlayerControls.ScaleIndexForDigit(Key.Digit5), Is.EqualTo(4));
            Assert.That(PlayerControls.ScaleIndexForDigit(Key.Digit6), Is.EqualTo(5));
            Assert.That(PlayerControls.ScaleIndexForDigit(Key.Digit7), Is.EqualTo(6));
            Assert.That(PlayerControls.ScaleIndexForDigit(Key.Digit8), Is.EqualTo(7));
            Assert.That(PlayerControls.ScaleIndexForDigit(Key.Digit9), Is.EqualTo(8));
            Assert.That(PlayerControls.ScaleIndexForDigit(Key.Digit0), Is.EqualTo(9));

            Assert.That(PlayerControls.ScaleIndexForDigit(Key.Space), Is.EqualTo(-1));
            Assert.That(PlayerControls.ScaleIndexForDigit(Key.Numpad1), Is.EqualTo(-1));
            Assert.That(PlayerControls.ScaleIndexForDigit(Key.None), Is.EqualTo(-1));
        }

        [Test]
        public void EachDigitPressSelectsItsScaleIndex()
        {
            Key[] digits =
            {
                Key.Digit1, Key.Digit2, Key.Digit3, Key.Digit4, Key.Digit5,
                Key.Digit6, Key.Digit7, Key.Digit8, Key.Digit9
            };

            for (int index = 0; index < digits.Length; index++)
            {
                PlayerControls.Hotkeys keys = PlayerControls.ReadHotkeys(Held(digits[index]));
                Assert.That(keys.ScaleIndex, Is.EqualTo(index), $"digit {index + 1}");
            }

            PlayerControls.Hotkeys zero = PlayerControls.ReadHotkeys(Held(Key.Digit0));
            Assert.That(zero.ScaleIndex, Is.EqualTo(9));
        }

        [Test]
        public void TwoDigitsInOneFrameTakeTheHighestIndex()
        {
            // The old loop walked the scales in ascending order, so the last key
            // it matched won. Keep that, even though real hardware never sends
            // two key-downs in one frame.
            PlayerControls.Hotkeys keys =
                PlayerControls.ReadHotkeys(Held(Key.Digit1, Key.Digit5));

            Assert.That(keys.ScaleIndex, Is.EqualTo(4));
        }

        [Test]
        public void EveryHotkeyEdgeMapsToItsOwnIntent()
        {
            PlayerControls.Hotkeys home = PlayerControls.ReadHotkeys(Held(Key.Space));
            Assert.That(home.Home, Is.True);
            Assert.That(home.Pause, Is.False, "Space is the camera home, not pause");

            PlayerControls.Hotkeys pause = PlayerControls.ReadHotkeys(Held(Key.P));
            Assert.That(pause.Pause, Is.True);
            Assert.That(pause.Home, Is.False, "P is pause, not the camera home");

            PlayerControls.Hotkeys tab = PlayerControls.ReadHotkeys(Held(Key.Tab));
            Assert.That(tab.CycleTarget, Is.True);

            PlayerControls.Hotkeys focus = PlayerControls.ReadHotkeys(Held(Key.F));
            Assert.That(focus.FocusSelected, Is.True);

            PlayerControls.Hotkeys escape = PlayerControls.ReadHotkeys(Held(Key.Escape));
            Assert.That(escape.CancelFocus, Is.True);

            PlayerControls.Hotkeys reset = PlayerControls.ReadHotkeys(Held(Key.R));
            Assert.That(reset.Reset, Is.True);

            PlayerControls.Hotkeys cancel = PlayerControls.ReadHotkeys(Held(Key.X));
            Assert.That(cancel.CancelFlight, Is.True);

            PlayerControls.Hotkeys freeFlight = PlayerControls.ReadHotkeys(Held(Key.Backquote));
            Assert.That(freeFlight.ToggleFreeFlight, Is.True);

            PlayerControls.Hotkeys trueScale = PlayerControls.ReadHotkeys(Held(Key.T));
            Assert.That(trueScale.ToggleTrueScale, Is.True);

            PlayerControls.Hotkeys orbits = PlayerControls.ReadHotkeys(Held(Key.O));
            Assert.That(orbits.ToggleOrbits, Is.True);

            PlayerControls.Hotkeys labels = PlayerControls.ReadHotkeys(Held(Key.L));
            Assert.That(labels.ToggleLabels, Is.True);

            PlayerControls.Hotkeys hud = PlayerControls.ReadHotkeys(Held(Key.H));
            Assert.That(hud.ToggleHud, Is.True);
        }

        [Test]
        public void AnUnpressedFrameHasNoIntent()
        {
            PlayerControls.Hotkeys keys = PlayerControls.ReadHotkeys(Held());

            Assert.That(keys.ScaleIndex, Is.EqualTo(-1));
            Assert.That(keys.Home, Is.False);
            Assert.That(keys.Pause, Is.False);
            Assert.That(keys.CycleTarget, Is.False);
            Assert.That(keys.FocusSelected, Is.False);
            Assert.That(keys.CancelFocus, Is.False);
            Assert.That(keys.Reset, Is.False);
            Assert.That(keys.CancelFlight, Is.False);
            Assert.That(keys.ToggleFreeFlight, Is.False);
            Assert.That(keys.ToggleTrueScale, Is.False);
            Assert.That(keys.ToggleOrbits, Is.False);
            Assert.That(keys.ToggleLabels, Is.False);
            Assert.That(keys.ToggleHud, Is.False);
        }

        [Test]
        public void WasdqeMapToTheLocalMoveAxes()
        {
            Assert.That(PlayerControls.ReadMoveAxes(Held(Key.W)), Is.EqualTo(Vector3.forward));
            Assert.That(PlayerControls.ReadMoveAxes(Held(Key.S)), Is.EqualTo(Vector3.back));
            Assert.That(PlayerControls.ReadMoveAxes(Held(Key.D)), Is.EqualTo(Vector3.right));
            Assert.That(PlayerControls.ReadMoveAxes(Held(Key.A)), Is.EqualTo(Vector3.left));
            Assert.That(PlayerControls.ReadMoveAxes(Held(Key.E)), Is.EqualTo(Vector3.up));
            Assert.That(PlayerControls.ReadMoveAxes(Held(Key.Q)), Is.EqualTo(Vector3.down));

            Assert.That(PlayerControls.ReadMoveAxes(Held(Key.W, Key.D, Key.E)),
                Is.EqualTo(new Vector3(1f, 1f, 1f)));
            Assert.That(PlayerControls.ReadMoveAxes(Held(Key.W, Key.S)), Is.EqualTo(Vector3.zero));
            Assert.That(PlayerControls.ReadMoveAxes(Held()),
                Is.EqualTo(Vector3.zero));
        }

        [Test]
        public void ShiftAndControlSetTheSpeedMultiplier()
        {
            Assert.That(PlayerControls.ReadSpeedMultiplier(Held()), Is.EqualTo(1.0));
            Assert.That(PlayerControls.ReadSpeedMultiplier(Held(Key.LeftShift)), Is.EqualTo(10.0));
            Assert.That(PlayerControls.ReadSpeedMultiplier(Held(Key.RightShift)), Is.EqualTo(10.0));
            Assert.That(PlayerControls.ReadSpeedMultiplier(Held(Key.LeftCtrl)), Is.EqualTo(0.1));
            Assert.That(PlayerControls.ReadSpeedMultiplier(Held(Key.RightCtrl)), Is.EqualTo(0.1));
            Assert.That(PlayerControls.ReadSpeedMultiplier(Held(Key.LeftShift, Key.LeftCtrl)),
                Is.EqualTo(0.1), "Control wins over Shift, as the old ifs did");
        }

        [Test]
        public void PeriodAndCommaStepTheFreeFlightSpeed()
        {
            Assert.That(PlayerControls.ReadSpeedSteps(Held(Key.Period)), Is.EqualTo(1f));
            Assert.That(PlayerControls.ReadSpeedSteps(Held(Key.Comma)), Is.EqualTo(-1f));
            Assert.That(PlayerControls.ReadSpeedSteps(Held(Key.Period, Key.Comma)), Is.EqualTo(0f));
            Assert.That(PlayerControls.ReadSpeedSteps(Held()), Is.EqualTo(0f));
        }

        [Test]
        public void TheMouseDeltaKeepsTheLegacySensitivity()
        {
            // The old Mouse X/Y axes applied a 0.1 sensitivity to pixels.
            Assert.That(PlayerControls.ReadMouseAxis(new Vector2(10f, -20f)),
                Is.EqualTo(new Vector2(1f, -2f)));
            Assert.That(PlayerControls.ReadMouseAxis(Vector2.zero), Is.EqualTo(Vector2.zero));
            Assert.That(PlayerControls.LegacyMouseAxisSensitivity, Is.EqualTo(0.1f));
        }

        [Test]
        public void OneWheelNotchIsOneStepOnEveryPlatform()
        {
            // macOS and Linux report about one unit per notch...
            Assert.That(PlayerControls.NormaliseScroll(1f, RuntimePlatform.OSXPlayer),
                Is.EqualTo(1f).Within(1e-6f));
            Assert.That(PlayerControls.NormaliseScroll(1f, RuntimePlatform.OSXEditor),
                Is.EqualTo(1f).Within(1e-6f));
            Assert.That(PlayerControls.NormaliseScroll(0.5f, RuntimePlatform.LinuxPlayer),
                Is.EqualTo(0.5f).Within(1e-6f));

            // ...while Windows reports 120 units per notch.
            Assert.That(PlayerControls.NormaliseScroll(120f, RuntimePlatform.WindowsPlayer),
                Is.EqualTo(1f).Within(1e-6f));
            Assert.That(PlayerControls.NormaliseScroll(-240f, RuntimePlatform.WindowsEditor),
                Is.EqualTo(-2f).Within(1e-6f));
            Assert.That(PlayerControls.UsesWindowsScrollUnits(RuntimePlatform.WindowsPlayer), Is.True);
            Assert.That(PlayerControls.UsesWindowsScrollUnits(RuntimePlatform.OSXPlayer), Is.False);
        }

        [Test]
        public void NullDevicesReturnNeutralValues()
        {
            // Batch-mode tests and a machine with no hardware must not throw.
            Assert.That(PlayerControls.IsHeld(null, Key.Space), Is.False);
            Assert.That(PlayerControls.IsPressed(null, Key.Space), Is.False);
            Assert.That(PlayerControls.ReadPointer(null), Is.EqualTo(Vector2.zero));
            Assert.That(PlayerControls.ReadLeftClick(null), Is.False);
            Assert.That(PlayerControls.ReadLeftClickHeld(null), Is.False);
            Assert.That(PlayerControls.ReadLeftClickReleased(null), Is.False);
            Assert.That(PlayerControls.ReadRightDragStarted(null), Is.False);
            Assert.That(PlayerControls.ReadRightDragEnded(null), Is.False);
            Assert.That(PlayerControls.ReadDragDelta(null), Is.EqualTo(Vector2.zero));
            Assert.That(PlayerControls.ReadWheelSteps(null), Is.EqualTo(0f));
            Assert.That(PlayerControls.ReadMoveAxes(null), Is.EqualTo(Vector3.zero));
            Assert.That(PlayerControls.ReadSpeedMultiplier(null), Is.EqualTo(1.0));
            Assert.That(PlayerControls.ReadSpeedSteps(null), Is.EqualTo(0f));

            PlayerControls.Hotkeys keys = PlayerControls.ReadHotkeys((Func<Key, bool>)null);
            Assert.That(keys.ScaleIndex, Is.EqualTo(-1));
            Assert.That(keys.Home, Is.False);
            Assert.That(keys.Pause, Is.False);
            Assert.That(keys.CancelFocus, Is.False);
        }

        [Test]
        public void ThePointerConvertsFromScreenToGuiSpace()
        {
            Assert.That(PlayerControls.ToGuiPointer(new Vector2(100f, 200f), 1080),
                Is.EqualTo(new Vector2(100f, 880f)));
            Assert.That(PlayerControls.ToGuiPointer(Vector2.zero, 1080),
                Is.EqualTo(new Vector2(0f, 1080f)));
            Assert.That(PlayerControls.ToGuiPointer(new Vector2(50f, 1080f), 1080),
                Is.EqualTo(new Vector2(50f, 0f)));
        }

        [Test]
        public void ClickSlopSeparatesAClickFromAMapPan()
        {
            var press = new Vector2(100f, 100f);

            Assert.That(PlayerControls.ClickSlopPixels, Is.EqualTo(4f));
            Assert.That(PlayerControls.ExceedsClickSlop(press, press), Is.False);
            Assert.That(PlayerControls.ExceedsClickSlop(press, press + new Vector2(3f, 0f)),
                Is.False);
            Assert.That(PlayerControls.ExceedsClickSlop(press,
                press + new Vector2(PlayerControls.ClickSlopPixels, 0f)), Is.False,
                "the boundary itself is still a click");
            Assert.That(PlayerControls.ExceedsClickSlop(press,
                press + new Vector2(PlayerControls.ClickSlopPixels + 0.5f, 0f)), Is.True);
            Assert.That(PlayerControls.ExceedsClickSlop(press, press + new Vector2(3f, 3f)),
                Is.True, "both axes count, so a diagonal drag leaves the slop");
        }
    }
}
