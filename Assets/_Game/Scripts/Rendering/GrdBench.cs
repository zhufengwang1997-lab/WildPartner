using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Unity.Profiling;
using UnityEngine;
using WildPartner.Core;
using WildPartner.Player;
using WildPartner.World;

namespace WildPartner.Rendering
{
    /// <summary>
    /// FR-RD-06 / NFR-03: GPU Resident Drawer on vs off on the same route.
    ///   WildPartner -grdbench -demoout=&lt;dir&gt; -grd=on|off [-occlusion=on|off]
    /// One process per setting (GRD is set up before the world loads), tools/grd.sh runs both and writes
    /// docs/GRD评估报告.md. Per view: 3 s of Draw Calls / Batches / SetPass Calls (ProfilerRecorder), main and render
    /// thread time and frame time (FrameTimingManager, v-sync off). Load time = startup until the world is ready.
    /// </summary>
    public class GrdBench : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (!Environment.GetCommandLineArgs().Contains("-grdbench")) return;
            new GameObject("GrdBench").AddComponent<GrdBench>();
        }

        IEnumerator Start()
        {
            string arg = Environment.GetCommandLineArgs().FirstOrDefault(a => a.StartsWith("-demoout="));
            string outDir = arg != null ? arg.Substring(9) : Path.Combine(Application.persistentDataPath, "grd");
            Directory.CreateDirectory(outDir);
            yield return new WaitForSeconds(1.5f);
            GameFlow.Instance.NewGame();
            float t = 0; yield return null;
            while (CabinInterior.Busy && t < 10f) { t += Time.deltaTime; yield return null; }
            float loadS = Time.realtimeSinceStartup - 1.5f;   // world generation + scene setup (the fixed 1.5 s wait excluded)
            var env = EnvController.Instance; var player = PlayerController.Instance;
            Weather.Instance.Set(false, true);
            QualitySettings.vSyncCount = 0; Application.targetFrameRate = -1;

            var draws = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");
            var batches = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Batches Count");
            var setpass = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count");
            string grd = GrdSettings.IsEnabled() ? "on" : "off";
            string file = Path.Combine(outDir, $"grd_{grd}.csv");
            var csv = new StringBuilder();
            csv.AppendLine($"# gpu={SystemInfo.graphicsDeviceName};api={SystemInfo.graphicsDeviceType};res={Screen.width}x{Screen.height};tier={Zfw.QualityTiers.QualityTiers.Current};grd={grd};grd_requested={(GrdSettings.Grd ? "on" : "off")};occlusion={(GrdSettings.OcclusionActive ? "on" : "off")};load_s={loadS:0.000}");
            csv.AppendLine("view,frames,draw_calls,batches,setpass_calls,main_ms,render_ms,frame_ms,gpu_ms");

            var views = new (string name, Vector2 at, float yaw, float hour, bool inside)[]
            {
                ("草地白天", new Vector2(400, 470) * WorldLayout.K, 300f, 11f, false),
                ("深林", new Vector2(70f, 150f), 20f, 16f, false),
                ("湖岸夜晚", WorldLayout.Bridge + new Vector2(6f, 8f), 200f, 22f, false),
                ("木屋室内", Vector2.zero, 0f, 20f, true),
            };
            if (Environment.GetCommandLineArgs().Contains("-grddiag"))
            {
                // diagnostic: one cube per shader in front of the camera, to see which shaders draw under GRD
                var cam = Camera.main.transform;
                string[] shaders = { "Universal Render Pipeline/Lit", "Universal Render Pipeline/Simple Lit", "WildPartner/Toon" };
                for (int i = 0; i < shaders.Length; i++)
                {
                    var sh = Shader.Find(shaders[i]);
                    var c = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    Destroy(c.GetComponent<Collider>());
                    c.transform.position = cam.position + cam.forward * 4f + cam.right * (i - 1) * 1.6f;
                    if (sh) c.GetComponent<MeshRenderer>().sharedMaterial = new Material(sh) { color = Color.red };
                    Debug.Log($"[GrdDiag] cube {i} shader {shaders[i]} found={(sh != null)}");
                }
                yield return new WaitForSeconds(1f);
                ScreenCapture.CaptureScreenshot(Path.Combine(outDir, $"diag_grd_{grd}.png"));
                yield return new WaitForSeconds(0.5f);
                Application.Quit(); yield break;
            }
            var ft = new FrameTiming[1];
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
                ScreenCapture.CaptureScreenshot(Path.Combine(outDir, $"view_{v.name}_grd_{grd}.png"));
                yield return null;
                var dc = new List<float>(); var bt = new List<float>(); var sp = new List<float>();
                var main = new List<float>(); var rend = new List<float>(); var frame = new List<float>(); var gpu = new List<float>();
                ulong last = 0;
                for (float s = 0; s < 3f; s += Time.unscaledDeltaTime)
                {
                    dc.Add(draws.LastValue); bt.Add(batches.LastValue); sp.Add(setpass.LastValue);
                    frame.Add(Time.unscaledDeltaTime * 1000f);
                    FrameTimingManager.CaptureFrameTimings();
                    if (FrameTimingManager.GetLatestTimings(1, ft) > 0 && ft[0].frameStartTimestamp > last)
                    {
                        last = ft[0].frameStartTimestamp;
                        main.Add((float)ft[0].cpuMainThreadFrameTime); rend.Add((float)ft[0].cpuRenderThreadFrameTime);
                        if (ft[0].gpuFrameTime > 0) gpu.Add((float)ft[0].gpuFrameTime);
                    }
                    yield return null;
                }
                csv.AppendLine($"{v.name},{frame.Count},{Med(dc):0},{Med(bt):0},{Med(sp):0},{Med(main):0.000},{Med(rend):0.000},{Med(frame):0.000},{Med(gpu):0.000}");
                Debug.Log($"[GrdBench] {v.name} grd={grd}: draws {Med(dc):0} batches {Med(bt):0} setpass {Med(sp):0} main {Med(main):0.00} render {Med(rend):0.00} frame {Med(frame):0.00} ms");
                File.WriteAllText(file, csv.ToString());
            }
            CabinInterior.SetInside(false);
            draws.Dispose(); batches.Dispose(); setpass.Dispose();
            File.WriteAllText(file, csv.ToString());
            Debug.Log($"[GrdBench] done -> {file}");
            yield return new WaitForSeconds(0.3f);
            Application.Quit();
        }

        static float Med(List<float> l)
        {
            if (l.Count == 0) return -1f;
            var a = l.OrderBy(x => x).ToArray();
            return a[a.Length / 2];
        }
    }
}
