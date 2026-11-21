using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace TimeToExplore.Player
{
    /// <summary>
    /// The project's single gameplay input seam. It polls the Input System's
    /// keyboard and mouse devices directly and exposes intents (pause, scale
    /// index, pointer, click and drag edges, wheel steps, movement axes) so no
    /// other gameplay script mentions <see cref="UnityEngine.InputSystem"/> or the
    /// legacy <c>UnityEngine.Input</c> API.
    ///
    /// Active Input Handling is **New** only. uGUI receives its own pointer and
    /// click events from <c>UiInput</c>'s <c>InputSystemUIInputModule</c>; the
    /// two paths read the same devices but never duplicate a mapping. No project
    /// code reads the legacy Input Manager, and <c>InputManager.asset</c> is
    /// inert.
    ///
    /// Every device read is null-guarded, and the pure helpers take the device (or
    /// a key predicate) explicitly, so the mapping is testable without hardware.
    /// </summary>
    public static class PlayerControls
    {
        /// <summary>
        /// The old Input Manager's default mouse-axis sensitivity. The new device
        /// delta is in raw pixels, so multiplying by this keeps the camera's
        /// degrees-per-unit and pixels-per-unit values unchanged.
        /// </summary>
        public const float LegacyMouseAxisSensitivity = 0.1f;

        /// <summary>
        /// Windows reports one wheel notch as 120 units; macOS reports about 1.
        /// Dividing only on Windows keeps one notch as one zoom step everywhere.
        /// </summary>
        public const float WindowsScrollUnitsPerNotch = 120f;

        /// <summary>
        /// Pointer movement, in pixels, that separates a click from a drag. A
        /// left press that stays inside this radius selects on release; one that
        /// leaves it selects nothing.
        /// </summary>
        public const float ClickSlopPixels = 4f;

        /// <summary>
        /// The explicit digit table. The clock has fewer scales than there are
        /// number keys today; the caller clamps the index, so adding a scale is a
        /// one-line change and never silently changes the key mapping.
        /// </summary>
        private static readonly Key[] ScaleKeys =
        {
            Key.Digit1, Key.Digit2, Key.Digit3, Key.Digit4, Key.Digit5,
            Key.Digit6, Key.Digit7, Key.Digit8, Key.Digit9, Key.Digit0
        };

        /// <summary>One-frame edge intents for the bootstrap and the HUD.</summary>
        public struct Hotkeys
        {
            /// <summary>Space: ease the camera back to the ship.</summary>
            public bool Home;
            public bool Pause;
            public int ScaleIndex; // -1 when no number key was pressed
            public bool CycleTarget;
            public bool FocusSelected;
            public bool CancelFocus;
            public bool Reset;
            public bool CancelFlight;
            public bool ToggleFreeFlight;
            public bool ToggleTrueScale;
            public bool ToggleOrbits;
            public bool ToggleLabels;
            public bool ToggleHud;
        }

        // Cached so the per-frame reads do not allocate a delegate each time.
        private static readonly Func<Key, bool> HeldPredicate = Held;
        private static readonly Func<Key, bool> PressedPredicate = Pressed;

        // --- Device-backed intent for the frame ---------------------------------

        public static Vector2 PointerPosition => ReadPointer(Mouse.current);

        /// <summary>Left button down this frame: starts a click.</summary>
        public static bool LeftClickPressed => ReadLeftClick(Mouse.current);

        /// <summary>The left button is held: a click candidate may turn into a drag.</summary>
        public static bool LeftClickHeld => ReadLeftClickHeld(Mouse.current);

        /// <summary>Left button up this frame: a click selects, a drag does not.</summary>
        public static bool LeftClickReleased => ReadLeftClickReleased(Mouse.current);

        public static bool RightDragStarted => ReadRightDragStarted(Mouse.current);
        public static bool RightDragEnded => ReadRightDragEnded(Mouse.current);
        public static Vector2 DragDelta => ReadDragDelta(Mouse.current);
        public static float WheelSteps => ReadWheelSteps(Mouse.current);
        public static Vector3 MoveAxes => ReadMoveAxes(HeldPredicate);
        public static double SpeedMultiplier => ReadSpeedMultiplier(HeldPredicate);
        public static float SpeedSteps => ReadSpeedSteps(HeldPredicate);
        public static bool CancelPressed => IsPressed(Keyboard.current, Key.Escape);

        /// <summary>The one-frame hotkey set, or all-false when no keyboard exists.</summary>
        public static Hotkeys ReadHotkeys() => ReadHotkeys(PressedPredicate);

        public static bool Held(Key key) => IsHeld(Keyboard.current, key);

        public static bool Pressed(Key key) => IsPressed(Keyboard.current, key);

        // --- Pure mapping, with the device (or key predicate) supplied ----------

        public static bool IsHeld(Keyboard keyboard, Key key)
        {
            if (keyboard == null || key == Key.None) return false;
            var control = keyboard[key];
            return control != null && control.isPressed;
        }

        public static bool IsPressed(Keyboard keyboard, Key key)
        {
            if (keyboard == null || key == Key.None) return false;
            var control = keyboard[key];
            return control != null && control.wasPressedThisFrame;
        }

        public static Vector2 ReadPointer(Mouse mouse) =>
            mouse != null ? mouse.position.ReadValue() : Vector2.zero;

        public static bool ReadLeftClick(Mouse mouse) =>
            mouse != null && mouse.leftButton.wasPressedThisFrame;

        public static bool ReadLeftClickHeld(Mouse mouse) =>
            mouse != null && mouse.leftButton.isPressed;

        public static bool ReadLeftClickReleased(Mouse mouse) =>
            mouse != null && mouse.leftButton.wasReleasedThisFrame;

        /// <summary>
        /// True when the pointer has moved further than the click slop, in
        /// pixels. The HUD uses it to tell a click on a body from a drag that
        /// happened to start on the body.
        /// </summary>
        public static bool ExceedsClickSlop(Vector2 pressPoint, Vector2 currentPoint,
            float slopPixels = ClickSlopPixels) =>
            (currentPoint - pressPoint).sqrMagnitude > slopPixels * slopPixels;

        public static bool ReadRightDragStarted(Mouse mouse) =>
            mouse != null && mouse.rightButton.wasPressedThisFrame;

        public static bool ReadRightDragEnded(Mouse mouse) =>
            mouse != null && mouse.rightButton.wasReleasedThisFrame;

        /// <summary>
        /// The legacy mouse axis for this frame: raw pixels times the old 0.1
        /// sensitivity. The rig's degrees and pixels per unit still mean what they
        /// meant before the migration.
        /// </summary>
        public static Vector2 ReadDragDelta(Mouse mouse) =>
            mouse != null ? ReadMouseAxis(mouse.delta.ReadValue()) : Vector2.zero;

        public static Vector2 ReadMouseAxis(Vector2 rawDelta) =>
            rawDelta * LegacyMouseAxisSensitivity;

        /// <summary>One wheel notch is one zoom step on every platform.</summary>
        public static float ReadWheelSteps(Mouse mouse) =>
            mouse != null ? NormaliseScroll(mouse.scroll.ReadValue().y, Application.platform) : 0f;

        public static float NormaliseScroll(float rawScroll, RuntimePlatform platform) =>
            UsesWindowsScrollUnits(platform)
                ? rawScroll / WindowsScrollUnitsPerNotch
                : rawScroll;

        public static bool UsesWindowsScrollUnits(RuntimePlatform platform) =>
            platform == RuntimePlatform.WindowsPlayer ||
            platform == RuntimePlatform.WindowsEditor;

        public static Vector3 ReadMoveAxes(Func<Key, bool> held)
        {
            if (held == null) return Vector3.zero;

            float forward = (held(Key.W) ? 1f : 0f) - (held(Key.S) ? 1f : 0f);
            float right = (held(Key.D) ? 1f : 0f) - (held(Key.A) ? 1f : 0f);
            float up = (held(Key.E) ? 1f : 0f) - (held(Key.Q) ? 1f : 0f);
            return new Vector3(right, up, forward);
        }

        /// <summary>Shift multiplies speed by ten; Control by a tenth and wins.</summary>
        public static double ReadSpeedMultiplier(Func<Key, bool> held)
        {
            if (held == null) return 1.0;

            bool shift = held(Key.LeftShift) || held(Key.RightShift);
            bool control = held(Key.LeftCtrl) || held(Key.RightCtrl);

            if (control) return 0.1;
            if (shift) return 10.0;
            return 1.0;
        }

        /// <summary>
        /// Period increases and comma decreases the free-flight speed. The wheel
        /// stays with the camera, so the two controls never fight.
        /// </summary>
        public static float ReadSpeedSteps(Func<Key, bool> held)
        {
            if (held == null) return 0f;
            return (held(Key.Period) ? 1f : 0f) - (held(Key.Comma) ? 1f : 0f);
        }

        /// <summary>
        /// The hotkey edges. When two number keys are down in the same frame the
        /// highest wins, which is what the old ascending scale loop did.
        /// </summary>
        public static Hotkeys ReadHotkeys(Func<Key, bool> pressed)
        {
            var hotkeys = new Hotkeys { ScaleIndex = -1 };
            if (pressed == null) return hotkeys;

            hotkeys.Home = pressed(Key.Space);
            hotkeys.Pause = pressed(Key.P);
            for (int i = 0; i < ScaleKeys.Length; i++)
            {
                if (!pressed(ScaleKeys[i])) continue;
                hotkeys.ScaleIndex = ScaleIndexForDigit(ScaleKeys[i]);
            }

            hotkeys.CycleTarget = pressed(Key.Tab);
            hotkeys.FocusSelected = pressed(Key.F);
            hotkeys.CancelFocus = pressed(Key.Escape);
            hotkeys.Reset = pressed(Key.R);
            hotkeys.CancelFlight = pressed(Key.X);
            hotkeys.ToggleFreeFlight = pressed(Key.Backquote);
            hotkeys.ToggleTrueScale = pressed(Key.T);
            hotkeys.ToggleOrbits = pressed(Key.O);
            hotkeys.ToggleLabels = pressed(Key.L);
            hotkeys.ToggleHud = pressed(Key.H);
            return hotkeys;
        }

        /// <summary>
        /// The scale index for a number key, or -1. Kept as an explicit switch
        /// rather than <c>Key.Digit1 + index</c>; a test pins the whole table so a
        /// layout reorder cannot quietly move a hotkey.
        /// </summary>
        public static int ScaleIndexForDigit(Key key)
        {
            switch (key)
            {
                case Key.Digit1: return 0;
                case Key.Digit2: return 1;
                case Key.Digit3: return 2;
                case Key.Digit4: return 3;
                case Key.Digit5: return 4;
                case Key.Digit6: return 5;
                case Key.Digit7: return 6;
                case Key.Digit8: return 7;
                case Key.Digit9: return 8;
                case Key.Digit0: return 9;
                default: return -1;
            }
        }

        /// <summary>
        /// Screen coordinates (origin bottom-left, as the Input System reports
        /// them) to IMGUI space (origin top-left).
        /// </summary>
        public static Vector2 ToGuiPointer(Vector2 screenPoint, int screenHeight) =>
            new Vector2(screenPoint.x, screenHeight - screenPoint.y);
    }
}
