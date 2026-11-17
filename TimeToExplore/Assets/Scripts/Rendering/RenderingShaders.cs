using UnityEngine;

namespace TimeToExplore.Rendering
{
    /// <summary>
    /// Names of the shaders the renderer builds at runtime. All three live in
    /// <c>Assets/Resources/Shaders</c>, so <see cref="Shader.Find"/> resolves
    /// them in a player build as well as in the editor.
    /// </summary>
    public static class RenderingShaders
    {
        /// <summary>Lit body shader: Lambert from the global sun position.</summary>
        public const string BodyLit = "TimeToExplore/BodyLit";

        /// <summary>Flat colour, used for the Sun, the ship, and markers.</summary>
        public const string Unlit = "TimeToExplore/Unlit";

        /// <summary>Transparent vertex-coloured line shader with an alpha texture.</summary>
        public const string Line = "TimeToExplore/Line";

        /// <summary>
        /// The body shader for a catalogue body. The Sun is unlit; planets,
        /// moons, asteroids, and stations share the lit shader.
        /// </summary>
        public static string ForBody(bool isStar) => isStar ? Unlit : BodyLit;
    }
}
