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
    [Category("presentation")]
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

        // --- Bright stars ---------------------------------------------------

        private static BrightStarFile CommittedCatalogue()
        {
            TextAsset asset = Resources.Load<TextAsset>(StarCatalogue.Resource);
            Assert.That(asset, Is.Not.Null,
                "missing Resources/Sky/bright_stars.json; " +
                "run tools/generate_bright_stars.py");
            return StarCatalogue.Parse(asset.text);
        }

        [Test]
        public void TheBrightStarCatalogueIsCommittedAndPlausible()
        {
            BrightStarFile file = CommittedCatalogue();

            Assert.That(file.stars, Is.Not.Null);
            Assert.That(file.stars.Length, Is.GreaterThan(400),
                "the constellation figures need a few hundred stars");

            BrightStar sirius = null;
            foreach (BrightStar star in file.stars)
            {
                Assert.That(star.magnitude, Is.LessThanOrEqualTo(file.magnitudeLimit + 1e-4f),
                    $"{star.bayer} is brighter than the catalogue cut");
                float length = Mathf.Sqrt(star.x * star.x + star.y * star.y + star.z * star.z);
                Assert.That(length, Is.EqualTo(1f).Within(1e-3f),
                    $"{star.bayer} has a direction that is not a unit vector");
                if (star.bayer == "Alp CMa") sirius = star;
            }

            Assert.That(sirius, Is.Not.Null, "Sirius must be in the catalogue");
            Assert.That(sirius.magnitude, Is.EqualTo(-1.46f).Within(0.01f),
                "Sirius is the brightest star in the sky; a catalogue that " +
                "disagrees is reading the wrong column");
            Assert.That(file.magnitudeLimit, Is.EqualTo(StarCatalogue.MagnitudeLimit),
                "the generator's cutoff and the encoder's must agree");
        }

        [Test]
        public void TheSkyUvMatchesTheShaderMapping()
        {
            // u = atan2(z, x) / 2pi + 0.5, v = asin(y) / pi + 0.5, the mapping the
            // band's generator and the shader both use.
            AssertNearly(StarCatalogue.SkyUv(Vector3.right), new Vector2(0.5f, 0.5f));
            AssertNearly(StarCatalogue.SkyUv(Vector3.up), new Vector2(0.5f, 1f));
            AssertNearly(StarCatalogue.SkyUv(Vector3.down), new Vector2(0.5f, 0f));
            AssertNearly(StarCatalogue.SkyUv(Vector3.forward), new Vector2(0.75f, 0.5f));
            AssertNearly(StarCatalogue.SkyUv(Vector3.back), new Vector2(0.25f, 0.5f));
        }

        [Test]
        public void TheStarMapIsPointFilteredAndUnmipmapped()
        {
            Texture2D map = StarCatalogue.Build(CommittedCatalogue());
            try
            {
                Assert.That(map.width, Is.EqualTo(StarCatalogue.Columns));
                Assert.That(map.height, Is.EqualTo(StarCatalogue.Rows));
                Assert.That(map.format, Is.EqualTo(TextureFormat.RGBAHalf));
                Assert.That(map.mipmapCount, Is.EqualTo(1),
                    "the map is sampled at LOD 0, so it must have no mip chain");
                Assert.That(map.filterMode, Is.EqualTo(FilterMode.Point),
                    "bilinear filtering would blend empty cells into stars");
                Assert.That(map.wrapModeU, Is.EqualTo(TextureWrapMode.Repeat));
                Assert.That(map.wrapModeV, Is.EqualTo(TextureWrapMode.Clamp));
            }
            finally
            {
                Object.DestroyImmediate(map);
            }
        }

        [Test]
        public void EveryCatalogueStarGetsItsOwnCell()
        {
            BrightStarFile file = CommittedCatalogue();
            Texture2D map = StarCatalogue.Build(file);
            try
            {
                Color[] pixels = map.GetPixels();
                int occupied = 0;
                foreach (Color pixel in pixels)
                {
                    if (pixel.b > 0f) occupied++;
                }

                Assert.That(occupied, Is.EqualTo(file.stars.Length),
                    "a star was lost: two stars share a cell, which the generator " +
                    "is meant to have merged away");
            }
            finally
            {
                Object.DestroyImmediate(map);
            }
        }

        [Test]
        public void SiriusLandsInTheCellItsDirectionPointsAt()
        {
            BrightStarFile file = CommittedCatalogue();
            BrightStar sirius = null;
            foreach (BrightStar star in file.stars)
            {
                if (star.bayer == "Alp CMa") { sirius = star; break; }
            }
            Assert.That(sirius, Is.Not.Null);

            Texture2D map = StarCatalogue.Build(file);
            try
            {
                Vector2 uv = StarCatalogue.SkyUv(new Vector3(sirius.x, sirius.y, sirius.z));
                int column = Mathf.Clamp(Mathf.FloorToInt(uv.x * StarCatalogue.Columns), 0, StarCatalogue.Columns - 1);
                int row = Mathf.Clamp(Mathf.FloorToInt(uv.y * StarCatalogue.Rows), 0, StarCatalogue.Rows - 1);

                Color cell = map.GetPixel(column, row);
                Assert.That(cell.b, Is.GreaterThan(0f), "Sirius is not in its own cell");

                float decodedU = (column + cell.r) / StarCatalogue.Columns;
                float decodedV = (row + cell.g) / StarCatalogue.Rows;
                Assert.That(decodedU, Is.EqualTo(uv.x).Within(0.5f / StarCatalogue.Columns),
                    "the stored fraction must land within half a texel");
                Assert.That(decodedV, Is.EqualTo(uv.y).Within(0.5f / StarCatalogue.Rows));
            }
            finally
            {
                Object.DestroyImmediate(map);
            }
        }

        [Test]
        public void TwoStarsInOneCellKeepTheBrighterOne()
        {
            // Two directions inside the same cell, a bright and a faint.
            Vector2 cellUv = new Vector2(0.3f, 0.6f);
            Vector3 bright = DirectionFromUv(cellUv + new Vector2(0.0001f, 0.0001f));
            Vector3 faint = DirectionFromUv(cellUv + new Vector2(0.0003f, 0.0002f));

            var file = new BrightStarFile
            {
                stars = new[]
                {
                    new BrightStar { hr = 1, bayer = "faint", magnitude = 3.9f, bv = 0.6f,
                        x = faint.x, y = faint.y, z = faint.z },
                    new BrightStar { hr = 2, bayer = "bright", magnitude = 0.5f, bv = 0.6f,
                        x = bright.x, y = bright.y, z = bright.z },
                }
            };

            Texture2D map = StarCatalogue.Build(file);
            try
            {
                int column = Mathf.Clamp(Mathf.FloorToInt(cellUv.x * StarCatalogue.Columns), 0, StarCatalogue.Columns - 1);
                int row = Mathf.Clamp(Mathf.FloorToInt(cellUv.y * StarCatalogue.Rows), 0, StarCatalogue.Rows - 1);
                Color cell = map.GetPixel(column, row);

                Assert.That(cell.b, Is.EqualTo(StarCatalogue.BrightnessCode(0.5f)).Within(1e-3f),
                    "the brighter star must win the cell, whatever the order");
            }
            finally
            {
                Object.DestroyImmediate(map);
            }
        }

        [Test]
        public void TheCodesAreMonotonicAndLeaveZeroForAnEmptyCell()
        {
            Assert.That(StarCatalogue.BrightnessCode(StarCatalogue.MagnitudeLimit),
                Is.EqualTo(1f / 255f).Within(1e-6f),
                "the faintest star is one step above empty");
            Assert.That(StarCatalogue.BrightnessCode(-3f), Is.EqualTo(1f),
                "anything brighter than the span saturates");
            Assert.That(StarCatalogue.BrightnessCode(0f),
                Is.GreaterThan(StarCatalogue.BrightnessCode(3f)));
            Assert.That(StarCatalogue.ColourCode(0.65f), Is.EqualTo(0.4375f).Within(0.01f),
                "a solar B-V sits just below the middle of the colour ramp");
            Assert.That(StarCatalogue.BrightnessCode(StarCatalogue.MagnitudeLimit),
                Is.GreaterThan(0f), "zero means an empty cell, never a star");
        }

        [Test]
        public void TheShaderAndTheCatalogueAgreeOnTheCellGrid()
        {
            string path = Path.Combine(
                Application.dataPath, "Resources", "Shaders", "TimeToExploreSkybox.shader");
            string source = File.ReadAllText(path);

            Assert.That(source, Does.Contain(
                    "StarMapColumns = " + StarCatalogue.Columns + ".0"),
                "the shader's column count drifted from StarCatalogue.Columns");
            Assert.That(source, Does.Contain(
                    "StarMapRows = " + StarCatalogue.Rows + ".0"),
                "the shader's row count drifted from StarCatalogue.Rows");
            Assert.That(source, Does.Contain("_StarMap"),
                "the shader must declare the bright-star map");
        }

        [Test]
        public void TheSkyViewCarriesTheBrightStarsAndReleasesThem()
        {
            _skyObject = new GameObject("Sky");
            SkyView sky = _skyObject.AddComponent<SkyView>();
            sky.Initialise(_camera);

            Assert.That(sky.StarMap, Is.Not.Null);
            Assert.That(sky.Material.GetTexture("_StarMap"), Is.SameAs(sky.StarMap));
            Assert.That(sky.Material.GetFloat("_CatalogueGain"), Is.EqualTo(1f));

            Texture2D map = sky.StarMap;
            sky.Remove();

            Assert.That(sky.StarMap, Is.Null);
            Assert.That(map == null, Is.True, "the runtime star map must be destroyed");
            Assert.That(sky.Texture, Is.Not.Null,
                "the band is a Resources asset and must survive");
        }

        private static Vector3 DirectionFromUv(Vector2 uv)
        {
            float longitude = (uv.x - 0.5f) * 2f * Mathf.PI;
            float latitude = (uv.y - 0.5f) * Mathf.PI;
            float cosLat = Mathf.Cos(latitude);
            return new Vector3(
                cosLat * Mathf.Cos(longitude),
                Mathf.Sin(latitude),
                cosLat * Mathf.Sin(longitude));
        }

        private static void AssertNearly(Vector2 actual, Vector2 expected)
        {
            Assert.That(actual.x, Is.EqualTo(expected.x).Within(1e-5f));
            Assert.That(actual.y, Is.EqualTo(expected.y).Within(1e-5f));
        }
    }
}
