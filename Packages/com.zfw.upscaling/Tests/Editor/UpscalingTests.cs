using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Zfw.GpuCaps;

namespace Zfw.Upscaling.Tests
{
    public class UpscalingTests
    {
        static readonly GpuCapsReport Caps = new GpuCapsReport();

        [Test] public void TierMapsToQuality()
        {
            Assert.AreEqual(UpscalerQuality.Performance, UpscalerSelector.QualityForTier(0));
            Assert.AreEqual(UpscalerQuality.Balanced, UpscalerSelector.QualityForTier(1));
            Assert.AreEqual(UpscalerQuality.Quality, UpscalerSelector.QualityForTier(2));
            Assert.AreEqual(UpscalerQuality.Quality, UpscalerSelector.QualityForTier(3));
        }

        [Test] public void SelectorPicksFirstSupportedInOrder()
        {
            var c = UpscalerSelector.Select(Caps, 2, "", "", new string[0]);
            var expected = UpscalerSelector.Available(Caps).First();
            Assert.AreEqual(expected, c.backend);
            Assert.AreEqual(UpscalerQuality.Quality, c.quality);
            // RenderScale is always last and always available
            Assert.AreEqual("RenderScale", UpscalerSelector.Available(Caps).Last());
            // DLSS / MetalFX are not in this build: never selected
            Assert.IsFalse(UpscalerSelector.Available(Caps).Contains("DLSS"));
        }

        [Test] public void PlayerChoiceThenCommandLineWin()
        {
            var c = UpscalerSelector.Select(Caps, 0, "RenderScale", "Quality", new string[0]);
            Assert.AreEqual("RenderScale", c.backend); Assert.AreEqual(UpscalerQuality.Quality, c.quality);
            c = UpscalerSelector.Select(Caps, 0, "RenderScale", "Quality", new[] { "-upscaler=fsr", "-quality=Performance" });
            Assert.AreEqual("FSR1", c.backend); Assert.AreEqual(UpscalerQuality.Performance, c.quality);
            Assert.AreEqual("启动参数", c.reason);
            c = UpscalerSelector.Select(Caps, 2, "", "", new[] { "-upscaler=DLSS" });
            Assert.AreNotEqual("DLSS", c.backend, "an unavailable backend on the command line is ignored");
        }

        [Test] public void RenderSizeAndMipBias()
        {
            var d = new Vector2Int(2560, 1440);
            Assert.AreEqual(new Vector2Int(1280, 720), UpscalerMath.RenderSize(d, UpscalerQuality.Performance));
            Assert.AreEqual(new Vector2Int(1707, 960), UpscalerMath.RenderSize(d, UpscalerQuality.Quality));
            Assert.AreEqual(d, UpscalerMath.RenderSize(d, UpscalerQuality.NativeAA));
            Assert.AreEqual(-2f, UpscalerMath.DefaultMipBias(1280, 2560), 1e-5f);    // log2(0.5) - 1
            Assert.AreEqual(-1f, UpscalerMath.DefaultMipBias(2560, 2560), 1e-5f);
        }

        [Test] public void InitializeIsTheRenderSizeSource()
        {
            var u = new RenderScaleUpscaler();
            var r = u.Initialize(new Vector2Int(1920, 1080), UpscalerQuality.Balanced);
            Assert.AreEqual(UpscalerMath.RenderSize(new Vector2Int(1920, 1080), UpscalerQuality.Balanced), r);
            Assert.AreEqual(r, u.RenderSize);
            Assert.AreEqual(UpscalerMath.DefaultMipBias(r.x, 1920), u.GetMipBias(), 1e-5f);
            Assert.AreEqual(Vector2.zero, u.GetJitter(5), "spatial upscalers do not jitter");
        }

        [Test] public void HaltonSequenceAndPhaseCount()
        {
            Assert.AreEqual(0.5f, UpscalerMath.Halton(1, 2), 1e-6f);
            Assert.AreEqual(0.25f, UpscalerMath.Halton(2, 2), 1e-6f);
            Assert.AreEqual(1f / 3f, UpscalerMath.Halton(1, 3), 1e-6f);
            Assert.AreEqual(2f / 3f, UpscalerMath.Halton(2, 3), 1e-6f);
            Assert.AreEqual(32, UpscalerMath.PhaseCount(1280, 2560));   // 8 x 2^2
            Assert.AreEqual(18, UpscalerMath.PhaseCount(1280, 1920));   // 8 x 1.5^2
            Assert.AreEqual(8, UpscalerMath.PhaseCount(1920, 1920));
            var j = UpscalerMath.HaltonJitter(0, 8);
            Assert.That(Mathf.Abs(j.x) <= 0.5f && Mathf.Abs(j.y) <= 0.5f);
            Assert.AreEqual(UpscalerMath.HaltonJitter(3, 8), UpscalerMath.HaltonJitter(11, 8), "repeats after the phase count");
        }

        /// <summary>Constraint C3: game assemblies never reference a vendor SDK assembly.</summary>
        [Test] public void GameDoesNotReferenceVendorSdks()
        {
            string[] vendor = { "NVIDIA", "DLSS", "FidelityFX", "FSR2", "FSR3", "MetalFX", "XeSS", "Streamline" };
            var asmdefs = Directory.GetFiles(Path.Combine(Application.dataPath, "_Game"), "*.asmdef", SearchOption.AllDirectories);
            Assert.IsNotEmpty(asmdefs);
            foreach (var f in asmdefs)
            {
                string text = File.ReadAllText(f);
                foreach (var v in vendor) StringAssert.DoesNotContain(v, text, $"{Path.GetFileName(f)} references a vendor SDK ({v})");
            }
            var game = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "WildPartner.Runtime");
            if (game != null)
                foreach (var r in game.GetReferencedAssemblies())
                    foreach (var v in vendor) Assert.IsFalse(r.Name.Contains(v), $"WildPartner.Runtime references {r.Name}");
        }
    }
}
