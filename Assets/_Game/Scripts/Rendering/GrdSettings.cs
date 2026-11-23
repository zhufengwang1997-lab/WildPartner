using System;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using WildPartner.Core;

namespace WildPartner.Rendering
{
    /// <summary>
    /// Stage 4 (FR-RD-01..03): GPU Resident Drawer and GPU occlusion culling switches.
    /// Priority: -grd=on|off / -occlusion=on|off &gt; settings file &gt; off. Off by default: on Mac (Metal) every
    /// renderer using the WildPartner/Toon shader stops drawing when GRD is on (root cause not found yet; DECISIONS). Applied to whichever URP asset the current
    /// quality tier uses (tier changes swap the asset, so <see cref="Apply"/> runs again from Bootstrap.ReapplyAA).
    /// All tier assets are saved with GRD = Instanced Drawing (same lesson as STP: settings nobody saves get stripped),
    /// and GraphicsSettings keeps every BatchRendererGroup variant.
    /// </summary>
    public static class GrdSettings
    {
        static bool? _argGrd, _argOcc;

        public static bool Grd => _argGrd ?? SaveSystem.Settings.grdEnabled;
        public static bool Occlusion => _argOcc ?? SaveSystem.Settings.gpuOcclusion;
        static readonly System.Reflection.MethodInfo _isEnabled = typeof(GPUResidentDrawer).GetMethod("IsEnabled",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
        /// <summary>GPU occlusion culling actually running.</summary>
        public static bool OcclusionActive => GPUResidentDrawer.IsInstanceOcclusionCullingEnabled();

        public static void Init()
        {
            foreach (var a in Environment.GetCommandLineArgs())
            {
                if (a.StartsWith("-grd=", StringComparison.OrdinalIgnoreCase)) _argGrd = a.Substring(5).ToLowerInvariant() == "on";
                if (a.StartsWith("-occlusion=", StringComparison.OrdinalIgnoreCase)) _argOcc = a.Substring(11).ToLowerInvariant() == "on";
            }
            Apply();
        }

        /// <summary>True when GRD is actually running (the SRP can refuse it, e.g. unsupported platform or settings).
        /// GPUResidentDrawer.IsEnabled is internal in Core 17, read via reflection (debug display only).</summary>
        public static bool IsEnabled()
        {
            try { return _isEnabled != null && (bool)_isEnabled.Invoke(null, null); } catch { return false; }
        }

        public static void Apply()
        {
            var a = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (!a) return;
            var mode = Grd ? GPUResidentDrawerMode.InstancedDrawing : GPUResidentDrawerMode.Disabled;
            if (a.gpuResidentDrawerMode != mode) a.gpuResidentDrawerMode = mode;
            bool occ = Grd && Occlusion;
            if (a.gpuResidentDrawerEnableOcclusionCullingInCameras != occ) a.gpuResidentDrawerEnableOcclusionCullingInCameras = occ;
            Debug.Log($"[GRD] mode={mode} occlusion={occ} (asset {a.name})");
        }

        public static void Set(bool grd, bool occlusion)
        {
            _argGrd = null; _argOcc = null;
            SaveSystem.Settings.grdEnabled = grd; SaveSystem.Settings.gpuOcclusion = occlusion;
            SaveSystem.SaveSettings();
            Apply();
        }
    }
}
