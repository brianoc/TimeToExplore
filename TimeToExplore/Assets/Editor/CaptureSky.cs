using System;
using System.IO;
using TimeToExplore.Rendering;
using UnityEditor;
using UnityEngine;

namespace TimeToExplore.EditorTools
{
    /// <summary>
    /// Renders the real sky material to PNGs for review, since the look of a
    /// sky is a judgement and not an assertion. Writes the opening view, Orion,
    /// the Plough, and two isolating passes — catalogue only, and the procedural
    /// field with the catalogue switched off — into <c>SKY_CAPTURE_DIR</c>.
    ///
    /// Run it in batch mode; the command is in [The sky](/technical/sky.md).
    /// </summary>
    public static class CaptureSky
    {
        private const int Width = 1920;
        private const int Height = 1080;

        public static void Capture()
        {
            Texture2D band = Resources.Load<Texture2D>(SkyView.TextureResource);
            TextAsset json = Resources.Load<TextAsset>(StarCatalogue.Resource);
            if (band == null || json == null)
            {
                throw new InvalidOperationException(
                    $"missing sky assets: band={band != null}, catalogue={json != null}");
            }

            BrightStarFile catalogue = StarCatalogue.Parse(json.text);
            Texture2D starMap = StarCatalogue.Build(catalogue);
            Shader shader = Shader.Find(RenderingShaders.Skybox);
            if (!SkyView.TryCreate(band, starMap, shader, out Material material))
            {
                throw new InvalidOperationException("the skybox material could not be built");
            }

            Camera camera = null;
            RenderTexture target = null;
            try
            {
                camera = new GameObject("Sky Capture Camera").AddComponent<Camera>();
                camera.clearFlags = CameraClearFlags.Skybox;
                camera.fieldOfView = 60f;
                camera.nearClipPlane = 0.1f;
                camera.farClipPlane = 1000f;
                camera.allowHDR = false;

                target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32);
                target.Create();
                camera.targetTexture = target;
                RenderSettings.skybox = material;

                string outDir = Environment.GetEnvironmentVariable("SKY_CAPTURE_DIR")
                                ?? Path.Combine(Path.GetTempPath(), "sky-capture");
                Directory.CreateDirectory(outDir);

                Shoot(camera, target, outDir, "opening", 180f, 14f);
                Shoot(camera, target, outDir, "orion",
                    YawTowards(catalogue, "Alp Ori"), PitchTowards(catalogue, "Alp Ori"));
                Shoot(camera, target, outDir, "plough",
                    YawTowards(catalogue, "Eta UMa"), PitchTowards(catalogue, "Eta UMa"));

                material.SetFloat("_BandGain", 0f);
                material.SetFloat("_StarGain", 0f);
                Shoot(camera, target, outDir, "catalogue-orion",
                    YawTowards(catalogue, "Alp Ori"), PitchTowards(catalogue, "Alp Ori"));

                material.SetFloat("_BandGain", 1f);
                material.SetFloat("_StarGain", 1f);
                material.SetFloat("_CatalogueGain", 0f);
                Shoot(camera, target, outDir, "procedural-orion",
                    YawTowards(catalogue, "Alp Ori"), PitchTowards(catalogue, "Alp Ori"));

                Probe(starMap, catalogue, "Alp Ori");
                Probe(starMap, catalogue, "Alp CMa");
                Probe(starMap, catalogue, "Alp Lyr");

                Debug.Log($"sky captures written to {outDir}");
            }
            finally
            {
                if (camera != null) UnityEngine.Object.DestroyImmediate(camera.gameObject);
                if (target != null)
                {
                    target.Release();
                    UnityEngine.Object.DestroyImmediate(target);
                }
                if (material != null) UnityEngine.Object.DestroyImmediate(material);
                if (starMap != null) UnityEngine.Object.DestroyImmediate(starMap);
            }
        }

        private static void Shoot(
            Camera camera, RenderTexture target, string dir, string name, float yaw, float pitch)
        {
            camera.transform.rotation = Quaternion.Euler(-pitch, yaw, 0f);
            camera.Render();

            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = target;
            var image = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            try
            {
                image.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
                image.Apply();
                File.WriteAllBytes(Path.Combine(dir, $"sky-{name}.png"), image.EncodeToPNG());
                Debug.Log($"wrote sky-{name}.png");
            }
            finally
            {
                RenderTexture.active = previous;
                UnityEngine.Object.DestroyImmediate(image);
            }
        }

        private static void Probe(Texture2D map, BrightStarFile file, string bayer)
        {
            if (!TryFind(file, bayer, out Vector3 direction))
            {
                Debug.Log($"probe {bayer}: not in the catalogue");
                return;
            }

            Vector3 d = direction.normalized;
            Vector2 uv = StarCatalogue.SkyUv(d);
            int column = Mathf.Clamp(Mathf.FloorToInt(uv.x * StarCatalogue.Columns), 0, StarCatalogue.Columns - 1);
            int row = Mathf.Clamp(Mathf.FloorToInt(uv.y * StarCatalogue.Rows), 0, StarCatalogue.Rows - 1);
            Color cell = map.GetPixel(column, row);

            float starU = (column + cell.r) / StarCatalogue.Columns;
            float starV = (row + cell.g) / StarCatalogue.Rows;
            float b = cell.b;
            float radius = 0.0007f * Mathf.Pow(2f, 1.9f * b);
            float gain = 0.28f * Mathf.Pow(2f, 3.4f * b);
            float angle = 0f;
            if (cell.b > 0f)
            {
                float du = starU - uv.x;
                du -= Mathf.Floor(du + 0.5f);
                float dv = starV - uv.y;
                float duRad = du * 2f * Mathf.PI * Mathf.Sqrt(d.x * d.x + d.z * d.z);
                float dvRad = dv * Mathf.PI;
                angle = Mathf.Sqrt(duRad * duRad + dvRad * dvRad);
            }

            Debug.Log(
                $"probe {bayer}: uv=({uv.x:F5},{uv.y:F5}) cell=({column},{row}) " +
                $"rgba=({cell.r:F4},{cell.g:F4},{cell.b:F4},{cell.a:F4}) " +
                $"starUv=({starU:F5},{starV:F5}) centreOffset={angle:E3} " +
                $"radius={radius:E3} gain={gain:F3}");
        }

        private static bool TryFind(BrightStarFile file, string bayer, out Vector3 direction)
        {
            direction = Vector3.zero;
            if (file?.stars == null) return false;
            foreach (BrightStar star in file.stars)
            {
                if (star.bayer != bayer) continue;
                direction = new Vector3(star.x, star.y, star.z);
                return true;
            }
            return false;
        }

        private static float YawTowards(BrightStarFile file, string bayer)
        {
            if (!TryFind(file, bayer, out Vector3 d)) return 0f;
            return Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
        }

        private static float PitchTowards(BrightStarFile file, string bayer)
        {
            if (!TryFind(file, bayer, out Vector3 d)) return 0f;
            return Mathf.Asin(Mathf.Clamp(d.y, -1f, 1f)) * Mathf.Rad2Deg;
        }
    }
}
