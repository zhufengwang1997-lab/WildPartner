using System.Collections.Generic;
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


        /// <summary>Stand at `at` facing `yaw`, press E once, wait for the door transition to finish.</summary>
        IEnumerator UseDoor(PlayerController player, Vector3 at, float yaw, string midShot = null)
        {
            player.Teleport(at, yaw);
            yield return new WaitForSeconds(0.6f);
            GameInput.SimInteract = true; yield return null; GameInput.SimInteract = false;
            float t = 0; yield return null;
            if (midShot != null) { yield return new WaitForSeconds(0.42f); yield return Shot(midShot); }
            while (CabinInterior.Busy && t < 6f) { t += Time.deltaTime; yield return null; }
            yield return new WaitForSeconds(0.5f);
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
            int starters = CreatureDefs.All.Count(d => d.startsWithPlayer);
            Step(flow.State == GameFlow.FlowState.Playing && flow.CaughtCount == starters, $"新游戏：进入游戏，已收集 {starters}（默认宠物）");
            yield return Shot("02_newgame");

            // 试玩反馈 1123b-3: wake up in the cabin, go out through the door, come back in, sleep, go out again
            var cabin = CabinInterior.Instance;
            var catC = FindObjectsByType<Creature>(FindObjectsSortMode.None).FirstOrDefault(x => x.defId == "cat");
            if (cabin)
            {
                float tw = 0;
                while (CabinInterior.Busy && tw < 8f) { tw += Time.deltaTime; yield return null; }
                Step(CabinInterior.Inside && Vector3.Distance(player.transform.position, cabin.bedStand) < 1.5f, "新游戏：在木屋床边醒来（室内）");
                yield return Shot("02a_wake_room");
                yield return UseDoor(player, cabin.insideDoor + Vector3.forward * 0.6f, 180);
                Step(!CabinInterior.Inside && Vector3.Distance(player.transform.position, cabin.outsideSpot) < 1.5f &&
                     catC && Vector3.Distance(catC.transform.position, player.transform.position) < 4f, "出门：门口按 E 开门，淡出淡入到屋外，猫跟出来");
                yield return Shot("02b_outside");
            }
            else Step(false, "木屋室内不存在（CabinInterior）");

            // Space = jump: rises ~jumpHeight, plays the jump animation, lands again
            {
                yield return new WaitForSeconds(0.5f);
                float y0 = player.transform.position.y, top = y0, tj = 0;
                bool sawAir = false, sawJumpAnim = false, shotTaken = false;
                var anim = player.GetComponentInChildren<Animator>();
                GameInput.SimJump = true; yield return null; GameInput.SimJump = false;
                while (tj < 2.5f)
                {
                    tj += Time.deltaTime; top = Mathf.Max(top, player.transform.position.y);
                    sawAir |= player.Airborne;
                    if (anim) sawJumpAnim |= anim.GetCurrentAnimatorStateInfo(0).IsName("JumpUp") || anim.GetCurrentAnimatorStateInfo(0).IsName("Fall");
                    if (sawAir && !player.Airborne && tj > 0.3f) break;
                    if (!shotTaken && player.transform.position.y - y0 > player.jumpHeight * 0.8f) { shotTaken = true; yield return Shot("02b_jump"); }
                    yield return null;
                }
                Step(top - y0 > player.jumpHeight * 0.7f && !player.Airborne && (anim == null || sawJumpAnim),
                     $"空格跳跃：起跳高度 {top - y0:0.00} m，落地，{(anim ? (sawJumpAnim ? "播放了跳跃动画" : "没有跳跃动画") : "无骨骼动画")}");
            }

            // footsteps: fired by the walk clip's foot-contact events (not a timer), sound picked by the ground
            {
                int steps = 0; var times = new List<float>(); var surfaces = new HashSet<string>();
                System.Action onStep = () => { steps++; times.Add(Time.time); surfaces.Add(Audio.ProceduralAudio.SurfaceAt(player.transform.position).ToString()); };
                player.Footstep += onStep;
                GameInput.SimMove = new Vector2(0.35f, 1); yield return new WaitForSeconds(3f); GameInput.SimMove = Vector2.zero;
                player.Footstep -= onStep;
                var gaps = new List<string>(); for (int i = 1; i < times.Count; i++) gaps.Add((times[i] - times[i - 1]).ToString("0.00"));
                Debug.Log($"[Smoke] footsteps {steps}, gaps {string.Join(" ", gaps)} s, surfaces {string.Join("/", surfaces)}");
                Step(steps >= 4, $"脚步声：走 3 秒触发 {steps} 次（动画落脚帧），地面 {string.Join("/", surfaces)}");
            }

            // 试玩反馈 1123b-5: she stops -> the cat walks to ~0.5 m on her right and faces her way
            if (catC)
            {
                float tc = 0;
                while (!catC.AtSideSpot && tc < 6f) { tc += Time.deltaTime; yield return null; }
                yield return new WaitForSeconds(1f);
                var pt = player.transform;
                Vector3 off = catC.transform.position - pt.position; off.y = 0;
                float side = Vector3.Dot(off, pt.right);
                float ang = Vector3.Angle(Vector3.ProjectOnPlane(catC.transform.forward, Vector3.up), Vector3.ProjectOnPlane(pt.forward, Vector3.up));
                string where = catC.SideSpotKind switch { 0 => "右侧", 1 => "左侧（右侧被挡）", 2 => "身后（两侧被挡）", _ => "附近" };
                Step(catC.AtSideSpot && off.magnitude < 0.9f && ang < 50f && (catC.SideSpotKind != 0 || side > 0.25f),
                     $"猫：主角停下后走到{where}，离她 {off.magnitude:0.00} m，朝向差 {ang:0}°");
                yield return Shot("02b1_cat_beside");
            }

            if (cabin)
            {
                var fwd = new Vector3(WorldLayout.CabinFacing.x, 0, WorldLayout.CabinFacing.y);
                yield return UseDoor(player, cabin.outsideDoor + fwd * 1.7f + Vector3.up * 0.4f, Mathf.Atan2(-fwd.x, -fwd.z) * Mathf.Rad2Deg, "02c0_door_opening");
                Step(CabinInterior.Inside && Vector3.Distance(player.transform.position, cabin.insideSpot) < 1.5f &&
                     catC && Vector3.Distance(catC.transform.position, player.transform.position) < 4f, "进门：门口按 E 开门，进到室内，猫跟进来");
                yield return Shot("02c_inside");
                // bed: sleep switches day/night and saves
                float h0 = EnvController.Instance.timeOfDay;
                player.Teleport(cabin.bedStand, 0);
                yield return null; yield return null;
                GameInput.SimInteract = true; yield return null; GameInput.SimInteract = false;
                float ts = 0; yield return null;
                while (CabinInterior.Busy && ts < 6f) { ts += Time.deltaTime; yield return null; }
                float h1 = EnvController.Instance.timeOfDay;
                bool dayBefore = h0 >= 6f && h0 < 18.5f, dayAfter = h1 >= 6f && h1 < 18.5f;
                Step(dayBefore != dayAfter, $"室内：床上按 E 睡觉，{h0:0.0} 点 → {h1:0.0} 点");
                yield return UseDoor(player, cabin.insideDoor + Vector3.forward * 0.6f, 180);
                Step(!CabinInterior.Inside, "再次出门（后续测试在屋外）");
            }

            var env = EnvController.Instance;
            var weather = Weather.Instance;
            var creatures = FindObjectsByType<Creature>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            int shot = 3;
            bool leadTested = false;
            foreach (var def in CreatureDefs.All)
            {
                var c = creatures.First(x => x.defId == def.id && x.instance == 0);
                int count = creatures.Count(x => x.defId == def.id);
                if (def.Count > 1) Step(count == def.Count, $"{def.name}：场景里有 {count} 只（应为 {def.Count}）");
                if (def.startsWithPlayer)
                {
                    Step(c.Current == Creature.State.Companion && flow.Save.HasCaught(def.id), $"{def.name}：开局即为伙伴");
                    continue;
                }
                // a time/weather that satisfies the creature's condition, with an hour of margin either side
                bool rain = !def.ConditionMet(12f, false) && def.ConditionMet(12f, true);
                float hour = 12f;
                for (float h = 0; h < 24; h += 0.5f)
                    if (def.ConditionMet(h, rain) && def.ConditionMet(Mathf.Repeat(h - 1, 24), rain) && def.ConditionMet(Mathf.Repeat(h + 1.5f, 24), rain)) { hour = h; break; }
                env.timeOfDay = hour;
                weather.Set(rain, true);

                // park the player away from the habitat so the creature can appear, then wait for it
                var a = c.Home;
                player.Teleport(new Vector3(a.x + 60, Creature.GroundHeight(a.x + 60, a.y), a.y), 270);
                float t = 0;
                while (c.Current == Creature.State.Hidden && t < 5f) { t += Time.deltaTime; yield return null; }
                Step(c.Current != Creature.State.Hidden, $"{def.name}：条件（{def.ConditionText}）满足后出现");

                if (!leadTested && c.Current != Creature.State.Hidden)
                {
                    // the cat leads the way: 20 m from the first wild creature, walk a few steps
                    leadTested = true;
                    var cat = creatures.First(x => x.defId == "cat");
                    var cp = c.transform.position; var away = player.transform.position - cp; away.y = 0; away.Normalize();
                    var st = cp + away * 20f; st.y = Creature.GroundHeight(st.x, st.z) + 0.1f;
                    player.Teleport(st, Quaternion.LookRotation(-away).eulerAngles.y);
                    cat.WarpNear(st + away * 1.5f, Quaternion.LookRotation(-away).eulerAngles.y);   // leading starts only within 12 m of her
                    yield return new WaitForSeconds(0.6f);
                    cat.LeadSoon();
                    GameInput.SimMove = new Vector2(0, 1);
                    yield return new WaitForSeconds(0.5f);
                    GameInput.SimMove = Vector2.zero;
                    float tl = 0;
                    while (!cat.IsLeading && tl < 6f) { tl += Time.deltaTime; yield return null; }
                    Step(cat.IsLeading, "小猫带路：跑到前面停下、抬爪朝未收集的宠物叫");
                    if (cat.IsLeading) yield return Shot($"{shot++:00}_cat_lead");
                }

                if (def.grazes && c.Current != Creature.State.Hidden)
                {
                    // watched from 60 m away: it strolls and stops to graze on its own
                    bool walked = false, grazed = false; float tw = 0, walkRun = 0, longestWalk = 0;
                    while (tw < 14f && !(walked && grazed)) {
                        tw += Time.deltaTime;
                        if (c.Grazing) { grazed = true; walkRun = 0; }
                        else if (c.Current == Creature.State.Wander) { walkRun += Time.deltaTime; longestWalk = Mathf.Max(longestWalk, walkRun); if (walkRun > 1f) walked = true; }
                        yield return null;
                    }
                    Step(walked && grazed, $"{def.name}：自己走动并停下吃草（{tw:0.0} 秒内）");
                }
                if (def.startleFirst && c.Current != Creature.State.Hidden)
                    yield return StartleCheck(player, c, def, shot++);

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
                if (def.rideable && caught)
                    yield return RideCheck(player, c, shot++);
                if (def.Count > 1)
                {
                    var rest = creatures.Where(x => x.defId == def.id && x != c).ToArray();
                    Step(rest.All(x => x.Current != Creature.State.Companion && !x.CanBeCaptured),
                         $"{def.name}：其余 {rest.Length} 只留在野外，不能再捕捉、不显示提示");
                }
            }
            Step(flow.AllCaught, $"图鉴集齐 {flow.CaughtCount}/{flow.Total}");
            yield return FishCheck(player, shot++);
            weather.Set(false, true);
            env.timeOfDay = 19f;

            // grass cost check (试玩反馈 5): stand in the meadow, uncapped frame rate, 3 s average. Logged, and fails
            // only below 30 fps on this machine (the real budget check is the perf runner on the 1070 Ti)
            {
                var meadow = new Vector2(400, 470) * WorldLayout.K;
                player.Teleport(new Vector3(meadow.x, Creature.GroundHeight(meadow.x, meadow.y) + 0.2f, meadow.y), 300);
                env.timeOfDay = 11f;
                int vs = QualitySettings.vSyncCount, tf = Application.targetFrameRate;
                QualitySettings.vSyncCount = 0; Application.targetFrameRate = -1;
                yield return new WaitForSeconds(1.5f);
                int frames = 0; float t0 = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - t0 < 3f) { frames++; yield return null; }
                float ms = (Time.realtimeSinceStartup - t0) * 1000f / frames;
                QualitySettings.vSyncCount = vs; Application.targetFrameRate = tf;
                int chunks = GrassRenderer.Instance ? GrassRenderer.Instance.LastChunkCount : 0;
                Step(ms < 33.3f, $"草地帧时间 {ms:0.0} ms（{1000f / ms:0} fps，{Screen.width}x{Screen.height}，草地块 {chunks}）");
                yield return Shot($"{shot++:00}_meadow_grass");
            }

            flow.OpenJournal();
            yield return new WaitForSecondsRealtime(0.5f);
            Step(flow.State == GameFlow.FlowState.Journal, "打开图鉴");
            yield return Shot($"{shot++:00}_journal");
            flow.Resume();

            // big map (试玩反馈 3): pre-baked texture, opens with M, pauses the game
            Step(WildPartner.UI.MapUI.Exists, "地图底图已烘焙（Resources/Map/WorldMap）");
            flow.OpenMap();
            yield return new WaitForSecondsRealtime(0.5f);
            Step(flow.State == GameFlow.FlowState.Map && Time.timeScale == 0f, "M 打开大地图并暂停");
            yield return Shot($"{shot++:00}_map");
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
            Step(CreatureDefs.All.All(d => creatures.Count(x => x.defId == d.id && x.Current == Creature.State.Companion) == 1),
                 "继续游戏：伙伴全部恢复跟随（每种一只，同种其余的留在野外）");
            yield return Shot($"{shot++:00}_continue");

            _log.WriteLine(_fails == 0 ? "RESULT PASS" : $"RESULT FAIL ({_fails})");
            _log.Close();
            Debug.Log($"[Smoke] done, fails={_fails}, out={_out}");
            yield return new WaitForSeconds(0.5f);
            Application.Quit(_fails == 0 ? 0 : 1);
        }
            /// <summary>试玩反馈 1123b-6: a capture spoiled by the alert meter only startles the rabbit (ears up ~1 s); if
        /// the witch keeps still it relaxes and can be tried again.</summary>
        IEnumerator StartleCheck(PlayerController player, Creature c, CreatureDef def, int shotNo)
        {
            var p = c.transform.position;
            var dir = player.transform.position - p; dir.y = 0; dir = dir.sqrMagnitude > 0.01f ? dir.normalized : Vector3.back;
            var stand = p + dir * def.captureRange * 0.55f;
            stand.y = Creature.GroundHeight(stand.x, stand.z) + 0.1f;
            GameInput.SimSneak = false;
            player.Teleport(stand, Quaternion.LookRotation(-dir).eulerAngles.y);
            yield return new WaitForSeconds(0.3f);
            c.DebugSetAlert(0.9f);
            GameInput.SimCapture = true;
            float t = 0;
            while (!c.IsStartled && c.Current != Creature.State.Flee && t < 3f) { t += Time.deltaTime; yield return null; }
            GameInput.SimCapture = false;
            bool startled = c.IsStartled;
            yield return new WaitForSeconds(0.35f);
            float ears = c.EarsUp;
            if (startled) yield return Shot($"{shotNo:00}_{def.id}_ears_up");
            Step(startled && ears > 0.8f, $"{def.name}：捕捉被打断时没有逃走，竖起耳朵警觉（耳朵 {ears:0.00}）");
            t = 0;
            while (c.IsStartled && t < 3f) { t += Time.deltaTime; yield return null; }
            Step(startled && (c.Current == Creature.State.Alert || c.Current == Creature.State.Wander),
                 $"{def.name}：主角站着不动，约 1 秒后放松（状态 {c.Current}）");
            yield return new WaitForSeconds(1.5f);
        }
            /// <summary>试玩反馈 1123b-8: walk up to the caught deer, E to get on, ride forward, E to get off.</summary>
        IEnumerator RideCheck(PlayerController player, Creature deer, int shotNo)
        {
            yield return new WaitForSeconds(1.5f);
            var dp = deer.transform.position;
            var st = dp - deer.transform.right * 1.8f; st.y = Creature.GroundHeight(st.x, st.z) + 0.1f;
            player.Teleport(st, deer.transform.eulerAngles.y);
            yield return new WaitForSeconds(0.4f);
            string prompt = Creature.RidePrompt;
            GameInput.SimInteract = true; yield return null; GameInput.SimInteract = false;
            yield return new WaitForSeconds(0.3f);
            Step(player.Riding && deer.Ridden, $"白鹿：靠近出现提示「{prompt}」，按 E 骑上");
            var p0 = player.transform.position;
            player.SetCameraOrbit(player.transform.eulerAngles.y + 60f, 12f);
            GameInput.SimRun = true; GameInput.SimMove = new Vector2(0, 1);
            yield return new WaitForSeconds(1.6f);
            yield return Shot($"{shotNo:00}_white_deer_riding");
            yield return new WaitForSeconds(0.6f);
            GameInput.SimRun = false; GameInput.SimMove = Vector2.zero;
            float moved = Vector3.Distance(p0, player.transform.position);
            Vector3 gap = deer.transform.position - player.transform.position; gap.y = 0;
            Step(player.Riding && moved > 10f && gap.magnitude < 1.5f, $"骑鹿奔跑：2.2 秒跑了 {moved:0.0} m，鹿一直在她身下（水平偏差 {gap.magnitude:0.00} m）");
            yield return new WaitForSeconds(0.5f);
            GameInput.SimInteract = true; yield return null; GameInput.SimInteract = false;
            yield return new WaitForSeconds(0.5f);
            Step(!player.Riding && !deer.Ridden, "按 E 从白鹿背上下来");
        }

        /// <summary>试玩反馈 1123b-7: ordinary fish schools - count, scatter when she comes close, regroup.</summary>
        IEnumerator FishCheck(PlayerController player, int shotNo)
        {
            var fs = FishSchools.Instance;
            int n = fs ? fs.Schools.Count : 0;
            bool sizes = fs && fs.Schools.All(x => x.fish.Count >= 6 && x.fish.Count <= 12);
            Step(n >= 3 && n <= 5 && sizes, $"鱼群：{n} 群，每群 {(fs ? string.Join("/", fs.Schools.Select(x => x.fish.Count)) : "-")} 条");
            if (!fs || n == 0) yield break;
            var env = EnvController.Instance; env.timeOfDay = 11f;
            var sc = fs.Schools[0];
            // stand on the bank 9 m from the school, looking at it from above
            Vector3 c = sc.centre;
            Vector3 bank = c; float best = 0;
            for (int i = 0; i < 24; i++)
            {
                float a = i * Mathf.PI / 12f;
                var q = c + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * 9f;
                if (!WorldLayout.WaterSurface(q.x, q.z, out _)) { bank = q; best = 1; break; }
            }
            if (best == 0) bank = c + Vector3.right * 9f;
            bank.y = Creature.GroundHeight(bank.x, bank.z) + 0.2f;
            var look = c - bank; look.y = 0;
            float yaw = Quaternion.LookRotation(look).eulerAngles.y;
            player.Teleport(bank, yaw);
            player.SetCameraOrbit(yaw, 38f);
            player.SetCameraDistance(0.8f);
            yield return new WaitForSeconds(2.5f);
            float calm = sc.Spread;
            yield return Shot($"{shotNo:00}_fish_school");
            // walk right up to it: the fish dart away
            var f0 = sc.fish[0].p; f0.y = Creature.GroundHeight(f0.x, f0.z);
            player.Teleport(new Vector3(f0.x, Mathf.Max(f0.y, sc.surface - 0.4f), f0.z) + look.normalized * -1.5f, yaw);
            float peak = 0, tt = 0;
            while (tt < 1.2f) { tt += Time.deltaTime; peak = Mathf.Max(peak, sc.Spread); yield return null; }
            bool scattered = sc.scattered;
            player.Teleport(bank + (bank - c).normalized * 14f, yaw);
            yield return new WaitForSeconds(5f);
            float after = sc.Spread;
            Step(scattered && peak > calm + 0.6f && after < peak * 0.6f,
                 $"鱼群：靠近时散开（离队平均 {calm:0.00} → {peak:0.00} m），离开后重新聚拢（{after:0.00} m）");
            player.SetCameraDistance(1f);
        }
    }
}
