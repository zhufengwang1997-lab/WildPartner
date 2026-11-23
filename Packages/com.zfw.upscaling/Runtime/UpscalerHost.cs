using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Zfw.Upscaling
{
    /// <summary>
    /// The runtime side: holds the active backend, re-initialises it when the display size or quality changes
    /// (FR-UP-01 rule 2), pushes it into the URP asset and into every base camera just before it renders, and
    /// forwards ResetHistory (IN-08). The game calls <see cref="Use"/>, <see cref="ResetHistory"/> and reads the state;
    /// nothing else.
    /// </summary>
    public static class UpscalerHost
    {
        public static IUpscaler Active { get; private set; }
        public static UpscalerQuality Quality { get; private set; } = UpscalerQuality.NativeAA;
        public static Vector2Int DisplaySize { get; private set; }
        public static Vector2Int RenderSize { get; private set; }
        /// <summary>True when an upscaler drives the pipeline (any quality but NativeAA).</summary>
        public static bool IsUpscaling => Active != null && Quality != UpscalerQuality.NativeAA;
        /// <summary>The game's anti-aliasing wish for spatial backends (true = keep TAA on the camera).</summary>
        public static Func<bool> GameWantsTaa = () => true;
        /// <summary>Which URP asset to drive (the game supplies its tier's asset).</summary>
        public static Func<UniversalRenderPipelineAsset> Asset = () => GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
        public static event Action Changed;
        public static int Reinitialisations { get; private set; }

        static bool _hooked;

        /// <summary>Switch backend / quality. NativeAA hands the pipeline back to the game's own AA settings.</summary>
        public static void Use(IUpscaler upscaler, UpscalerQuality quality)
        {
            if (Active != null && Active != upscaler) Active.Dispose();
            Active = upscaler; Quality = quality;
            Hook();
            Reinit(CurrentDisplay());
            Changed?.Invoke();
        }

        public static void Disable() { Use(null, UpscalerQuality.NativeAA); }

        public static void ResetHistory() { Active?.ResetHistory(); }

        /// <summary>Re-push into the URP asset (after the game swapped the asset, e.g. a tier change).</summary>
        public static void Reapply()
        {
            if (!IsUpscaling) return;
            (Active as UrpBuiltinUpscaler)?.Configure(Asset());
            Active.ResetHistory();
        }

        static Vector2Int CurrentDisplay() => new Vector2Int(Screen.width, Screen.height);

        static void Reinit(Vector2Int display)
        {
            DisplaySize = display;
            if (!IsUpscaling) { RenderSize = display; return; }
            RenderSize = Active.Initialize(display, Quality);          // the one source of the render size
            (Active as UrpBuiltinUpscaler)?.Configure(Asset());
            Reinitialisations++;
            Debug.Log($"[Upscaling] {Active.Name} {Quality}: display {display.x}x{display.y} -> render {RenderSize.x}x{RenderSize.y}, mip bias {Active.GetMipBias():0.00}");
        }

        static void Hook()
        {
            if (_hooked) return;
            _hooked = true;
            RenderPipelineManager.beginContextRendering += (_, __) =>
            {
                if (IsUpscaling && CurrentDisplay() != DisplaySize) Reinit(CurrentDisplay());   // window resized (TC-03)
            };
            RenderPipelineManager.beginCameraRendering += (_, cam) =>
            {
                if (!IsUpscaling || !cam || cam.cameraType != CameraType.Game) return;
                var data = cam.GetUniversalAdditionalCameraData();
                if (!data || data.renderType != CameraRenderType.Base) return;
                (Active as UrpBuiltinUpscaler)?.ConfigureCamera(cam, data, GameWantsTaa());
            };
        }
    }
}
