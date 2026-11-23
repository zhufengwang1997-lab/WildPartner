using System.Collections.Generic;
using UnityEngine;

namespace WildPartner.Rendering
{
    /// <summary>
    /// IN-06: renderers that have no usable motion vectors (transparent particles: rain, the waterfall's mist and spray,
    /// the moon butterfly's stardust, ...). The upscaler's reactive-mask pass draws every live one of these into the
    /// mask so the temporal history is not trusted there. Registering also turns motion vectors off for the renderer.
    /// </summary>
    public static class ReactiveMaskSources
    {
        /// <summary>Rendering layer bit that marks reactive renderers (the mask pass draws a renderer list filtered by it,
        /// which also works for particle systems).</summary>
        public const int LayerBit = 7;
        public const uint LayerMask = 1u << LayerBit;
        static readonly List<Renderer> _all = new List<Renderer>();
        public static IReadOnlyList<Renderer> All => _all;

        public static void Register(Renderer r)
        {
            if (!r || _all.Contains(r)) return;
            r.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            r.renderingLayerMask |= LayerMask;
            _all.Add(r);
        }

        public static void Unregister(Renderer r) { if (r) _all.Remove(r); }

        /// <summary>Drop destroyed renderers (call before drawing the mask).</summary>
        public static void Prune() => _all.RemoveAll(r => !r);
    }
}
