using System.IO;
using NUnit.Framework;
using TimeToExplore.Rendering;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace TimeToExplore.Tests.EditMode
{
    /// <summary>
    /// The sky is an asset plus a shader plus one camera switch, so the suite
    /// checks all three: the generated texture is present, dark, and has a band;
    /// the shader compiles; and the view puts the camera on it and restores the
    /// previous state when it goes away.
    /// </summary>
    public class SkyboxTests
    {
        private GameObject _cameraObject;
        private GameObject _skyObject;
        private Camera _camera;
        private Material _originalSkybox;

        [SetUp]
        public void SetUp()
        {
            _originalSkybox = RenderSettings.skybox;
            RenderSettings.skybox = null;

            _cameraObject = new GameObject("Sky Test Camera");
            _camera = _cameraObject.AddComponent<Camera>();
            _camera.clearFlags = CameraClearFlags.SolidColor;
        }

        [TearDown]
        public void TearDown()
        {
            if (_skyObject != null)
            {
                SkyView sky = _skyObject.GetComponent<SkyView>();
                if (sky != null) sky.Remove();
                Object.DestroyImmediate(_skyObject);
            }

            if (_cameraObject != null) Object.DestroyImmediate(_cameraObject);
            RenderSettings.skybox = _originalSkybox;
        }

        [Test]
        public void TheSkyTextureIsCommittedAtTheExpectedSize()
        {
            Texture2D texture = Resources.Load<Texture2D>(SkyView.TextureResource);

            Assert.That(texture, Is.Not.Null,
                "missing Resources/Sky/milky_way.png; run tools/generate_skybox.py");
            Assert.That(texture.width, Is.EqualTo(2048));
            Assert.That(texture.height, Is.EqualTo(1024));
            Assert.That(texture.mipmapCount, Is.EqualTo(1),
                "the sky is sampled at LOD 0, so the importer must not build mips");
        }

        [Test]
        public void TheSkyTextureIsDarkWithABrightBand()
        {
            Texture2D asset = Resources.Load<Texture2D>(SkyView.TextureResource);
            Assert.That(asset, Is.Not.Null);

            // The importer leaves the texture non-readable, which is what a
            // skybox wants. Decode the PNG directly so the pixels can be checked.
            Texture2D probe = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                Assert.That(probe.LoadImage(File.ReadAllBytes(AssetDatabase.GetAssetPath(asset))),
                    Is.True);
                Color32[] pixels = probe.GetPixels32();

                double sum = 0.0;
                double maximum = 0.0;
                int lit = 0;
                int dark = 0;
                foreach (Color32 pixel in pixels)
                {
                    double luma = (0.2126 * pixel.r + 0.7152 * pixel.g + 0.0722 * pixel.b) / 255.0;
                    sum += luma;
                    if (luma > maximum) maximum = luma;
                    if (luma > 0.12) lit++;
                    if (luma < 0.02) dark++;
                }

                double mean = sum / pixels.Length;
                double litFraction = (double)lit / pixels.Length;
                double darkFraction = (double)dark / pixels.Length;

                Assert.That(maximum, Is.GreaterThan(0.45),
                    "the galactic bulge must be bright enough to read");
                Assert.That(mean, Is.LessThan(0.12),
                    "the sky must stay dark enough for the HUD and markers");
                Assert.That(litFraction, Is.InRange(0.005, 0.35),
                    "the band should cover part of the sky, not all of it");
                Assert.That(darkFraction, Is.GreaterThan(0.5),
                    "most of the sky is empty background");
            }
            finally
            {
                Object.DestroyImmediate(probe);
            }
        }

        [Test]
        public void TheSkyboxShaderCompiles()
        {
            Shader shader = Shader.Find(RenderingShaders.Skybox);

            Assert.That(shader, Is.Not.Null,
                "Shader.Find could not see Resources/Shaders/TimeToExploreSkybox.shader");
            Assert.That(ShaderUtil.ShaderHasError(shader), Is.False,
                "the skybox shader has compile errors");
        }

        [Test]
        public void InitialisingTheSkyPutsTheCameraOnIt()
        {
            _skyObject = new GameObject("Sky");
            SkyView sky = _skyObject.AddComponent<SkyView>();

            sky.Initialise(_camera);

            Assert.That(sky.Applied, Is.True);
            Assert.That(sky.Texture, Is.Not.Null);
            Assert.That(sky.Material, Is.Not.Null);
            Assert.That(sky.Material.shader, Is.EqualTo(Shader.Find(RenderingShaders.Skybox)));
            Assert.That(sky.Material.GetTexture("_MainTex"), Is.SameAs(sky.Texture));
            Assert.That(_camera.clearFlags, Is.EqualTo(CameraClearFlags.Skybox));
            Assert.That(RenderSettings.skybox, Is.SameAs(sky.Material));
        }

        [Test]
        public void RemovingTheSkyRestoresTheCameraAndRenderSettings()
        {
            _skyObject = new GameObject("Sky");
            SkyView sky = _skyObject.AddComponent<SkyView>();
            sky.Initialise(_camera);

            sky.Remove();

            Assert.That(sky.Applied, Is.False);
            Assert.That(sky.Material, Is.Null);
            Assert.That(_camera.clearFlags, Is.EqualTo(CameraClearFlags.SolidColor));
            Assert.That(RenderSettings.skybox, Is.Null);
        }

        [Test]
        public void AMissingTextureOrShaderIsRefused()
        {
            Texture2D texture = Resources.Load<Texture2D>(SkyView.TextureResource);

            Assert.That(SkyView.TryCreate(null, Shader.Find(RenderingShaders.Skybox), out _),
                Is.False);
            Assert.That(SkyView.TryCreate(texture, null, out _), Is.False);
            Assert.That(SkyView.TryCreate(texture, Shader.Find(RenderingShaders.Skybox), out Material made),
                Is.True);
            Assert.That(made, Is.Not.Null);
            Object.DestroyImmediate(made);
        }
    }
}
