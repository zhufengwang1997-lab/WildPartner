using UnityEngine;

namespace Zfw.Upscaling
{
    /// <summary>Scale factors, render size, mip bias and the IN-04 Halton(2,3) jitter.</summary>
    public static class UpscalerMath
    {
        /// <summary>Per-axis render scale for each quality (the usual 1.5× / 1.7× / 2× ratios).</summary>
        public static float Scale(UpscalerQuality q) => q switch
        {
            UpscalerQuality.Quality => 1f / 1.5f,
            UpscalerQuality.Balanced => 1f / 1.7f,
            UpscalerQuality.Performance => 0.5f,
            _ => 1f,
        };

        public static Vector2Int RenderSize(Vector2Int display, UpscalerQuality q)
        {
            float s = Scale(q);
            return new Vector2Int(Mathf.Max(1, Mathf.RoundToInt(display.x * s)), Mathf.Max(1, Mathf.RoundToInt(display.y * s)));
        }

        /// <summary>FR-UP-01: log2(render width / display width) − 1.</summary>
        public static float DefaultMipBias(int renderWidth, int displayWidth) =>
            Mathf.Log(Mathf.Max(1, renderWidth) / (float)Mathf.Max(1, displayWidth), 2f) - 1f;

        /// <summary>Radical inverse in base b (Halton), index ≥ 1.</summary>
        public static float Halton(int index, int b)
        {
            float f = 1f, r = 0f;
            while (index > 0) { f /= b; r += f * (index % b); index /= b; }
            return r;
        }

        /// <summary>IN-04: phase count = round(8 × (display width / render width)²).</summary>
        public static int PhaseCount(int renderWidth, int displayWidth)
        {
            float k = displayWidth / (float)Mathf.Max(1, renderWidth);
            return Mathf.Max(1, Mathf.RoundToInt(8f * k * k));
        }

        /// <summary>IN-04 jitter in pixels, centred on 0 (−0.5..0.5).</summary>
        public static Vector2 HaltonJitter(int frameIndex, int phases)
        {
            int i = (frameIndex % Mathf.Max(1, phases)) + 1;
            return new Vector2(Halton(i, 2) - 0.5f, Halton(i, 3) - 0.5f);
        }
    }
}
