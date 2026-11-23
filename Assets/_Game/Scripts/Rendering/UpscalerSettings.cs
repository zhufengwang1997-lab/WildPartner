using System;
using UnityEngine;
using Zfw.QualityTiers;
using Zfw.Upscaling;

namespace WildPartner.Rendering
{
    /// <summary>
    /// The game's side of the upscaling middleware: picks the backend and quality with <see cref="UpscalerSelector"/>
    /// (player choice, tier, -upscaler= / -quality=), hands it to <see cref="UpscalerHost"/>, saves the choice, and
    /// keeps the anti-aliasing switch and the upscaler from fighting over the URP asset. Only Zfw.Upscaling types are
    /// used here, no backend internals (constraint C3 / NFR-05).
    ///  - Backend "关" or quality "原生" → no upscaling; the anti-aliasing switch (关 / TAA / STP) decides alone.
    ///  - Otherwise the backend renders at the reduced size. Spatial backends (RenderScale, FSR1) keep TAA when the
    ///    anti-aliasing switch is on; STP is temporal itself.
    /// </summary>
    public static class UpscalerSettings
    {
        public const string Off = "Off";
        public static string Backend { get; private set; } = Off;
        public static UpscalerQuality Quality { get; private set; } = UpscalerQuality.NativeAA;
        public static string LastReason { get; private set; } = "";
        static bool _init;

        public static void Init()
        {
            if (_init) return;
            _init = true;
            UpscalerHost.GameWantsTaa = () => AntiAliasing.Mode != AAMode.Off;
            UpscalerHost.Asset = () => AntiAliasing.Asset;
            UpscalerHost.Changed += () => { if (!UpscalerHost.IsUpscaling) AntiAliasing.Apply(); };
            QualityTiers.TierChanged += _ => { if (string.IsNullOrEmpty(Core.SaveSystem.Settings.upscalerQuality)) Select(); else UpscalerHost.Reapply(); };
            Select();
        }

        /// <summary>Re-run the selection from the saved settings, tier and command line.</summary>
        public static void Select()
        {
            var s = Core.SaveSystem.Settings;
            if (s.upscaler == Off && !HasArg("-upscaler=")) { Apply(Off, UpscalerQuality.NativeAA, "玩家关闭"); return; }
            var c = UpscalerSelector.Select(Zfw.GpuCaps.GpuCaps.Current, (int)QualityTiers.Current, s.upscaler, s.upscalerQuality, Environment.GetCommandLineArgs());
            Apply(c.backend, c.quality, c.reason);
        }

        /// <summary>Player choice from the settings menu / F3 overlay. backend "" = auto.</summary>
        public static void Set(string backend, string quality)
        {
            var s = Core.SaveSystem.Settings;
            s.upscaler = backend ?? ""; s.upscalerQuality = quality ?? "";
            Core.SaveSystem.SaveSettings();
            Select();
        }

        static void Apply(string backend, UpscalerQuality q, string reason)
        {
            Backend = backend; Quality = q; LastReason = reason;
            var u = backend == Off || q == UpscalerQuality.NativeAA ? null : UpscalerSelector.Create(backend);
            if (u == null) UpscalerHost.Disable();
            else UpscalerHost.Use(u, q);
            Debug.Log($"[Upscaler] 选择：{Label(backend)} / {QualityLabel(q)}（{reason}），渲染 {UpscalerHost.RenderSize.x}x{UpscalerHost.RenderSize.y}，显示 {Screen.width}x{Screen.height}");
        }

        static bool HasArg(string prefix) => Array.Exists(Environment.GetCommandLineArgs(), a => a.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

        public static string Label(string b) => b switch { Off => "关", "RenderScale" => "渲染缩放", "FSR1" => "FSR 1", "STP" => "STP", "" => "自动", _ => b };
        public static string QualityLabel(UpscalerQuality q) => q switch
        {
            UpscalerQuality.NativeAA => "原生",
            UpscalerQuality.Quality => "质量",
            UpscalerQuality.Balanced => "平衡",
            _ => "性能",
        };
    }
}
