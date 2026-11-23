using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Zfw.GpuCaps;

namespace Zfw.Upscaling
{
    /// <summary>
    /// FR-UP-04: picks the backend and quality.
    ///  - Order: DLSS → MetalFX → STP → FSR → RenderScale, first one supported wins. DLSS and MetalFX are not
    ///    implemented in this build (design only), so they are not registered. STP goes before FSR because it is
    ///    temporal: it reconstructs detail from several frames, FSR1 only sharpens one.
    ///  - Tier → quality: Low → Performance, Medium → Balanced, High / Ultra → Quality.
    ///  - The player's choice wins over the automatic one; command line (-upscaler= / -quality=) wins over both.
    /// Pure logic (no Unity state) so the EditMode tests can drive it.
    /// </summary>
    public static class UpscalerSelector
    {
        public static readonly string[] Order = { "DLSS", "MetalFX", "STP", "FSR1", "RenderScale" };

        public static IUpscaler Create(string name) => name switch
        {
            "STP" => new StpUpscaler(),
            "FSR1" or "FSR" => new FsrUpscaler(),
            "RenderScale" => new RenderScaleUpscaler(),
            _ => null,                                   // DLSS / MetalFX: not in this build
        };

        public static IEnumerable<string> Available(GpuCapsReport caps) =>
            Order.Where(n => { var u = Create(n); return u != null && u.IsSupported(caps); });

        /// <summary>Tier index 0..3 (Low..Ultra) → quality.</summary>
        public static UpscalerQuality QualityForTier(int tier) => tier switch
        {
            0 => UpscalerQuality.Performance,
            1 => UpscalerQuality.Balanced,
            _ => UpscalerQuality.Quality,
        };

        public struct Choice { public string backend; public UpscalerQuality quality; public string reason; public override string ToString() => $"{backend} / {quality}（{reason}）"; }

        /// <param name="manualBackend">Player's saved choice ("" = auto).</param>
        /// <param name="manualQuality">Player's saved quality ("" = from tier).</param>
        public static Choice Select(GpuCapsReport caps, int tier, string manualBackend, string manualQuality, string[] args)
        {
            var avail = Available(caps).ToList();
            var c = new Choice { backend = avail.FirstOrDefault() ?? "RenderScale", quality = QualityForTier(tier), reason = "自动" };
            if (!string.IsNullOrEmpty(manualBackend) && avail.Contains(Norm(manualBackend))) { c.backend = Norm(manualBackend); c.reason = "玩家选择"; }
            if (!string.IsNullOrEmpty(manualQuality) && Enum.TryParse(manualQuality, true, out UpscalerQuality mq)) { c.quality = mq; c.reason = "玩家选择"; }
            string a = args?.FirstOrDefault(x => x.StartsWith("-upscaler=", StringComparison.OrdinalIgnoreCase));
            if (a != null)
            {
                var n = Norm(a.Substring(10));
                if (avail.Contains(n)) { c.backend = n; c.reason = "启动参数"; }
                else c.reason += $"；启动参数 {n} 不可用，已忽略";
            }
            string q = args?.FirstOrDefault(x => x.StartsWith("-quality=", StringComparison.OrdinalIgnoreCase));
            if (q != null && Enum.TryParse(q.Substring(9), true, out UpscalerQuality cq)) { c.quality = cq; c.reason = "启动参数"; }
            return c;
        }

        static string Norm(string n) => n.Trim().ToUpperInvariant() switch
        {
            "FSR" or "FSR1" => "FSR1", "STP" => "STP", "RENDERSCALE" or "BILINEAR" => "RenderScale",
            "DLSS" => "DLSS", "METALFX" => "MetalFX", _ => n.Trim(),
        };
    }
}
