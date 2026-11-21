using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;

namespace TimeToExplore.UI
{
    /// <summary>
    /// The uGUI input bridge. The HUD is built from code, so the one
    /// <see cref="EventSystem"/> is created on demand with an
    /// <see cref="InputSystemUIInputModule"/> carrying the Input System's default
    /// UI actions.
    ///
    /// This is the second and last runtime file allowed to name the Input
    /// System (the other is <c>PlayerControls</c>, the gameplay seam).
    /// Gameplay and world picking still read devices through that seam; this
    /// class only gives uGUI a pointer, click, scroll, and navigation source, so
    /// no device is read twice. See <c>Docs/technical/input.md</c>.
    /// </summary>
    public static class UiInput
    {
        /// <summary>The name given to the EventSystem object this class creates.</summary>
        public const string EventSystemName = "EventSystem";

        /// <summary>
        /// Return the scene's EventSystem, creating one with an
        /// <see cref="InputSystemUIInputModule"/> only when none exists. An
        /// authored EventSystem is left alone so it is never double-equipped.
        /// </summary>
        /// <param name="created">
        /// True when this call created the EventSystem, so the caller can destroy
        /// it with the rest of the HUD.
        /// </param>
        public static EventSystem EnsureEventSystem(out bool created)
        {
            created = false;

            EventSystem existing = Object.FindFirstObjectByType<EventSystem>(
                FindObjectsInactive.Include);
            if (existing != null)
            {
                existing.sendNavigationEvents = false;
                return existing;
            }

            var host = new GameObject(EventSystemName);
            var system = host.AddComponent<EventSystem>();

            // The old IMGUI HUD was pointer-only. Leave navigation off so a
            // selected button cannot be re-submitted by Space (camera home), P
            // (pause), Enter, or the keyboard arrows, which are gameplay keys.
            system.sendNavigationEvents = false;

            var module = host.AddComponent<InputSystemUIInputModule>();

            // AddComponent already assigns the defaults through OnEnable; this is
            // explicit so the action set does not depend on component order.
            module.AssignDefaultActions();

            created = true;
            return system;
        }

        /// <summary>
        /// True when the pointer is over a uGUI element that accepts raycasts,
        /// which is how panel clicks are swallowed rather than reaching the
        /// world. False when there is no EventSystem, so a headless batch run and
        /// an empty scene keep the old picking behaviour.
        /// </summary>
        public static bool PointerOverUi()
        {
            EventSystem system = EventSystem.current;
            return system != null && system.IsPointerOverGameObject();
        }
    }
}
