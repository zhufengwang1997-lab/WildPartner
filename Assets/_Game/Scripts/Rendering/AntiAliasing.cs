using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace WildPartner.Rendering
{
    public enum AAMode { Off, TAA, STP }

    /// <summary>
    /// 超分阶段第一步 (docs/最终指令.md): temporal anti-aliasing at native resolution, switchable Off / TAA / STP.
    ///  - TAA: URP's temporal AA on the camera (high quality) with its contrast-adaptive sharpening.
    ///  - STP: URP's Spatial-Temporal Post-processing as the pipeline's upscaling filter. At 100 % render scale URP still
    ///    runs it (as an anti-aliaser) and forces the camera to TAA mode internally; it then replaces TAA.
    ///  - Off: neither (the old look: no AA at all).
    /// Both temporal modes need: post-processing on the camera, no MSAA, no dynamic resolution, motion vectors
    /// (Toon / grass / skinned meshes write them). The mode is saved in the settings; the tier switch (which swaps the
    /// URP asset) re-applies it. <see cref="ResetHistory"/> is IN-08 (camera cuts, teleports).
    /// </summary>
    public static class AntiAliasing
    {
        public static AAMode Mode { get; private set; } = AAMode.STP;
        public static event Action<AAMode> Changed;
        /// <summary>TAA's sharpening (0..1).</summary>
        public const float TaaSharpening = 0.4f;

        public static bool Supported(AAMode m) => m != AAMode.STP || (SystemInfo.supportsComputeShaders && SystemInfo.graphicsDeviceType != GraphicsDeviceType.OpenGLES3);

        public static void Set(AAMode m, bool save = true)
        {
            if (!Supported(m)) m = AAMode.TAA;
            Mode = m;
            Apply();
            if (save) { Core.SaveSystem.Settings.antiAliasing = m.ToString(); Core.SaveSystem.SaveSettings(); }
            Debug.Log($"[AA] anti-aliasing -> {m}");
            Changed?.Invoke(m);
        }

        public static UniversalRenderPipelineAsset Asset =>
            (QualitySettings.renderPipeline ? QualitySettings.renderPipeline : GraphicsSettings.defaultRenderPipeline) as UniversalRenderPipelineAsset;

        static bool _hooked;

        /// <summary>
        /// Cameras made after <see cref="Apply"/> (the world scene's camera, created after Bootstrap) must get the same
        /// settings: with the pipeline on STP and a camera still on "no AA", URP 17 runs STP without a history and
        /// throws every frame (black screen). So every base camera is checked just before it renders.
        /// </summary>
        static void Hook()
        {
            if (_hooked) return;
            _hooked = true;
            RenderPipelineManager.beginCameraRendering += (_, cam) =>
            {
                if (!cam || cam.cameraType != CameraType.Game) return;
                var data = cam.GetUniversalAdditionalCameraData();
                if (!data || data.renderType != CameraRenderType.Base) return;
                var want = Mode == AAMode.Off ? AntialiasingMode.None : AntialiasingMode.TemporalAntiAliasing;
                if (Zfw.Upscaling.UpscalerHost.IsUpscaling) return;           // the upscaler owns the camera then
                if (data.antialiasing != want || cam.allowMSAA || cam.allowDynamicResolution || !data.renderPostProcessing) ApplyTo(cam);
            };
        }

        public static void Apply()
        {
            Hook();
            var asset = Asset;
            if (Zfw.Upscaling.UpscalerHost.IsUpscaling) { Zfw.Upscaling.UpscalerHost.Reapply(); ResetHistory(); return; }   // spatial backends read Mode via GameWantsTaa
            if (asset)
            {
                asset.upscalingFilter = Mode == AAMode.STP ? UpscalingFilterSelection.STP : UpscalingFilterSelection.Auto;
                asset.renderScale = 1f;                                       // native resolution
                asset.msaaSampleCount = 1;
            }
            foreach (var cam in Camera.allCameras) ApplyTo(cam);
            ResetHistory();
        }

        public static void ApplyTo(Camera cam)
        {
            if (!cam || cam.cameraType != CameraType.Game) return;
            var data = cam.GetUniversalAdditionalCameraData();
            if (!data || data.renderType != CameraRenderType.Base) return;
            cam.allowMSAA = false;
            cam.allowDynamicResolution = false;
            data.renderPostProcessing = true;
            data.antialiasing = Mode == AAMode.Off ? AntialiasingMode.None : AntialiasingMode.TemporalAntiAliasing;
            data.antialiasingQuality = AntialiasingQuality.High;
            ref var taa = ref data.taaSettings;
            taa.quality = TemporalAAQuality.High;
            taa.contrastAdaptiveSharpening = Mode == AAMode.TAA ? TaaSharpening : 0f;
        }

        /// <summary>IN-08: drop the temporal history (camera cut through the cabin door, teleport, mode change).</summary>
        public static void ResetHistory()
        {
            Zfw.Upscaling.UpscalerHost.ResetHistory();
            foreach (var cam in Camera.allCameras)
            {
                if (!cam || cam.cameraType != CameraType.Game) continue;
                var data = cam.GetUniversalAdditionalCameraData();
                if (data) data.resetHistory = true;
            }
        }

        public static void LoadSaved()
        {
            var s = Core.SaveSystem.Settings.antiAliasing;
            AAMode m = WildPartnerTier.Current.defaultAA;        // nothing picked yet: the tier's default
            if (!string.IsNullOrEmpty(s)) Enum.TryParse(s, out m);
            string arg = Array.Find(Environment.GetCommandLineArgs(), a => a.StartsWith("-aa=", StringComparison.Ordinal));
            if (arg != null && Enum.TryParse(arg.Substring(4), true, out AAMode forced)) m = forced;
            Set(m, save: false);
        }

        public static string Label(AAMode m) => m switch { AAMode.Off => "关", AAMode.TAA => "TAA", _ => "STP" };
    }
}
