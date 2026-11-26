using UnityEngine;

namespace TimeToExplore.Rendering
{
    /// <summary>
    /// Names of the shaders the renderer builds at runtime. All five live in
    /// <c>Assets/Resources/Shaders</c>, so <see cref="Shader.Find"/> resolves
    /// them in a player build as well as in the editor.
    /// </summary>
    public static class RenderingShaders
    {
        /// <summary>Lit body shader: Lambert from the global sun position.</summary>
        public const string BodyLit = "TimeToExplore/BodyLit";

        /// <summary>Additive rim-glow shell drawn just above an atmosphere.</summary>
        public const string Atmosphere = "TimeToExplore/BodyAtmosphere";

        /// <summary>Flat colour, used for the Sun, the ship, and markers.</summary>
        public const string Unlit = "TimeToExplore/Unlit";

        /// <summary>Transparent vertex-coloured line shader with an alpha texture.</summary>
        public const string Line = "TimeToExplore/Line";

        /// <summary>Skybox: the baked Milky Way band plus procedural stars.</summary>
        public const string Skybox = "TimeToExplore/Skybox";

        /// <summary>
        /// The body shader for a catalogue body. The Sun is unlit; planets,
        /// moons, asteroids, and stations share the lit shader.
        /// </summary>
        public static string ForBody(bool isStar) => isStar ? Unlit : BodyLit;
    }
}
