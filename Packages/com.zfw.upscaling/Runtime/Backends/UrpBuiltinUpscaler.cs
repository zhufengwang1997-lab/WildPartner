using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;
using Zfw.GpuCaps;

namespace Zfw.Upscaling
{
    /// <summary>
    /// Base for the three backends that URP implements itself (bilinear, FSR1, STP). <see cref="Initialize"/> computes
    /// the render size and <see cref="Configure"/> writes it into the active URP asset (render scale + upscaling filter)
    /// and the camera (temporal or not, mip bias, history reset). URP then renders at exactly that size and records the
    /// upscale pass itself, so <see cref="RecordPass"/> is empty (the one documented deviation from FR-UP-01).
    /// </summary>
    public abstract class UrpBuiltinUpscaler : IUpscaler
    {
        public abstract string Name { get; }
        protected abstract UpscalingFilterSelection Filter { get; }
        /// <summary>True when the camera must run TAA (STP replaces it); spatial backends keep whatever the game chose.</summary>
        public abstract bool Temporal { get; }

        public Vector2Int DisplaySize { get; private set; }
        public Vector2Int RenderSize { get; private set; }
        public UpscalerQuality Quality { get; private set; }
        public float RenderScale => DisplaySize.x > 0 ? RenderSize.x / (float)DisplaySize.x : 1f;
        bool _reset;

        public virtual bool IsSupported(GpuCapsReport caps) => true;

        public virtual Vector2Int Initialize(Vector2Int displaySize, UpscalerQuality quality)
        {
            DisplaySize = displaySize; Quality = quality;
            RenderSize = UpscalerMath.RenderSize(displaySize, quality);
            ResetHistory();
            return RenderSize;
        }

        public virtual Vector2 GetJitter(int frameIndex) => Vector2.zero;

        public virtual float GetMipBias() => UpscalerMath.DefaultMipBias(RenderSize.x, DisplaySize.x);

        public virtual void RecordPass(RenderGraph graph, in UpscalerInputs inputs, TextureHandle output) { }

        public void ResetHistory() => _reset = true;

        /// <summary>Write this backend's state into the URP asset (once per change).</summary>
        public virtual void Configure(UniversalRenderPipelineAsset asset)
        {
            if (!asset) return;
            asset.renderScale = RenderScale;
            asset.upscalingFilter = Filter;
            asset.msaaSampleCount = 1;
        }

        /// <summary>Per camera, just before it renders.</summary>
        public virtual void ConfigureCamera(Camera cam, UniversalAdditionalCameraData data, bool gameWantsTaa)
        {
            cam.allowDynamicResolution = false;
            cam.allowMSAA = false;
            bool taa = Temporal || gameWantsTaa;
            if (taa) data.renderPostProcessing = true;
            data.antialiasing = taa ? AntialiasingMode.TemporalAntiAliasing : (data.antialiasing == AntialiasingMode.TemporalAntiAliasing ? AntialiasingMode.None : data.antialiasing);
            ref var t = ref data.taaSettings;
            t.mipBias = Mathf.Min(0f, GetMipBias());
            if (_reset) { data.resetHistory = true; _reset = false; }
        }

        public virtual void Dispose() { }
    }

    /// <summary>Render scale + bilinear. Always supported (FR-UP-03).</summary>
    public sealed class RenderScaleUpscaler : UrpBuiltinUpscaler
    {
        public override string Name => "RenderScale";
        protected override UpscalingFilterSelection Filter => UpscalingFilterSelection.Linear;
        public override bool Temporal => false;
    }

    /// <summary>
    /// FSR: the fallback path the spec allows — URP's built-in FSR 1 (EASU upscale + RCAS sharpen, spatial). FSR 2/3
    /// as a native plugin is design only (docs/设计方案).
    /// </summary>
    public sealed class FsrUpscaler : UrpBuiltinUpscaler
    {
        public override string Name => "FSR1";
        protected override UpscalingFilterSelection Filter => UpscalingFilterSelection.FSR;
        public override bool Temporal => false;
        /// <summary>RCAS sharpness 0..1.</summary>
        public float sharpness = 0.8f;
        public override void Configure(UniversalRenderPipelineAsset asset)
        {
            base.Configure(asset);
            if (!asset) return;
            asset.fsrOverrideSharpness = true;
            asset.fsrSharpness = sharpness;
        }
    }

    /// <summary>
    /// STP (Unity's spatial-temporal upscaler, URP built-in). Not in the spec: added so jitter, motion vectors and
    /// history reset run end-to-end on the machines we have, and as the NFR-05 proof that a backend can be added
    /// without touching game code. Needs compute shaders.
    /// </summary>
    public sealed class StpUpscaler : UrpBuiltinUpscaler
    {
        public override string Name => "STP";
        protected override UpscalingFilterSelection Filter => UpscalingFilterSelection.STP;
        public override bool Temporal => true;
        public override bool IsSupported(GpuCapsReport caps) => STP.IsSupported();
        /// <summary>The jitter URP applies for STP (its own 16-phase sequence, in pixels).</summary>
        public override Vector2 GetJitter(int frameIndex) => STP.Jit16(frameIndex);
    }
}
