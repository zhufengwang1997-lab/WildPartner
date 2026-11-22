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
        int _frame;
        bool _record;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (!Environment.GetCommandLineArgs().Contains("-showcase")) return;
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
                var sc = fs.Schools[0];   // the river below the bridge
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
