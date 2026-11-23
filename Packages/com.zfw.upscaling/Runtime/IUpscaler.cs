using System;
using UnityEngine;
using UnityEngine.Rendering.RenderGraphModule;
using Zfw.GpuCaps;

namespace Zfw.Upscaling
{
    /// <summary>FR-UP-01. NativeAA = render at display resolution, the backend only anti-aliases.</summary>
    public enum UpscalerQuality { NativeAA, Quality, Balanced, Performance }

    /// <summary>
    /// FR-UP-01: one socket, many upscalers. The game only talks to this interface (and <see cref="UpscalerSelector"/>);
    /// it never references a vendor SDK (constraint C3).
    /// Rules: the size <see cref="Initialize"/> returns is the only source of every low-resolution target size; a display
    /// size or quality change re-initialises and resets history; <see cref="RecordPass"/> only declares render-graph
    /// reads/writes; <see cref="GetMipBias"/> defaults to log2(render width / display width) − 1.
    /// </summary>
    public interface IUpscaler : IDisposable
    {
        string Name { get; }
        bool IsSupported(GpuCapsReport caps);
        /// <returns>The render resolution.</returns>
        Vector2Int Initialize(Vector2Int displaySize, UpscalerQuality quality);
        /// <summary>Sub-pixel jitter for this frame, in pixels (zero for spatial upscalers).</summary>
        Vector2 GetJitter(int frameIndex);
        float GetMipBias();
        /// <summary>
        /// Declares the upscale pass. The URP built-in backends (RenderScale / FSR1 / STP) leave this empty: URP records
        /// their pass itself at the end of post-processing (see docs/调查-URP超分能力.md). Native backends record here.
        /// </summary>
        void RecordPass(RenderGraph graph, in UpscalerInputs inputs, TextureHandle output);
        void ResetHistory();
    }

    /// <summary>FR-UP-01 inputs, all at render resolution.</summary>
    public struct UpscalerInputs
    {
        public TextureHandle color;          // render resolution, HDR linear
        public TextureHandle depth;          // render resolution
        public TextureHandle motionVectors;  // render resolution, pixels or UV (the backend converts)
        public TextureHandle reactiveMask;   // may be null
        public TextureHandle exposure;       // 1x1, may be null (auto exposure)
        public Vector2 jitter;               // this frame's jitter
        public float deltaTimeMs;
        public bool resetHistory;
        public Matrix4x4 viewToClip;
    }
}
