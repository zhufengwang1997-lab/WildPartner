using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine;
using WildPartner.UI;

namespace WildPartner.Core
{
    /// <summary>
    /// 11-24: screenshots of every screen at several window sizes, to check nothing is cut off or overlaps.
    ///   WildPartner -uishots -demoout=&lt;dir&gt; [-uires=1280x720,1366x768,...]
    /// One folder per resolution: hud, pause, settings (4 pages), journal, map, main menu.
    /// </summary>
    public class UiShots : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (!Environment.GetCommandLineArgs().Contains("-uishots")) return;
            new GameObject("UiShots").AddComponent<UiShots>();
        }

        string _out;

        IEnumerator Start()
        {
            var args = Environment.GetCommandLineArgs();
            _out = args.FirstOrDefault(a => a.StartsWith("-demoout=", StringComparison.Ordinal))?.Substring(9) ?? Path.Combine(Application.persistentDataPath, "uishots");
            string resArg = args.FirstOrDefault(a => a.StartsWith("-uires=", StringComparison.Ordinal))?.Substring(7)
                            ?? "1280x720,1366x768,1920x1080,2560x1440,1920x1200,3440x1440";
            Directory.CreateDirectory(_out);
            var log = new StreamWriter(Path.Combine(_out, "uishots.txt"));
            yield return new WaitForSeconds(4f);   // opens in bed
            var flow = GameFlow.Instance; var ui = FindFirstObjectByType<GameUI>();
            foreach (var r in resArg.Split(','))
            {
                var p = r.Split('x'); int w = int.Parse(p[0]), h = int.Parse(p[1]);
                Screen.SetResolution(w, h, FullScreenMode.Windowed);
                float t = 0; while ((Screen.width != w || Screen.height != h) && t < 4f) { t += Time.unscaledDeltaTime; yield return null; }
                yield return new WaitForSecondsRealtime(1f);
                string dir = Path.Combine(_out, $"{w}x{h}"); Directory.CreateDirectory(dir);
                log.WriteLine($"{w}x{h}: actual {Screen.width}x{Screen.height}"); log.Flush();
                if (flow.State != GameFlow.FlowState.Playing) { if (flow.State == GameFlow.FlowState.MainMenu) flow.Continue(); else flow.Resume(); }
                yield return new WaitForSecondsRealtime(1f);
                yield return Shot(dir, "1_hud");
                flow.Pause(); yield return new WaitForSecondsRealtime(0.5f);
                yield return Shot(dir, "2_pause");
                foreach (var page in new[] { "显示", "画面", "操作", "声音" })
                {
                    ui.ShowSettingsPage(page); yield return new WaitForSecondsRealtime(0.5f);
                    yield return Shot(dir, "3_settings_" + page);
                }
                ui.ShowSettingsPage(null); flow.Resume(); yield return new WaitForSecondsRealtime(0.4f);
                flow.OpenJournal(); yield return new WaitForSecondsRealtime(0.6f);
                yield return Shot(dir, "4_journal");
                flow.Resume(); yield return new WaitForSecondsRealtime(0.3f);
                flow.OpenMap(); yield return new WaitForSecondsRealtime(0.6f);
                yield return Shot(dir, "5_map");
                flow.Resume(); yield return new WaitForSecondsRealtime(0.3f);
                flow.BackToMenu(); yield return new WaitForSecondsRealtime(0.8f);
                yield return Shot(dir, "6_main_menu");
                flow.Continue(); yield return new WaitForSecondsRealtime(0.8f);
            }
            log.WriteLine("done"); log.Close();
            Application.Quit();
        }

        static IEnumerator Shot(string dir, string name)
        {
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(Path.Combine(dir, name + ".png"));
            yield return null;
        }
    }
}
