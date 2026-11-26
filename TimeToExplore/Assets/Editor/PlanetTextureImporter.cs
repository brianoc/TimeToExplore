using UnityEditor;
using UnityEngine;

namespace TimeToExplore.EditorTools
{
    /// <summary>
    /// Pins the import settings for the planet albedo maps under
    /// <c>Assets/Resources/Planets</c>, so a copied or re-fetched JPEG imports
    /// the same way every time instead of inheriting whatever the previous
    /// import happened to use.
    /// </summary>
    /// <remarks>
    /// An albedo map is colour, not data: it carries the light a surface
    /// reflects, so it must be sampled as sRGB and decoded to linear before the
    /// shader multiplies it. Under [ADR-021](../../Docs/decisions/adr-021-linear-colour-space.md)
    /// every authored colour stays sRGB and converts once, where it enters a
    /// shader; for a texture that boundary is the sRGB import flag. A mask,
    /// normal map or the bright-star map is data and must stay linear, but these
    /// maps are not.
    ///
    /// Repeat wrapping lets the equirectangular map close seamlessly at the
    /// longitude seam, mipmaps keep the far body from shimmering when it is only
    /// a few pixels across, and 2048 is the source resolution so the importer
    /// neither upscales nor silently halves it. CompressedHQ keeps the memory
    /// modest while leaving the terminator and the cloud bands clean.
    /// </remarks>
    public sealed class PlanetTextureImporter : AssetPostprocessor
    {
        private const string PlanetTextureFolder = "/Resources/Planets/";

        private void OnPreprocessTexture()
        {
            if (assetPath.IndexOf(PlanetTextureFolder, System.StringComparison.Ordinal) < 0)
                return;

            var importer = (TextureImporter)assetImporter;
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = true;
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.mipmapEnabled = true;
            importer.maxTextureSize = 2048;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
        }
    }
}
