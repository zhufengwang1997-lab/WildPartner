using System.Collections.Generic;
using UnityEngine;

namespace WildPartner.World
{
    /// <summary>
    /// 试玩反馈 1123d-2: things floating down the river (foam flecks, petals, fallen leaves), so the direction of the flow
    /// reads at a glance. Each calm stretch of a river (between waterfalls) carries its own flecks; they move at the
    /// local flow speed (<see cref="WorldLayout.RiverFlow"/>, the same factor the water shader scrolls with), fade in
    /// at the top of the stretch and out at its end (the lake, the pond or the next waterfall's lip), then start again.
    /// Drawn as one particle system, flat on the water; only near the camera.
    /// </summary>
    public class RiverDrift : MonoBehaviour
    {
        /// <summary>Metres per second at flow factor 1 (Water.shader: _FlowSpeed 0.9 x 2.2).</summary>
        public const float BaseSpeed = 1.98f;
        const float Lift = 0.21f;

        class Run { public Vector3[] pts; public float[] cum; public float length; }
        struct Fleck { public Run run; public float s, side, size; public Color32 col; public float spin; }

        readonly List<Run> _runs = new List<Run>();
        readonly List<Fleck> _flecks = new List<Fleck>();
        ParticleSystem _ps;
        ParticleSystem.Particle[] _buf;
        System.Random _rng = new System.Random(1123);

        /// <summary>Smoke test: mean downstream speed (m/s) of the flecks on each river last frame, sign + = downstream.</summary>
        public static float MeanDownstream { get; private set; }
        public static int Count { get; private set; }
        public static RiverDrift Instance { get; private set; }

        void Awake() => Instance = this;
        void OnDestroy() { if (Instance == this) Instance = null; }

        float R(float a, float b) => a + (float)_rng.NextDouble() * (b - a);

        void Start()
        {
            Split(WorldLayout.RiverIn);
            Split(WorldLayout.RiverOut);
            foreach (var run in _runs)
            {
                int n = Mathf.Clamp(Mathf.RoundToInt(run.length / 2.2f), 4, 70);
                for (int i = 0; i < n; i++) _flecks.Add(NewFleck(run, R(0, run.length)));
            }
            Count = _flecks.Count;

            var go = new GameObject("RiverDrift_FX", typeof(ParticleSystem));
            go.transform.SetParent(transform, false);
            _ps = go.GetComponent<ParticleSystem>();
            var main = _ps.main;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = _flecks.Count;
            main.playOnAwake = false; main.loop = false;
            var em = _ps.emission; em.enabled = false;
            var shape = _ps.shape; shape.enabled = false;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.HorizontalBillboard;
            r.sharedMaterial = FxMat.Alpha;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            _buf = new ParticleSystem.Particle[_flecks.Count];
            Debug.Log($"[RiverDrift] {_runs.Count} calm stretches, {_flecks.Count} floating flecks");
        }

        Fleck NewFleck(Run run, float s)
        {
            double k = _rng.NextDouble();
            Color32 col = k < 0.55 ? new Color32(245, 248, 250, 230)                    // foam
                        : k < 0.78 ? new Color32(250, 196, 214, 240)                    // petals
                        : k < 0.92 ? new Color32(232, 150, 60, 245)                     // maple leaves
                        : new Color32(214, 190, 80, 245);                               // birch leaves
            float size = k < 0.55 ? R(0.12f, 0.3f) : k < 0.78 ? R(0.1f, 0.16f) : R(0.18f, 0.28f);
            return new Fleck { run = run, s = s, side = R(-0.75f, 0.75f), size = size, col = col, spin = R(0, 360) };
        }

        /// <summary>Calm stretches of a river polyline (segments that are not waterfalls), with their arc lengths.</summary>
        void Split(Vector3[] pts)
        {
            var cur = new List<Vector3>();
            for (int i = 0; i < pts.Length - 1; i++)
            {
                bool fall = pts[i].z - pts[i + 1].z > 3f;
                if (!fall)
                {
                    if (cur.Count == 0) cur.Add(pts[i]);
                    cur.Add(pts[i + 1]);
                }
                if (fall || i == pts.Length - 2)
                {
                    if (cur.Count >= 2)
                    {
                        // a fall drops over the end of its segment: the stretch below it starts at the foot
                        var run = new Run { pts = cur.ToArray(), cum = new float[cur.Count] };
                        for (int k = 1; k < cur.Count; k++)
                            run.cum[k] = run.cum[k - 1] + Vector2.Distance(new Vector2(cur[k].x, cur[k].y), new Vector2(cur[k - 1].x, cur[k - 1].y));
                        run.length = run.cum[cur.Count - 1];
                        if (run.length > 8f) _runs.Add(run);
                    }
                    cur = new List<Vector3>();
                }
            }
        }

        static void At(Run run, float s, out Vector2 pos, out Vector2 dir)
        {
            int i = 0;
            while (i < run.pts.Length - 2 && run.cum[i + 1] < s) i++;
            float seg = run.cum[i + 1] - run.cum[i];
            float t = seg > 1e-4f ? Mathf.Clamp01((s - run.cum[i]) / seg) : 0f;
            var a = new Vector2(run.pts[i].x, run.pts[i].y); var b = new Vector2(run.pts[i + 1].x, run.pts[i + 1].y);
            pos = Vector2.Lerp(a, b, t);
            dir = (b - a).normalized;
        }

        void Update()
        {
            if (!_ps) return;
            float dt = Mathf.Min(Time.deltaTime, 0.05f);
            var cam = Camera.main;
            Vector3 cp = cam ? cam.transform.position : Vector3.zero;
            int n = 0; float sum = 0; int moved = 0;
            for (int i = 0; i < _flecks.Count; i++)
            {
                var f = _flecks[i];
                At(f.run, f.s, out var pos, out var dir);
                var across = new Vector2(-dir.y, dir.x);
                var xz = pos + across * (f.side * WorldLayout.RiverHalfWidth);
                WorldLayout.RiverFlow(xz.x, xz.y, out var fd, out float sp);
                float v = BaseSpeed * Mathf.Max(sp, 0.05f) * (1f - 0.35f * Mathf.Abs(f.side));   // slower near the banks
                f.s += v * dt;
                if (dt > 0) { sum += v; moved++; }
                f.spin += dt * 25f * sp;
                if (f.s >= f.run.length) f = NewFleck(f.run, R(0f, 3f));
                _flecks[i] = f;

                var wp = new Vector3(xz.x, 0, xz.y);
                if ((wp - new Vector3(cp.x, 0, cp.z)).sqrMagnitude > 85f * 85f) continue;
                WorldLayout.RiverDistance(xz.x, xz.y, out float level);
                wp.y = level + Lift + Mathf.Sin(Time.time * 1.7f + f.spin * 0.1f) * 0.02f;
                // fade in at the top of the stretch and out at its end
                float fade = Mathf.Clamp01(f.s / 3f) * Mathf.Clamp01((f.run.length - f.s) / 4f);
                var c = f.col; c.a = (byte)(c.a * fade);
                _buf[n++] = new ParticleSystem.Particle
                {
                    position = wp, startSize = f.size, startColor = c, rotation = f.spin,
                    remainingLifetime = 1f, startLifetime = 2f,
                };
            }
            MeanDownstream = moved > 0 ? sum / moved : 0;
            _ps.SetParticles(_buf, n);
        }
    }
}
