using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using WildPartner.Core;
using WildPartner.Creatures;
using WildPartner.Player;
using WildPartner.World;
using Zfw.Upscaling;

namespace WildPartner.Rendering
{
    /// <summary>
    /// UpscaleBench (FR-VB-01..04): six fixed scenes x every configuration, same frames every time.
    ///   WildPartner -bench -capture=&lt;dir&gt; [-bench-scene=SC-02] [-bench-configs=ref,native_stp,STP_Quality,...]
    /// For each scene and configuration: replay the scene with a fixed time step (60 fps game time, fixed random seed),
    /// save frame 60 as PNG; then GPU frame time (FrameTimingManager, v-sync off), the upscale pass's own GPU time from
    /// URP's profiling samplers (upscale_ms), graphics memory (gfx_mem_mb); for the still scenes also the temporal
    /// stability (mean absolute luminance difference of 60 consecutive frames). Writes bench.csv + PNGs; the Python
    /// tools compute FLIP / SSIM / PSNR against the "ref" configuration (2x supersampled: render scale 2, downsampled).
    /// UI is hidden while capturing.
    /// </summary>
    public class UpscaleBench : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (!Environment.GetCommandLineArgs().Contains("-bench")) return;
            new GameObject("UpscaleBench").AddComponent<UpscaleBench>();
        }

        public struct Config { public string id, backend; public UpscalerQuality quality; public AAMode aa; public float refScale; public bool noMotion; public string onlyScene; }

        public static List<Config> AllConfigs()
        {
            var l = new List<Config>
            {
                new Config { id = "ref", backend = UpscalerSettings.Off, quality = UpscalerQuality.NativeAA, aa = AAMode.Off, refScale = 2f },
                // same settings as "ref", captured again: FLIP(ref2, ref) = the noise floor (whatever the bench cannot hold still)
                new Config { id = "ref2", backend = UpscalerSettings.Off, quality = UpscalerQuality.NativeAA, aa = AAMode.Off, refScale = 2f },
                new Config { id = "native_off", backend = UpscalerSettings.Off, quality = UpscalerQuality.NativeAA, aa = AAMode.Off },
                new Config { id = "native_taa", backend = UpscalerSettings.Off, quality = UpscalerQuality.NativeAA, aa = AAMode.TAA },
                new Config { id = "native_stp", backend = UpscalerSettings.Off, quality = UpscalerQuality.NativeAA, aa = AAMode.STP },
            };
            foreach (var b in UpscalerSelector.Available(Zfw.GpuCaps.GpuCaps.Current))
                foreach (var q in new[] { UpscalerQuality.Quality, UpscalerQuality.Balanced, UpscalerQuality.Performance })
                    l.Add(new Config { id = $"{b}_{q}", backend = b, quality = q, aa = AAMode.TAA });   // spatial ones keep TAA (IN-01 note)
            // TC-05 reverse check: STP without object motion vectors on the running scene -> expect ghosting
            if (l.Any(c => c.backend == "STP"))
                l.Add(new Config { id = "STP_Quality_noMV", backend = "STP", quality = UpscalerQuality.Quality, aa = AAMode.TAA, noMotion = true, onlyScene = "SC-01" });
            return l;
        }

        public static readonly string[] Scenes = { "SC-01", "SC-02", "SC-03", "SC-04", "SC-05", "SC-06" };
        const int CaptureFrame = 60, StabilityFrames = 60, Seed = 1123;
        string _out;
        readonly StringBuilder _csv = new StringBuilder();
        ProfilerRecorder _gfxMem;
        static readonly string[] UpscaleSamplers = { "StpSetup", "StpPreTaa", "StpTaa", "RG_FinalSetup", "RG_FinalFSRScale", "RG_FinalBlit" };

        /// <summary>GPU ms of URP's upscale passes in the last frame; NaN where the build has no sampler GPU timings
        /// (release players report 0). Shared with PerfRunner's upscale_ms column.</summary>
        public static float UpscaleMs()
        {
            float sum = 0; bool any = false;
            foreach (var n in UpscaleSamplers)
            {
                var r = UnityEngine.Profiling.Recorder.Get(n);
                if (!r.isValid) continue;
                if (!r.enabled) r.enabled = true;
                sum += r.gpuElapsedNanoseconds / 1e6f; any = true;
            }
            return any && sum > 0 ? sum : float.NaN;
        }

        static string Arg(string key)
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i].StartsWith(key + "=", StringComparison.OrdinalIgnoreCase)) return args[i].Substring(key.Length + 1);
                if (args[i] == key && i + 1 < args.Length) return args[i + 1];
            }
            return null;
        }

        IEnumerator Start()
        {
            _out = Arg("-capture") ?? Path.Combine(Application.persistentDataPath, "bench");
            Directory.CreateDirectory(_out);
            _gfxMem = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "Gfx Used Memory");
            foreach (var n in UpscaleSamplers) { var r = UnityEngine.Profiling.Recorder.Get(n); if (r.isValid) r.enabled = true; }
            yield return new WaitForSeconds(1.5f);
            GameFlow.Instance.NewGame();
            float t = 0; yield return null;
            while (CabinInterior.Busy && t < 10f) { t += Time.deltaTime; yield return null; }
            CabinInterior.SetInside(false);
            foreach (var c in FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None)) c.enabled = false;
            var ov = FindFirstObjectByType<DebugOverlay>(); if (ov) ov.enabled = false;

            var scenes = Arg("-bench-scene") is string one ? new[] { one } : Scenes;
            var configs = AllConfigs();
            if (Arg("-bench-configs") is string only) { var set = only.Split(','); configs = configs.Where(c => set.Contains(c.id)).ToList(); }
            _csv.AppendLine($"# gpu={SystemInfo.graphicsDeviceName};api={SystemInfo.graphicsDeviceType};display={Screen.width}x{Screen.height};tier={Zfw.QualityTiers.QualityTiers.Current};unity={Application.unityVersion};date={DateTime.Now:yyyy-MM-dd HH:mm}");
            _csv.AppendLine("scene,config,backend,quality,aa,render_w,render_h,display_w,display_h,gpu_ms,cpu_ms,frame_ms,upscale_ms,gfx_mem_mb,temporal_diff,image");
            Debug.Log($"[Bench] {scenes.Length} scenes x {configs.Count} configs -> {_out}");

            foreach (var sc in scenes)
                foreach (var cfg in configs)
                {
                    if (cfg.onlyScene != null && cfg.onlyScene != sc) continue;
                    yield return Run(sc, cfg);
                    File.WriteAllText(Path.Combine(_out, "bench.csv"), _csv.ToString());
                }
            Time.captureFramerate = 0;
            UpscalerSettings.Set(SaveSystem.Settings.upscaler, SaveSystem.Settings.upscalerQuality);
            Debug.Log($"[Bench] done -> {_out}");
            yield return new WaitForSeconds(0.3f);
            Application.Quit();
        }

        static readonly List<(Renderer r, MotionVectorGenerationMode m)> _mvSaved = new List<(Renderer, MotionVectorGenerationMode)>();

        /// <summary>TC-05: object motion vectors off for every renderer (camera motion still there).</summary>
        static void SetObjectMotion(bool off)
        {
            if (off && _mvSaved.Count == 0)
                foreach (var r in FindObjectsByType<Renderer>(FindObjectsSortMode.None))
                    if (r.motionVectorGenerationMode != MotionVectorGenerationMode.ForceNoMotion) { _mvSaved.Add((r, r.motionVectorGenerationMode)); r.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion; }
            if (!off) { foreach (var (r, m) in _mvSaved) if (r) r.motionVectorGenerationMode = m; _mvSaved.Clear(); }
        }

        static void ApplyConfig(Config c)
        {
            SetObjectMotion(c.noMotion);
            AntiAliasing.Set(c.aa, save: false);
            var host = c.backend == UpscalerSettings.Off ? null : UpscalerSelector.Create(c.backend);
            if (host == null) UpscalerHost.Disable(); else UpscalerHost.Use(host, c.quality);
            if (c.refScale > 1f) { var a = AntiAliasing.Asset; a.renderScale = c.refScale; a.upscalingFilter = UpscalingFilterSelection.Linear; }
            AntiAliasing.ResetHistory();
        }

        IEnumerator Run(string scene, Config cfg)
        {
            ApplyConfig(cfg);
            Time.captureFramerate = 60;
            UnityEngine.Random.InitState(Seed);
            var player = PlayerController.Instance; var env = EnvController.Instance;
            var s = new SceneDriver(scene, player, env);
            s.Setup();
            if (env) env.ResetWindClock();   // grass pose must match across configs, else FLIP measures wind phase
            AntiAliasing.ResetHistory();
            string img = $"{scene}_{cfg.id}.png";
            for (int f = 1; f <= CaptureFrame; f++)
            {
                s.Frame(f);
                if (f == CaptureFrame) { yield return new WaitForEndOfFrame(); ScreenCapture.CaptureScreenshot(Path.Combine(_out, img)); }
                yield return null;
            }
            GameInput.SimMove = Vector2.zero; GameInput.SimRun = false;

            // temporal stability on the still scenes: 60 consecutive frames, nothing moves but wind / animation
            float temporal = -1f;
            if (s.Still)
            {
                float sum = 0; float[] prev = null;
                for (int f = 0; f < StabilityFrames; f++)
                {
                    yield return new WaitForEndOfFrame();
                    var tex = ScreenCapture.CaptureScreenshotAsTexture();
                    var lum = Luma(tex); Destroy(tex);
                    if (prev != null) { double d = 0; for (int i = 0; i < lum.Length; i++) d += Math.Abs(lum[i] - prev[i]); sum += (float)(d / lum.Length); }
                    prev = lum;
                    yield return null;
                }
                temporal = sum / (StabilityFrames - 1);
            }
            Time.captureFramerate = 0;

            // performance at the scene's end pose
            var res = default(GpuTimer.Result);
            yield return GpuTimer.Measure(1.5f, r => res = r, 0.4f);
            float up = 0; foreach (var n in UpscaleSamplers) { var r = UnityEngine.Profiling.Recorder.Get(n); if (r.isValid) up += r.gpuElapsedNanoseconds / 1e6f; }
            float mem = _gfxMem.Valid ? _gfxMem.LastValue / (1024f * 1024f) : -1f;
            var asset = AntiAliasing.Asset;
            int rw = Mathf.RoundToInt(Screen.width * asset.renderScale), rh = Mathf.RoundToInt(Screen.height * asset.renderScale);
            _csv.AppendLine($"{scene},{cfg.id},{cfg.backend},{cfg.quality},{cfg.aa},{rw},{rh},{Screen.width},{Screen.height},{res.gpuMs:0.000},{res.cpuMs:0.000},{res.frameMs:0.000},{up:0.000},{mem:0.0},{(temporal < 0 ? "" : temporal.ToString("0.00000"))},{img}");
            Debug.Log($"[Bench] {scene} {cfg.id}: {rw}x{rh} {res} upscale {up:0.000} ms mem {mem:0} MB temporal {temporal:0.0000}");
        }

        /// <summary>Luminance on a 1/4 grid (enough for a flicker number, cheap to read back).</summary>
        static float[] Luma(Texture2D t)
        {
            var px = t.GetPixels32();
            int w = t.width, h = t.height, sw = w / 4, sh = h / 4;
            var l = new float[sw * sh];
            for (int y = 0; y < sh; y++)
                for (int x = 0; x < sw; x++)
                {
                    var c = px[(y * 4) * w + x * 4];
                    l[y * sw + x] = (0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b) / 255f;
                }
            return l;
        }

        /// <summary>The six fixed scenes (FR-VB-01). Positions, times and paths are constants.</summary>
        class SceneDriver
        {
            readonly string _id; readonly PlayerController _p; readonly EnvController _env;
            public bool Still => _id == "SC-02" || _id == "SC-06";
            Vector3 _a, _b; float _yaw;
            public SceneDriver(string id, PlayerController p, EnvController env) { _id = id; _p = p; _env = env; }

            Vector3 Ground(Vector2 xz) => new Vector3(xz.x, Creature.GroundHeight(xz.x, xz.y) + 0.2f, xz.y);

            public void Setup()
            {
                Weather.Instance.Set(false, true);
                switch (_id)
                {
                    case "SC-01":   // running witch + cat cross the frame, bright sky behind (camera fixed, low)
                        _env.timeOfDay = 15f; _env.Apply();
                        _a = Ground(WorldLayout.StoneCircle + new Vector2(10f, -14f)); _yaw = 0f;
                        _p.Teleport(_a, 90f); _p.SetCameraOrbit(0f, -4f); _p.SetCameraDistance(1.6f);
                        break;
                    case "SC-02":   // meadow, camera still, grass in the wind
                        _env.timeOfDay = 11f; _env.Apply();
                        _p.Teleport(Ground(new Vector2(400, 470) * WorldLayout.K), 300f); _p.SetCameraOrbit(300f, 10f); _p.SetCameraDistance(1f);
                        break;
                    case "SC-03":   // cat close-up, slow orbit (fur / whiskers / small geometry)
                        _env.timeOfDay = 16.5f; _env.Apply();
                        _a = Ground(WorldLayout.StoneCircle + new Vector2(14f, -8f)); _yaw = 40f;
                        _p.Teleport(_a, _yaw); _p.SetCameraOrbit(_yaw, 6f); _p.SetCameraDistance(0.45f);
                        break;
                    case "SC-04":   // rain over the lake by the bridge
                        _env.timeOfDay = 14f; _env.Apply(); Weather.Instance.Set(true, true);
                        _p.Teleport(Ground(WorldLayout.Bridge + new Vector2(6f, 8f)), 200f); _p.SetCameraOrbit(200f, 8f); _p.SetCameraDistance(1f);
                        break;
                    case "SC-05":   // camera cut half way (history reset)
                        _env.timeOfDay = 13f; _env.Apply();
                        _a = Ground(new Vector2(70f, 150f)); _b = Ground(new Vector2(400, 470) * WorldLayout.K);
                        _p.Teleport(_a, 20f); _p.SetCameraOrbit(20f, 8f); _p.SetCameraDistance(1f);
                        break;
                    case "SC-06":   // far mountains, wide, still (from the watchtower hill)
                        _env.timeOfDay = 10f; _env.Apply();
                        _p.Teleport(Ground(WorldLayout.Watchtower + new Vector2(6f, 6f)), 250f); _p.SetCameraOrbit(250f, 2f); _p.SetCameraDistance(1.4f);
                        break;
                }
            }

            public void Frame(int f)
            {
                switch (_id)
                {
                    case "SC-01": GameInput.SimRun = true; GameInput.SimMove = new Vector2(f < 8 ? 0 : 1, 0); break;
                    case "SC-03": _p.SetCameraOrbit(_yaw + f * 0.5f, 6f); break;
                    case "SC-05": if (f == 40) { _p.Teleport(_b, 300f); _p.SetCameraOrbit(300f, 10f); } break;
                }
            }
        }
    }
}
