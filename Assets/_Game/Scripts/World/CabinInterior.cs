using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using WildPartner.Core;
using WildPartner.Creatures;
using WildPartner.Player;

namespace WildPartner.World
{
    /// <summary>
    /// Going in and out of the cabin (试玩反馈 1123b-3). At the front door "按 E 开门": the door swings inwards with
    /// a creak, the screen fades to black, the witch and her companions are moved to the interior room (built by
    /// WorldBuilder.BuildInterior, outside the map) and the screen fades back in while the inside door swings shut.
    /// The same from the inside door. Indoors the bed sleeps (save + day/night switch) and the book on the table
    /// opens the journal. A new game starts with the witch waking up in the bed.
    /// Every transition is a camera cut: PlayerController.Teleport raises CameraCut, which is where the upscaler
    /// resets its temporal history (IN-08, ResetHistory()).
    /// </summary>
    public class CabinInterior : MonoBehaviour
    {
        public static CabinInterior Instance { get; private set; }
        public static bool Inside => EnvController.Indoors;
        /// <summary>Interaction prompt for the HUD (null when there is nothing to do here).</summary>
        public static string Prompt { get; private set; }
        public static bool Busy => Instance && Instance._busy;
        public static event Action DoorOpened, DoorClosed;

        public Transform outsideLeaf, insideHinge;
        public Vector3 outsideDoor, outsideSpot, insideDoor, insideSpot, bed, bedLie, bedStand, book, roomCenter;
        public float outsideYaw, insideYaw, bedYaw;
        public Light[] lights, windowLights;
        public Light fireLight;
        public Material windowMaterial;

        const float OpenAngle = 70f;
        bool _busy;
        float _outSign = 1, _inSign = 1;
        Quaternion _outClosed, _inClosed;
        Image _fade;
        float _windowBase0 = 2.4f, _windowBase1 = 1.8f;

        void Awake()
        {
            Instance = this;
            if (outsideLeaf) { _outClosed = outsideLeaf.localRotation; _outSign = OpenSign(outsideLeaf, outsideDoor - new Vector3(WorldLayout.CabinFacing.x, 0, WorldLayout.CabinFacing.y) * 2f); }
            if (insideHinge) { _inClosed = insideHinge.localRotation; _inSign = OpenSign(insideHinge, roomCenter); }
            if (windowLights != null && windowLights.Length > 1) { _windowBase0 = windowLights[0].intensity; _windowBase1 = windowLights[1].intensity; }

            var canvasGo = new GameObject("CabinFade", typeof(Canvas), typeof(CanvasScaler));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 400;
            var img = new GameObject("Black", typeof(RectTransform), typeof(Image));
            img.transform.SetParent(canvasGo.transform, false);
            var rt = (RectTransform)img.transform; rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero;
            _fade = img.GetComponent<Image>(); _fade.color = new Color(0, 0, 0, 0); _fade.raycastTarget = false;
        }

        void OnDestroy() { if (Instance == this) Instance = null; }

        /// <summary>Which way (±1) round the hinge swings the leaf towards `into` (the inside of the house).</summary>
        static float OpenSign(Transform hinge, Vector3 into)
        {
            var r = hinge.GetComponentInChildren<Renderer>();
            if (!r) return 1;
            Vector3 arm = r.bounds.center - hinge.position; arm.y = 0;
            Vector3 a = hinge.position + Quaternion.AngleAxis(40, Vector3.up) * arm, b = hinge.position + Quaternion.AngleAxis(-40, Vector3.up) * arm;
            return Vector3.Distance(a, into) < Vector3.Distance(b, into) ? 1 : -1;
        }

        static bool Near(Vector3 p, Vector3 q, float r) { var d = p - q; return Mathf.Abs(d.y) < 2.5f && new Vector2(d.x, d.z).magnitude < r; }

        void Update()
        {
            UpdateWindows();
            Prompt = null;
            var flow = GameFlow.Instance; var pc = PlayerController.Instance;
            if (_busy || flow == null || flow.State != GameFlow.FlowState.Playing || !pc || CaptureSystem.Candidate) return;
            var p = pc.transform.position;
            bool e = GameInput.InteractPressed;
            if (!Inside)
            {
                if (Near(p, outsideDoor, 2.0f)) { Prompt = "按 E 开门"; if (e) StartCoroutine(Pass(true)); }
                return;
            }
            if (Near(p, insideDoor, 1.7f)) { Prompt = "按 E 开门"; if (e) StartCoroutine(Pass(false)); }
            else if (Near(p, book, 1.4f)) { Prompt = "按 E 翻开日志"; if (e) flow.OpenJournal(); }
            else if (Near(p, bed, 1.9f)) { Prompt = "按 E 睡觉（存档，白天/夜晚切换）"; if (e) StartCoroutine(Sleep()); }
        }

        void UpdateWindows()
        {
            if (!Inside) return;
            if (windowMaterial) windowMaterial.SetColor("_BaseColor", EnvController.OutdoorSky);
            if (windowLights != null && windowLights.Length > 1)
            {
                Color c = EnvController.OutdoorLight;
                float k = Mathf.Max(c.r, Mathf.Max(c.g, c.b));
                for (int i = 0; i < windowLights.Length; i++)
                {
                    windowLights[i].color = k > 0.001f ? c / k : Color.white;
                    windowLights[i].intensity = (i == 0 ? _windowBase0 : _windowBase1) * k;
                }
            }
        }

        // ------------------------------------------------------------------------------------------- transitions
        public static IEnumerator Fade(Image img, float to, float seconds)
        {
            float from = img.color.a;
            for (float t = 0; t < seconds; t += Time.unscaledDeltaTime)
            {
                img.color = new Color(0, 0, 0, Mathf.Lerp(from, to, Mathf.SmoothStep(0, 1, t / seconds)));
                yield return null;
            }
            img.color = new Color(0, 0, 0, to);
        }

        IEnumerator Swing(Transform leaf, Quaternion closed, float sign, float from, float to, float seconds)
        {
            for (float t = 0; t < seconds; t += Time.deltaTime)
            {
                float k = Mathf.SmoothStep(0, 1, t / seconds);
                leaf.localRotation = Quaternion.AngleAxis(sign * Mathf.Lerp(from, to, k), leaf.parent ? leaf.parent.InverseTransformDirection(Vector3.up) : Vector3.up) * closed;
                yield return null;
            }
            leaf.localRotation = Quaternion.AngleAxis(sign * to, leaf.parent ? leaf.parent.InverseTransformDirection(Vector3.up) : Vector3.up) * closed;
        }

        IEnumerator Pass(bool enter)
        {
            _busy = true;
            var pc = PlayerController.Instance;
            pc.Frozen = true;
            Transform leaf = enter ? outsideLeaf : insideHinge;
            Quaternion closed = enter ? _outClosed : _inClosed;
            float sign = enter ? _outSign : _inSign;
            // turn to face the door
            Vector3 door = enter ? outsideDoor : insideDoor;
            Vector3 to = door - pc.transform.position; to.y = 0;
            if (to.sqrMagnitude > 0.01f) pc.transform.rotation = Quaternion.LookRotation(to);
            DoorOpened?.Invoke();
            if (leaf) StartCoroutine(Swing(leaf, closed, sign, 0, OpenAngle, 0.75f));
            yield return new WaitForSeconds(0.45f);   // the door is visibly open before the screen goes dark
            yield return Fade(_fade, 1, 0.45f);
            if (leaf) leaf.localRotation = closed;

            SetInside(enter);
            Vector3 spot = enter ? insideSpot : outsideSpot;
            float yaw = enter ? insideYaw : outsideYaw;
            pc.Teleport(spot, yaw);   // camera cut -> PlayerController.CameraCut -> upscaler ResetHistory (IN-08)
            pc.SetCameraOrbit(yaw, enter ? 14f : 12f);
            BringCompanions(spot, yaw);
            yield return null;

            // the door on the arriving side swings shut behind the witch
            Transform back = enter ? insideHinge : outsideLeaf;
            if (back)
            {
                Quaternion bc = enter ? _inClosed : _outClosed;
                float bs = enter ? _inSign : _outSign;
                StartCoroutine(Swing(back, bc, bs, OpenAngle * 0.8f, 0, 0.8f));
            }
            yield return Fade(_fade, 0, 0.5f);
            DoorClosed?.Invoke();
            pc.Frozen = false;
            _busy = false;
            Debug.Log(enter ? "[Cabin] entered" : "[Cabin] left");
        }

        /// <summary>Lighting, sound and save state follow this flag.</summary>
        public static void SetInside(bool inside)
        {
            EnvController.Indoors = inside;
            if (EnvController.Instance) EnvController.Instance.Apply();
            if (Weather.Instance && Weather.Instance.rain) Weather.Instance.rain.Clear();
            var pc = PlayerController.Instance;
            if (pc) pc.SetCameraDistance(inside ? 0.65f : 1f);   // the room is small: start close behind her
        }

        static void BringCompanions(Vector3 spot, float yaw)
        {
            var back = Quaternion.Euler(0, yaw, 0) * Vector3.back;
            var right = Quaternion.Euler(0, yaw, 0) * Vector3.right;
            int i = 0;
            foreach (var c in FindObjectsByType<Creature>(FindObjectsSortMode.None))
                if (c.Current == Creature.State.Companion)
                {
                    // cat (and other walkers) step in right behind her; flyers arrive at shoulder height
                    c.WarpNear(spot + back * 0.9f + right * ((i % 2 == 0 ? 1 : -1) * (0.6f + 0.3f * (i / 2))), yaw);
                    i++;
                }
        }

        IEnumerator Sleep()
        {
            _busy = true;
            var pc = PlayerController.Instance;
            pc.Frozen = true;
            yield return Fade(_fade, 1, 0.8f);
            var env = EnvController.Instance;
            bool day = env && env.timeOfDay >= 6f && env.timeOfDay < 18.5f;
            if (env) { env.timeOfDay = day ? 20.5f : 7f; env.Apply(); }
            pc.Teleport(bedStand, bedYaw + 90);
            if (GameFlow.Instance) { GameFlow.Instance.SaveNow(); GameFlow.Instance.ShowToast(day ? "一觉睡到了夜里。已存档。" : "一觉睡到了清晨。已存档。"); }
            yield return new WaitForSecondsRealtime(0.9f);
            yield return Fade(_fade, 0, 0.8f);
            pc.Frozen = false;
            _busy = false;
            Debug.Log($"[Cabin] slept -> {(day ? "night" : "morning")}");
        }

        /// <summary>New game: black screen, the witch lying in the bed, fade in, she gets up.</summary>
        public void WakeUp() => StartCoroutine(WakeRoutine());

        IEnumerator WakeRoutine()
        {
            _busy = true;
            var pc = PlayerController.Instance;
            _fade.color = Color.black;
            SetInside(true);
            pc.Frozen = true;
            pc.Teleport(bedLie, bedYaw);
            var vis = pc.visual;
            Quaternion vr = vis ? vis.localRotation : Quaternion.identity;
            Vector3 vp = vis ? vis.localPosition : Vector3.zero;
            if (vis) { vis.localRotation = Quaternion.Euler(-90, 0, 0); vis.localPosition = vp + Vector3.up * 0.12f; }
            // the camera looks at her lying body, not at where her head would be standing
            var camTarget = pc.transform.Find("CameraTarget");
            Vector3 ct = camTarget ? camTarget.localPosition : Vector3.zero;
            if (camTarget) camTarget.position = bedLie + Vector3.up * 0.55f + Quaternion.Euler(0, bedYaw, 0) * Vector3.back * 0.8f;
            Vector3 side = roomCenter - bedLie; side.y = 0;
            pc.SetCameraOrbit(Mathf.Atan2(-side.x, -side.z) * Mathf.Rad2Deg, 30f);
            BringCompanions(bedStand, bedYaw + 90);
            yield return new WaitForSecondsRealtime(0.4f);
            yield return Fade(_fade, 0, 1.4f);
            yield return new WaitForSecondsRealtime(1.3f);
            yield return Fade(_fade, 1, 0.35f);
            if (vis) { vis.localRotation = vr; vis.localPosition = vp; }
            if (camTarget) camTarget.localPosition = ct;
            pc.Teleport(bedStand, bedYaw + 90);
            yield return null;
            yield return Fade(_fade, 0, 0.5f);
            pc.Frozen = false;
            _busy = false;
            Debug.Log("[Cabin] woke up in bed");
        }

        /// <summary>Where the witch is on the world map: indoors she is in the cabin.</summary>
        public static Vector3 MapPosition(Vector3 p) =>
            Inside ? new Vector3(WorldLayout.House.x, p.y, WorldLayout.House.y) : p;
    }
}
