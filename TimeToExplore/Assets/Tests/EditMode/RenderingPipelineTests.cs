using System.IO;
using NUnit.Framework;
using TimeToExplore.Rendering;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace TimeToExplore.Tests.EditMode
{
    /// <summary>
    /// Guards the URP migration without rendering a frame: the graphics
    /// default, the pipeline asset's cheap settings, the runtime shaders, and
    /// the material property the renderer code writes.
    /// </summary>
    public class RenderingPipelineTests
    {
        private const string PipelineAssetPath =
            "Assets/Settings/TimeToExploreUniversalRenderPipeline.asset";
        private const string RendererAssetPath =
            "Assets/Settings/TimeToExploreUniversalRenderer.asset";
        private const string ShaderFolder = "Assets/Resources/Shaders/";

        [Test]
        public void GraphicsSettingsUseTheUniversalRenderPipeline()
        {
            RenderPipelineAsset pipeline = GraphicsSettings.defaultRenderPipeline;

            Assert.That(pipeline, Is.Not.Null,
                "GraphicsSettings.defaultRenderPipeline is not assigned");
            Assert.That(pipeline, Is.InstanceOf<UniversalRenderPipelineAsset>(),
                "the graphics default is not a URP asset");
        }

        [Test]
        public void TheShippedPipelineAssetIsTheGraphicsDefault()
        {
            var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(
                PipelineAssetPath);
            var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(
                RendererAssetPath);

            Assert.That(pipeline, Is.Not.Null, PipelineAssetPath + " is missing");
            Assert.That(renderer, Is.Not.Null, RendererAssetPath + " is missing");
            Assert.That(GraphicsSettings.defaultRenderPipeline, Is.SameAs(pipeline),
                "the shipped pipeline asset is not the graphics default");
            Assert.That(pipeline.rendererDataList.Length, Is.GreaterThan(0));
            Assert.That(pipeline.rendererDataList[0], Is.SameAs(renderer),
                "the pipeline asset does not point at the shipped renderer");
        }

        [Test]
        public void ThePipelineStaysCheap()
        {
            var pipeline = GraphicsSettings.defaultRenderPipeline as UniversalRenderPipelineAsset;
            Assert.That(pipeline, Is.Not.Null);

            Assert.That(pipeline.supportsHDR, Is.False, "HDR is not required");
            Assert.That(pipeline.msaaSampleCount, Is.EqualTo(1), "MSAA must be off");
            Assert.That(pipeline.renderScale, Is.EqualTo(1f).Within(1e-6f),
                "render scale must be 1");
            Assert.That(pipeline.supportsCameraDepthTexture, Is.False,
                "no depth texture is used");
            Assert.That(pipeline.supportsCameraOpaqueTexture, Is.False,
                "no opaque copy is used");
            Assert.That(pipeline.supportsMainLightShadows, Is.False,
                "shadows are disabled at solar scale");
            Assert.That(pipeline.supportsAdditionalLightShadows, Is.False,
                "shadows are disabled at solar scale");
            Assert.That(pipeline.supportsSoftShadows, Is.False);
            Assert.That(pipeline.mainLightRenderingMode,
                Is.EqualTo(LightRenderingMode.Disabled),
                "the orrery lights itself; URP main lights stay off");
            Assert.That(pipeline.additionalLightsRenderingMode,
                Is.EqualTo(LightRenderingMode.Disabled),
                "there are no additional lights");

            var renderer = pipeline.rendererDataList[0] as UniversalRendererData;
            Assert.That(renderer, Is.Not.Null);
            Assert.That(renderer.intermediateTextureMode,
                Is.EqualTo(IntermediateTextureMode.Auto),
                "no renderer features, so no intermediate texture is needed");
        }

        [Test]
        public void QualityLevelsDoNotOverrideTheGraphicsDefault()
        {
            for (int i = 0; i < QualitySettings.names.Length; i++)
            {
                RenderPipelineAsset level = QualitySettings.GetRenderPipelineAssetAt(i);
                Assert.That(level == null || level is UniversalRenderPipelineAsset, Is.True,
                    $"quality level '{QualitySettings.names[i]}' has a non-URP pipeline override");
            }
        }

        [Test]
        public void TheBodyShaderCompilesForTheUniversalPipeline()
        {
            Shader shader = AssertUniversalShader(RenderingShaders.BodyLit);

            AssertMaterialProperty(shader, "_Color");
            AssertMaterialProperty(shader, "_Ambient");
        }

        [Test]
        public void TheUnlitShaderIsRuntimeAvailable()
        {
            Shader shader = AssertUniversalShader(RenderingShaders.Unlit);

            AssertMaterialProperty(shader, "_Color");
        }

        [Test]
        public void TheLineShaderSupportsVertexColourAndTiling()
        {
            Shader shader = AssertUniversalShader(RenderingShaders.Line);

            AssertMaterialProperty(shader, "_MainTex");
            AssertMaterialProperty(shader, "_Color");

            // LineRenderer supplies start/end colours as vertex colours and the
            // dash pattern as a tiled _MainTex; both must reach the fragment.
            string source = File.ReadAllText(AssetDatabase.GetAssetPath(shader));
            StringAssert.Contains(": COLOR", source);
            StringAssert.Contains("TRANSFORM_TEX", source);
        }

        [Test]
        public void TheBodyMaterialKeepsMaterialColorWorking()
        {
            Shader shader = Shader.Find(RenderingShaders.BodyLit);
            Assert.That(shader, Is.Not.Null);

            var material = new Material(shader);
            try
            {
                var colour = new Color(0.2f, 0.4f, 0.8f, 1f);
                material.color = colour;

                Assert.That(material.color.r, Is.EqualTo(colour.r).Within(1e-4f));
                Assert.That(material.color.g, Is.EqualTo(colour.g).Within(1e-4f));
                Assert.That(material.color.b, Is.EqualTo(colour.b).Within(1e-4f));
                Assert.That(material.GetColor("_Color").b,
                    Is.EqualTo(colour.b).Within(1e-4f));
            }
            finally
            {
                Object.DestroyImmediate(material);
            }
        }

        [TestCase(true, RenderingShaders.Unlit)]
        [TestCase(false, RenderingShaders.BodyLit)]
        public void ForBodyChoosesTheSunShaders(bool isStar, string expected)
        {
            Assert.That(RenderingShaders.ForBody(isStar), Is.EqualTo(expected));
        }

        /// <summary>
        /// A shader survives runtime <c>Shader.Find</c> only when it lives in a
        /// Resources folder, and URP draws it only when the SubShader carries
        /// the Universal pipeline tag and the pass is a forward pass. The pass
        /// tags are checked in the source because batch mode with
        /// <c>-nographics</c> does not populate <c>Shader.FindPassTagValue</c>
        /// (it reports one pass for every shader, built-in or URP).
        /// </summary>
        private static Shader AssertUniversalShader(string name)
        {
            Shader shader = Shader.Find(name);
            Assert.That(shader, Is.Not.Null, name + " was not found");

            Assert.That(ShaderUtil.ShaderHasError(shader), Is.False,
                name + " has compile errors");

            string path = AssetDatabase.GetAssetPath(shader);
            Assert.That(path, Does.StartWith(ShaderFolder),
                name + " must live under Resources so a build can find it");

            string source = File.ReadAllText(path);
            StringAssert.Contains("\"RenderPipeline\" = \"UniversalPipeline\"", source,
                name + " is not tagged for the Universal pipeline");
            StringAssert.Contains("\"LightMode\" = \"UniversalForward\"", source,
                name + " has no UniversalForward pass");

            return shader;
        }

        private static void AssertMaterialProperty(Shader shader, string property)
        {
            var material = new Material(shader);
            try
            {
                Assert.That(material.HasProperty(property), Is.True,
                    shader.name + " does not declare " + property);
            }
            finally
            {
                Object.DestroyImmediate(material);
            }
        }
    }
}
