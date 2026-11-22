using System.Collections.Generic;
using UnityEngine;
using WildPartner.Creatures;
using WildPartner.Player;

namespace WildPartner.World
{
    /// <summary>
    /// Ordinary fish (试玩反馈 1123b-7): a few schools of 6-12 small silver-grey or pale-gold fish in clear shallow
    /// water. Scenery only - not creatures, not catchable, not in the journal.
    ///
    /// Each school follows its own closed, curving loop (made at start from a wobbly ellipse pulled in until every
    /// point is in water at least 0.45 m deep). Every fish has a slot behind the leading point with its own spacing,
    /// side offset, depth and a slow speed wobble, and steers towards its slot with a damped spring, so the school
    /// keeps its spacing but never moves in lock-step. When the witch comes within ~4.5 m the school scatters
    /// (each fish darts away from her with some randomness), then after ~2 s drifts back into formation.
    /// Cost: one tiny procedural mesh, two shared materials, plain MeshRenderers (SRP batcher), no shadows, and
    /// schools farther than 70 m from the camera are switched off entirely.
    /// </summary>
    public class FishSchools : MonoBehaviour
    {
        public static FishSchools Instance { get; private set; }
        public Material silver, gold;

        public class Fish
        {
            public Transform t;
            public Vector3 p, v;
            public float order, side, depth, phase, size;
            public Quaternion heading = Quaternion.identity;
        }

        public class School
        {
            public Vector3[] path;            // closed loop, world positions at the water surface
            public float[] cum;               // arc length at each point
            public float length, s, speed, scatterTime, surface;
            public Vector3 centre;
            public List<Fish> fish = new List<Fish>();
            public GameObject root;
            public bool scattered;
            /// <summary>Mean distance of the fish from their formation slots (smoke test).</summary>
            public float Spread;
        }

        public readonly List<School> Schools = new List<School>();

        /// <summary>School centres (world xz), fish count, gold or silver.</summary>
        static readonly (Vector2 c, int n, bool gold)[] Spots =
        {
            // 1123c-B5: ten schools along the whole river, the pond and the lake (tools: HabitatPicker)
            (new Vector2(140.4f, 271.6f), 8, true),     // upper river, below the falls
            (new Vector2(182.3f, 254.8f), 7, false),
            (new Vector2(211.8f, 241.8f), 10, true),
            (new Vector2(233.2f, 246.7f), 8, false),
            (new Vector2(263.0f, 211.4f), 9, true),     // below the bridge
            (new Vector2(264.6f, 189.0f), 7, false),
            (new Vector2(258.5f, 165.2f), 8, true),     // the bend
            (new Vector2(239.3f, 147.2f), 9, false),    // lower river, by the pond
            (new Vector2(311.7f, 322.0f), 12, true),    // lake shallows, east
            (new Vector2(259.1f, 324.1f), 10, false),   // lake, west
        };

        void Awake() => Instance = this;
        void OnDestroy() { if (Instance == this) Instance = null; }

        void Start()
        {
            var mesh = FishMesh();
            var rnd = new System.Random(1123);
            float R(float a, float b) => a + (float)rnd.NextDouble() * (b - a);
            foreach (var spot in Spots)
            {
                float x = spot.c.x, z = spot.c.y;
                if (!Creature.SwimmablePoint(ref x, ref z, out float level)) continue;
                Shallows(ref x, ref z, ref level);
                var sc = new School { centre = new Vector3(x, level, z), surface = level, speed = R(0.55f, 0.85f) };
                if (!BuildPath(sc, R(3.5f, 6f), R(2.5f, 4f), R(0f, 6.28f))) continue;
                sc.root = new GameObject($"School_{Schools.Count}");
                sc.root.transform.SetParent(transform, false);
                for (int i = 0; i < spot.n; i++)
                {
                    var go = new GameObject($"Fish_{i}");
                    go.transform.SetParent(sc.root.transform, false);
                    go.AddComponent<MeshFilter>().sharedMesh = mesh;
                    var mr = go.AddComponent<MeshRenderer>();
                    mr.sharedMaterial = spot.gold ? gold : silver;
                    mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    mr.receiveShadows = false;
                    var f = new Fish
                    {
                        t = go.transform,
                        order = i * R(0.32f, 0.5f) + R(0f, 0.2f),
                        side = R(-0.45f, 0.45f),
                        depth = R(0.1f, 0.22f),
                        phase = R(0f, 6.28f),
                        size = R(1.5f, 2.0f),   // ~0.3-0.4 m: readable from the usual camera distance
                    };
                    go.transform.localScale = Vector3.one * f.size;
                    f.p = Slot(sc, f, 0f);
                    go.transform.position = f.p;
                    sc.fish.Add(f);
                }
                Schools.Add(sc);
                Debug.Log($"[Fish] school {Schools.Count - 1} at ({x:0.0}, {z:0.0}) surface {level:0.00} bed {Creature.GroundHeight(x, z):0.00} loop {sc.length:0.0} m, {spot.n} fish");
            }
            Debug.Log($"[Fish] {Schools.Count} schools, {Schools.ConvertAll(s => s.fish.Count).Sum()} fish");
        }

        /// <summary>Move a school centre to the nearest clear shallows (water 0.6-1.4 m deep) within 25 m, so the bed
        /// shows through around the fish; stays put if there is none.</summary>
        static void Shallows(ref float x, ref float z, ref float level)
        {
            float d0 = level - Creature.GroundHeight(x, z);
            if (d0 <= 1.4f) return;
            for (int ring = 1; ring <= 25; ring++)
            {
                int n = ring * 6;
                for (int i = 0; i < n; i++)
                {
                    float a = i * Mathf.PI * 2f / n, px = x + Mathf.Cos(a) * ring, pz = z + Mathf.Sin(a) * ring;
                    if (!WorldLayout.WaterSurface(px, pz, out float lv)) continue;
                    float d = lv - Creature.GroundHeight(px, pz);
                    if (d >= 0.6f && d <= 1.4f) { x = px; z = pz; level = lv; return; }
                }
            }
        }

        bool BuildPath(School sc, float rx, float rz, float rot)
        {
            const int N = 40;
            var pts = new Vector3[N];
            float cs = Mathf.Cos(rot), sn = Mathf.Sin(rot);
            for (int i = 0; i < N; i++)
            {
                float a = i * Mathf.PI * 2f / N;
                float wob = 1f + 0.22f * Mathf.Sin(a * 3f + rot) + 0.1f * Mathf.Sin(a * 5f);
                float lx = Mathf.Cos(a) * rx * wob, lz = Mathf.Sin(a) * rz * wob;
                Vector2 off = new Vector2(lx * cs - lz * sn, lx * sn + lz * cs);
                // pull in towards the centre until the point is in water deep enough
                Vector3 p = sc.centre;
                for (float k = 1f; k > 0.05f; k -= 0.1f)
                {
                    float x = sc.centre.x + off.x * k, z = sc.centre.z + off.y * k;
                    if (WorldLayout.WaterSurface(x, z, out float lv) && lv - Creature.GroundHeight(x, z) > 0.45f) { p = new Vector3(x, lv, z); break; }
                }
                pts[i] = p;
            }
            // smooth twice so the pulled-in points still make a flowing curve
            for (int it = 0; it < 2; it++)
            {
                var q = new Vector3[N];
                for (int i = 0; i < N; i++) q[i] = (pts[(i + N - 1) % N] + pts[i] * 2f + pts[(i + 1) % N]) * 0.25f;
                pts = q;
            }
            sc.path = pts;
            sc.cum = new float[N + 1];
            for (int i = 0; i < N; i++) sc.cum[i + 1] = sc.cum[i] + Vector3.Distance(pts[i], pts[(i + 1) % N]);
            sc.length = sc.cum[N];
            return sc.length > 6f;
        }

        static void PathAt(School sc, float s, out Vector3 pos, out Vector3 dir)
        {
            s = Mathf.Repeat(s, sc.length);
            int n = sc.path.Length, i = 0;
            while (i < n - 1 && sc.cum[i + 1] < s) i++;
            float seg = sc.cum[i + 1] - sc.cum[i];
            float t = seg > 1e-4f ? (s - sc.cum[i]) / seg : 0f;
            Vector3 a = sc.path[i], b = sc.path[(i + 1) % n];
            pos = Vector3.Lerp(a, b, t);
            dir = b - a; dir.y = 0;
            dir = dir.sqrMagnitude > 1e-6f ? dir.normalized : Vector3.forward;
        }

        static Vector3 Slot(School sc, Fish f, float time)
        {
            // each fish lags a little differently (speed variation) and weaves slightly sideways
            float lag = f.order + Mathf.Sin(time * 0.7f + f.phase) * 0.25f;
            PathAt(sc, sc.s - lag, out var pos, out var dir);
            Vector3 side = new Vector3(dir.z, 0, -dir.x);
            pos += side * (f.side + Mathf.Sin(time * 1.3f + f.phase * 2f) * 0.12f);
            pos.y = sc.surface - f.depth;
            return pos;
        }

        void Update()
        {
            float dt = Mathf.Min(Time.deltaTime, 0.05f);
            if (dt <= 0f) return;
            var cam = Camera.main;
            var player = PlayerController.Instance;
            float time = Time.time;
            foreach (var sc in Schools)
            {
                bool near = !cam || (cam.transform.position - sc.centre).sqrMagnitude < 70f * 70f;
                if (sc.root.activeSelf != near) sc.root.SetActive(near);
                if (!near) continue;

                sc.s += sc.speed * (1f + 0.25f * Mathf.Sin(time * 0.31f + sc.centre.x)) * dt;
                Vector3 pp = player ? player.transform.position : new Vector3(1e5f, 0, 0);

                // scatter when she comes close (any school fish within 4.5 m, or the school's centre within 6 m)
                bool close = false;
                foreach (var f in sc.fish)
                {
                    Vector3 d = f.p - pp; d.y = 0;
                    if (d.sqrMagnitude < 3.8f * 3.8f) { close = true; break; }
                }
                if (close && sc.scatterTime <= 0f)
                {
                    sc.scatterTime = 2.2f;
                    sc.scattered = true;
                    foreach (var f in sc.fish)
                    {
                        Vector3 away = f.p - pp; away.y = 0;
                        away = away.sqrMagnitude > 1e-4f ? away.normalized : Random.insideUnitSphere;
                        away = Quaternion.Euler(0, Random.Range(-50f, 50f), 0) * away;
                        f.v += away * Random.Range(2.2f, 3.4f);
                    }
                }
                sc.scatterTime -= dt;
                if (sc.scatterTime <= 0f && !close) sc.scattered = false;

                float pull = sc.scatterTime > 0f ? 0.6f : 5f;      // weak while scattered, then regroup
                float spread = 0f;
                foreach (var f in sc.fish)
                {
                    Vector3 slot = Slot(sc, f, time);
                    spread += Vector3.Distance(new Vector3(f.p.x, 0, f.p.z), new Vector3(slot.x, 0, slot.z));
                    Vector3 a = (slot - f.p) * pull - f.v * (sc.scatterTime > 0f ? 1.2f : 2.6f);
                    f.v += a * dt;
                    if (f.v.sqrMagnitude > 16f) f.v = f.v.normalized * 4f;
                    Vector3 np = f.p + f.v * dt;
                    // stay in the water: bounce off the shallows / bank
                    if (!WorldLayout.WaterSurface(np.x, np.z, out float lv) || lv - Creature.GroundHeight(np.x, np.z) < 0.3f)
                    {
                        f.v = -f.v * 0.4f;
                        np = f.p;
                    }
                    np.y = Mathf.Lerp(f.p.y, slot.y, 1f - Mathf.Exp(-3f * dt));
                    f.p = np;

                    // face the swimming direction, tail-beat as a small fast yaw wobble (faster when darting)
                    Vector3 hv = f.v; hv.y = 0;
                    float sp = hv.magnitude;
                    if (sp > 0.05f)
                    {
                        var look = Quaternion.LookRotation(hv);
                        f.heading = Quaternion.Slerp(f.heading, look, 1f - Mathf.Exp(-8f * dt));
                    }
                    f.phase += dt * (8f + sp * 10f);
                    f.t.position = f.p;
                    f.t.rotation = f.heading * Quaternion.Euler(0, Mathf.Sin(f.phase) * (7f + sp * 5f), 0);
                }
                sc.Spread = spread / Mathf.Max(1, sc.fish.Count);
            }
        }

        /// <summary>A small fish, ~0.2 m long, +Z forward: spindle body (6 rings x 6 sides) and a forked tail.</summary>
        static Mesh FishMesh()
        {
            var v = new List<Vector3>(); var t = new List<int>();
            float[] zs = { 0.11f, 0.08f, 0.03f, -0.02f, -0.06f, -0.09f };
            float[] rs = { 0.004f, 0.018f, 0.026f, 0.022f, 0.013f, 0.005f };
            const int S = 6;
            for (int r = 0; r < zs.Length; r++)
                for (int k = 0; k < S; k++)
                {
                    float a = k * Mathf.PI * 2f / S;
                    v.Add(new Vector3(Mathf.Cos(a) * rs[r] * 0.7f, Mathf.Sin(a) * rs[r], zs[r]));   // narrower than tall
                }
            for (int r = 0; r < zs.Length - 1; r++)
                for (int k = 0; k < S; k++)
                {
                    int a = r * S + k, b = r * S + (k + 1) % S, c = a + S, d = b + S;
                    t.AddRange(new[] { a, b, c, b, d, c });
                }
            int nose = v.Count; v.Add(new Vector3(0, 0, 0.125f));
            for (int k = 0; k < S; k++) t.AddRange(new[] { nose, (k + 1) % S, k });
            // forked tail, both sides with their own vertices: shared ones would get opposite normals summed to zero
            // (NaN in the lighting -> black fish inside a huge bloom halo)
            for (int side = 0; side < 2; side++)
            {
                int tail0 = v.Count;
                v.Add(new Vector3(0, 0, -0.085f)); v.Add(new Vector3(0, 0.035f, -0.13f)); v.Add(new Vector3(0, 0.005f, -0.11f));
                v.Add(new Vector3(0, -0.035f, -0.13f));
                if (side == 0) t.AddRange(new[] { tail0, tail0 + 1, tail0 + 2, tail0, tail0 + 2, tail0 + 3 });
                else t.AddRange(new[] { tail0, tail0 + 2, tail0 + 1, tail0, tail0 + 3, tail0 + 2 });
            }
            // make every triangle face outwards from the spine (z axis) so the toon lighting is right on both sides
            for (int i = 0; i < t.Count; i += 3)
            {
                Vector3 a = v[t[i]], b = v[t[i + 1]], c = v[t[i + 2]];
                Vector3 n = Vector3.Cross(b - a, c - a), cen = (a + b + c) / 3f;
                Vector3 outDir = new Vector3(cen.x, cen.y, 0f);
                if (outDir.sqrMagnitude < 1e-8f) continue;   // tail fin: double-sided anyway
                if (Vector3.Dot(n, outDir) < 0f) { int k = t[i + 1]; t[i + 1] = t[i + 2]; t[i + 2] = k; }
            }
            var m = new Mesh { name = "Fish" };
            m.SetVertices(v); m.SetTriangles(t, 0);
            m.RecalculateNormals(); m.RecalculateBounds();
            var nrm = m.normals;   // belt and braces: no zero-length normal may reach the shader
            for (int i = 0; i < nrm.Length; i++) if (nrm[i].sqrMagnitude < 0.5f) nrm[i] = Vector3.up;
            m.normals = nrm;
            return m;
        }
    }

    static class ListSum
    {
        public static int Sum(this List<int> l) { int s = 0; foreach (var x in l) s += x; return s; }
    }
}
