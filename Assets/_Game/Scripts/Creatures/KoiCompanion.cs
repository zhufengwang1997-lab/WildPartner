using UnityEngine;
using WildPartner.Player;
using WildPartner.World;

namespace WildPartner.Creatures
{
    /// <summary>
    /// 试玩反馈 1123e-2: how the ink koi keeps her company (replaces floating behind her).
    ///  - On land it lives in a small glowing water bottle hanging at her right hip (glass, cork, water inside),
    ///    swimming small circles in it; the bottle swings with her steps.
    ///  - Within ~3 m of water it leaps out of the bottle in a little arc (splash) and swims in the water near her,
    ///    now and then jumping clear of the surface; leaving the water it leaps back into the bottle.
    ///  - While she walks on the water it swims right under her feet and every footstep leaves a ring of faint red
    ///    light on the surface - the koi carrying her.
    ///  - While she swims it swims beside her.
    /// Switching to the koi the bottle appears with a few sparkles; switching away it vanishes.
    /// Driven from Creature.UpdateCompanion (only while the koi is the shown pet).
    /// </summary>
    public class KoiCompanion : MonoBehaviour
    {
        public enum Mode { Bottle, JumpOut, Water, JumpIn }
        public Mode Current { get; private set; } = Mode.Bottle;
        /// <summary>Smoke test / showcase.</summary>
        public static KoiCompanion Instance { get; private set; }
        public Transform Bottle => _bottle;
        public Vector3 BottleCentre => _bottle ? _bottle.TransformPoint(new Vector3(0, BodyH * 0.45f, 0)) : transform.position;
        public bool BottleShown => _bottle && _bottle.gameObject.activeSelf;
        public static int Ripples { get; private set; }
        public static int Leaps { get; private set; }

        const float BodyH = 0.15f, BodyR = 0.052f, InBottleLength = 0.075f;
        const float NearWater = 3f, LeaveWater = 4.5f;

        Creature _c;
        Transform _bottle, _hips;
        Vector3 _baseScale;
        float _bottleScale = 0.15f;
        Vector2 _swing, _swingVel;
        Vector3 _lastAnchor;
        bool _hasAnchor;

        // in-water state
        Vector3 _waterPoint; bool _haveWater; float _waterLevel; float _nextScan;
        Vector3 _vel; float _nextLeap; float _leapT = -1f; Vector3 _leapFrom, _leapTo;
        // arc between bottle and water
        float _arcT; Vector3 _arcFrom;
        float _wanderA;

        ParticleSystem _rings, _redGlow, _drops, _sparkle;
        PlayerController _player;
        bool _subscribed;

        void Awake()
        {
            _c = GetComponent<Creature>();
            _baseScale = transform.localScale;
            Instance = this;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (_player && _subscribed) _player.Footstep -= OnFootstep;
            if (_bottle) Destroy(_bottle.gameObject);
        }

        void Start()
        {
            // how big is the fish at scale 1? It sits in the bottle at ~7.5 cm long
            var b = new Bounds(transform.position, Vector3.zero); bool any = false;
            foreach (var r in GetComponentsInChildren<Renderer>(true))
                if (!(r is ParticleSystemRenderer) && !(r is TrailRenderer)) { if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds); }
            float len = any ? Mathf.Max(b.size.x, b.size.z) / Mathf.Max(transform.lossyScale.x, 1e-4f) : 0.5f;
            _bottleScale = Mathf.Clamp(InBottleLength / Mathf.Max(len, 0.05f), 0.04f, 0.6f);
            BuildBottle();
            BuildFx();
            Debug.Log($"[Koi] companion presentation ready: fish {len:0.00} m long, in the bottle x{_bottleScale:0.00}");
        }

        // ------------------------------------------------------------------ per frame (from Creature)
        /// <summary>Called by Creature.UpdateCompanion while the koi is the shown current pet.</summary>
        public void Drive(PlayerController player)
        {
            if (!_bottle) return;
            if (_player != player)
            {
                if (_player && _subscribed) _player.Footstep -= OnFootstep;
                _player = player; _player.Footstep += OnFootstep; _subscribed = true;
                _hips = FindBone(player.transform, "Hips") ?? FindBone(player.transform, "Spine");
            }
            if (!_bottle.gameObject.activeSelf) ShowBottle(true);
            UpdateBottle(player);

            bool indoors = EnvController.Indoors;
            if (Time.time >= _nextScan)
            {
                _nextScan = Time.time + 0.2f;
                _haveWater = !indoors && FindWater(player, Current == Mode.Water ? LeaveWater : NearWater, out _waterPoint, out _waterLevel);
            }
            bool mustSwim = player.WaterWalking || player.Swimming;
            bool wantWater = (_haveWater || mustSwim) && !indoors;

            switch (Current)
            {
                case Mode.Bottle:
                    InBottle();
                    if (wantWater) { Current = Mode.JumpOut; _arcT = 0f; _arcFrom = BottleCentre; Splash(BottleCentre, 0, 4); }
                    break;
                case Mode.JumpOut:
                {
                    Vector3 to = SwimTarget(player);
                    _arcT += Time.deltaTime / 0.55f;
                    Arc(_arcFrom, to, _arcT, 0.55f, Mathf.Lerp(_bottleScale, 1f, Mathf.SmoothStep(0, 1, _arcT)));
                    if (_arcT >= 1f) { Current = Mode.Water; Splash(to, 2, 10); _vel = Vector3.zero; _nextLeap = Time.time + Random.Range(5f, 10f); }
                    break;
                }
                case Mode.Water:
                    if (!wantWater) { Current = Mode.JumpIn; _arcT = 0f; _arcFrom = transform.position; Splash(transform.position, 2, 10); _leapT = -1f; break; }
                    InWater(player);
                    break;
                case Mode.JumpIn:
                {
                    _arcT += Time.deltaTime / 0.5f;
                    Arc(_arcFrom, BottleCentre, _arcT, 0.6f, Mathf.Lerp(1f, _bottleScale, Mathf.SmoothStep(0, 1, _arcT)));
                    if (_arcT >= 1f) { Current = Mode.Bottle; Splash(BottleCentre, 0, 5); }
                    break;
                }
            }
        }

        void LateUpdate()
        {
            // the bottle only hangs at her hip while the koi is the current pet
            bool shown = _c && _c.Current == Creature.State.Companion && _c.PetShown;
            if (_bottle && _bottle.gameObject.activeSelf && !shown) ShowBottle(false);
            if (!shown && transform.localScale != _baseScale && _c && _c.Current != Creature.State.Companion) transform.localScale = _baseScale;
        }

        void ShowBottle(bool on)
        {
            if (on)
            {
                Current = Mode.Bottle; _hasAnchor = false; _swing = _swingVel = Vector2.zero;
                _bottle.gameObject.SetActive(true);
                if (_player) UpdateBottle(_player);
                Sparkle(BottleCentre);
            }
            else
            {
                if (_bottle.gameObject.activeSelf) Sparkle(BottleCentre);
                _bottle.gameObject.SetActive(false);
            }
        }

        // ------------------------------------------------------------------ the bottle
        void UpdateBottle(PlayerController player)
        {
            var pt = player.transform;
            Vector3 hip = _hips ? _hips.position : pt.position + Vector3.up * 0.95f;
            // 0.28 m out (was 0.19): at 0.19 it hung inside the skirt's flare and only its glow showed through
            Vector3 anchor = hip + pt.right * 0.28f + pt.forward * 0.06f - Vector3.up * 0.02f;
            float dt = Mathf.Max(Time.deltaTime, 1e-4f);
            if (!_hasAnchor) { _lastAnchor = anchor; _hasAnchor = true; }
            Vector3 accel = (anchor - _lastAnchor) / dt; _lastAnchor = anchor;
            // a pendulum on a short strap: lags behind her motion, settles back, a little bob from her steps
            Vector2 push = new Vector2(Vector3.Dot(accel, pt.right), Vector3.Dot(accel, pt.forward)) * -6f;
            _swingVel += (push * dt) + (-_swing * 90f - _swingVel * 7f) * dt;
            _swing += _swingVel * dt;
            _swing = Vector2.ClampMagnitude(_swing, 28f);
            float bob = player.Speed > 0.3f ? Mathf.Sin(Time.time * player.Speed * 5.5f) * 4f : 0f;
            var rot = Quaternion.LookRotation(pt.forward) * Quaternion.Euler(_swing.y + bob, 0, -_swing.x + 6f);
            _bottle.SetPositionAndRotation(anchor - rot * Vector3.up * (BodyH + 0.07f), rot);
        }

        void InBottle()
        {
            transform.localScale = _baseScale * _bottleScale;
            float t = Time.time * 1.6f;
            var local = new Vector3(Mathf.Cos(t) * 0.018f, BodyH * 0.42f + Mathf.Sin(t * 0.7f) * 0.012f, Mathf.Sin(t) * 0.018f);
            transform.position = _bottle.TransformPoint(local);
            var tangent = _bottle.TransformDirection(new Vector3(-Mathf.Sin(t), 0, Mathf.Cos(t)));
            transform.rotation = Quaternion.LookRotation(tangent, _bottle.up);
            _c.CompanionVelocity = tangent * 0.4f;
        }

        // ------------------------------------------------------------------ in the water
        Vector3 SwimTarget(PlayerController player)
        {
            var pt = player.transform;
            if (player.WaterWalking)
            {
                // right under her feet, a little ahead
                Vector3 p = pt.position + pt.forward * 0.25f;
                WorldLayout.WaterSurface(p.x, p.z, out float lv);
                return new Vector3(p.x, lv - 0.24f, p.z);
            }
            if (player.Swimming)
            {
                Vector3 p = pt.position + pt.right * 0.85f + pt.forward * 0.1f;
                WorldLayout.WaterSurface(p.x, p.z, out float lv);
                return new Vector3(p.x, lv - 0.22f, p.z);
            }
            // near her in the water: circle the closest deep-enough water point
            _wanderA += Time.deltaTime * 0.6f;
            Vector3 w = _waterPoint + new Vector3(Mathf.Cos(_wanderA), 0, Mathf.Sin(_wanderA * 0.8f)) * 0.7f;
            if (!WorldLayout.WaterSurface(w.x, w.z, out float lw) || Creature.GroundHeight(w.x, w.z) > lw - 0.35f) w = _waterPoint;
            return new Vector3(w.x, _waterLevel - 0.25f, w.z);
        }

        void InWater(PlayerController player)
        {
            transform.localScale = _baseScale;
            Vector3 target = SwimTarget(player);
            if (_leapT >= 0f)
            {
                _leapT += Time.deltaTime / 0.8f;
                Arc(_leapFrom, _leapTo, _leapT, 0.55f, 1f);
                if (_leapT >= 1f) { _leapT = -1f; Splash(_leapTo, 2, 8); }
                return;
            }
            float smooth = player.WaterWalking || player.Swimming ? 0.18f : 0.45f;
            Vector3 p = Vector3.SmoothDamp(transform.position, target, ref _vel, smooth, 9f);
            if (Vector3.Distance(p, target) > 25f) p = target;
            transform.position = p;
            Vector3 face = _vel.sqrMagnitude > 0.04f ? _vel : player.transform.forward;
            face.y = 0;
            if (face.sqrMagnitude > 1e-4f) transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(face), Time.deltaTime * 6f);
            _c.CompanionVelocity = _vel;

            // now and then it jumps clear of the surface (not while carrying her)
            if (!player.WaterWalking && !player.Swimming && Time.time >= _nextLeap)
            {
                _nextLeap = Time.time + Random.Range(6f, 12f);
                var dir = transform.forward; dir.y = 0; dir.Normalize();
                _leapFrom = transform.position;
                _leapTo = transform.position + dir * 1.3f;
                if (WorldLayout.WaterSurface(_leapTo.x, _leapTo.z, out float lv) && Creature.GroundHeight(_leapTo.x, _leapTo.z) < lv - 0.3f)
                {
                    _leapTo.y = lv - 0.25f;
                    _leapT = 0f; Leaps++;
                    Splash(new Vector3(_leapFrom.x, _waterLevel, _leapFrom.z), 2, 8);
                    Debug.Log("[Koi] leaps out of the water");
                }
            }
        }

        void Arc(Vector3 from, Vector3 to, float t, float height, float scale)
        {
            t = Mathf.Clamp01(t);
            Vector3 p = Vector3.Lerp(from, to, t) + Vector3.up * (4f * height * t * (1 - t));
            Vector3 dp = (to - from) + Vector3.up * (4f * height * (1 - 2 * t));
            transform.position = p;
            if (dp.sqrMagnitude > 1e-5f) transform.rotation = Quaternion.LookRotation(dp.normalized, Vector3.up);
            transform.localScale = _baseScale * scale;
            _c.CompanionVelocity = dp;
        }

        /// <summary>Closest point (within `radius` of her) with water deep enough to swim in.</summary>
        static bool FindWater(PlayerController player, float radius, out Vector3 point, out float level)
        {
            Vector3 c = player.transform.position;
            point = c; level = 0f;
            float best = float.MaxValue;
            for (float r = 0.6f; r <= radius + 0.01f; r += 0.6f)
                for (int i = 0; i < 14; i++)
                {
                    float a = i / 14f * Mathf.PI * 2f + r;
                    float x = c.x + Mathf.Cos(a) * r, z = c.z + Mathf.Sin(a) * r;
                    if (!WorldLayout.WaterSurface(x, z, out float lv)) continue;
                    if (Creature.GroundHeight(x, z) > lv - 0.4f) continue;   // too shallow
                    if (lv < c.y - 4f || lv > c.y + 1.5f) continue;          // a pool far below a cliff is not "near"
                    if (r < best) { best = r; point = new Vector3(x, lv, z); level = lv; }
                }
            return best < float.MaxValue;
        }

        // ------------------------------------------------------------------ footstep ripples on the water
        void OnFootstep()
        {
            if (!_player || !_player.WaterWalking || !_rings) return;
            if (!(_c && _c.Current == Creature.State.Companion && _c.PetShown)) return;
            var p = _player.transform.position;
            WorldLayout.WaterSurface(p.x, p.z, out float lv);
            var at = new Vector3(p.x, lv + 0.03f, p.z);
            _rings.Emit(new ParticleSystem.EmitParams { position = at, applyShapeToPosition = false }, 1);
            _redGlow.Emit(new ParticleSystem.EmitParams { position = at + Vector3.up * 0.02f, applyShapeToPosition = false }, 1);
            Ripples++;
        }

        void Splash(Vector3 at, int rings, int drops)
        {
            if (!_drops) return;
            if (rings > 0) _rings.Emit(new ParticleSystem.EmitParams { position = new Vector3(at.x, at.y + 0.25f, at.z), applyShapeToPosition = false, startColor = new Color(0.9f, 0.95f, 1f, 0.7f) }, rings);
            _drops.Emit(new ParticleSystem.EmitParams { position = at, applyShapeToPosition = true }, drops);
        }

        void Sparkle(Vector3 at)
        {
            if (!_sparkle) return;
            _sparkle.transform.position = at;
            _sparkle.Emit(14);
        }

        // ------------------------------------------------------------------ building
        void BuildBottle()
        {
            var root = new GameObject("KoiBottle").transform;
            root.gameObject.SetActive(false);
            // glass: a lathe from the base up the round body, shoulder, neck and lip
            float[] r = { 0.000f, 0.044f, BodyR, BodyR, 0.048f, 0.032f, 0.021f, 0.020f, 0.024f };
            float[] y = { 0.000f, 0.004f, 0.020f, 0.100f, 0.122f, 0.140f, 0.152f, 0.180f, 0.186f };
            var glass = Part(root, "Glass", Lathe(r, y, 20), Tint(new Color(0.78f, 0.92f, 1f, 0.26f), false));
            // water inside, a little below the shoulder; faintly glowing
            float[] wr = { 0f, 0.040f, BodyR - 0.005f, BodyR - 0.005f, 0f };
            float[] wy = { 0.004f, 0.007f, 0.022f, 0.112f, 0.112f };
            Part(root, "Water", Lathe(wr, wy, 20), Tint(new Color(0.35f, 0.70f, 0.95f, 0.30f), false));
            Part(root, "WaterGlow", Lathe(wr, wy, 20), Tint(new Color(0.25f, 0.45f, 0.75f, 0.18f), true));
            // cork
            float[] cr = { 0f, 0.019f, 0.022f, 0.023f, 0f };
            float[] cy = { 0.168f, 0.168f, 0.196f, 0.205f, 0.205f };
            Part(root, "Cork", Lathe(cr, cy, 12), Tint(new Color(0.62f, 0.45f, 0.28f, 1f), false));
            // strap from the neck up to her belt
            float[] sr = { 0.004f, 0.004f };
            float[] sy = { 0.18f, 0.25f };
            Part(root, "Strap", Lathe(sr, sy, 6), Tint(new Color(0.36f, 0.22f, 0.32f, 1f), false));
            // a soft glow around it (and a very small light at night)
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Destroy(q.GetComponent<Collider>());
            q.name = "Glow"; q.transform.SetParent(root, false);
            q.transform.localPosition = new Vector3(0, 0.07f, 0); q.transform.localScale = Vector3.one * 0.34f;
            var gm = new Material(FxMat.Additive); gm.SetColor("_BaseColor", new Color(0.55f, 0.75f, 1f, 0.32f));
            q.GetComponent<MeshRenderer>().sharedMaterial = gm;
            q.AddComponent<FaceCam>();
            var lg = new GameObject("BottleLight", typeof(Light));
            lg.transform.SetParent(root, false); lg.transform.localPosition = new Vector3(0, 0.07f, 0.05f);
            var l = lg.GetComponent<Light>(); l.type = LightType.Point; l.color = new Color(0.55f, 0.78f, 1f); l.range = 1.2f; l.intensity = 0.35f; l.shadows = LightShadows.None;
            _bottle = root;
        }

        static Material Tint(Color c, bool add)
        {
            var m = new Material(add ? FxMat.Additive : FxMat.Alpha);
            m.SetTexture("_BaseMap", Texture2D.whiteTexture);
            m.SetColor("_BaseColor", c);
            if (!add && c.a >= 0.99f) { m.renderQueue = 2450; m.SetFloat("_ZWrite", 1f); }
            return m;
        }

        static Renderer Part(Transform parent, string name, Mesh mesh, Material m)
        {
            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(parent, false);
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = m; mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; mr.receiveShadows = false;
            return mr;
        }

        /// <summary>Surface of revolution around +y; radius r[i] at height y[i]. Own normals (no shared seams).</summary>
        static Mesh Lathe(float[] r, float[] y, int seg)
        {
            var v = new System.Collections.Generic.List<Vector3>();
            var n = new System.Collections.Generic.List<Vector3>();
            var t = new System.Collections.Generic.List<int>();
            int rows = r.Length;
            for (int i = 0; i < rows; i++)
            {
                // profile normal from the neighbouring points
                float dr = r[Mathf.Min(i + 1, rows - 1)] - r[Mathf.Max(i - 1, 0)];
                float dy = y[Mathf.Min(i + 1, rows - 1)] - y[Mathf.Max(i - 1, 0)];
                var pn = new Vector2(dy, -dr).normalized; if (pn.sqrMagnitude < 0.5f) pn = new Vector2(0, i == 0 ? -1 : 1);
                for (int s = 0; s <= seg; s++)
                {
                    float a = s / (float)seg * Mathf.PI * 2f;
                    float ca = Mathf.Cos(a), sa = Mathf.Sin(a);
                    v.Add(new Vector3(ca * r[i], y[i], sa * r[i]));
                    n.Add(new Vector3(ca * pn.x, pn.y, sa * pn.x).normalized);
                }
            }
            for (int i = 0; i < rows - 1; i++)
                for (int s = 0; s < seg; s++)
                {
                    int a = i * (seg + 1) + s, b = a + seg + 1;
                    t.AddRange(new[] { a, b, a + 1, a + 1, b, b + 1 });
                }
            var m = new Mesh { name = "Lathe" };
            m.SetVertices(v); m.SetNormals(n); m.SetTriangles(t, 0); m.RecalculateBounds();
            return m;
        }

        void BuildFx()
        {
            // rings on the water (footsteps: faint red; splashes: white)
            _rings = Ps("KoiRings", FxMat.Ring, ParticleSystemRenderMode.HorizontalBillboard);
            var m = _rings.main;
            m.startLifetime = 1.3f; m.startSize = 0.45f; m.startSpeed = 0f; m.maxParticles = 40;
            m.startColor = new Color(1f, 0.52f, 0.52f, 0.75f);
            var sz = _rings.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0, 0.35f, 1, 2.6f));
            var col = _rings.colorOverLifetime; col.enabled = true; col.color = Fade(0.12f);

            _redGlow = Ps("KoiStepGlow", FxMat.Additive, ParticleSystemRenderMode.HorizontalBillboard);
            m = _redGlow.main;
            m.startLifetime = 0.9f; m.startSize = 0.7f; m.startSpeed = 0f; m.maxParticles = 20;
            m.startColor = new Color(1f, 0.35f, 0.35f, 0.35f);
            sz = _redGlow.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0, 0.6f, 1, 1.4f));
            col = _redGlow.colorOverLifetime; col.enabled = true; col.color = Fade(0.1f);

            _drops = Ps("KoiDrops", FxMat.Alpha, ParticleSystemRenderMode.Billboard);
            m = _drops.main;
            m.startLifetime = new ParticleSystem.MinMaxCurve(0.4f, 0.7f); m.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.07f);
            m.startSpeed = new ParticleSystem.MinMaxCurve(1.2f, 2.4f); m.gravityModifier = 1.4f; m.maxParticles = 80;
            m.startColor = new Color(0.9f, 0.96f, 1f, 0.85f);
            var sh = _drops.shape; sh.enabled = true; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 28f; sh.radius = 0.05f; sh.rotation = new Vector3(-90, 0, 0);

            _sparkle = Ps("KoiSparkle", FxMat.Additive, ParticleSystemRenderMode.Billboard);
            m = _sparkle.main;
            m.startLifetime = new ParticleSystem.MinMaxCurve(0.4f, 0.8f); m.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.08f);
            m.startSpeed = new ParticleSystem.MinMaxCurve(0.2f, 0.7f); m.gravityModifier = -0.05f; m.maxParticles = 40;
            m.startColor = new ParticleSystem.MinMaxGradient(new Color(0.6f, 0.85f, 1f, 1f), new Color(1f, 0.7f, 0.75f, 1f));
            sh = _sparkle.shape; sh.enabled = true; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = 0.08f;
            col = _sparkle.colorOverLifetime; col.enabled = true; col.color = Fade(0.1f);
        }

        static Gradient Fade(float inAt)
        {
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                      new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, inAt), new GradientAlphaKey(0, 1) });
            return g;
        }

        static ParticleSystem Ps(string name, Material mat, ParticleSystemRenderMode mode)
        {
            var go = new GameObject(name, typeof(ParticleSystem));
            var ps = go.GetComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main; main.simulationSpace = ParticleSystemSimulationSpace.World; main.playOnAwake = false; main.loop = true;   // never stops: emitted by hand only
            var em = ps.emission; em.enabled = false;
            var sh = ps.shape; sh.enabled = false;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = mat; r.renderMode = mode; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ps.Play();
            return ps;
        }

        static Transform FindBone(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>()) if (t.name == name) return t;
            return null;
        }

        class FaceCam : MonoBehaviour
        {
            void LateUpdate() { var c = Camera.main; if (c) transform.rotation = c.transform.rotation; }
        }
    }
}
