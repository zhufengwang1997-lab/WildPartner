using System;
using UnityEngine;
using WildPartner.Core;
using WildPartner.Player;
using WildPartner.World;

namespace WildPartner.Creatures
{
    /// <summary>
    /// 试玩反馈 1123d-1: the ink koi is caught by fishing, not by the hold-E spell.
    /// At a <see cref="FishingSpot"/>: E -> she takes out a magic rod and casts (FishCast), the float lands -> waits
    /// 3-8 s (FishWait; a nibble or two) -> the float shakes and goes under: E within 1.5 s strikes (FishHook) ->
    /// about 5 s of reeling (FishReel): hold E to reel in (tension up), let go to give line (tension down); the fish
    /// pulls left and right so the pointer swings; keep it in the green. Too long in the red (slack or too tight)
    /// and it gets off. Landed: a koi comes out of the water and joins her; an ordinary fish is let go again.
    /// Esc or moving puts the rod away at any time. The koi chance rises with each try (65 % / 80 % / 100 %).
    /// </summary>
    [DefaultExecutionOrder(50)]
    public class FishingSystem : MonoBehaviour
    {
        public enum Phase { Idle, Cast, Wait, Bite, Reel, Landed, Escaped }
        public static FishingSystem Instance { get; private set; }
        public Phase Current { get; private set; } = Phase.Idle;
        public static bool Active => Instance && Instance.Current != Phase.Idle;

        // ---- tuning ----
        public const float BiteWindow = 1.5f, ReelSeconds = 5f;
        public const float SafeMin = 0.3f, SafeMax = 0.72f, DangerLow = 0.1f, DangerHigh = 0.9f, DangerLimit = 1.2f;
        const float CastSeconds = 1.2f, ReleaseAt = 0.55f, LandSeconds = 1.4f, StandRange = 2.4f;

        /// <summary>0 slack .. 1 about to snap.</summary>
        public float Tension { get; private set; }
        /// <summary>0..1 how far the fish has been reeled in.</summary>
        public float Progress { get; private set; }
        /// <summary>Seconds the pointer has spent in a red zone (resets slowly in the safe zone).</summary>
        public float DangerTime { get; private set; }
        public bool HookedKoi { get; private set; }
        public FishingSpot Spot { get; private set; }
        public FishingSpot NearSpot { get; private set; }
        /// <summary>Tries since the last koi: the koi chance is 65 % / 80 % / 100 %.</summary>
        public int Tries { get; private set; }

        public static event Action CastOut, FloatLanded, Nibble, Bite, Hooked, ReelTick;
        public static event Action<bool> Caught;          // true = koi
        public static event Action<string> Lost;          // reason (toast)

        /// <summary>Smoke test: force the wait (s) and what bites (null = random).</summary>
        public static float? ForceWait;
        public static bool? ForceKoi;

        PlayerController _player;
        Animator _anim;
        Transform _hand, _fore;
        static readonly int FishId = Animator.StringToHash("Fish");
        float _t, _wait, _nibbleAt, _phaseStruggle, _kick, _kickT, _savedRadial = 1f;
        bool _landedFloat, _nibbled;
        Creature _koi;
        Vector3 _fishFrom;
        GameObject _rod, _float, _commonFish;
        Transform _rodTip;
        LineRenderer _line;
        Vector3 _floatPos, _across;
        System.Random _rng = new System.Random();

        float Rand(float a, float b) => a + (float)_rng.NextDouble() * (b - a);

        void Awake() => Instance = this;
        void OnDestroy() { if (Instance == this) Instance = null; }

        void Start()
        {
            _player = PlayerController.Instance;
            if (_player)
            {
                _anim = _player.GetComponentInChildren<Animator>();
                foreach (var t in _player.GetComponentsInChildren<Transform>(true))
                {
                    if (t.name == "Hand.R") _hand = t;
                    else if (t.name == "ForeArm.R") _fore = t;
                }
            }
            // koi live where the fishing spots are: two per spot, either side of the cast point
            var koi = CreatureDefs.Get("ink_koi");
            if (koi != null && koi.homes != null)
                for (int i = 0; i < koi.homes.Length && FishingSpot.All.Count > 0; i++)
                {
                    var sp = FishingSpot.All[Mathf.Min(i / 2, FishingSpot.All.Count - 1)];
                    var c = sp.cast; var side = Quaternion.Euler(0, sp.yaw + 90, 0) * Vector3.forward;
                    var fwd = Quaternion.Euler(0, sp.yaw, 0) * Vector3.forward;
                    var h = c + side * (i % 2 == 0 ? 1.6f : -1.4f) + fwd * (i % 2 == 0 ? 0.6f : 1.4f);
                    koi.homes[i] = new Vector2(h.x, h.z);
                }
            BuildProps();
        }

        // ------------------------------------------------------------------ props: rod, line, float
        void BuildProps()
        {
            var wood = FishingSpot.All.Count > 0 ? FishingSpot.All[0].wood : null;
            _rod = new GameObject("MagicRod");
            var shaft = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Destroy(shaft.GetComponent<Collider>());
            shaft.transform.SetParent(_rod.transform, false);
            shaft.transform.localScale = new Vector3(0.028f, 0.9f, 0.028f);
            shaft.transform.localPosition = Vector3.up * 0.9f;
            var r = shaft.GetComponent<MeshRenderer>();
            if (wood) r.sharedMaterial = wood;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var grip = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Destroy(grip.GetComponent<Collider>());
            grip.transform.SetParent(_rod.transform, false);
            grip.transform.localScale = new Vector3(0.045f, 0.16f, 0.045f);
            grip.transform.localPosition = Vector3.up * 0.18f;
            grip.GetComponent<MeshRenderer>().sharedMaterial = Solid(new Color(0.28f, 0.16f, 0.36f));
            _rodTip = new GameObject("Tip").transform;
            _rodTip.SetParent(_rod.transform, false);
            _rodTip.localPosition = Vector3.up * 1.8f;
            var gem = GameObject.CreatePrimitive(PrimitiveType.Sphere);   // the magic part: a little glowing stone at the tip
            Destroy(gem.GetComponent<Collider>());
            gem.transform.SetParent(_rodTip, false);
            gem.transform.localScale = Vector3.one * 0.07f;
            gem.GetComponent<MeshRenderer>().sharedMaterial = Solid(new Color(0.8f, 0.6f, 1f), true);
            Glow(_rodTip, 0.35f, new Color(0.7f, 0.5f, 1f, 0.7f));
            _rod.SetActive(false);

            _float = new GameObject("FishingFloat");
            var red = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(red.GetComponent<Collider>());
            red.transform.SetParent(_float.transform, false);
            red.transform.localScale = new Vector3(0.11f, 0.09f, 0.11f);
            red.GetComponent<MeshRenderer>().sharedMaterial = Solid(new Color(0.95f, 0.18f, 0.22f));
            var white = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(white.GetComponent<Collider>());
            white.transform.SetParent(_float.transform, false);
            white.transform.localScale = new Vector3(0.07f, 0.07f, 0.07f);
            white.transform.localPosition = Vector3.up * 0.05f;
            white.GetComponent<MeshRenderer>().sharedMaterial = Solid(new Color(1f, 0.97f, 0.92f));
            Glow(_float.transform, 0.4f, new Color(1f, 0.6f, 0.7f, 0.5f));
            _float.SetActive(false);

            var lineGo = new GameObject("FishingLine");
            _line = lineGo.AddComponent<LineRenderer>();
            _line.sharedMaterial = Solid(new Color(0.92f, 0.9f, 1f, 0.85f));
            _line.widthMultiplier = 0.012f;
            _line.positionCount = 14;
            _line.useWorldSpace = true;
            _line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _line.enabled = false;

            _commonFish = new GameObject("CaughtFish");
            var fm = _commonFish.AddComponent<MeshFilter>();
            fm.sharedMesh = FishSchools.SharedMesh;
            var fr = _commonFish.AddComponent<MeshRenderer>();
            if (FishSchools.Instance) fr.sharedMaterial = FishSchools.Instance.silver;
            _commonFish.transform.localScale = Vector3.one * 1.8f;
            _commonFish.SetActive(false);
        }

        /// <summary>Unlit colour (URP particle shader: always in the build).</summary>
        static Material Solid(Color c, bool add = false)
        {
            var m = new Material(add ? FxMat.Additive : FxMat.Alpha);
            m.SetTexture("_BaseMap", Texture2D.whiteTexture);
            m.SetColor("_BaseColor", c);
            if (!add && c.a >= 0.99f) { m.renderQueue = 2450; m.SetFloat("_ZWrite", 1f); }
            return m;
        }

        static void Glow(Transform parent, float size, Color c)
        {
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Destroy(q.GetComponent<Collider>());
            q.name = "Glow";
            q.transform.SetParent(parent, false);
            q.transform.localScale = Vector3.one * size;
            var m = new Material(FxMat.Additive); m.SetColor("_BaseColor", c);
            q.GetComponent<MeshRenderer>().sharedMaterial = m;
            q.AddComponent<FaceCamera>();
        }

        class FaceCamera : MonoBehaviour
        {
            void LateUpdate() { var c = UnityEngine.Camera.main; if (c) transform.rotation = c.transform.rotation; }
        }

        // ------------------------------------------------------------------ flow
        public static string Prompt
        {
            get
            {
                var f = Instance;
                if (!f) return null;
                switch (f.Current)
                {
                    case Phase.Idle: return f.NearSpot ? "按 E 钓鱼" : null;
                    case Phase.Cast: return "抛竿……";
                    case Phase.Wait: return "等鱼上钩……（Esc 或移动收竿）";
                    case Phase.Bite: return "上钩了！快按 E 提竿！";
                    case Phase.Reel: return "按住 E 收线、松开放线，指针停在绿色区";
                    default: return null;
                }
            }
        }

        bool CanStart()
        {
            var flow = GameFlow.Instance;
            return _player && flow != null && flow.State == GameFlow.FlowState.Playing && !EnvController.Indoors &&
                   !_player.Riding && !_player.Swimming && !_player.Climbing && !_player.Airborne && !CabinInterior.Busy;
        }

        void Update()
        {
            if (!_player) return;
            float dt = Time.deltaTime;
            if (Current == Phase.Idle)
            {
                NearSpot = null;
                if (CanStart())
                    foreach (var s in FishingSpot.All)
                    {
                        var d = s.stand - _player.transform.position; d.y = 0;
                        if (d.magnitude < StandRange && Mathf.Abs(s.stand.y - _player.transform.position.y) < 1.5f) { NearSpot = s; break; }
                    }
                if (NearSpot && GameInput.InteractPressed) Begin(NearSpot);
                return;
            }
            if (GameFlow.Instance == null || GameFlow.Instance.State != GameFlow.FlowState.Playing) return;

            // put the rod away: Esc, moving or jumping (not while a caught fish is in the air)
            if (Current != Phase.Landed && (GameInput.PausePressed || GameInput.Move.sqrMagnitude > 0.3f || GameInput.JumpPressed))
            {
                End();
                return;
            }

            _t += dt;
            switch (Current)
            {
                case Phase.Cast:
                    SetAnim(1);
                    if (_t >= CastSeconds) { Enter(Phase.Wait); SetAnim(2); }
                    break;
                case Phase.Wait:
                    SetAnim(2);
                    if (!_nibbled && _t > _nibbleAt) { _nibbled = true; _kickT = 0.35f; Nibble?.Invoke(); }
                    if (_t >= _wait)
                    {
                        HookedKoi = ForceKoi ?? DecideKoi();
                        ForceKoi = null;
                        Enter(Phase.Bite);
                        Bite?.Invoke();
                        Spot.Splash(_floatPos, 2);
                    }
                    break;
                case Phase.Bite:
                    if (GameInput.InteractPressed)
                    {
                        Enter(Phase.Reel); SetAnim(3);
                        Tension = 0.5f; Progress = 0; DangerTime = 0;
                        _phaseStruggle = Rand(0, 6.28f);
                        _koi = HookedKoi ? NearestKoi() : null;
                        Hooked?.Invoke();
                    }
                    else if (_t > BiteWindow) Lose("鱼咬了一下就跑掉了——再抛一次吧（按 E）。");
                    break;
                case Phase.Reel:
                    if (_t > 0.6f) SetAnim(4);
                    Reel(dt);
                    break;
                case Phase.Landed:
                    LandFish();
                    break;
                case Phase.Escaped:
                    if (_t > 1.0f) End();
                    break;
            }
            CameraFollow();
        }

        void Begin(FishingSpot s)
        {
            Spot = s;
            _player.Frozen = true;
            _player.Place(new Vector3(s.stand.x, _player.transform.position.y, s.stand.z));
            _player.transform.rotation = Quaternion.Euler(0, s.yaw, 0);
            var fwd = Quaternion.Euler(0, s.yaw, 0) * Vector3.forward;
            _across = new Vector3(fwd.z, 0, -fwd.x);
            _savedRadial = _player.CameraRadial;
            _camYaw = CameraYawFor(s);
            _rod.SetActive(true);
            _line.enabled = true;
            _float.SetActive(true);
            _landedFloat = false;
            Enter(Phase.Cast);
            SetAnim(1);
            _wait = ForceWait ?? Rand(3f, 8f);
            _nibbleAt = Rand(1.2f, Mathf.Max(1.3f, _wait - 1f));
            _nibbled = false;
            CastOut?.Invoke();
            Debug.Log($"[Fish] cast at {s.label}, wait {_wait:0.0} s, try {Tries + 1}");
        }

        void Enter(Phase p) { Current = p; _t = 0; }

        bool DecideKoi()
        {
            var flow = GameFlow.Instance;
            if (flow != null && flow.Save.HasCaught("ink_koi")) return false;   // the koi is already with her: only ordinary fish
            if (!NearestKoi()) return false;
            float chance = Tries == 0 ? 0.65f : Tries == 1 ? 0.8f : 1f;
            return _rng.NextDouble() < chance;
        }

        Creature NearestKoi()
        {
            Creature best = null; float bd = 30f;
            foreach (var c in FindObjectsByType<Creature>(FindObjectsSortMode.None))
            {
                if (c.defId != "ink_koi" || c.Current == Creature.State.Companion || c.Current == Creature.State.Hidden) continue;
                float d = Vector3.Distance(c.transform.position, Spot.cast);
                if (d < bd) { bd = d; best = c; }
            }
            return best;
        }

        void Reel(float dt)
        {
            bool hold = GameInput.CaptureHeld;
            float strength = HookedKoi ? 1f : 0.7f;
            // the fish pulls left and right: a slow swing plus sudden runs now and then
            _kickT -= dt;
            if (_kickT <= 0f) { _kick = Rand(-1f, 1f) * 0.9f * strength; _kickT = Rand(0.5f, 1.1f); }
            float struggle = (Mathf.Sin(_t * 2.3f + _phaseStruggle) * 0.35f + _kick) * strength;
            Tension = Mathf.Clamp01(Tension + ((hold ? 0.62f : -0.55f) + struggle) * dt);
            bool safe = Tension >= SafeMin && Tension <= SafeMax;
            bool danger = Tension < DangerLow || Tension > DangerHigh;
            Progress = Mathf.Clamp01(Progress + dt / ReelSeconds * (safe ? 1f : danger ? 0f : 0.45f));
            DangerTime = danger ? DangerTime + dt : Mathf.Max(0f, DangerTime - dt * 0.5f);
            if (Time.frameCount % 20 == 0 && hold) ReelTick?.Invoke();
            if (DangerTime > DangerLimit)
            {
                Lose(Tension > 0.5f ? "线绷得太紧，鱼挣脱了！松开 E 放点线。" : "线太松，鱼脱钩跑掉了！按住 E 收线。");
                return;
            }
            // the float is dragged under, towards her, swinging with the fish
            var toStand = Spot.stand - Spot.cast; toStand.y = 0;
            _floatPos = Spot.cast + toStand * (Progress * 0.6f) + _across * (Mathf.Sin(_t * 2.3f + _phaseStruggle) * 0.7f + _kick * 0.5f);
            _floatPos.y = Spot.cast.y - 0.12f;
            if (_koi) { _koi.Scripted = true; _koi.transform.position = _floatPos - Vector3.up * 0.15f; _koi.transform.rotation = Quaternion.LookRotation(-toStand.normalized + _across * _kick); }
            if (Time.frameCount % 45 == 0) Spot.Splash(_floatPos, 1);
            if (Progress >= 1f)
            {
                Enter(Phase.Landed);
                _fishFrom = _floatPos;
                Spot.Splash(_floatPos, 3);
                if (!HookedKoi) { _commonFish.SetActive(true); }
                SetAnim(3);
            }
        }

        /// <summary>The fish comes out of the water in an arc to her side; the koi then joins her, an ordinary fish
        /// flips back in.</summary>
        void LandFish()
        {
            float u = Mathf.Clamp01(_t / 0.9f);
            var to = _player.transform.position + _player.transform.right * 0.7f + _player.transform.forward * 0.6f + Vector3.up * 1.2f;
            var p = Vector3.Lerp(_fishFrom, to, u) + Vector3.up * Mathf.Sin(u * Mathf.PI) * 1.4f;
            var spin = Quaternion.Euler(Mathf.Sin(_t * 18f) * 25f, _player.transform.eulerAngles.y + 90f, Mathf.Sin(_t * 23f) * 30f);
            _floatPos = p;
            if (HookedKoi && _koi) { _koi.transform.SetPositionAndRotation(p, spin); }
            else if (!HookedKoi)
            {
                if (_t > 0.9f)   // back into the water
                {
                    float b = Mathf.Clamp01((_t - 0.9f) / 0.5f);
                    p = Vector3.Lerp(to, Spot.cast, b) + Vector3.up * Mathf.Sin(b * Mathf.PI) * 0.8f;
                }
                _commonFish.transform.SetPositionAndRotation(p, spin);
            }
            if (_t >= LandSeconds)
            {
                bool koi = HookedKoi;
                Tries = koi ? 0 : Tries + 1;
                _commonFish.SetActive(false);
                if (!koi) Spot.Splash(Spot.cast, 2);
                Debug.Log($"[Fish] landed {(koi ? "the ink koi" : "an ordinary fish")}");
                var k = _koi;
                End();
                if (koi && k)
                {
                    k.Scripted = false;
                    if (GameFlow.Instance && GameFlow.Instance.Pets) { GameFlow.Instance.Pets.Puff(k.transform.position); GameFlow.Instance.Pets.Puff(k.transform.position + Vector3.up * 0.3f); }
                    CaptureSystem.Award(k);
                }
                Caught?.Invoke(koi);
            }
        }

        void Lose(string why)
        {
            Tries++;
            Enter(Phase.Escaped);
            SetAnim(2);
            if (_koi) { _koi.Scripted = false; _koi = null; }
            Spot.Splash(_floatPos, 3);
            Debug.Log($"[Fish] lost: {why}");
            Lost?.Invoke(why);
        }

        public void End()
        {
            if (_koi) { _koi.Scripted = false; _koi = null; }
            Current = Phase.Idle;
            SetAnim(0);
            _rod.SetActive(false); _float.SetActive(false); _line.enabled = false; _commonFish.SetActive(false);
            if (_player) { _player.Frozen = false; _player.SetCameraDistance(_savedRadial); }
        }

        void SetAnim(int v) { if (_anim && _anim.GetInteger(FishId) != v) _anim.SetInteger(FishId, v); }

        /// <summary>Closer, from behind her right shoulder, so she and the float are both in view.</summary>
        void CameraFollow()
        {
            if (!Spot) return;
            float k = 1 - Mathf.Exp(-3f * Time.deltaTime);
            _player.BlendCameraOrbit(_camYaw, 16f, 0.78f, k);
        }

        float _camYaw;
        /// <summary>Side-behind her, on whichever side does not look into a nearby waterfall (its glare and mist).</summary>
        static float CameraYawFor(FishingSpot s)
        {
            float a = s.yaw - 32f, b = s.yaw + 32f;
            var st = new Vector2(s.stand.x, s.stand.z);
            var pts = WorldLayout.RiverIn;
            Vector2 fall = Vector2.zero; float best = 30f;
            for (int i = 0; i < pts.Length - 1; i++)
            {
                if (pts[i].z - pts[i + 1].z <= 3f) continue;               // not a waterfall segment
                var foot = new Vector2(pts[i + 1].x, pts[i + 1].y);
                float d = Vector2.Distance(foot, st);
                if (d < best) { best = d; fall = foot; }
            }
            if (best >= 30f) return a;
            var to = (fall - st).normalized;
            Vector2 F(float y) { var f = Quaternion.Euler(0, y, 0) * Vector3.forward; return new Vector2(f.x, f.z); }
            return Vector2.Dot(F(a), to) <= Vector2.Dot(F(b), to) ? a : b;
        }

        // ------------------------------------------------------------------ props each frame
        void LateUpdate()
        {
            if (Current == Phase.Idle || !_hand) return;
            // the rod sits in her right hand and points along the forearm, tilted up
            Vector3 fwd = _player.transform.forward;
            Vector3 fore = _fore ? (_hand.position - _fore.position).normalized : fwd;
            var dir = (fore + Vector3.up * 0.6f + fwd * 0.25f).normalized;
            if (Current == Phase.Reel) dir = (dir + _across * (Mathf.Sin(_t * 2.3f + _phaseStruggle) * 0.12f + _kick * 0.08f)).normalized;
            _rod.transform.SetPositionAndRotation(_hand.position - dir * 0.22f, Quaternion.FromToRotation(Vector3.up, dir));
            Vector3 tip = _rodTip.position;

            float level = Spot.cast.y;
            switch (Current)
            {
                case Phase.Cast:
                {
                    float u = Mathf.InverseLerp(ReleaseAt * CastSeconds, CastSeconds - 0.1f, _t);
                    if (u <= 0f) _floatPos = tip + Vector3.down * 0.3f;
                    else
                    {
                        _floatPos = Vector3.Lerp(tip, Spot.cast, u) + Vector3.up * Mathf.Sin(u * Mathf.PI) * 2.2f;
                        if (u >= 1f && !_landedFloat) { _landedFloat = true; Spot.Splash(Spot.cast, 2); FloatLanded?.Invoke(); }
                    }
                    break;
                }
                case Phase.Wait:
                {
                    if (!_landedFloat) { _landedFloat = true; Spot.Splash(Spot.cast, 2); FloatLanded?.Invoke(); }
                    float nib = _kickT > 0 ? Mathf.Sin(Time.time * 40f) * 0.03f * (_kickT / 0.35f) : 0f;
                    if (_kickT > 0) _kickT -= Time.deltaTime;
                    _floatPos = Spot.cast + Vector3.up * (0.02f + Mathf.Sin(Time.time * 2.2f) * 0.015f + nib);
                    break;
                }
                case Phase.Bite:
                    // shakes and goes under
                    _floatPos = Spot.cast + new Vector3(Mathf.Sin(Time.time * 31f) * 0.04f, -Mathf.Min(_t * 0.5f, 0.14f) + Mathf.Sin(Time.time * 23f) * 0.03f, Mathf.Cos(Time.time * 27f) * 0.04f);
                    break;
                case Phase.Escaped:
                    _floatPos = Vector3.Lerp(_floatPos, new Vector3(_floatPos.x, level + 0.02f, _floatPos.z), 0.1f);
                    break;
            }
            _float.transform.position = _floatPos;

            // line: tip -> float, sagging with slack
            float slack = Current == Phase.Reel ? (1f - Tension) * 0.5f : Current == Phase.Landed ? 0.05f : Current == Phase.Cast ? 0.2f : 0.7f;
            var end = Current == Phase.Landed ? (HookedKoi && _koi ? _koi.transform.position : _commonFish.transform.position) : _floatPos;
            for (int i = 0; i < _line.positionCount; i++)
            {
                float s = i / (float)(_line.positionCount - 1);
                var p = Vector3.Lerp(tip, end, s);
                p.y -= Mathf.Sin(s * Mathf.PI) * slack * Mathf.Min(1f, Vector3.Distance(tip, end) * 0.2f);
                if (p.y < level + 0.01f && s < 0.98f) p.y = Mathf.Max(p.y, level + 0.01f);
                _line.SetPosition(i, p);
            }
        }
    }
}
