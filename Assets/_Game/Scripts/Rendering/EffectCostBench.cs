using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using WildPartner.Core;
using WildPartner.Player;
using WildPartner.World;

namespace WildPartner.Rendering
{
    /// <summary>
    /// 特效开销清单: what each effect costs on the GPU, measured, not guessed.
    ///   WildPartner -fxcost -demoout=&lt;dir&gt; [-tier=High]
    /// Four fixed views (meadow by day, deep forest, lake shore at night, inside the cabin). In each: a baseline, then
    /// one effect switched off at a time (and back on), GPU frame time from FrameTimingManager with v-sync off
    /// (<see cref="GpuTimer"/>). Cost = baseline − without. Writes fxcost.csv; tools/fxcost_report.py turns it into
    /// docs/特效开销清单.md. Numbers are for the machine it ran on (the GPU name is in the file).
    /// </summary>
    public class EffectCostBench : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (!Environment.GetCommandLineArgs().Contains("-fxcost")) return;
            new GameObject("EffectCostBench").AddComponent<EffectCostBench>();
        }

        string _out;
        readonly StringBuilder _csv = new StringBuilder();

        struct Toggle { public string name; public Func<Action> off; }   // off() switches it off and returns the undo

        IEnumerator Start()
        {
            string arg = Environment.GetCommandLineArgs().FirstOrDefault(a => a.StartsWith("-demoout="));
            _out = arg != null ? arg.Substring(9) : Path.Combine(Application.persistentDataPath, "fxcost");
            Directory.CreateDirectory(_out);
            yield return new WaitForSeconds(1.5f);
            GameFlow.Instance.NewGame();
            float t = 0; yield return null;
            while (CabinInterior.Busy && t < 10f) { t += Time.deltaTime; yield return null; }
            var env = EnvController.Instance; var player = PlayerController.Instance;
            Weather.Instance.Set(false, true);
            _csv.AppendLine($"# gpu={SystemInfo.graphicsDeviceName};api={SystemInfo.graphicsDeviceType};res={Screen.width}x{Screen.height};tier={Zfw.QualityTiers.QualityTiers.Current};aa={AntiAliasing.Mode}");
            _csv.AppendLine("view,effect,baseline_gpu_ms,without_gpu_ms,cost_ms,baseline_frame_ms,without_frame_ms,gpu_valid");

            var toggles = Toggles(env);
            var views = new (string name, Vector2 at, float yaw, float hour, bool inside)[]
            {
                ("草地白天", new Vector2(400, 470) * WorldLayout.K, 300f, 11f, false),
                ("深林", new Vector2(70f, 150f), 20f, 16f, false),
                ("湖岸夜晚", WorldLayout.Bridge + new Vector2(6f, 8f), 200f, 22f, false),
                ("木屋室内", Vector2.zero, 0f, 20f, true),
            };
            foreach (var v in views)
            {
                env.timeOfDay = v.hour; env.Apply();
                if (v.inside)
                {
                    var ci = CabinInterior.Instance;
                    CabinInterior.SetInside(true);
                    var toDoor = ci.insideDoor - ci.roomCenter; toDoor.y = 0;
                    player.Teleport(ci.roomCenter - toDoor.normalized * 0.8f, Quaternion.LookRotation(-toDoor).eulerAngles.y);
                }
                else
                {
                    CabinInterior.SetInside(false);
                    player.Teleport(new Vector3(v.at.x, Creatures.Creature.GroundHeight(v.at.x, v.at.y) + 0.2f, v.at.y), v.yaw);
                    player.SetCameraOrbit(v.yaw, 8f);
                }
                yield return new WaitForSeconds(2f);
                ScreenCapture.CaptureScreenshot(Path.Combine(_out, $"view_{v.name}.png"));
                foreach (var tg in toggles)
                {
                    var b = default(GpuTimer.Result); var w = default(GpuTimer.Result);
                    yield return GpuTimer.Measure(1.5f, r => b = r, 0.5f);
                    var undo = tg.off();
                    yield return GpuTimer.Measure(1.5f, r => w = r, 0.5f);
                    undo?.Invoke();
                    float cost = b.gpuValid && w.gpuValid ? b.gpuMs - w.gpuMs : b.frameMs - w.frameMs;
                    _csv.AppendLine($"{v.name},{tg.name},{b.gpuMs:0.000},{w.gpuMs:0.000},{cost:0.000},{b.frameMs:0.000},{w.frameMs:0.000},{(b.gpuValid && w.gpuValid ? 1 : 0)}");
                    Debug.Log($"[FxCost] {v.name} / {tg.name}: {b} -> {w} = {cost:0.00} ms");
                }
                // the anti-aliasing modes as well (same view)
                var saved = AntiAliasing.Mode;
                var per = new Dictionary<AAMode, GpuTimer.Result>();
                foreach (AAMode m in Enum.GetValues(typeof(AAMode)))
                {
                    if (!AntiAliasing.Supported(m)) continue;
                    AntiAliasing.Set(m, save: false);
                    var r0 = default(GpuTimer.Result);
                    yield return GpuTimer.Measure(1.5f, r => r0 = r, 0.6f);
                    per[m] = r0;
                }
                AntiAliasing.Set(saved, save: false);
                if (per.TryGetValue(AAMode.Off, out var off))
                    foreach (var kv in per.Where(k => k.Key != AAMode.Off))
                        _csv.AppendLine($"{v.name},抗锯齿 {AntiAliasing.Label(kv.Key)}（相对关）,{kv.Value.gpuMs:0.000},{off.gpuMs:0.000},{kv.Value.gpuMs - off.gpuMs:0.000},{kv.Value.frameMs:0.000},{off.frameMs:0.000},{(kv.Value.gpuValid && off.gpuValid ? 1 : 0)}");
                File.WriteAllText(Path.Combine(_out, "fxcost.csv"), _csv.ToString());
            }
            CabinInterior.SetInside(false);
            File.WriteAllText(Path.Combine(_out, "fxcost.csv"), _csv.ToString());
            Debug.Log($"[FxCost] done -> {_out}");
            yield return new WaitForSeconds(0.3f);
            Application.Quit();
        }

        static List<Toggle> Toggles(EnvController env)
        {
            var vol = FindFirstObjectByType<Bootstrap>()?.volume;
            var list = new List<Toggle>
            {
                new Toggle { name = "草地（GPU 草）", off = () => { var g = GrassRenderer.Instance; if (!g) return null; g.enabled = false; return () => g.enabled = true; } },
                new Toggle { name = "SSAO 环境光遮蔽", off = () => Feature("AmbientOcclusion") },
                new Toggle { name = "高度雾", off = () => Feature("HeightFog") },
                new Toggle { name = "泛光 Bloom", off = () => VolumeComp<Bloom>(vol) },
                new Toggle { name = "主光阴影", off = () =>
                    {
                        var s = env ? env.sun : null; if (!s) return null;
                        var was = s.shadows; s.shadows = LightShadows.None; return () => s.shadows = was;
                    } },
                new Toggle { name = "点光源（全部附加光）", off = () =>
                    {
                        var ls = FindObjectsByType<Light>(FindObjectsSortMode.None).Where(l => l.enabled && l.type != LightType.Directional).ToArray();
                        foreach (var l in ls) l.enabled = false; return () => { foreach (var l in ls) if (l) l.enabled = true; };
                    } },
                new Toggle { name = "粒子（全部粒子系统）", off = () =>
                    {
                        var rs = FindObjectsByType<ParticleSystemRenderer>(FindObjectsSortMode.None).Where(r => r.enabled).ToArray();
                        foreach (var r in rs) r.enabled = false; return () => { foreach (var r in rs) if (r) r.enabled = true; };
                    } },
                new Toggle { name = "水面（含反射）", off = () =>
                    {
                        var rs = FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None)
                            .Where(r => r.enabled && r.sharedMaterial && r.sharedMaterial.shader && r.sharedMaterial.shader.name.Contains("Water")).ToArray();
                        foreach (var r in rs) r.enabled = false; return () => { foreach (var r in rs) if (r) r.enabled = true; };
                    } },
            };
            return list;
        }

        static Action Feature(string nameContains)
        {
            var f = WildPartnerTier.Features().FirstOrDefault(x => x && x.name.Contains(nameContains) && x.isActive);
            if (!f) return null;
            f.SetActive(false); return () => f.SetActive(true);
        }

        static Action VolumeComp<T>(Volume vol) where T : VolumeComponent
        {
            if (!vol || !vol.profile || !vol.profile.TryGet(out T c) || !c.active) return null;
            c.active = false; return () => c.active = true;
        }
    }
}
