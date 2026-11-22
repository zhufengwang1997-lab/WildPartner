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
    /// Records the hair/skirt check (试玩反馈 1123b-4) as numbered JPG frames at a fixed 30 fps game clock:
    ///   WildPartner -motiondemo -demoout=&lt;dir&gt;   (tools/motion_demo.sh turns them into a GIF)
    /// Side view on the meadow: stand, run, a sharp turn, stop, two jumps, then a drop from 8 m.
    /// </summary>
    public class MotionDemo : MonoBehaviour
    {
        string _out;
        int _frame;
        bool _record;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (!Environment.GetCommandLineArgs().Contains("-motiondemo")) return;
            new GameObject("MotionDemo").AddComponent<MotionDemo>();
        }

        IEnumerator Start()
        {
            string arg = Environment.GetCommandLineArgs().FirstOrDefault(a => a.StartsWith("-demoout="));
            _out = arg != null ? arg.Substring(9) : Path.Combine(Application.persistentDataPath, "motiondemo");
            Directory.CreateDirectory(_out);
            yield return new WaitForSeconds(1.5f);
            var flow = GameFlow.Instance;
            flow.NewGame();
            float t = 0;
            yield return null;
            while (CabinInterior.Busy && t < 10f) { t += Time.deltaTime; yield return null; }
            CabinInterior.SetInside(false);
            var player = PlayerController.Instance;
            var a = CreatureDefs.Get("moon_rabbit").anchors[0];
            var start = new Vector3(a.x - 8, Creature.GroundHeight(a.x - 8, a.y) + 0.2f, a.y);
            // the camera looks north and stays there; "move right" runs her east across the screen: a side view
            player.Teleport(start, 90f);
            player.SetCameraOrbit(0f, 5f);
            player.SetCameraDistance(0.5f);
            yield return new WaitForSeconds(1f);

            Time.captureFramerate = 30;
            _record = true;
            StartCoroutine(Capture());
            yield return Hold(0.8f);                                                  // standing
            GameInput.SimRun = true; GameInput.SimMove = new Vector2(1, 0);
            yield return Hold(2.4f);                                                  // run (to the right)
            GameInput.SimMove = new Vector2(0, -1);
            yield return Hold(0.7f);                                                  // sharp turn towards the camera
            GameInput.SimMove = Vector2.zero; GameInput.SimRun = false;
            yield return Hold(1.6f);                                                  // stop + settle
            for (int j = 0; j < 2; j++)
            {
                GameInput.SimJump = true; yield return null; GameInput.SimJump = false;
                yield return Hold(1.4f);                                              // jump, land, bounce
            }
            var p = player.transform.position;
            player.Teleport(p + Vector3.up * 8f, 90f);
            player.SetCameraOrbit(0f, 5f);
            yield return Hold(3.2f);                                                  // 8 m drop, landing, settle
            _record = false;
            Time.captureFramerate = 0;
            Debug.Log($"[Demo] {_frame} frames -> {_out}");
            yield return new WaitForSecondsRealtime(0.5f);
            Application.Quit();
        }

        static IEnumerator Hold(float s) { yield return new WaitForSeconds(s); }

        StreamWriter _csv;
        Transform _hairA, _hairC, _skirtF, _skirtB;
        /// <summary>Per frame: vertical speed, airborne, hair lift (tip angle from straight down, + = up/back) and
        /// the front/back skirt panel angles from straight down -> motion.csv for the bounce check.</summary>
        void Log()
        {
            var pc = PlayerController.Instance;
            if (_csv == null)
            {
                foreach (var t in pc.GetComponentsInChildren<Transform>())
                    switch (t.name) { case "HairA.L": _hairA = t; break; case "HairC.L": _hairC = t; break; case "Skirt.F": _skirtF = t; break; case "Skirt.B": _skirtB = t; break; }
                _csv = new StreamWriter(Path.Combine(_out, "motion.csv"));
                _csv.WriteLine("frame,t,speed,vy,airborne,hair_deg,skirtF_deg,skirtB_deg,hair_lift_cm,skirtB_lift_cm");
                _prevY = pc.transform.position.y;
                _sm = pc.GetComponent<SecondaryMotion>();
            }
            float vy = (pc.transform.position.y - _prevY) / Mathf.Max(Time.deltaTime, 1e-4f); _prevY = pc.transform.position.y;
            Vector3 tip = _hairC.position + _hairC.up * 0.14f;
            float hair = Vector3.Angle(tip - _hairA.position, Vector3.down);
            float sf = Vector3.Angle(_skirtF.up, Vector3.down), sb = Vector3.Angle(_skirtB.up, Vector3.down);
            _csv.WriteLine($"{_frame},{_frame / 30f:0.000},{pc.Speed:0.00},{vy:0.00},{(pc.Airborne ? 1 : 0)},{hair:0.0},{sf:0.0},{sb:0.0},{(_sm ? _sm.DebugHairLift * 100 : 0):0.0},{(_sm ? _sm.DebugSkirtLift * 100 : 0):0.0}");
            _csv.Flush();
        }
        float _prevY;
        SecondaryMotion _sm;

        IEnumerator Capture()
        {
            while (_record)
            {
                yield return new WaitForEndOfFrame();
                Log();
                var tex = ScreenCapture.CaptureScreenshotAsTexture();
                File.WriteAllBytes(Path.Combine(_out, $"f{_frame++:0000}.jpg"), tex.EncodeToJPG(85));
                Destroy(tex);
            }
        }
    }
}
