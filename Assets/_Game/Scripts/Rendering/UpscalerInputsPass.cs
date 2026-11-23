using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace WildPartner.Rendering
{
    public enum InputView { None, MotionVectors, ReactiveMask }

    /// <summary>
    /// Upscaler input preparation that URP does not do by itself, plus the F3 views of the inputs:
    ///  - IN-06 reactive mask: every renderer in <see cref="ReactiveMaskSources"/> (marked with a rendering-layer bit) is
    ///    drawn into an R8 texture at render resolution, before post-processing. Published as the global
    ///    <c>_WP_ReactiveMask</c>. The built-in backends (STP / FSR1 / bilinear) do not read it ("已生成、当前后端未消费");
    ///    a native FSR2/3 or DLSS backend would take it from <see cref="Zfw.Upscaling.UpscalerInputs.reactiveMask"/>.
    ///  - IN-03 view: URP's motion vector texture, coloured, full screen (F3 → 运动向量).
    ///  - IN-06 view: the reactive mask, full screen (F3 → Reactive Mask).
    /// Injected per camera from beginCameraRendering (no renderer-asset edit needed).
    /// </summary>
    public static class UpscalerInputsPass
    {
        public static InputView View = InputView.None;
        /// <summary>Pixels covered by the mask last frame is not read back (no GPU sync); the smoke test checks the pass ran.</summary>
        public static int FramesWithMask { get; private set; }
        public static int LastSourceCount { get; private set; }

        static MaskPass _mask; static ViewPass _view; static bool _hooked;
        static Material _maskMat, _viewMat;

        public static void Init()
        {
            if (_hooked) return;
            var ms = Resources.Load<Shader>("Shaders/ReactiveMask");
            var vs = Resources.Load<Shader>("Shaders/UpscalerDebug");
            if (!ms || !vs) { Debug.LogWarning("[Upscaler] input shaders missing"); return; }
            _maskMat = CoreUtils.CreateEngineMaterial(ms);
            _viewMat = CoreUtils.CreateEngineMaterial(vs);
            _mask = new MaskPass { renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing };
            _view = new ViewPass { renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing };
            _hooked = true;
            RenderPipelineManager.beginCameraRendering += (_, cam) =>
            {
                if (!cam || cam.cameraType != CameraType.Game) return;
                var data = cam.GetUniversalAdditionalCameraData();
                if (!data || data.renderType != CameraRenderType.Base || data.scriptableRenderer == null) return;
                data.scriptableRenderer.EnqueuePass(_mask);
                if (View != InputView.None) { _view.ConfigureInput(View == InputView.MotionVectors ? ScriptableRenderPassInput.Motion : ScriptableRenderPassInput.None); data.scriptableRenderer.EnqueuePass(_view); }
            };
        }

        class MaskData : ContextItem { public TextureHandle mask; public override void Reset() { mask = TextureHandle.nullHandle; } }

        class MaskPass : ScriptableRenderPass
        {
            static readonly ShaderTagId[] Tags = { new ShaderTagId("UniversalForward"), new ShaderTagId("SRPDefaultUnlit"), new ShaderTagId("UniversalForwardOnly") };
            static readonly int MaskId = Shader.PropertyToID("_WP_ReactiveMask");
            class PassData { public RendererListHandle list; }

            public override void RecordRenderGraph(RenderGraph graph, ContextContainer frame)
            {
                var res = frame.Get<UniversalResourceData>();
                var cam = frame.Get<UniversalCameraData>();
                var rd = frame.Get<UniversalRenderingData>();
                var ld = frame.Get<UniversalLightData>();
                var desc = cam.cameraTargetDescriptor;                       // render resolution
                var td = new TextureDesc(desc.width, desc.height) { colorFormat = UnityEngine.Experimental.Rendering.GraphicsFormat.R8_UNorm, clearBuffer = true, clearColor = Color.clear, name = "_WP_ReactiveMask" };
                var mask = graph.CreateTexture(td);
                var drawing = RenderingUtils.CreateDrawingSettings(new List<ShaderTagId>(Tags), rd, cam, ld, SortingCriteria.CommonTransparent);
                drawing.overrideMaterial = _maskMat; drawing.overrideMaterialPassIndex = 0;
                var filter = new FilteringSettings(RenderQueueRange.all) { renderingLayerMask = ReactiveMaskSources.LayerMask };
                var list = graph.CreateRendererList(new RendererListParams(rd.cullResults, drawing, filter));
                using (var b = graph.AddRasterRenderPass<PassData>("WP Reactive Mask (IN-06)", out var pd))
                {
                    pd.list = list;
                    b.UseRendererList(list);
                    b.SetRenderAttachment(mask, 0, AccessFlags.Write);
                    b.SetGlobalTextureAfterPass(mask, MaskId);
                    b.AllowPassCulling(false);
                    b.SetRenderFunc((PassData d, RasterGraphContext ctx) => ctx.cmd.DrawRendererList(d.list));
                }
                frame.GetOrCreate<MaskData>().mask = mask;
                FramesWithMask++;
                LastSourceCount = ReactiveMaskSources.All.Count;
            }
        }

        class ViewPass : ScriptableRenderPass
        {
            static readonly int ScaleId = Shader.PropertyToID("_MvScale");
            class PassData { public TextureHandle src; public int pass; }

            public override void RecordRenderGraph(RenderGraph graph, ContextContainer frame)
            {
                var res = frame.Get<UniversalResourceData>();
                TextureHandle src = View == InputView.MotionVectors ? res.motionVectorColor
                                  : frame.Contains<MaskData>() ? frame.Get<MaskData>().mask : TextureHandle.nullHandle;
                if (!src.IsValid()) return;
                using (var b = graph.AddRasterRenderPass<PassData>("WP Upscaler Input View", out var pd))
                {
                    pd.src = src; pd.pass = View == InputView.MotionVectors ? 0 : 1;
                    b.UseTexture(src);
                    b.SetRenderAttachment(res.activeColorTexture, 0, AccessFlags.Write);
                    b.AllowPassCulling(false);
                    b.SetRenderFunc((PassData d, RasterGraphContext ctx) =>
                    {
                        _viewMat.SetFloat(ScaleId, 40f);
                        Blitter.BlitTexture(ctx.cmd, d.src, new Vector4(1, 1, 0, 0), _viewMat, d.pass);
                    });
                }
            }
        }
    }
}
