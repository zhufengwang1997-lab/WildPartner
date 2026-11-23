using System;
using System.Collections;
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
    /// TC-10 soak: -soak=&lt;seconds&gt; -demoout=&lt;dir&gt;. The witch wanders (walk / run, turning, weather changing every
    /// two minutes, the day cycle running normally) while one CSV row per second records frame time, the slowest frame,
    /// GC and memory. Pass = no crash, the last minute's frame rate within 10 % of the first minute (after warmup),
    /// memory growth reported. Runs with the game's normal v-sync, so FPS shows the playing experience.
    /// </summary>
    public class SoakTest : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (!Environment.GetCommandLineArgs().Any(a => a.StartsWith("-soak"))) return;
            new GameObject("SoakTest").AddComponent<SoakTest>();
        }

        IEnumerator Start()
        {
            var args = Environment.GetCommandLineArgs();
            string s = args.FirstOrDefault(a => a.StartsWith("-soak="));
            float seconds = s != null && float.TryParse(s.Substring(6), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 600f;
            string o = args.FirstOrDefault(a => a.StartsWith("-demoout="));
            string dir = o != null ? o.Substring(9) : Path.Combine(Application.persistentDataPath, "soak");
            Directory.CreateDirectory(dir);
            yield return new WaitForSeconds(1.5f);
            GameFlow.Instance.NewGame();
            float t0 = 0; yield return null;
            while (CabinInterior.Busy && t0 < 10f) { t0 += Time.deltaTime; yield return null; }

            var mem = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "Total Used Memory");
            var gcMem = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Used Memory");
            var csv = new StringBuilder();
            csv.AppendLine($"# gpu={SystemInfo.graphicsDeviceName};api={SystemInfo.graphicsDeviceType};res={Screen.width}x{Screen.height};tier={Zfw.QualityTiers.QualityTiers.Current};upscaler={UpscalerSettings.Label(UpscalerSettings.Backend)};seconds={seconds}");
            csv.AppendLine("t,fps,worst_ms,gc_count,total_mem_mb,gc_mem_mb,hour,rain");
            string file = Path.Combine(dir, "soak.csv");
            var player = PlayerController.Instance; var env = EnvController.Instance;
            var rng = new System.Random(1123);
            float start = Time.realtimeSinceStartup, nextRow = 1f, nextTurn = 0f, nextWeather = 120f;
            int frames = 0; float worst = 0; Vector2 dir2 = new Vector2(0, 1);
            Vector3 home = player.transform.position;
            while (Time.realtimeSinceStartup - start < seconds)
            {
                float el = Time.realtimeSinceStartup - start;
                if (el >= nextTurn)
                {
                    // wander; head back home when too far so the walk stays inside the map
                    float a = (float)rng.NextDouble() * Mathf.PI * 2f;
                    dir2 = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                    GameInput.SimRun = rng.NextDouble() < 0.4;
                    nextTurn = el + 3f + (float)rng.NextDouble() * 5f;
                }
                var toHome = home - player.transform.position; toHome.y = 0;
                GameInput.SimMove = toHome.magnitude > 60f ? new Vector2(toHome.x, toHome.z).normalized : dir2;
                if (el >= nextWeather) { Weather.Instance.Set(rng.NextDouble() < 0.5, true); nextWeather += 120f; }
                frames++; worst = Mathf.Max(worst, Time.unscaledDeltaTime * 1000f);
                if (el >= nextRow)
                {
                    csv.AppendLine($"{el:0},{frames},{worst:0.0},{GC.CollectionCount(0)},{mem.LastValue / 1048576f:0},{gcMem.LastValue / 1048576f:0},{env.timeOfDay:0.0},{(Weather.Instance.RainAmount > 0.1f ? 1 : 0)}");
                    frames = 0; worst = 0; nextRow += 1f;
                    if ((int)el % 30 == 0) File.WriteAllText(file, csv.ToString());
                }
                yield return null;
            }
            GameInput.SimMove = Vector2.zero; GameInput.SimRun = false;
            mem.Dispose(); gcMem.Dispose();
            File.WriteAllText(file, csv.ToString());
            Debug.Log($"[Soak] done {seconds:0} s -> {file}");
            yield return new WaitForSeconds(0.3f);
            Application.Quit();
        }
    }
}
