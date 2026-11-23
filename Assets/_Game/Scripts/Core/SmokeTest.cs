using System.Collections.Generic;
using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering.Universal;
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
        // every exception the player logs during the run (a render-graph exception every frame once gave black
        // screenshots while all checks still passed)
        static int _exceptions; static string _firstException;
        static void OnLog(string msg, string stack, LogType type)
        {
            if (type != LogType.Exception && type != LogType.Error) return;
            if (msg.StartsWith("[Smoke]", StringComparison.Ordinal)) return;
            if (System.Threading.Interlocked.Increment(ref _exceptions) == 1) _firstException = msg.Split('\n')[0];
        }

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
            Application.logMessageReceivedThreaded += OnLog;
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
                float tw = 0; int wakeShot = 0;
                while (CabinInterior.Busy && tw < 14f)
                {
                    tw += Time.deltaTime;
                    // 1123c-A5 key frames of the wake-up clip: lying, sitting up, on the edge, standing up
                    if (CabinInterior.WakePlaying && wakeShot < 4 && tw > 1.9f + wakeShot * 1.35f) { yield return Shot($"02a{wakeShot}_wake"); wakeShot++; }
                    yield return null;
                }
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
                // 1123e-1: a mesh whose normals sum to zero shades as NaN and bloom blows it up (the "glowing" door)
                int badN = 0, meshes = 0;
                foreach (var mf in cabin.GetComponentsInChildren<MeshFilter>())
                {
                    var m = mf.sharedMesh; if (!m || !m.isReadable) continue;
                    meshes++;
                    foreach (var nv in m.normals) if (!(nv.sqrMagnitude > 0.25f)) { badN++; break; }
                }
                Step(meshes > 0 && badN == 0, $"室内：{meshes} 个可读网格的法线都有效（{badN} 个含零或 NaN 法线；门板不再发白）");
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
                if (def.id == "moon_butterfly") yield return MoonCheck(player, flow, c, def);

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
                    // walked = at least 1.5 m covered while wandering (short strolls count together)
                    bool grazed = false; float tw = 0, moved = 0;
                    Vector3 last = c.transform.position;
                    while (tw < 20f && !(moved > 1.5f && grazed)) {
                        tw += Time.deltaTime;
                        if (c.Grazing) grazed = true;
                        if (c.Current == Creature.State.Wander) { var d = c.transform.position - last; d.y = 0; moved += d.magnitude; }
                        last = c.transform.position;
                        yield return null;
                    }
                    Step(moved >= 1.0f && grazed, $"{def.name}：自己走动（{moved:0.0} m）并停下吃草（{tw:0.0} 秒内）");
                }
                if (def.startleFirst && c.Current != Creature.State.Hidden)
                    yield return StartleCheck(player, c, def, shot++);

                if (def.viewAngle > 0f && c.Current != Creature.State.Hidden)
                {
                    // 1123c-B1: in front of the deer, even sneaking, the spell is seen and it runs
                    var p0 = c.transform.position; var f0 = c.transform.forward; f0.y = 0; f0.Normalize();
                    var st0 = p0 + f0 * def.captureRange * 0.8f; st0.y = Creature.GroundHeight(st0.x, st0.z) + 0.1f;
                    GameInput.SimSneak = true;
                    player.Teleport(st0, Quaternion.LookRotation(-f0).eulerAngles.y);
                    yield return new WaitForSeconds(0.3f);
                    GameInput.SimCapture = true;
                    t = 0;
                    while (!flow.Save.HasCaught(def.id) && c.Current != Creature.State.Flee && t < def.captureSeconds + 2f) { t += Time.deltaTime; yield return null; }
                    GameInput.SimCapture = false; GameInput.SimSneak = false;
                    bool frontFailed = !flow.Save.HasCaught(def.id);
                    Step(frontFailed, $"{def.name}：正面潜行施法被看见（{(c.Current == Creature.State.Flee ? "逃走" : c.Current.ToString())}，{t:0.0} 秒）");
                    t = 0;
                    while (c.Current == Creature.State.Flee && t < 9f) { t += Time.deltaTime; yield return null; }
                    yield return new WaitForSeconds(1f);
                }

                bool caught = false;
                if (def.fishable)
                {
                    yield return FishingFlow(player, flow, def);
                    caught = flow.Save.HasCaught(def.id);
                    c = creatures.FirstOrDefault(x => x.defId == def.id && x.Current == Creature.State.Companion) ?? c;
                }
                else for (int attempt = 0; attempt < 3 && !caught; attempt++)
                {
                    // approach: stand inside capture range, sneaking, facing it (a deer: from behind, 1123c-B1)
                    // a deer: wait until it has its head down grazing (it sees less then), like a player would
                    if (def.viewAngle > 0f) { t = 0; while (!c.Grazing && t < 10f) { t += Time.deltaTime; yield return null; } }
                    var p = c.transform.position;
                    var dir = (player.transform.position - p); dir.y = 0; dir = dir.sqrMagnitude > 0.01f ? dir.normalized : Vector3.back;
                    if (def.viewAngle > 0f) { dir = -c.transform.forward; dir.y = 0; dir.Normalize(); }
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
                // a group of rabbits: the capture takes the nearest one, not necessarily the one we walked up to
                if (caught) c = creatures.FirstOrDefault(x => x.defId == def.id && x.Current == Creature.State.Companion) ?? c;
                Step(caught && c.Current == Creature.State.Companion, def.fishable ? $"{def.name}：钓上来并成为伙伴" : $"{def.name}：按住捕捉成功并成为伙伴");
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
            yield return FlowCheck(player, flow);
            yield return BoundsCheck(player);
            yield return PetCheck(player, flow);
            yield return ButterflyGlowCheck(player, flow);
            yield return AudioMixCheck(player, flow);
            yield return AntiAliasCheck(player, flow);
            yield return UpscaleCheck(player);
            yield return SwimCheck(player, flow);
            yield return ClimbCheck(player);
            yield return AmbientCheck(player);
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
                // 1123c-C1: the densest forest view (deep forest, looking along the trees), same method
                var forest = new Vector2(70f, 150f);
                player.Teleport(new Vector3(forest.x, Creature.GroundHeight(forest.x, forest.y) + 0.2f, forest.y), 20);
                player.SetCameraOrbit(20f, 8f);
                QualitySettings.vSyncCount = 0; Application.targetFrameRate = -1;
                yield return new WaitForSeconds(1.5f);
                frames = 0; t0 = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - t0 < 3f) { frames++; yield return null; }
                float fms = (Time.realtimeSinceStartup - t0) * 1000f / frames;
                QualitySettings.vSyncCount = vs; Application.targetFrameRate = tf;
                yield return Shot("46_forest_perf");
                Step(fms < 33.3f, $"森林帧时间 {fms:0.0} ms（{1000f / fms:0} fps，{Screen.width}x{Screen.height}，深绿林里看向树林）");
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

            Step(_exceptions == 0, _exceptions == 0 ? "全程没有异常或错误日志（含渲染管线）" : $"全程有 {_exceptions} 条异常/错误日志，第一条：{_firstException}");
            _log.WriteLine(_fails == 0 ? "RESULT PASS" : $"RESULT FAIL ({_fails})");
            _log.Close();
            Debug.Log($"[Smoke] done, fails={_fails}, out={_out}");
            yield return new WaitForSeconds(0.5f);
            Application.Quit(_fails == 0 ? 0 : 1);
        }
            /// <summary>试玩反馈 1123b-6: a capture spoiled by the alert meter only startles the rabbit (ears up ~1 s); if
        /// the witch keeps still it relaxes and can be tried again.</summary>
        // ---------------------------------------------------------------- 试玩反馈 1123d
        /// <summary>1123d-1: the koi lives by the two jetties and is caught by fishing. Moving / Esc put the rod away,
        /// a missed bite loses the fish, an ordinary fish is let go, then the koi: bite, strike, reel in the green.</summary>
        IEnumerator FishingFlow(PlayerController player, GameFlow flow, CreatureDef def)
        {
            var fs = FishingSystem.Instance;
            var spots = FishingSpot.All;
            Step(fs && spots.Count == 2, $"钓鱼点：{spots.Count} 处（{string.Join("、", spots.Select(x => x.label))}），带木栈台和水面光圈");
            if (!fs || spots.Count == 0) yield break;
            var koi = FindObjectsByType<Creature>(FindObjectsSortMode.None).Where(x => x.defId == def.id).ToArray();
            yield return new WaitForSeconds(1.2f);
            var near = spots.Select(sp => koi.Count(k => k.Current != Creature.State.Hidden && Vector3.Distance(k.transform.position, sp.cast) < 8f)).ToArray();
            float minDepth = koi.Min(k => { WorldLayout.WaterSurface(k.transform.position.x, k.transform.position.z, out float lv); return lv - k.transform.position.y; });
            Step(near.All(n => n >= 1) && near.Sum() == koi.Length, $"赤墨锦鲤只在两处：{string.Join("、", spots.Select((sp, i) => $"{sp.label} {near[i]} 条"))}（一共 {koi.Length} 条，都在钓鱼点 8 m 内，浮在水面下）");

            var spot = spots.First();
            void Stand() => player.Teleport(spot.stand + Vector3.up * 0.1f - Quaternion.Euler(0, spot.yaw, 0) * Vector3.forward * 0.8f, spot.yaw);
            IEnumerator Press() { GameInput.SimInteract = true; yield return null; GameInput.SimInteract = false; }
            Stand();
            yield return new WaitForSeconds(0.6f);
            Step(fs.NearSpot == spot && FishingSystem.Prompt == "按 E 钓鱼", $"站上{spot.label}的木栈台：提示「{FishingSystem.Prompt}」");
            yield return Shot("50_fishing_spot");

            // cancel by moving, then by Esc
            FishingSystem.ForceWait = 6f;
            yield return Press();
            float t = 0;
            while (fs.Current != FishingSystem.Phase.Wait && t < 3f) { t += Time.deltaTime; yield return null; }
            bool waited = fs.Current == FishingSystem.Phase.Wait;
            yield return new WaitForSeconds(0.4f);
            yield return Shot("51_fishing_wait");
            GameInput.SimMove = new Vector2(0, -1);
            yield return null; yield return null;
            GameInput.SimMove = Vector2.zero;
            bool movedOff = fs.Current == FishingSystem.Phase.Idle && !player.Frozen;
            Stand(); yield return new WaitForSeconds(0.3f);
            yield return Press();
            yield return new WaitForSeconds(0.5f);
            GameInput.SimPause = true; yield return null; GameInput.SimPause = false;
            yield return null;
            bool escOff = fs.Current == FishingSystem.Phase.Idle && flow.State == GameFlow.FlowState.Playing;
            Step(waited && movedOff && escOff, "钓鱼：按 E 抛竿、浮标落水后等待；移动或按 Esc 立即收竿（不会打开暂停菜单）");

            // a missed bite
            Stand(); yield return new WaitForSeconds(0.3f);
            FishingSystem.ForceWait = 1.5f;
            string lost = null; System.Action<string> onLost = w => lost = w;
            FishingSystem.Lost += onLost;
            yield return Press();
            t = 0;
            while (fs.Current != FishingSystem.Phase.Bite && t < 5f) { t += Time.deltaTime; yield return null; }
            bool bit = fs.Current == FishingSystem.Phase.Bite;
            t = 0;
            while (fs.Current == FishingSystem.Phase.Bite && t < 3f) { t += Time.deltaTime; yield return null; }
            Step(bit && lost != null && t >= FishingSystem.BiteWindow - 0.1f, $"钓鱼：浮标下沉后 {t:0.0} 秒内没提竿，鱼跑掉（「{lost}」）");
            while (fs.Current != FishingSystem.Phase.Idle && t < 6f) { t += Time.deltaTime; yield return null; }

            // an ordinary fish first, then the koi
            bool? caughtKoi = null; System.Action<bool> onCaught = k => caughtKoi = k;
            FishingSystem.Caught += onCaught;
            foreach (bool wantKoi in new[] { false, true })
            {
                Stand(); yield return new WaitForSeconds(0.3f);
                caughtKoi = null; lost = null;
                FishingSystem.ForceWait = 2f; FishingSystem.ForceKoi = wantKoi;
                yield return Press();
                t = 0;
                while (fs.Current != FishingSystem.Phase.Bite && t < 6f) { t += Time.deltaTime; yield return null; }
                if (wantKoi) yield return Shot("52_fishing_bite");
                yield return new WaitForSeconds(0.25f);
                yield return Press();
                // reeling like a player: hold E while the pointer is on the slack side, let go on the tight side
                t = 0; float minT = 1, maxT = 0, reelT = 0; bool shotReel = false;
                while ((fs.Current == FishingSystem.Phase.Reel || fs.Current == FishingSystem.Phase.Bite) && t < 15f)
                {
                    t += Time.deltaTime;
                    if (fs.Current == FishingSystem.Phase.Reel) { reelT += Time.deltaTime; minT = Mathf.Min(minT, fs.Tension); maxT = Mathf.Max(maxT, fs.Tension); }
                    GameInput.SimCapture = fs.Tension < 0.5f;
                    if (wantKoi && !shotReel && reelT > 2f) { shotReel = true; GameInput.SimCapture = false; yield return Shot("53_fishing_reel"); continue; }
                    yield return null;
                }
                GameInput.SimCapture = false;
                if (wantKoi && fs.Current == FishingSystem.Phase.Landed) { yield return new WaitForSeconds(0.45f); yield return Shot("54_fishing_koi_out"); }
                t = 0;
                while (caughtKoi == null && lost == null && t < 4f) { t += Time.deltaTime; yield return null; }
                Step(caughtKoi == wantKoi && reelT > 3f && reelT < 12f && (!wantKoi || maxT - minT > 0.15f),
                     wantKoi ? $"钓鱼：咬钩→按 E 提竿→收线 {reelT:0.0} 秒（张力在 {minT:0.00}–{maxT:0.00} 摆动，锦鲤挣扎）→ 拉出水面成为伙伴"
                             : $"钓鱼：先钓到普通鱼（收线 {reelT:0.0} 秒，张力 {minT:0.00}–{maxT:0.00}），放回水里并提示{(lost != null ? "（失败：" + lost + "）" : "")}");
                if (wantKoi) yield return new WaitForSeconds(0.3f);
            }
            FishingSystem.Caught -= onCaught; FishingSystem.Lost -= onLost;
            FishingSystem.ForceWait = null; FishingSystem.ForceKoi = null;
            Step(!player.Frozen && fs.Current == FishingSystem.Phase.Idle, "钓鱼结束后可以自由行动，镜头恢复");

            // the koi chance without forcing: 65 % / 80 % / 100 % -> always within three tries
            Step(fs.Tries == 0, $"钓到锦鲤后计数清零（{fs.Tries}）；锦鲤几率 65% / 80% / 100%，第 3 次必定是锦鲤");
        }

        /// <summary>1123d-2: the river runs from the falls down to the lake, fast below the falls and slow near the
        /// lake; floating flecks drift downstream; the current carries her when she walks on the water with the koi.</summary>
        IEnumerator FlowCheck(PlayerController player, GameFlow flow)
        {
            var ri = WorldLayout.RiverIn;
            Vector2 P(Vector3 v) => new Vector2(v.x, v.y);
            var lake = WorldLayout.LakeCentre;
            var below = P(ri[6]) + (P(ri[7]) - P(ri[6])).normalized * 6f;
            var mid = Vector2.Lerp(P(ri[8]), P(ri[9]), 0.5f);
            var mouth = Vector2.Lerp(P(ri[9]), P(ri[10]), 0.6f);
            WorldLayout.RiverFlow(below.x, below.y, out var d0, out float s0);
            WorldLayout.RiverFlow(mid.x, mid.y, out var d1, out float s1);
            WorldLayout.RiverFlow(mouth.x, mouth.y, out var d2, out float s2);
            bool inLake = WorldLayout.RiverFlow(lake.x, lake.y, out _, out float s3);
            bool toLake = Vector2.Dot(d1, (lake - mid).normalized) > 0.5f && Vector2.Dot(d2, (lake - mouth).normalized) > 0.5f;
            Step(toLake && s0 > s1 && s1 > s2 && !inLake && s3 == 0,
                 $"水流方向：从瀑布流向湖（朝湖口 {Vector2.Dot(d1, (lake - mid).normalized):0.00}），流速 瀑布下 {s0:0.00} > 中段 {s1:0.00} > 湖口 {s2:0.00} > 湖里 0");
            var drift = RiverDrift.Instance;
            Step(drift && RiverDrift.Count > 40 && RiverDrift.MeanDownstream > 0.3f, $"河面漂浮物（泡沫、花瓣、落叶）{RiverDrift.Count} 个，顺流漂，平均 {RiverDrift.MeanDownstream:0.00} m/s");

            // walking on the water with the koi in the river: the current pushes her downstream
            flow.Pets.SetActive("ink_koi");
            var at = Vector2.Lerp(P(ri[7]), P(ri[8]), 0.5f);
            WorldLayout.RiverDistance(at.x, at.y, out float lv);
            player.Teleport(new Vector3(at.x, lv + 0.4f, at.y), 0);
            yield return new WaitForSeconds(0.6f);
            var a0 = player.transform.position;
            yield return new WaitForSeconds(2f);
            var moved = player.transform.position - a0; moved.y = 0;
            WorldLayout.RiverFlow(at.x, at.y, out var fd, out _);
            float along = Vector2.Dot(new Vector2(moved.x, moved.z), fd);
            Step(player.WaterWalking && along > 0.2f, $"踏水站在河上被水流推向下游：2 秒顺流 {along:0.00} m");
            yield return Shot("55_river_flow");
            flow.Pets.SetActive("cat");
            yield return new WaitForSeconds(0.5f);
        }

        /// <summary>1123d-3: the moon butterfly only on a clear night, resting on the glowing moonflower on the island;
        /// the cat and a line on screen point there; sleeping into the night is clear at least 70 % of the time and
        /// never rainy three times running.</summary>
        IEnumerator MoonCheck(PlayerController player, GameFlow flow, Creature c, CreatureDef def)
        {
            var env = EnvController.Instance; var weather = Weather.Instance;
            var bloom = MoonFlowerBloom.Instance;
            if (!bloom) { Step(false, "月光花不存在（MoonFlowerBloom）"); yield break; }
            float t = 0;
            while (bloom.Bloom < 0.99f && t < 5f) { t += Time.deltaTime; yield return null; }
            var flat = c.transform.position - bloom.Top; 
            var light = bloom.GetComponentInChildren<Light>();
            Step(bloom.Bloom > 0.99f && light && light.enabled && light.intensity > 0.9f && flat.magnitude < 1.2f,
                 $"晴朗夜晚：月光花开放发光（灯光 {(light ? light.intensity : 0):0.0}），月影蝶停在花上（离花心 {flat.magnitude:0.00} m）");

            // from the lake shore, the cat at her side
            flow.Pets.SetActive("cat");
            var isl = WorldLayout.Islands[0];
            var toShore = new Vector2(isl.x, isl.y) - WorldLayout.LakeCentre; toShore = new Vector2(-0.3f, -1f).normalized;
            Vector3 shore = Vector3.zero;
            for (float r = 20f; r < 90f; r += 1f)
            {
                var q = new Vector2(isl.x, isl.y) + toShore * r;
                float ld = WorldLayout.LakeDistance(q.x, q.y);
                if (ld > 1.12f && Creature.GroundHeight(q.x, q.y) > WorldLayout.LakeLevel + 0.4f) { shore = new Vector3(q.x, Creature.GroundHeight(q.x, q.y) + 0.1f, q.y); break; }
            }
            var look = bloom.Top - shore; look.y = 0;
            player.Teleport(shore, Quaternion.LookRotation(look).eulerAngles.y);
            player.SetCameraOrbit(Quaternion.LookRotation(look).eulerAngles.y, 6f);
            var cat = PetSwitcher.Pet("cat");
            if (cat) cat.WarpNear(shore + Quaternion.LookRotation(look) * new Vector3(0.6f, 0, -0.8f), Quaternion.LookRotation(look).eulerAngles.y);
            bool looked = false, hinted = false;
            System.Action onCat = () => looked = true, onHint = () => hinted = true;
            MoonFlowerBloom.CatCue += onCat; MoonFlowerBloom.ShoreHint += onHint;
            bloom.ResetCues();
            t = 0;
            while (t < 4f && !(looked && hinted)) { t += Time.deltaTime; yield return null; }
            yield return new WaitForSeconds(1f);
            MoonFlowerBloom.CatCue -= onCat; MoonFlowerBloom.ShoreHint -= onHint;
            Vector3 cf = cat ? cat.transform.forward : Vector3.zero; cf.y = 0;
            var toIsl = bloom.Top - (cat ? cat.transform.position : shore); toIsl.y = 0;
            float catAng = cat ? Vector3.Angle(cf, toIsl) : 180f;
            Step(hinted && flow.Save.moonHintShown && looked && catAng < 40f,
                 $"晴朗夜晚第一次走到湖岸：屏幕提示月光花；猫转向湖心岛叫了一声（朝向偏差 {catAng:0}°，离岛 {Vector3.Distance(shore, bloom.Top):0} m）");
            yield return new WaitForSeconds(0.6f);
            yield return Shot("56_moonflower_from_shore");

            // rain: the flower closes and the butterfly goes; by day it is not there either
            weather.Set(true, true);
            t = 0;
            while ((c.Current != Creature.State.Hidden || bloom.Bloom > 0.01f) && t < 6f) { t += Time.deltaTime; yield return null; }
            bool goneRain = c.Current == Creature.State.Hidden && bloom.Bloom < 0.01f;
            weather.Set(false, true); env.timeOfDay = 12f;
            t = 0; bool showedDay = false;
            while (t < 2.5f) { t += Time.deltaTime; if (c.Current != Creature.State.Hidden || bloom.Bloom > 0.01f) showedDay = true; yield return null; }
            env.timeOfDay = 21.5f;
            t = 0;
            while (c.Current == Creature.State.Hidden && t < 5f) { t += Time.deltaTime; yield return null; }
            Step(goneRain && !showedDay && c.Current != Creature.State.Hidden, "月影蝶：下雨的夜晚和白天都不出现、月光花合上；晴夜又回到花上");

            // sleeping into the night: two rainy nights in a row, then the third is clear for sure; 70 %+ clear overall
            int row = 0; var seq = new List<bool>();
            for (int i = 0; i < 3; i++) seq.Add(weather.SleptIntoNight(ref row, 0.1));
            int clear = 0, rowR = 0, maxRow = 0; var rng = new System.Random(5);
            for (int i = 0; i < 2000; i++) { if (!weather.SleptIntoNight(ref rowR, rng.NextDouble())) clear++; maxRow = Mathf.Max(maxRow, rowR); }
            Step(seq[0] && seq[1] && !seq[2] && clear >= 1400 && maxRow <= 2,
                 $"床上睡到夜晚：晴天 {clear / 20f:0}%（2000 次），连续两次雨夜后第三次必定晴（{string.Join("/", seq.Select(r => r ? "雨" : "晴"))}），最多连续 {maxRow} 次雨夜");
            weather.Set(false, true);
            env.timeOfDay = 21.5f;
            yield return new WaitForSeconds(0.5f);
        }

        IEnumerator StartleCheck(PlayerController player, Creature c, CreatureDef def, int shotNo)
        {
            var p = c.transform.position;
            var dir = player.transform.position - p; dir.y = 0; dir = dir.sqrMagnitude > 0.01f ? dir.normalized : Vector3.back;
            var stand = p + dir * def.captureRange * 0.55f;
            stand.y = Creature.GroundHeight(stand.x, stand.z) + 0.1f;
            GameInput.SimSneak = false;
            player.Teleport(stand, Quaternion.LookRotation(-dir).eulerAngles.y);
            yield return new WaitForSeconds(0.3f);
            GameInput.SimCapture = true;
            float t = 0;
            // the capture locks onto the nearest rabbit, which may be the other one of the pair: test that one
            while (CaptureSystem.Target == null && t < 1.5f) { t += Time.deltaTime; yield return null; }
            if (CaptureSystem.Target) c = CaptureSystem.Target;
            c.DebugSetAlert(0.9f);
            t = 0;
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
        /// <summary>1123c-A1: run (and jump) straight at each map edge for 7 s; she must stop inside the bounds.
        /// Then drop her under the terrain: she must be put back on safe ground.</summary>
        IEnumerator BoundsCheck(PlayerController player)
        {
            float S = WorldLayout.Size, lim = PlayerController.BoundaryInset;
            var dirs = new[] { ("北", new Vector2(S * 0.5f, S - 32f), 0f), ("东", new Vector2(S - 32f, S * 0.45f), 90f),
                               ("南", new Vector2(S * 0.55f, 32f), 180f), ("西", new Vector2(32f, S * 0.5f), 270f) };
            var res = new List<string>(); bool ok = true;
            foreach (var (name, at, yaw) in dirs)
            {
                player.Teleport(new Vector3(at.x, Creature.GroundHeight(at.x, at.y) + 0.3f, at.y), yaw);
                player.SetCameraOrbit(yaw, 10f);
                yield return new WaitForSeconds(0.3f);
                GameInput.SimMove = new Vector2(0, 1); GameInput.SimRun = true;
                float t = 0, worst = float.MaxValue;
                while (t < 7f)
                {
                    t += Time.deltaTime;
                    if (Mathf.Repeat(t, 0.9f) < Time.deltaTime) { GameInput.SimJump = true; } else GameInput.SimJump = false;
                    var p = player.transform.position;
                    worst = Mathf.Min(worst, Mathf.Min(Mathf.Min(p.x, S - p.x), Mathf.Min(p.z, S - p.z)));
                    yield return null;
                }
                GameInput.SimMove = Vector2.zero; GameInput.SimRun = false; GameInput.SimJump = false;
                ok &= worst >= lim - 0.05f && worst < lim + 3f;   // reached the edge, didn't pass it
                res.Add($"{name} {worst:0.0} m");
            }
            Step(ok, $"地图边界：朝四个方向跑跳 7 秒，离地图边缘最近 {string.Join("、", res)}（限制 {lim} m）");
            yield return Shot("40_bounds_edge");
            int rec = player.Recoveries;
            player.DebugFallThrough();
            yield return new WaitForSeconds(0.5f);
            var q = player.transform.position;
            Step(player.Recoveries == rec + 1 && q.y > Creature.GroundHeight(q.x, q.z) - 0.3f, $"掉出地形后自动回到安全地点（{q.x:0} , {q.z:0}）");
        }

        /// <summary>超分阶段 step 1: anti-aliasing Off / TAA / STP switch actually reaches the camera and the pipeline.</summary>
        /// <summary>
        /// 超分中间件: every backend x quality renders at the size Initialize returned (IN-01 / FR-UP-01), 50 quick
        /// switches without errors (TC-02), a window resize re-initialises (TC-03), GPU time per combination.
        /// </summary>
        IEnumerator UpscaleCheck(PlayerController player)
        {
            var env = EnvController.Instance;
            env.timeOfDay = 16.5f; env.Apply();
            var meadow = new Vector2(WorldLayout.StoneCircle.x + 14f, WorldLayout.StoneCircle.y - 8f);
            player.Teleport(new Vector3(meadow.x, Creature.GroundHeight(meadow.x, meadow.y) + 0.2f, meadow.y), 30);
            player.SetCameraOrbit(30f, 8f); player.SetCameraDistance(1f);
            yield return new WaitForSeconds(1f);
            var s = SaveSystem.Settings; string sb = s.upscaler, sq = s.upscalerQuality;
            var backends = Zfw.Upscaling.UpscalerSelector.Available(Zfw.GpuCaps.GpuCaps.Current).ToList();
            var rep = new List<string>(); bool sizeOk = true;
            foreach (var b in backends)
                foreach (var q in new[] { "Quality", "Performance" })
                {
                    WildPartner.Rendering.UpscalerSettings.Set(b, q);
                    yield return new WaitForSeconds(0.6f);
                    var host = Zfw.Upscaling.UpscalerHost.RenderSize;
                    var asset = WildPartner.Rendering.AntiAliasing.Asset;
                    int urpW = Mathf.RoundToInt(Screen.width * asset.renderScale);
                    bool ok = Mathf.Abs(urpW - host.x) <= 1 && Zfw.Upscaling.UpscalerHost.Active != null && Zfw.Upscaling.UpscalerHost.Active.Name == b;
                    sizeOk &= ok;
                    var res = default(WildPartner.Rendering.GpuTimer.Result);
                    yield return WildPartner.Rendering.GpuTimer.Measure(1.5f, r => res = r);
                    rep.Add($"{b}/{q} {host.x}x{host.y} {res}");
                    yield return Shot($"7x_up_{b.ToLower()}_{q.ToLower()}");
                }
            Step(sizeOk && backends.Count >= 3, $"超分：{backends.Count} 个后端 x 质量/性能，URP 渲染尺寸 = Initialize 返回值（{string.Join("；", rep)}，显示 {Screen.width}x{Screen.height}）");

            // TC-02: 50 switches in a row, then nothing logged
            int before = _exceptions;
            var all = new List<string>(backends) { WildPartner.Rendering.UpscalerSettings.Off };
            var qs = new[] { "NativeAA", "Quality", "Balanced", "Performance" };
            for (int i = 0; i < 50; i++)
            {
                WildPartner.Rendering.UpscalerSettings.Set(all[i % all.Count], qs[(i / all.Count) % qs.Length]);
                yield return null; yield return null;
            }
            yield return new WaitForSeconds(0.5f);
            Step(_exceptions == before, $"超分：连续切换后端和档位 50 次，没有报错（{_exceptions - before} 条）");

            // TC-03: window size change -> the render size is recomputed
            WildPartner.Rendering.UpscalerSettings.Set(backends[0], "Balanced");
            yield return new WaitForSeconds(0.3f);
            int w0 = Screen.width, h0 = Screen.height; var r0 = Zfw.Upscaling.UpscalerHost.RenderSize; int n0 = Zfw.Upscaling.UpscalerHost.Reinitialisations;
            Screen.SetResolution(1280, 720, FullScreenMode.Windowed);
            float t = 0; while ((Screen.width != 1280) && t < 3f) { t += Time.unscaledDeltaTime; yield return null; }
            yield return new WaitForSeconds(0.3f);
            var r1 = Zfw.Upscaling.UpscalerHost.RenderSize;
            bool resized = Screen.width == 1280 && r1 == Zfw.Upscaling.UpscalerMath.RenderSize(new Vector2Int(1280, 720), Zfw.Upscaling.UpscalerQuality.Balanced) && Zfw.Upscaling.UpscalerHost.Reinitialisations > n0;
            Screen.SetResolution(w0, h0, FullScreenMode.Windowed);
            t = 0; while (Screen.width != w0 && t < 3f) { t += Time.unscaledDeltaTime; yield return null; }
            yield return new WaitForSeconds(0.3f);
            Step(resized && Zfw.Upscaling.UpscalerHost.RenderSize == r0, $"超分：窗口 {w0}x{h0} → 1280x720 → 还原，渲染尺寸 {r0.x}x{r0.y} → {r1.x}x{r1.y} → {Zfw.Upscaling.UpscalerHost.RenderSize.x}x{Zfw.Upscaling.UpscalerHost.RenderSize.y}，每次重新初始化");

            // IN-03 / IN-06: the input views and the reactive mask pass
            {
                int ex0 = _exceptions, f0 = WildPartner.Rendering.UpscalerInputsPass.FramesWithMask;
                env.timeOfDay = 13f; env.Apply(); Weather.Instance.Set(true, true);         // rain = reactive particles on screen
                yield return new WaitForSeconds(1.5f);
                WildPartner.Rendering.UpscalerInputsPass.View = WildPartner.Rendering.InputView.MotionVectors;
                GameInput.SimMove = new Vector2(0, 1); yield return new WaitForSeconds(0.6f);
                yield return Shot("7y_view_motion_vectors");
                GameInput.SimMove = Vector2.zero;
                WildPartner.Rendering.UpscalerInputsPass.View = WildPartner.Rendering.InputView.ReactiveMask;
                yield return new WaitForSeconds(0.4f);
                yield return Shot("7y_view_reactive_mask");
                WildPartner.Rendering.UpscalerInputsPass.View = WildPartner.Rendering.InputView.None;
                Weather.Instance.Set(false, true);
                int frames = WildPartner.Rendering.UpscalerInputsPass.FramesWithMask - f0;
                Step(frames > 30 && WildPartner.Rendering.UpscalerInputsPass.LastSourceCount > 0 && _exceptions == ex0,
                     $"超分输入：Reactive Mask 每帧生成（{frames} 帧，{WildPartner.Rendering.UpscalerInputsPass.LastSourceCount} 个来源），运动向量和遮罩视图可切换（截图 7y_*），无报错");
            }
            WildPartner.Rendering.UpscalerSettings.Set(sb, sq);
            yield return new WaitForSeconds(0.3f);
        }

        IEnumerator AntiAliasCheck(PlayerController player, GameFlow flow)
        {
            // the anti-aliasing switch alone: upscaling off for this part (native resolution)
            string upB = SaveSystem.Settings.upscaler, upQ = SaveSystem.Settings.upscalerQuality;
            WildPartner.Rendering.UpscalerSettings.Set(WildPartner.Rendering.UpscalerSettings.Off, "");
            var env = EnvController.Instance;
            env.timeOfDay = 16.5f; env.Apply(); Weather.Instance.Set(false, true);
            var meadow = new Vector2(WorldLayout.StoneCircle.x + 14f, WorldLayout.StoneCircle.y - 8f);
            player.Teleport(new Vector3(meadow.x, Creature.GroundHeight(meadow.x, meadow.y) + 0.2f, meadow.y), 30);
            player.SetCameraOrbit(30f, 8f); player.SetCameraDistance(1f);
            yield return new WaitForSeconds(1.5f);
            var cam = Camera.main;
            var data = cam ? cam.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>() : null;
            var saved = WildPartner.Rendering.AntiAliasing.Mode;
            var report = new List<string>(); bool ok = data != null;
            foreach (var m in new[] { WildPartner.Rendering.AAMode.Off, WildPartner.Rendering.AAMode.TAA, WildPartner.Rendering.AAMode.STP })
            {
                if (!WildPartner.Rendering.AntiAliasing.Supported(m)) { report.Add($"{m} 不支持"); continue; }
                WildPartner.Rendering.AntiAliasing.Set(m, save: false);
                yield return new WaitForSeconds(1.2f);
                var res = default(WildPartner.Rendering.GpuTimer.Result);
                yield return WildPartner.Rendering.GpuTimer.Measure(2f, r => res = r);
                var asset = WildPartner.Rendering.AntiAliasing.Asset;
                bool right = m switch
                {
                    WildPartner.Rendering.AAMode.Off => data.antialiasing == UnityEngine.Rendering.Universal.AntialiasingMode.None && asset.upscalingFilter != UnityEngine.Rendering.Universal.UpscalingFilterSelection.STP,
                    WildPartner.Rendering.AAMode.TAA => data.antialiasing == UnityEngine.Rendering.Universal.AntialiasingMode.TemporalAntiAliasing && asset.upscalingFilter != UnityEngine.Rendering.Universal.UpscalingFilterSelection.STP,
                    _ => asset.upscalingFilter == UnityEngine.Rendering.Universal.UpscalingFilterSelection.STP && asset.msaaSampleCount == 1,
                };
                ok &= right && data.renderPostProcessing && !cam.allowMSAA;
                report.Add($"{WildPartner.Rendering.AntiAliasing.Label(m)} {res}");
                yield return Shot($"6{(int)m + 1}_aa_{m.ToString().ToLower()}");
            }
            WildPartner.Rendering.AntiAliasing.Set(saved, save: false);
            Step(ok, $"抗锯齿开关：关 / TAA / STP 都生效到相机和管线（{string.Join("，", report)}，同一草地机位，关垂直同步 2 秒平均，{Screen.width}x{Screen.height}）");
            // 画质分级扩展: each tier pushes its own grass / LOD / SSAO / water probe values into the game
            {
                var was = Zfw.QualityTiers.QualityTiers.Current; var wasMode = Zfw.QualityTiers.QualityTiers.Mode;
                var rep = new List<string>(); bool tok = true;
                foreach (Zfw.QualityTiers.Tier tier in Enum.GetValues(typeof(Zfw.QualityTiers.Tier)))
                {
                    Zfw.QualityTiers.QualityTiers.Set(tier, manual: true);
                    yield return null; yield return null;
                    var w = WildPartner.Rendering.WildPartnerTier.Current;
                    var ao = WildPartner.Rendering.WildPartnerTier.Features().FirstOrDefault(f => f && f.name.Contains("AmbientOcclusion"));
                    bool good = Mathf.Approximately(GrassRenderer.Quality, w.grassQuality) && Mathf.Approximately(QualitySettings.lodBias, w.lodBias)
                                && (ao == null || ao.isActive == w.ssao) && WaterProbe.Resolution == w.waterProbeResolution
                                && Camera.main.GetUniversalAdditionalCameraData().antialiasing == (WildPartner.Rendering.AntiAliasing.Mode == WildPartner.Rendering.AAMode.Off ? UnityEngine.Rendering.Universal.AntialiasingMode.None : UnityEngine.Rendering.Universal.AntialiasingMode.TemporalAntiAliasing);
                    tok &= good;
                    rep.Add($"{tier}：草 {w.grassQuality}、LOD {w.lodBias}、SSAO {(w.ssao ? "开" : "关")}、反射 {w.waterProbeResolution}");
                }
                Zfw.QualityTiers.QualityTiers.Set(was, manual: wasMode == Zfw.QualityTiers.SelectionMode.Manual);
                yield return null;
                Step(tok, $"画质分级扩展：切换四档后游戏参数随之生效（{string.Join("；", rep)}），抗锯齿设置跟着新管线走");
            }
            WildPartner.Rendering.UpscalerSettings.Set(upB, upQ);
            {
            }
        }

        /// <summary>1123e-4: music > ambience > footsteps; no effect louder than the music; settings rows exist.</summary>
        IEnumerator AudioMixCheck(PlayerController player, GameFlow flow)
        {
            var pa = WildPartner.Audio.ProceduralAudio.Instance;
            if (!pa) { Step(false, "混音：找不到 ProceduralAudio"); yield break; }
            pa.BuildLoudnessReport();
            var L = WildPartner.Audio.ProceduralAudio.LoudnessReport;
            float R(string k) => L.TryGetValue(k, out var v) ? v.x : 0f;
            float P(string k) => L.TryGetValue(k, out var v) ? v.y : 0f;
            float music = R("音乐");
            float amb = Mathf.Max(R("风"), R("雨"), R("瀑布（近处）"));
            float ambMin = Mathf.Min(R("风"), R("雨"));
            string[] steps = { "脚步（草地走路）", "脚步（木地板走路）", "脚步（土路走路）", "脚步（浅水走路）" };
            float stepMax = steps.Max(R);
            bool order = music > 0 && music > amb && ambMin > stepMax && R("脚步（草地跑步）") > R("脚步（草地走路）");
            string[] fx = { "猫叫", "呼噜", "捕捉成功", "开门", "脚步（草地跑步）" };
            bool noneOver = fx.All(k => R(k) <= music && P(k) <= P("音乐") * 1.05f);
            Step(order && noneOver, "混音层次：音乐 " + R("音乐").ToString("0.000") + " > 环境声（风 " + R("风").ToString("0.000") + "、雨 " + R("雨").ToString("0.000") + "、瀑布 " + R("瀑布（近处）").ToString("0.000") + "）> 脚步（走 " + stepMax.ToString("0.000") + "、跑 " + R("脚步（草地跑步）").ToString("0.000") + "）；猫叫 " + R("猫叫").ToString("0.000") + "、呼噜 " + R("呼噜").ToString("0.000") + " 都不超过音乐");
            var labels = WildPartner.UI.SettingsRegistry.Rows.Select(r => r.label).ToList();
            bool rows = new[] { "主音量", "音乐", "音效", "环境声" }.All(labels.Contains);
            float savedM = SaveSystem.Settings.musicVolume;
            SaveSystem.Settings.musicVolume = 0.3f; SaveSystem.SaveSettings();
            var reread = SaveSystem.LoadSettings();   // straight from settings.json
            bool persisted = reread != null && Mathf.Abs(reread.musicVolume - 0.3f) < 0.01f;
            SaveSystem.Settings.musicVolume = savedM; SaveSystem.SaveSettings();
            Step(rows && persisted, $"设置菜单有主音量 / 音乐 / 音效 / 环境声四项，改动写进设置存档（{(persisted ? "读回一致" : "读回不一致")}）");
            yield return null;
        }

        /// <summary>1123e-3: the moon butterfly glows and scatters stardust only at night; less in the rain.</summary>
        IEnumerator ButterflyGlowCheck(PlayerController player, GameFlow flow)
        {
            var env = EnvController.Instance;
            flow.Pets.SetActive("moon_butterfly");
            var bf = FindObjectsByType<Creature>(FindObjectsSortMode.None).FirstOrDefault(c => c.defId == "moon_butterfly" && c.Current == Creature.State.Companion);
            var bg = bf ? bf.GetComponent<ButterflyGlow>() : null;
            if (!bg) { Step(false, "月影蝶发光：找不到伙伴月影蝶"); yield break; }
            float saved = env.timeOfDay;
            IEnumerator At(float hour, bool rain, float wait)
            {
                env.timeOfDay = hour; Weather.Instance.Set(rain, true); env.Apply();
                yield return new WaitForSeconds(wait);
            }
            yield return At(12f, false, 3.5f);
            float day = bg.Glow; int dayDust = bg.DustAlive;
            yield return Shot("59_butterfly_day");
            yield return At(18.5f, false, 3.5f);
            float dusk = bg.Glow;
            yield return At(22f, false, 3.5f);
            float night = bg.Glow; int nightDust = bg.DustAlive;
            yield return Shot("60_butterfly_night");
            yield return At(22f, true, 3.5f);
            float rainy = bg.Glow;
            var dr = bg.Dust ? bg.Dust.GetComponent<ParticleSystemRenderer>() : null;
            bool reactive = dr && WildPartner.Rendering.ReactiveMaskSources.All.Contains(dr) && dr.motionVectorGenerationMode == MotionVectorGenerationMode.ForceNoMotion;
            Step(day < 0.02f && dayDust == 0 && dusk > 0.15f && dusk < 0.85f && night > 0.95f && nightDust > 5 && rainy > 0.25f && rainy < 0.6f && reactive,
                 $"月影蝶只在夜晚发光：白天 {day:0.00}（星尘 {dayDust}）→ 黄昏 {dusk:0.00} → 夜晚 {night:0.00}（星尘 {nightDust} 粒）；雨夜 {rainy:0.00}；星尘无运动向量、已写入 Reactive Mask（{reactive}）");
            Weather.Instance.Set(false, true);
            env.timeOfDay = saved; env.Apply();
            flow.Pets.SetActive("cat");
            yield return null;
        }

        /// <summary>1123c-B2: one pet out at a time, J cycles, each ability only with its pet.</summary>
        IEnumerator PetCheck(PlayerController player, GameFlow flow)
        {
            var all = FindObjectsByType<Creature>(FindObjectsSortMode.None);
            int Shown() => all.Count(c => c.Current == Creature.State.Companion && c.PetShown);
            var meadow = new Vector2(WorldLayout.StoneCircle.x + 14f, WorldLayout.StoneCircle.y - 8f);
            player.Teleport(new Vector3(meadow.x, Creature.GroundHeight(meadow.x, meadow.y) + 0.2f, meadow.y), 0);
            flow.Pets.SetActive("cat");
            yield return new WaitForSeconds(0.5f);
            var seen = new List<string>(); bool oneAtATime = Shown() == 1;
            for (int i = 0; i < PetSwitcher.Order.Length; i++)
            {
                GameInput.SimSwitchPet = true; yield return null; GameInput.SimSwitchPet = false;
                yield return new WaitForSeconds(0.4f);
                seen.Add(PetSwitcher.Active); oneAtATime &= Shown() == 1;
                var cur = all.FirstOrDefault(c => c.defId == PetSwitcher.Active && c.Current == Creature.State.Companion);
                oneAtATime &= cur && cur.PetShown && Vector3.Distance(cur.transform.position, player.transform.position) < 6f;
            }
            Step(oneAtATime && seen.Distinct().Count() == PetSwitcher.Order.Length, $"按 J 轮换宠物：{string.Join("→", seen.Select(id => CreatureDefs.Get(id).name))}，每次只有一只在身边");

            // jump height: cat vs moon rabbit
            float[] tops = new float[2];
            string[] who = { "cat", "moon_rabbit" };
            for (int k = 0; k < 2; k++)
            {
                flow.Pets.SetActive(who[k]);
                yield return new WaitForSeconds(0.6f);
                float y0 = player.transform.position.y, top = y0, tj = 0;
                GameInput.SimJump = true; yield return null; GameInput.SimJump = false;
                while (tj < 2f) { tj += Time.deltaTime; top = Mathf.Max(top, player.transform.position.y); yield return null; }
                tops[k] = top - y0;
            }
            Step(tops[1] > tops[0] * 1.5f, $"月影兔在身边时跳得更高：{tops[1]:0.00} m（猫 {tops[0]:0.00} m）");

            // 1123e-2: the koi lives in a water bottle at her hip on land, jumps into the water near it
            flow.Pets.SetActive("ink_koi");
            yield return new WaitForSeconds(0.8f);
            var koiPet = KoiCompanion.Instance;
            var koiC = all.FirstOrDefault(c => c.defId == "ink_koi" && c.Current == Creature.State.Companion);
            bool inBottle = koiPet && koiC && koiPet.BottleShown && koiPet.Current == KoiCompanion.Mode.Bottle
                            && Vector3.Distance(koiC.transform.position, koiPet.BottleCentre) < 0.08f;
            float hipD = koiPet ? Vector3.Distance(koiPet.Bottle.position, player.transform.position + Vector3.up * 0.75f) : 99f;
            GameInput.SimMove = new Vector2(0, 1); yield return new WaitForSeconds(1.2f); GameInput.SimMove = Vector2.zero;
            bool stillIn = koiPet && koiPet.Current == KoiCompanion.Mode.Bottle && Vector3.Distance(koiC.transform.position, koiPet.BottleCentre) < 0.08f;
            yield return Shot("57_koi_bottle");
            Step(inBottle && stillIn && hipD < 0.6f, $"锦鲤在陆地上住在腰间的水瓶里（瓶离腰 {hipD:0.00} m，走动时仍在瓶中）");
            var spot = FishingSpot.All.FirstOrDefault(f => f.label == "木桥边") ?? FishingSpot.All.FirstOrDefault();
            if (spot)
            {
                player.Teleport(spot.stand, spot.yaw);
                float tw = 0; while (tw < 4f && koiPet.Current != KoiCompanion.Mode.Water) { tw += Time.deltaTime; yield return null; }
                yield return new WaitForSeconds(1.0f);
                var kp = koiC.transform.position;
                WorldLayout.WaterSurface(kp.x, kp.z, out float klv);
                bool swims = koiPet.Current == KoiCompanion.Mode.Water && kp.y < klv && Vector3.Distance(kp, player.transform.position) < 4.5f;
                yield return Shot("58_koi_in_water");
                var away = spot.stand - Quaternion.Euler(0, spot.yaw, 0) * Vector3.forward * 12f;
                player.Teleport(new Vector3(away.x, Creature.GroundHeight(away.x, away.z) + 0.2f, away.z), spot.yaw + 180f);
                tw = 0; while (tw < 4f && koiPet.Current != KoiCompanion.Mode.Bottle) { tw += Time.deltaTime; yield return null; }
                bool back = koiPet.Current == KoiCompanion.Mode.Bottle;
                Step(swims && back, $"靠近水边锦鲤跃入水中游动（在水面下 {klv - kp.y:0.00} m，离她 {Vector3.Distance(kp, spot.stand):0.0} m），离开水边跃回瓶中（{(back ? "已回瓶" : "没回瓶")}，跃出水面 {KoiCompanion.Leaps} 次）");
            }
            else Step(false, "锦鲤入水：没有钓鱼点可用来找水边");

            // koi: walk on the lake, then switching away puts her on the shore
            var lake = WorldLayout.LakeCentre + new Vector2(-WorldLayout.LakeRadii.x * 0.6f, 0);
            player.Teleport(new Vector3(lake.x, WorldLayout.LakeLevel + 0.6f, lake.y), 90);
            yield return new WaitForSeconds(0.6f);
            GameInput.SimMove = new Vector2(0, 1);
            yield return new WaitForSeconds(2f);
            GameInput.SimMove = Vector2.zero;
            var pw = player.transform.position;
            bool onWater = player.WaterWalking && Mathf.Abs(pw.y - (WorldLayout.LakeLevel + 0.15f)) < 0.4f;
            var kw = all.First(c => c.defId == "ink_koi" && c.Current == Creature.State.Companion).transform.position;
            var flat = new Vector2(kw.x - pw.x, kw.z - pw.z).magnitude;
            bool underFeet = KoiCompanion.Instance && KoiCompanion.Instance.Current == KoiCompanion.Mode.Water && flat < 1.0f && kw.y < pw.y;
            int ripples = KoiCompanion.Ripples;
            yield return Shot("41_koi_water_walk");
            Step(underFeet && ripples > 0, $"踏水时锦鲤在脚下游动（水平距离 {flat:0.00} m），每步泛起淡红涟漪（{ripples} 圈）");
            GameInput.SimSwitchPet = true; yield return null; GameInput.SimSwitchPet = false;
            yield return new WaitForSeconds(0.6f);
            Step(KoiCompanion.Instance && !KoiCompanion.Instance.BottleShown, "切走锦鲤后腰间水瓶消失");
            var pa = player.transform.position;
            bool ashore = !player.WaterWalking && !(WorldLayout.WaterSurface(pa.x, pa.z, out float wl) && wl > Creature.GroundHeight(pa.x, pa.z) + 0.05f);
            Step(onWater && ashore, $"锦鲤在身边时踏水而行（脚下高度 {pw.y - WorldLayout.LakeLevel - 0.15f:+0.00;-0.00} m）；在水面按 J 先回到岸上（{(ashore ? "已上岸" : "仍在水里")}）");
            flow.Pets.SetActive("cat");
            yield return null;
        }

        /// <summary>1123c-B3: deep water -> swimming (floating, slower, no jumping); the island undertow pushes her
        /// back; with the koi she walks over the water onto the island.</summary>
        IEnumerator SwimCheck(PlayerController player, GameFlow flow)
        {
            flow.Pets.SetActive("cat");
            EnvController.Instance.timeOfDay = 11f;
            Weather.Instance.Set(false, true);
            var isl = WorldLayout.Islands[0];
            // deep open water at least 20 m from both islands' shores (no undertow)
            var spot = WorldLayout.LakeCentre;
            for (int i = 0; i < 32; i++)
            {
                float a = i * Mathf.PI / 16f;
                var q = WorldLayout.LakeCentre + new Vector2(Mathf.Cos(a) * WorldLayout.LakeRadii.x, Mathf.Sin(a) * WorldLayout.LakeRadii.y) * 0.55f;
                if (WorldLayout.Islands.All(s2 => Vector2.Distance(q, new Vector2(s2.x, s2.y)) > s2.z + 20f) && Creature.GroundHeight(q.x, q.y) < WorldLayout.LakeLevel - 2f) { spot = q; break; }
            }
            player.Teleport(new Vector3(spot.x, WorldLayout.LakeLevel - 0.6f, spot.y), 0);
            yield return new WaitForSeconds(1.0f);
            bool swimming = player.Swimming;
            var p0 = player.transform.position;
            GameInput.SimMove = new Vector2(0, 1);
            yield return new WaitForSeconds(3f);
            GameInput.SimMove = Vector2.zero;
            var p1 = player.transform.position;
            Debug.Log($"[Smoke] swim: Speed {player.Speed:0.00} running {player.Running} sneaking {player.Sneaking} swimming {player.Swimming} p0 {p0} p1 {p1}");
            float sp = Vector2.Distance(new Vector2(p0.x, p0.z), new Vector2(p1.x, p1.z)) / 3f;
            float lvl = WorldLayout.LakeLevel + 0.15f;
            bool floating = Mathf.Abs(p1.y - (lvl - PlayerController.SwimSink)) < 0.25f;
            float y0 = player.transform.position.y, top = y0;
            GameInput.SimJump = true; yield return null; GameInput.SimJump = false;
            for (float t = 0; t < 1f; t += Time.deltaTime) { top = Mathf.Max(top, player.transform.position.y); yield return null; }
            GameInput.SimMove = new Vector2(0, 1); yield return new WaitForSeconds(0.6f);
            yield return Shot("42_swim");
            GameInput.SimMove = Vector2.zero;
            Step(swimming && player.Swimming && floating && sp > 0.8f && sp < player.walkSpeed && top - y0 < 0.15f,
                 $"深水里自动游泳：浮在水面（脚下比水面低 {lvl - p1.y:0.00} m），速度 {sp:0.00} m/s（走路 {player.walkSpeed:0.0}），按空格不跳（{top - y0:0.00} m）");

            // undertow: swim (fast) straight at the island for 6 s
            var from = new Vector2(isl.x - isl.z - 10f, isl.y);
            player.Teleport(new Vector3(from.x, WorldLayout.LakeLevel - 0.6f, from.y), 90);
            yield return new WaitForSeconds(0.5f);
            bool hint = false; System.Action h = () => hint = true; PlayerController.UndertowHit += h;
            float minD = 999f;
            GameInput.SimRun = true;
            for (float t = 0; t < 6f; t += Time.deltaTime)
            {
                var q = player.transform.position;
                // keep heading at the island whatever the camera does
                var to = new Vector3(isl.x - q.x, 0, isl.y - q.z);
                player.transform.rotation = Quaternion.LookRotation(to);
                GameInput.SimMove = CamRel(to);
                minD = Mathf.Min(minD, to.magnitude - isl.z);
                yield return null;
            }
            GameInput.SimRun = false; GameInput.SimMove = Vector2.zero;
            PlayerController.UndertowHit -= h;
            Step(minD > 2f && hint && player.Swimming, $"湖心岛暗流：游 6 秒离岛岸最近 {minD:0.0} m，被推回，出现提示「水流太急，也许需要水中的伙伴帮忙」");

            // koi: up onto the water, walk to the island
            flow.Pets.SetActive("ink_koi");
            yield return new WaitForSeconds(0.5f);
            bool lifted = !player.Swimming && player.WaterWalking;
            bool climbedBank = false;
            for (float t = 0; t < 20f; t += Time.deltaTime)
            {
                var q = player.transform.position;
                var to = new Vector3(isl.x - q.x, 0, isl.y - q.z);
                climbedBank |= player.Climbing;
                // on the bank: W climbs up; otherwise walk at the island
                GameInput.SimMove = player.Climbing || player.Mantling ? new Vector2(0, 1) : CamRel(to);
                if (to.magnitude < isl.z - 6f && !player.Climbing && !player.Mantling) break;
                yield return null;
            }
            GameInput.SimMove = Vector2.zero;
            var pi = player.transform.position;
            float di = Vector2.Distance(new Vector2(pi.x, pi.z), new Vector2(isl.x, isl.y));
            bool onIsland = di < isl.z && !(WorldLayout.WaterSurface(pi.x, pi.z, out float wl2) && wl2 > Creature.GroundHeight(pi.x, pi.z) + 0.05f);
            yield return Shot("43_koi_island");
            Step(lifted && onIsland, $"游泳时换成锦鲤：浮上水面踏水走到湖心岛{(climbedBank ? "，爬上岸坡" : "")}（离岛中心 {di:0.0} m，岛半径 {isl.z:0.0} m）");
            flow.Pets.SetActive("cat");
            yield return null;
        }

        /// <summary>1123c-B4: climb the watchtower wall - grab, Ctrl lets go, climb to the top, climb over, reward.</summary>
        IEnumerator ClimbCheck(PlayerController player)
        {
            EnvController.Instance.timeOfDay = 10f;
            var tw = WorldLayout.Watchtower;
            float gy = Creature.GroundHeight(tw.x, tw.y);
            // a side of the tower with stone wall at waist and at 3 m (not the stairs)
            Vector3 stand = Vector3.zero, face = Vector3.forward; bool found = false;
            for (int i = 0; i < 16 && !found; i++)
            {
                float a = i * Mathf.PI / 8f;
                var dir = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                var o = new Vector3(tw.x, 0, tw.y) + dir * 9f;
                float g = Creature.GroundHeight(o.x, o.z);
                // the wall must run on unbroken (no window sills sticking out) from 1 m up to 6 m
                bool plain = true;
                if (!Physics.Raycast(o + Vector3.up * (g + 1.2f - o.y), -dir, out var h1, 9f) || !IsTower(h1.collider.transform) || Mathf.Abs(h1.normal.y) > 0.4f) plain = false;
                float prev = plain ? h1.distance : 0f;
                for (float hh = 1.6f; plain && hh <= 6f; hh += 0.4f)
                {
                    // the tower tapers, so only sudden steps count (window sills, ledges)
                    if (!Physics.Raycast(o + Vector3.up * (g + hh - o.y), -dir, out var h2, 9f) || !IsTower(h2.collider.transform) || Mathf.Abs(h2.distance - prev) > 0.15f) plain = false;
                    else prev = h2.distance;
                }
                if (plain)
                {
                    var p = h1.point + h1.normal * 0.55f; p.y = Creature.GroundHeight(p.x, p.z) + 0.05f;
                    stand = p; face = -new Vector3(h1.normal.x, 0, h1.normal.z).normalized; found = true;
                    Debug.Log($"[Smoke] climb side {i}: wall {h1.collider.name} at {h1.point}");
                }
            }
            if (!found) { Step(false, "攀爬：瞭望塔四周找不到能爬的墙面"); yield break; }
            player.Teleport(stand, Quaternion.LookRotation(face).eulerAngles.y);
            player.SetCameraOrbit(Quaternion.LookRotation(face).eulerAngles.y + 150f, 10f);
            yield return new WaitForSeconds(0.6f);
            GameInput.SimMove = CamRel(face);
            float t = 0;
            while (!player.Climbing && t < 2f) { t += Time.deltaTime; GameInput.SimMove = CamRel(face); yield return null; }
            bool grabbed = player.Climbing;
            GameInput.SimMove = new Vector2(0, 1);   // on the wall: W = up
            yield return new WaitForSeconds(1.2f);
            GameInput.SimMove = Vector2.zero;
            float h0 = player.transform.position.y - gy;
            GameInput.SimSneak = true; yield return null; yield return null; GameInput.SimSneak = false;
            bool letGo = !player.Climbing;
            yield return new WaitForSeconds(1.0f);
            Step(grabbed && letGo && h0 > 0.5f, $"攀爬：走向瞭望塔石墙自动抓住，往上爬了 {h0:0.0} m，按 Ctrl 松手落下");

            // all the way up
            float maxH = 0f, minSt = 100f; bool over = false, shot = false, top = false;
            System.Action onTop = () => top = true; PlayerController.TowerTopReached += onTop;
            System.Action onOver = () => over = true; player.ClimbedOver += onOver;
            t = 0;
            while (t < 30f && !over)
            {
                t += Time.deltaTime;
                if (!player.Climbing && !player.Mantling)
                {
                    var f = player.transform.forward; if (Vector3.Dot(f, face) < 0.5f) f = face;
                    GameInput.SimMove = CamRel(face);
                }
                else GameInput.SimMove = new Vector2(0, 1);   // on the wall: W = up
                maxH = Mathf.Max(maxH, player.transform.position.y - gy);
                minSt = Mathf.Min(minSt, player.Stamina);
                if (!shot && player.Climbing && player.transform.position.y - gy > 3f) { shot = true; yield return Shot("44_climb_tower"); }
                yield return null;
            }
            GameInput.SimMove = Vector2.zero;
            yield return new WaitForSeconds(1.2f);
            float endH = player.transform.position.y - gy;
            player.SetCameraOrbit(player.transform.eulerAngles.y + 180f, 25f);
            yield return new WaitForSeconds(0.4f);
            yield return Shot("45_tower_top");
            PlayerController.TowerTopReached -= onTop; player.ClimbedOver -= onOver;
            Step(over && endH > 4f && top && !player.Climbing, $"攀爬：爬上瞭望塔，翻上塔顶（离地 {endH:0.0} m，用时 {t:0.0} 秒，体力最低 {minSt:0}），出现塔顶奖励提示");
            var pe = player.transform.position;
            Step(Physics.Raycast(pe + Vector3.up * 0.5f, Vector3.down, 1.0f), "攀爬：翻上去后站在塔顶平台上（不穿模、不悬空）");
        }

        /// <summary>1123c-B5: scenery animals exist, and from anywhere on dry land some life is within ~40 m
        /// (about 25 s of walking).</summary>
        IEnumerator AmbientCheck(PlayerController player)
        {
            var amb = AmbientLife.Instance;
            Step(amb && amb.ButterflyCount >= 20 && amb.DuckCount >= 6 && amb.BirdCount >= 9,
                 $"环境小动物：蝴蝶 {(amb ? amb.ButterflyCount : 0)} 只、水鸟 {(amb ? amb.DuckCount : 0)} 只、飞鸟 {(amb ? amb.BirdCount : 0)} 只（不可捕捉）");
            if (!amb) yield break;
            var life = new List<Vector2>();
            life.AddRange(amb.LifeSpots.Select(p => new Vector2(p.x, p.z)));
            life.AddRange(CreatureDefs.All.Where(d => d.homes != null).SelectMany(d => d.homes));
            if (FishSchools.Instance) life.AddRange(FishSchools.Instance.Schools.Select(sc => new Vector2(sc.centre.x, sc.centre.z)));
            float worst = 0; Vector2 worstAt = default; int n = 0;
            float lo = PlayerController.BoundaryInset + 2f, hi = WorldLayout.Size - PlayerController.BoundaryInset - 2f;
            for (float x = lo; x <= hi; x += 10f)
                for (float z = lo; z <= hi; z += 10f)
                {
                    if (WorldLayout.WaterSurface(x, z, out _)) continue;
                    var p = new Vector2(x, z); n++;
                    float d = life.Min(l => Vector2.Distance(l, p));
                    if (d > worst) { worst = d; worstAt = p; }
                }
            Step(worst <= 42f, $"任何地方 25 秒步行（约 40 m）内都有动物：{n} 个采样点里最远的离动物 {worst:0} m（{worstAt.x:0}, {worstAt.y:0}），天上另有飞鸟");
            yield return null;
        }

        static bool IsTower(Transform t) { for (; t; t = t.parent) if (t.name.StartsWith("Watchtower")) return true; return false; }

        /// <summary>World direction -> the move input that walks that way with the current camera.</summary>
        static Vector2 CamRel(Vector3 dir)
        {
            var cam = Camera.main; dir.y = 0; dir.Normalize();
            if (!cam) return new Vector2(dir.x, dir.z);
            var f = Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up).normalized; var r = Vector3.Cross(Vector3.up, f);
            return new Vector2(Vector3.Dot(dir, r), Vector3.Dot(dir, f));
        }

        IEnumerator RideCheck(PlayerController player, Creature deer, int shotNo)
        {
            GameFlow.Instance.Pets.SetActive("white_deer");   // 1123c-B2: only the current pet can be ridden
            yield return null;
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
            Step(n >= 8 && n <= 10 && sizes, $"鱼群：{n} 群，每群 {(fs ? string.Join("/", fs.Schools.Select(x => x.fish.Count)) : "-")} 条");
            if (!fs || n == 0) yield break;
            var env = EnvController.Instance; env.timeOfDay = 11f;
            var sc = fs.Schools.OrderBy(x => Vector2.Distance(new Vector2(x.centre.x, x.centre.z), WorldLayout.Bridge)).First();   // the river below the bridge
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
