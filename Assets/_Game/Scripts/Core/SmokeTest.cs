using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine;
using WildPartner.Creatures;
using WildPartner.Player;
using WildPartner.World;

namespace WildPartner.Core
{
    /// <summary>
    /// End-to-end smoke test of the stage-1 loop, run in a built player:
    ///   WildPartner -smoketest [-smokeout=&lt;dir&gt;]
    /// new game -> for each creature: set its time/weather, wait for it to appear, walk up (sneaking) and
    /// hold capture -> go to the campfire -> ending -> menu -> continue (save restored). Screenshots and a
    /// result file (smoke_result.txt, one line per step) are written to the output dir; exit code 0 = pass.
    /// The test drives the game only through the same entry points the UI and input use.
    /// </summary>
    public class SmokeTest : MonoBehaviour
    {
        string _out;
        int _fails;
        StreamWriter _log;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (!Environment.GetCommandLineArgs().Contains("-smoketest")) return;
            new GameObject("SmokeTest").AddComponent<SmokeTest>();
        }

        void Step(bool ok, string what)
        {
            if (!ok) _fails++;
            string line = $"{(ok ? "PASS" : "FAIL")} {what}";
            Debug.Log($"[Smoke] {line}");
            _log.WriteLine(line); _log.Flush();
        }

        IEnumerator Shot(string name)
        {
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(Path.Combine(_out, name + ".png"));
            yield return null;
        }

        IEnumerator Start()
        {
            string arg = Environment.GetCommandLineArgs().FirstOrDefault(a => a.StartsWith("-smokeout=", StringComparison.Ordinal));
            _out = arg != null ? arg.Substring(10) : Path.Combine(Application.persistentDataPath, "smoke");
            Directory.CreateDirectory(_out);
            _log = new StreamWriter(Path.Combine(_out, "smoke_result.txt"));
            _log.WriteLine($"WildPartner smoke test {DateTime.Now:yyyy-MM-dd HH:mm:ss} commit={Bootstrap.BuildCommit} gpu={SystemInfo.graphicsDeviceName} api={SystemInfo.graphicsDeviceType}");

            yield return new WaitForSeconds(2f);
            var flow = GameFlow.Instance;
            Step(flow && flow.State == GameFlow.FlowState.MainMenu, "启动后进入主菜单");
            yield return Shot("01_menu");

            flow.NewGame();
            yield return new WaitForSeconds(2f);
            var player = PlayerController.Instance;
            Step(flow.State == GameFlow.FlowState.Playing && flow.CaughtCount == 0, "新游戏：进入游戏，已收集 0");
            yield return Shot("02_newgame");

            var env = EnvController.Instance;
            var weather = Weather.Instance;
            var creatures = FindObjectsByType<Creature>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            int shot = 3;
            foreach (var def in CreatureDefs.All)
            {
                var c = creatures.First(x => x.defId == def.id);
                // a time/weather that satisfies the creature's condition, with an hour of margin either side
                bool rain = !def.ConditionMet(12f, false) && def.ConditionMet(12f, true);
                float hour = 12f;
                for (float h = 0; h < 24; h += 0.5f)
                    if (def.ConditionMet(h, rain) && def.ConditionMet(Mathf.Repeat(h - 1, 24), rain) && def.ConditionMet(Mathf.Repeat(h + 1.5f, 24), rain)) { hour = h; break; }
                env.timeOfDay = hour;
                weather.Set(rain, true);

                // park the player away from the habitat so the creature can appear, then wait for it
                var a = def.anchors[0];
                player.Teleport(new Vector3(a.x + 60, Creature.GroundHeight(a.x + 60, a.y), a.y), 270);
                float t = 0;
                while (c.Current == Creature.State.Hidden && t < 5f) { t += Time.deltaTime; yield return null; }
                Step(c.Current != Creature.State.Hidden, $"{def.name}：条件（{def.ConditionText}）满足后出现");

                bool caught = false;
                for (int attempt = 0; attempt < 3 && !caught; attempt++)
                {
                    // approach: stand inside capture range, sneaking, facing it
                    var p = c.transform.position;
                    var dir = (player.transform.position - p); dir.y = 0; dir = dir.sqrMagnitude > 0.01f ? dir.normalized : Vector3.back;
                    var stand = p + dir * def.captureRange * 0.55f;
                    stand.y = Creature.GroundHeight(stand.x, stand.z) + 0.1f;
                    GameInput.SimSneak = true;
                    player.Teleport(stand, Quaternion.LookRotation(-dir).eulerAngles.y);
                    yield return new WaitForSeconds(0.3f);
                    if (attempt == 0) yield return Shot($"{shot++:00}_{def.id}_found");
                    GameInput.SimCapture = true;
                    t = 0;
                    while (!flow.Save.HasCaught(def.id) && t < def.captureSeconds + 3f) { t += Time.deltaTime; yield return null; }
                    GameInput.SimCapture = false;
                    caught = flow.Save.HasCaught(def.id);
                    if (!caught) { Debug.Log($"[Smoke] {def.id} attempt {attempt} failed (state {c.Current}), retrying"); yield return new WaitForSeconds(1.5f); }
                }
                GameInput.SimSneak = false;
                Step(caught && c.Current == Creature.State.Companion, $"{def.name}：按住捕捉成功并成为伙伴");
            }
            Step(flow.AllCaught, $"图鉴集齐 {flow.CaughtCount}/{flow.Total}");
            weather.Set(false, true);
            env.timeOfDay = 19f;

            flow.OpenJournal();
            yield return new WaitForSecondsRealtime(0.5f);
            Step(flow.State == GameFlow.FlowState.Journal, "打开图鉴");
            yield return Shot($"{shot++:00}_journal");
            flow.Resume();

            // back to the cabin: the ending triggers within 5 m of the campfire
            var fire = flow.campfire.position;
            player.Teleport(fire + new Vector3(3.5f, 0.3f, -2f), 300);
            float te = 0;
            while (flow.State != GameFlow.FlowState.Ending && te < 5f) { te += Time.deltaTime; yield return null; }
            Step(flow.State == GameFlow.FlowState.Ending, "回到篝火触发结局");
            yield return new WaitForSeconds(1.5f);
            yield return Shot($"{shot++:00}_ending");

            flow.BackToMenu();
            yield return new WaitForSeconds(0.5f);
            Step(flow.State == GameFlow.FlowState.MainMenu, "结局后返回主菜单");
            flow.Continue();
            yield return new WaitForSeconds(1.5f);
            Step(flow.State == GameFlow.FlowState.Playing && flow.CaughtCount == flow.Total, $"继续游戏：存档恢复（已收集 {flow.CaughtCount}/{flow.Total}）");
            Step(creatures.All(x => x.Current == Creature.State.Companion), "继续游戏：伙伴全部恢复跟随");
            yield return Shot($"{shot++:00}_continue");

            _log.WriteLine(_fails == 0 ? "RESULT PASS" : $"RESULT FAIL ({_fails})");
            _log.Close();
            Debug.Log($"[Smoke] done, fails={_fails}, out={_out}");
            yield return new WaitForSeconds(0.5f);
            Application.Quit(_fails == 0 ? 0 : 1);
        }
    }
}
