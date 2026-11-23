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
    /// Records the clips for 试玩反馈 1123b-6/7/8 in daylight with fixed camera set-ups (the smoke test's shots
    /// are taken wherever the checks happen to be, often at night or in rain):
    ///   WildPartner -showcase -demoout=&lt;dir&gt;   (tools/showcase.sh turns each folder into a GIF)
    /// rabbit/  a pair of moon rabbits hopping and grazing, then one startled (ears up) and relaxing again
    /// fish/    a school in the lake shallows; the witch walks up, it scatters, she backs off, it regroups
    /// deer/    two white deer walking ~3 s and grazing;  ride/  riding the caught one, side view
    /// Stills are written as still_*.jpg next to the folders.
    /// </summary>
    public class Showcase : MonoBehaviour
    {
        string _out, _dir;
        bool _mode1123d, _mode1123e;
        int _frame;
        bool _record;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            var args = Environment.GetCommandLineArgs();
            if (args.Contains("-showcase1123d")) { var s = new GameObject("Showcase").AddComponent<Showcase>(); s._mode1123d = true; return; }
            if (args.Contains("-showcase1123e")) { var s = new GameObject("Showcase").AddComponent<Showcase>(); s._mode1123e = true; return; }
            if (!args.Contains("-showcase")) return;
            new GameObject("Showcase").AddComponent<Showcase>();
        }

        IEnumerator Start()
        {
            string arg = Environment.GetCommandLineArgs().FirstOrDefault(a => a.StartsWith("-demoout="));
            _out = arg != null ? arg.Substring(9) : Path.Combine(Application.persistentDataPath, "showcase");
            Directory.CreateDirectory(_out);
            yield return new WaitForSeconds(1.5f);
            var flow = GameFlow.Instance;
            flow.NewGame();
            if (_mode1123d) { yield return Run1123d(); yield break; }
            if (_mode1123e) { yield return Run1123e(); Application.Quit(); yield break; }
            float t = 0;
            yield return null;
            while (CabinInterior.Busy && t < 10f) { t += Time.deltaTime; yield return null; }
            CabinInterior.SetInside(false);
            var env = EnvController.Instance;
            var player = PlayerController.Instance;
            var creatures = FindObjectsByType<Creature>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            env.timeOfDay = 9.5f;
            Weather.Instance.Set(false, true);
            var cat = creatures.First(c => c.defId == "cat");

            // ---------------- moon rabbits ----------------
            var r0 = creatures.First(c => c.defId == "moon_rabbit" && c.instance == 0);
            var r1 = creatures.First(c => c.defId == "moon_rabbit" && c.instance == 1);
            t = 0;
            while ((r0.Current == Creature.State.Hidden || r1.Current == Creature.State.Hidden) && t < 5f) { t += Time.deltaTime; yield return null; }
            Vector3 mid = (r0.transform.position + r1.transform.position) * 0.5f;
            // the side with the clearest view: no bush / rock / trunk between her and the pair
            Vector3 dir = OpenSide(mid, 4.2f);
            yield return StandLooking(player, cat, mid + dir * 6f, mid, -20f, 20f, 0.5f);
            GameInput.SimSneak = true;
            yield return new WaitForSeconds(2f);
            // close camera beside the pair, following the first rabbit
            Vector3 side = Vector3.Cross(Vector3.up, dir).normalized;
            Follow(r0.transform, side * 2.4f + dir * 0.8f + Vector3.up * 0.75f, Vector3.up * 0.2f);
            yield return Record("rabbit", 5f, 3f, "6_rabbits_pair");
            // a capture spoiled: ears up, she keeps still, it relaxes
            Vector3 rp = r0.transform.position;
            Vector3 away = player.transform.position - rp; away.y = 0; away.Normalize();
            GameInput.SimSneak = false;
            yield return StandLooking(player, cat, rp + away * 2.1f, rp, -30f, 10f, 0.5f);
            // a fixed camera off to the side, both of them in frame
            Vector3 sd = Vector3.Cross(Vector3.up, away).normalized;
            Frame(rp + away * 1.0f + sd * 2.6f + Vector3.up * 0.9f, rp + away * 0.9f + Vector3.up * 0.35f);
            r0.DebugSetAlert(0.9f);
            GameInput.SimCapture = true;
            _dir = "rabbit"; _record = true; Time.captureFramerate = 30; StartCoroutine(Capture());
            t = 0;
            while (!r0.IsStartled && t < 3f) { t += Time.deltaTime; yield return null; }
            GameInput.SimCapture = false;
            yield return new WaitForSeconds(0.35f);
            Still("6_rabbit_ears_up");
            yield return new WaitForSeconds(2.4f);
            _record = false; Time.captureFramerate = 0;
            Unframe();
            yield return null;

            // ---------------- fish school ----------------
            env.timeOfDay = 11f;
            var fs = FishSchools.Instance;
            if (fs && fs.Schools.Count > 0)
            {
                var sc = fs.Schools.OrderBy(x => Vector2.Distance(new Vector2(x.centre.x, x.centre.z), WorldLayout.Bridge)).First();   // the river below the bridge
                Vector3 c = sc.centre;
                // nearest dry bank: walk out from the school along 24 rays until the ground is above the water
                // ...on the sun side, so the camera looks away from the sun's glare on the water
                Vector3 sunFwd = RenderSettings.sun ? RenderSettings.sun.transform.forward : Vector3.forward; sunFwd.y = 0; sunFwd.Normalize();
                Vector3 bank = c + Vector3.right * 7f; float bestD = 1e9f, bestScore = -1e9f;
                for (int i = 0; i < 24; i++)
                {
                    float a = i * Mathf.PI / 12f;
                    var dirA = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                    for (float r = 3f; r < 14f; r += 0.5f)
                    {
                        var q = c + dirA * r;
                        if (Creature.GroundHeight(q.x, q.z) > sc.surface + 0.05f)
                        {
                            float score = Vector3.Dot(-dirA, sunFwd) * 6f - r;
                            if (score > bestScore) { bestScore = score; bestD = r; bank = q; }
                            break;
                        }
                    }
                }
                bank += (bank - c).normalized * 0.8f;
                Vector3 fishMid = Vector3.zero; foreach (var f in sc.fish) fishMid += f.p; fishMid /= sc.fish.Count;
                yield return StandLooking(player, cat, bank, (c + fishMid) * 0.5f, 0f, 36f, 0.6f);
                Debug.Log($"[Showcase] fish school 0 centre {c} bank {bank} ({bestD:0.0} m) fish0 {sc.fish[0].p} active {sc.root.activeSelf} cam {Camera.main.transform.position}");
                yield return new WaitForSeconds(1.5f);
                yield return Record("fish", 2.5f, 1.2f, "7_fish_school");
                _dir = "fish"; _record = true; Time.captureFramerate = 30; StartCoroutine(Capture());
                Debug.Log($"[Showcase] fish spread calm {sc.Spread:0.00}");
                GameInput.SimMove = new Vector2(0, 1);      // a few steps down to the water's edge
                yield return new WaitForSeconds(0.9f);
                GameInput.SimMove = Vector2.zero;
                yield return new WaitForSeconds(0.35f);
                Still("7_fish_scatter");
                Debug.Log($"[Showcase] fish spread after approach {sc.Spread:0.00} scattered {sc.scattered}");
                yield return new WaitForSeconds(0.6f);
                GameInput.SimMove = new Vector2(0, -1);
                yield return new WaitForSeconds(1.4f);
                GameInput.SimMove = Vector2.zero;
                yield return new WaitForSeconds(4f);
                Still("7_fish_regrouped");
                _record = false; Time.captureFramerate = 0;
                yield return null;
            }

            // ---------------- white deer ----------------
            env.timeOfDay = 10f;
            var d0 = creatures.First(c => c.defId == "white_deer" && c.instance == 0);
            var d1 = creatures.First(c => c.defId == "white_deer" && c.instance == 1);
            Vector3 dm = (d0.transform.position + d1.transform.position) * 0.5f;
            GameInput.SimSneak = true;
            yield return StandLooking(player, cat, dm + OpenSide(dm, 11f) * 11f, dm, -10f, 14f, 0.6f);
            yield return new WaitForSeconds(1f);
            Vector3 toDeer = (dm - player.transform.position); toDeer.y = 0; toDeer.Normalize();
            Follow(d0.transform, -toDeer * 6.5f + Vector3.Cross(Vector3.up, toDeer) * 2.5f + Vector3.up * 1.4f, Vector3.up * 0.9f);
            yield return Record("deer", 9f, 6f, "8_deer_walk_graze");
            Unframe();

            // catch d0 the way the smoke test does: sneak up, hold E
            for (int attempt = 0; attempt < 3 && !flow.Save.HasCaught("white_deer"); attempt++)
            {
                var p = d0.transform.position;
                var dd = player.transform.position - p; dd.y = 0; dd.Normalize();
                var st = p + dd * 2.3f; st.y = Creature.GroundHeight(st.x, st.z) + 0.1f;
                player.Teleport(st, Quaternion.LookRotation(-dd).eulerAngles.y);
                yield return new WaitForSeconds(0.3f);
                GameInput.SimCapture = true;
                t = 0;
                while (!flow.Save.HasCaught("white_deer") && t < 5f) { t += Time.deltaTime; yield return null; }
                GameInput.SimCapture = false;
                yield return new WaitForSeconds(1f);
            }
            GameInput.SimSneak = false;
            if (flow.Save.HasCaught("white_deer"))
            {
                yield return new WaitForSeconds(1.5f);
                d0.Mount(player);
                yield return null;
                // open meadow west of the rabbits, running east across the screen (camera looks north)
                var field = new Vector3(150f, 0, 160f); field.y = Creature.GroundHeight(field.x, field.z) + 0.2f;
                player.Teleport(field, 90f);
                player.SetCameraOrbit(0f, 6f);
                player.SetCameraDistance(1.05f);
                cat.WarpNear(field + new Vector3(-2, 0, -2), 90f);
                yield return new WaitForSeconds(1f);
                Still("8_riding_standing");
                _dir = "ride"; _record = true; Time.captureFramerate = 30; StartCoroutine(Capture());
                yield return new WaitForSeconds(0.5f);
                GameInput.SimMove = new Vector2(1, 0);
                yield return new WaitForSeconds(1.5f);
                GameInput.SimRun = true;
                yield return new WaitForSeconds(1.2f);
                Still("8_riding_run");
                yield return new WaitForSeconds(1.3f);
                GameInput.SimRun = false; GameInput.SimMove = Vector2.zero;
                yield return new WaitForSeconds(1.2f);
                _record = false; Time.captureFramerate = 0;
            }
            Debug.Log($"[Showcase] done -> {_out}");
            yield return new WaitForSecondsRealtime(0.5f);
            Application.Quit();
        }

        // ---------------------------------------------------------------- 试玩反馈 1123e
        IEnumerator Run1123e()
        {
            float t = 0;
            yield return null;
            while (CabinInterior.Busy && t < 10f) { t += Time.deltaTime; yield return null; }
            var env = EnvController.Instance; var player = PlayerController.Instance;
            var ci = CabinInterior.Instance;
            Weather.Instance.Set(false, true);

            // ---- 1: the inside of the door at day, dusk and night ----
            CabinInterior.SetInside(true);
            var toDoor = ci.insideDoor - ci.roomCenter; toDoor.y = 0;
            float yaw = Quaternion.LookRotation(toDoor).eulerAngles.y;
            var at = ci.roomCenter - toDoor.normalized * 0.8f;
            player.Teleport(at, yaw);
            DoorDiagnostics(ci);
            foreach (var (hour, name) in new[] { (13f, "day"), (18.3f, "dusk"), (22f, "night") })
            {
                env.timeOfDay = hour; env.Apply();
                player.SetCameraOrbit(yaw + 18f, 4f); player.SetCameraDistance(0.75f);
                yield return new WaitForSeconds(1.2f);
                Still($"1_door_inside_{name}");
                yield return new WaitForSeconds(0.3f);
            }
            if (!_door1123eOnly) { yield return Run1123eCreatures(env, player, GameFlow.Instance); yield break; }
            {
                env.timeOfDay = 22f; env.Apply();
                yield return new WaitForSeconds(0.5f);
                var camT = Camera.main.transform;
                foreach (var r in FindObjectsByType<Renderer>(FindObjectsSortMode.None))
                    if (r.enabled && r.isVisible && r.sharedMaterial && r.sharedMaterial.renderQueue >= 2450)
                        Debug.Log($"[DoorVis] {r.name} ({r.GetType().Name}) parent {(r.transform.parent ? r.transform.parent.name : "-")} shader {r.sharedMaterial.shader.name} q {r.sharedMaterial.renderQueue} center {r.bounds.center} size {r.bounds.size} dist {Vector3.Distance(camT.position, r.bounds.center):0.0}");
                Debug.Log($"[DoorVis] camera at {camT.position} fwd {camT.forward}");
                foreach (var l in FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                    if (Vector3.Distance(l.transform.position, camT.position) < 12f || l.type == LightType.Directional)
                        Debug.Log($"[DoorLight] {l.name} parent {(l.transform.parent ? l.transform.parent.name : "-")} {l.type} on {l.enabled && l.gameObject.activeInHierarchy} I={l.intensity:0.00} range {l.range:0.0} pos {l.transform.position} col {l.color}");
                foreach (var r in FindObjectsByType<Renderer>(FindObjectsSortMode.None))
                    if (r.enabled && r.isVisible && Vector3.Distance(r.bounds.center, camT.position) < 6f)
                        Debug.Log($"[DoorNear] {r.name} parent {(r.transform.parent ? r.transform.parent.name : "-")} shader {(r.sharedMaterial ? r.sharedMaterial.shader.name : "-")} center {r.bounds.center} size {r.bounds.size}");
                var leafR = ci.insideHinge.GetComponentsInChildren<Renderer>().First(r => r.name == "WoodDoor");
                var day = FindObjectsByType<Renderer>(FindObjectsSortMode.None).First(r => r.name == "DoorDaylight");
                day.enabled = false; yield return new WaitForSeconds(0.5f); Still("x_no_daylight_panel"); yield return new WaitForSeconds(0.3f); day.enabled = true;
                leafR.enabled = false; yield return new WaitForSeconds(0.5f); Still("x_no_leaf"); yield return new WaitForSeconds(0.3f); leafR.enabled = true;
                var beam = FindObjectsByType<Renderer>(FindObjectsSortMode.None).First(r => r.name == "DoorBacking").sharedMaterial;
                var keep = leafR.sharedMaterial; leafR.sharedMaterial = beam; yield return new WaitForSeconds(0.5f); Still("x_leaf_beam_mat"); yield return new WaitForSeconds(0.3f); leafR.sharedMaterial = keep;
                var pr = player.GetComponentsInChildren<Renderer>();
                foreach (var r in pr) r.enabled = false;
                yield return new WaitForSeconds(0.5f); Still("x_no_player"); yield return new WaitForSeconds(0.3f);
                foreach (var r in pr) r.enabled = true;
                Debug.Log($"[DoorVis] player at {player.transform.position} door {ci.insideDoor} room {ci.roomCenter}");
                foreach (var v in FindObjectsByType<UnityEngine.Rendering.Volume>(FindObjectsSortMode.None)) v.enabled = false;
                yield return new WaitForSeconds(0.5f); Still("x_no_post"); yield return new WaitForSeconds(0.3f);
                yield break;
            }
        }
        bool _door1123eOnly = Environment.GetCommandLineArgs().Contains("-dooronly");

        IEnumerator Run1123eCreatures(EnvController env, PlayerController player, GameFlow flow)
        {
            CabinInterior.SetInside(false);
            var creatures = FindObjectsByType<Creature>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var id in new[] { "ink_koi", "moon_butterfly" })
            {
                var c = creatures.FirstOrDefault(k => k.defId == id && k.Current != Creature.State.Companion);
                if (c) { c.gameObject.SetActive(true); CaptureSystem.Award(c); }
                yield return null;
            }
            env.timeOfDay = 10.5f; env.Apply(); Weather.Instance.Set(false, true);

            // ---- 2a: the koi in its bottle at her hip, walking ----
            flow.Pets.SetActive("ink_koi");
            var meadow = new Vector2(WorldLayout.StoneCircle.x + 14f, WorldLayout.StoneCircle.y - 8f);
            player.Teleport(new Vector3(meadow.x, Creature.GroundHeight(meadow.x, meadow.y) + 0.2f, meadow.y), 0);
            // 1123e-2 leftover: the bottle (15 cm, at her RIGHT hip, half inside the skirt's flare) was a speck in
            // the orbit shot from her left. For this one still the camera leaves Cinemachine and is aimed at the
            // bottle from ~1 m to her right-front; the near-camera dither is eased off so she does not fade.
            player.SetCameraOrbit(95f, 8f); player.SetCameraDistance(0.7f);
            yield return new WaitForSeconds(1.5f);
            var koi = FindAnyObjectByType<KoiCompanion>();
            var cam = Camera.main; var brain = cam.GetComponent<Unity.Cinemachine.CinemachineBrain>();
            var fade0 = EnvController.NearFade;
            if (koi && koi.BottleShown && brain)
            {
                brain.enabled = false; EnvController.NearFade = new Vector2(0.05f, 0.15f);
                var pt = player.transform;
                for (int i = 0; i < 20; i++)   // a few frames: the bottle swings, keep it centred
                {
                    var bc = koi.BottleCentre;
                    var at = bc + (pt.right * 0.85f + pt.forward * 0.55f).normalized * 1.0f + Vector3.up * 0.18f;
                    cam.transform.SetPositionAndRotation(at, Quaternion.LookRotation(bc + Vector3.up * 0.12f - at));
                    yield return null;
                }
                var vp = cam.WorldToViewportPoint(koi.BottleCentre);
                Debug.Log($"[Showcase] koi bottle at viewport {vp} (distance {vp.z:0.00} m)");
                Still("2_koi_bottle_close");
                yield return new WaitForSeconds(0.3f);   // the still is taken at the end of a frame
                EnvController.NearFade = fade0; brain.enabled = true;
                if (player.vcam) player.vcam.PreviousStateIsValid = false;
            }
            else { Debug.Log("[Showcase] koi bottle not shown"); Still("2_koi_bottle_close"); }
            yield return new WaitForSeconds(0.5f);
            GameInput.SimMove = new Vector2(0, 0.45f);
            yield return Record("clip_2_koi_bottle_walk", 4f, 2f, "2_koi_bottle_walk");
            GameInput.SimMove = Vector2.zero;

            // ---- 2b: by the water it leaps in, swims; leaving the water it leaps back ----
            var spot = FishingSpot.All.FirstOrDefault(f => f.label == "木桥边") ?? FishingSpot.All.First();
            var back = spot.stand - Quaternion.Euler(0, spot.yaw, 0) * Vector3.forward * 6f;
            player.Teleport(new Vector3(back.x, Creature.GroundHeight(back.x, back.z) + 0.2f, back.z), spot.yaw);
            player.SetCameraOrbit(spot.yaw + 60f, 14f); player.SetCameraDistance(0.9f);
            yield return new WaitForSeconds(1f);
            GameInput.SimMove = new Vector2(0, 0.6f);
            StartCoroutine(Record("clip_2_koi_into_water", 9f, 4.5f, "2_koi_in_water"));
            yield return new WaitForSeconds(2.6f);
            GameInput.SimMove = Vector2.zero;
            yield return new WaitForSeconds(6.6f);

            // ---- 2c: walking on the lake: the koi under her feet, red rings at each step ----
            var lake = WorldLayout.LakeCentre + new Vector2(-WorldLayout.LakeRadii.x * 0.6f, 0);
            player.Teleport(new Vector3(lake.x, WorldLayout.LakeLevel + 0.6f, lake.y), 90);
            player.SetCameraOrbit(90f + 150f, 26f); player.SetCameraDistance(0.85f);
            yield return new WaitForSeconds(1f);
            GameInput.SimMove = new Vector2(0, 0.55f);
            yield return Record("clip_2_koi_water_walk", 5f, 3f, "2_koi_water_walk");
            GameInput.SimMove = Vector2.zero;

            // ---- 3: the moon butterfly by day, at dusk and at night (stardust) ----
            flow.Pets.SetActive("moon_butterfly");
            player.Teleport(new Vector3(meadow.x, Creature.GroundHeight(meadow.x, meadow.y) + 0.2f, meadow.y), 0);
            player.SetCameraOrbit(160f, 6f); player.SetCameraDistance(0.55f);
            foreach (var (hour, name) in new[] { (12f, "day"), (18.6f, "dusk"), (22f, "night") })
            {
                env.timeOfDay = hour; env.Apply();
                yield return new WaitForSeconds(3.2f);
                Still($"3_butterfly_{name}");
                yield return new WaitForSeconds(0.3f);
            }
            GameInput.SimMove = new Vector2(0, 0.8f);
            yield return Record("clip_3_butterfly_stardust", 5f, 2.5f, "3_butterfly_stardust");
            GameInput.SimMove = Vector2.zero;
            Weather.Instance.Set(true, true);
            yield return new WaitForSeconds(3.2f);
            Still("3_butterfly_rainy_night");
            Weather.Instance.Set(false, true);
            flow.Pets.SetActive("cat");
        }

        static void DoorDiagnostics(CabinInterior ci)
        {
            if (!ci.insideHinge) { Debug.Log("[Door] no inside hinge"); return; }
            foreach (var r in ci.insideHinge.GetComponentsInChildren<Renderer>())
                foreach (var m in r.sharedMaterials)
                {
                    if (!m) continue;
                    string e = m.HasProperty("_Emission") ? m.GetFloat("_Emission").ToString("0.00") : "-";
                    string c = m.HasProperty("_Cull") ? m.GetFloat("_Cull").ToString("0") : "-";
                    string col = m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor").ToString() : "-";
                    string rim = m.HasProperty("_RimColor") ? m.GetColor("_RimColor").ToString() : "-";
                    Debug.Log($"[Door] {r.name}: mat {m.name} shader {m.shader.name} emission {e} cull {c} base {col} rim {rim} probe {r.reflectionProbeUsage} lightprobe {r.lightProbeUsage} bounds {r.bounds.center}");
                }
            foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None))
                if (l.enabled && l.intensity > 0 && (l.type == LightType.Directional || Vector3.Distance(l.transform.position, ci.insideDoor) < l.range + 1))
                    Debug.Log($"[Door] light {l.name} {l.type} I={l.intensity:0.00} range {l.range:0.0} dist {Vector3.Distance(l.transform.position, ci.insideDoor):0.0} col {l.color}");
            foreach (var r in FindObjectsByType<Renderer>(FindObjectsSortMode.None))
                if (Vector3.Distance(r.bounds.center, ci.insideDoor) < 1.5f)
                    Debug.Log($"[Door] near renderer {r.name} mat {(r.sharedMaterial ? r.sharedMaterial.name : "-")} shader {(r.sharedMaterial ? r.sharedMaterial.shader.name : "-")} enabled {r.enabled}");
        }

        // ---------------------------------------------------------------- 试玩反馈 1123d clips
        IEnumerator Run1123d()
        {
            float t = 0;
            yield return null;
            while (CabinInterior.Busy && t < 10f) { t += Time.deltaTime; yield return null; }
            CabinInterior.SetInside(false);
            var env = EnvController.Instance; var player = PlayerController.Instance; var flow = GameFlow.Instance;
            var creatures = FindObjectsByType<Creature>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            var cat = creatures.First(c => c.defId == "cat");
            env.timeOfDay = 10.5f; Weather.Instance.Set(false, true);

            // ---- 2: walk from below the waterfall along the river to the lake ----
            var ri = WorldLayout.RiverIn;
            Vector2 P(Vector3 v) => new Vector2(v.x, v.y);
            var way = new System.Collections.Generic.List<Vector3>();
            for (int i = 6; i < 10; i++)
                for (float u = 0; u < 1f; u += 0.25f)
                {
                    var c = Vector2.Lerp(P(ri[i]), P(ri[i + 1]), u);
                    var dir = (P(ri[i + 1]) - P(ri[i])).normalized; var side = new Vector2(dir.y, -dir.x);   // right bank looking downstream
                    Vector2 best = c + side * 9f;
                    for (float k = 7f; k < 14f; k += 0.5f)
                    {
                        var q = c + side * k;
                        if (!WorldLayout.WaterSurface(q.x, q.y, out float lv) || lv < Creature.GroundHeight(q.x, q.y) - 0.1f) { best = q; break; }
                    }
                    way.Add(new Vector3(best.x, Creature.GroundHeight(best.x, best.y) + 0.1f, best.y));
                }
            var d0 = way[1] - way[0]; d0.y = 0;
            player.Teleport(way[0], Quaternion.LookRotation(d0).eulerAngles.y);
            cat.WarpNear(way[0] - d0.normalized * 1.5f, Quaternion.LookRotation(d0).eulerAngles.y);
            yield return new WaitForSeconds(1f);
            _dir = "river"; _record = true; Time.captureFramerate = 30; StartCoroutine(Capture());
            int wi = 1; t = 0;
            GameInput.SimRun = true;
            while (wi < way.Count && t < 40f)
            {
                var to = way[wi] - player.transform.position; to.y = 0;
                if (to.magnitude < 2.5f) { wi++; continue; }
                float yaw = Quaternion.LookRotation(to).eulerAngles.y;
                // camera behind and to her right, looking down over the river on her left
                player.SetCameraOrbit(yaw + 28f, 22f);
                player.SetCameraDistance(1.25f);
                GameInput.SimMove = new Vector2(0, 1);
                if (wi == way.Count / 2 && !_still) Still("2_river_mid");
                t += Time.deltaTime;
                yield return null;
            }
            GameInput.SimMove = Vector2.zero; GameInput.SimRun = false;
            Still("2_river_lake");
            yield return new WaitForSeconds(1.5f);
            _record = false; Time.captureFramerate = 0;
            yield return null;

            // ---- 1: fishing at the bridge jetty, the koi on the second cast ----
            var spot = FishingSpot.All.First();
            player.Teleport(spot.stand + Vector3.up * 0.1f - Quaternion.Euler(0, spot.yaw, 0) * Vector3.forward * 0.6f, spot.yaw);
            player.SetCameraOrbit(spot.yaw + 20f, 12f); player.SetCameraDistance(1f);
            cat.WarpNear(spot.stand - Quaternion.Euler(0, spot.yaw, 0) * Vector3.forward * 1.6f, spot.yaw);
            yield return new WaitForSeconds(1.5f);
            Still("1_fishing_spot");
            yield return new WaitForSeconds(0.3f);
            var fs = FishingSystem.Instance;
            _dir = "fishing"; _record = true; Time.captureFramerate = 30; StartCoroutine(Capture());
            FishingSystem.ForceWait = 3.5f; FishingSystem.ForceKoi = true;
            GameInput.SimInteract = true; yield return null; GameInput.SimInteract = false;
            t = 0;
            while (fs.Current != FishingSystem.Phase.Bite && t < 8f) { t += Time.deltaTime; yield return null; }
            yield return new WaitForSeconds(0.2f);
            Still("1_fishing_bite");
            yield return new WaitForSeconds(0.4f);
            GameInput.SimInteract = true; yield return null; GameInput.SimInteract = false;
            t = 0; bool st = false;
            while ((fs.Current == FishingSystem.Phase.Reel || fs.Current == FishingSystem.Phase.Bite) && t < 15f)
            {
                t += Time.deltaTime;
                GameInput.SimCapture = fs.Tension < 0.5f;
                if (!st && t > 2.5f) { st = true; Still("1_fishing_reel"); }
                yield return null;
            }
            GameInput.SimCapture = false;
            yield return new WaitForSeconds(0.5f);
            Still("1_fishing_koi_out");
            yield return new WaitForSeconds(2f);
            _record = false; Time.captureFramerate = 0;
            FishingSystem.ForceWait = null; FishingSystem.ForceKoi = null;
            yield return null;

            // ---- 3: a clear night - the moonflower glowing on the island, seen from the shore, then close ----
            env.timeOfDay = 21.5f; Weather.Instance.Set(false, true);
            var bloom = MoonFlowerBloom.Instance;
            var isl = WorldLayout.Islands[0];
            Vector3 shore = Vector3.zero;
            var toShore = new Vector2(-0.3f, -1f).normalized;
            for (float r = 20f; r < 90f; r += 1f)
            {
                var q = new Vector2(isl.x, isl.y) + toShore * r;
                if (WorldLayout.LakeDistance(q.x, q.y) > 1.12f && Creature.GroundHeight(q.x, q.y) > WorldLayout.LakeLevel + 0.4f) { shore = new Vector3(q.x, Creature.GroundHeight(q.x, q.y) + 0.1f, q.y); break; }
            }
            var look = bloom.Top - shore; look.y = 0;
            float ly = Quaternion.LookRotation(look).eulerAngles.y;
            player.Teleport(shore, ly);
            player.SetCameraOrbit(ly, 6f); player.SetCameraDistance(1f);
            cat.WarpNear(shore + Quaternion.Euler(0, ly, 0) * new Vector3(0.7f, 0, -0.6f), ly);
            t = 0;
            while (bloom.Bloom < 0.99f && t < 5f) { t += Time.deltaTime; yield return null; }
            yield return new WaitForSeconds(1.5f);
            _dir = "moon"; _record = true; Time.captureFramerate = 30; StartCoroutine(Capture());
            yield return new WaitForSeconds(1.2f);
            Still("3_moon_from_shore");
            yield return new WaitForSeconds(3f);
            var bf = creatures.First(c => c.defId == "moon_butterfly");
            var camSide = Quaternion.Euler(0, ly + 150f, 0) * Vector3.forward;
            Frame(bloom.Top + camSide * 1.3f + Vector3.up * 0.35f, bloom.Top + Vector3.up * 0.05f);
            yield return new WaitForSeconds(1.5f);
            Still("3_moon_butterfly_close");
            yield return new WaitForSeconds(3f);
            _record = false; Time.captureFramerate = 0;
            Unframe();
            Debug.Log($"[Showcase] 1123d done -> {_out}");
            yield return new WaitForSecondsRealtime(0.5f);
            Application.Quit();
        }

        /// <summary>Direction (of 16) from `target` at distance `r` with nothing but terrain and creatures in
        /// between at eye height, and grass-free ground to stand on; falls back to east.</summary>
        static Vector3 OpenSide(Vector3 target, float r)
        {
            Vector3 best = Vector3.right; float bestScore = -1e9f;
            for (int i = 0; i < 16; i++)
            {
                float a = i * Mathf.PI / 8f;
                var d = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                var p = target + d * r; p.y = Creature.GroundHeight(p.x, p.z);
                Vector3 from = p + Vector3.up * 1.6f, to = target + Vector3.up * 0.4f;
                int blocks = 0;
                foreach (var h in Physics.RaycastAll(from, to - from, Vector3.Distance(from, to)))
                    if (!(h.collider is TerrainCollider) && !h.collider.GetComponentInParent<Creature>() && !h.collider.GetComponentInParent<PlayerController>()) blocks++;
                if (WorldLayout.WaterSurface(p.x, p.z, out float lv) && lv > p.y) blocks += 5;
                // bushes and small plants have no colliders: test the vegetation list (anything but flowers)
                var veg = FindFirstObjectByType<VegetationSpawner>();
                if (veg)
                    foreach (var batch in veg.batches)
                    {
                        if (!batch.prefab || batch.prefab.name.Contains("Flower")) continue;
                        foreach (var it in batch.items)
                        {
                            var q = new Vector2(it.x, it.z);
                            Vector2 a2 = new Vector2(p.x, p.z), b2 = new Vector2(target.x, target.z), ab = b2 - a2;
                            float tt = Mathf.Clamp01(Vector2.Dot(q - a2, ab) / ab.sqrMagnitude);
                            if (Vector2.Distance(q, a2 + ab * tt) < 1.6f) blocks++;
                        }
                    }
                float score = -blocks * 10f - Mathf.Abs(p.y - target.y);
                if (score > bestScore) { bestScore = score; best = d; }
            }
            return best;
        }

        // ---- a separate close-up camera, drawn over the game camera (the HUD stays on top) ----
        Camera _cam;
        Transform _follow;
        Vector3 _offset, _lookOffset;

        void EnsureCam()
        {
            if (_cam) return;
            var main = Camera.main;
            _cam = new GameObject("ShowcaseCam").AddComponent<Camera>();
            if (main) { _cam.CopyFrom(main); _cam.depth = main.depth + 5; }
            _cam.fieldOfView = 45f;
            _cam.nearClipPlane = 0.05f;
            var data = _cam.gameObject.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
            data.renderPostProcessing = true;
        }

        void Frame(Vector3 eye, Vector3 look)
        {
            EnsureCam();
            _follow = null;
            _cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(look - eye));
            _cam.enabled = true;
        }

        void Follow(Transform t, Vector3 offset, Vector3 lookOffset)
        {
            EnsureCam();
            _follow = t; _offset = offset; _lookOffset = lookOffset;
            _cam.transform.position = t.position + offset;
            _cam.transform.rotation = Quaternion.LookRotation(t.position + lookOffset - _cam.transform.position);
            _cam.enabled = true;
        }

        void Unframe() { if (_cam) _cam.enabled = false; _follow = null; }

        void LateUpdate()
        {
            if (!_cam || !_cam.enabled || !_follow) return;
            float k = 1f - Mathf.Exp(-2.5f * Time.deltaTime);
            Vector3 eye = Vector3.Lerp(_cam.transform.position, _follow.position + _offset, k);
            eye.y = Mathf.Max(eye.y, Creature.GroundHeight(eye.x, eye.z) + 0.5f);
            _cam.transform.position = eye;
            var want = Quaternion.LookRotation(_follow.position + _lookOffset - eye);
            _cam.transform.rotation = Quaternion.Slerp(_cam.transform.rotation, want, k * 2f);
        }

        IEnumerator StandLooking(PlayerController player, Creature cat, Vector3 at, Vector3 look, float yawOffset, float pitch, float dist)
        {
            at.y = Creature.GroundHeight(at.x, at.z) + 0.15f;
            var d = look - at; d.y = 0;
            float yaw = Quaternion.LookRotation(d).eulerAngles.y;
            player.Teleport(at, yaw);
            player.SetCameraOrbit(yaw + yawOffset, pitch);
            player.SetCameraDistance(dist);
            cat.WarpNear(at - d.normalized * 1.2f + Quaternion.Euler(0, yaw, 0) * Vector3.right * 0.6f, yaw);
            yield return new WaitForSeconds(0.6f);
        }

        IEnumerator Record(string dir, float seconds, float stillAt, string still)
        {
            _dir = dir; _record = true; Time.captureFramerate = 30;
            StartCoroutine(Capture());
            yield return new WaitForSeconds(stillAt);
            Still(still);
            yield return new WaitForSeconds(seconds - stillAt);
            _record = false; Time.captureFramerate = 0;
            yield return null;
        }

        bool _still; string _stillName;
        void Still(string name)
        {
            _still = true; _stillName = name;
            if (!_record) StartCoroutine(OneShot());
        }

        IEnumerator OneShot()
        {
            yield return new WaitForEndOfFrame();
            if (!_still) yield break;
            var tex = ScreenCapture.CaptureScreenshotAsTexture();
            File.WriteAllBytes(Path.Combine(_out, $"still_{_stillName}.jpg"), tex.EncodeToJPG(92));
            _still = false;
            Destroy(tex);
        }

        IEnumerator Capture()
        {
            string dir = Path.Combine(_out, _dir);
            Directory.CreateDirectory(dir);
            while (_record)
            {
                yield return new WaitForEndOfFrame();
                var tex = ScreenCapture.CaptureScreenshotAsTexture();
                File.WriteAllBytes(Path.Combine(dir, $"f{_frame++:00000}.jpg"), tex.EncodeToJPG(88));
                if (_still) { File.WriteAllBytes(Path.Combine(_out, $"still_{_stillName}.jpg"), tex.EncodeToJPG(92)); _still = false; }
                Destroy(tex);
            }
        }
    }
}
