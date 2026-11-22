using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using WildPartner.Creatures;
using WildPartner.Player;

namespace WildPartner.World
{
    /// <summary>
    /// 试玩反馈 1123c-B5: scenery animals that can't be caught, so the valley never looks empty —
    ///   butterflies fluttering over the meadows by day (groups of 2-3, extra groups fill any spot more than ~35 m
    ///   from other life), ducks paddling on the lake / river / pond (they paddle away if she comes close), and
    ///   small flocks of birds circling high up (seen from anywhere by looking up).
    /// Built at runtime from primitives (no models needed); far groups are switched off.
    /// </summary>
    public class AmbientLife : MonoBehaviour
    {
        public static AmbientLife Instance { get; private set; }
        public Material wing, duckBody, duckHead, beak, bird;

        class Flyer { public Transform t, wl, wr; public Vector3 target; public float phase, speed, hgt; }
        class Group { public Vector3 centre; public GameObject root; public List<Flyer> list = new List<Flyer>(); }
        class Duck { public Transform t; public Vector3 home, target; public float level, phase, speed; }
        class Flock { public Transform root; public Vector3 centre; public float radius, height, angle, speed; public List<Flyer> list = new List<Flyer>(); }

        readonly List<Group> _butterflies = new List<Group>();
        readonly List<Duck> _ducks = new List<Duck>();
        readonly List<Flock> _flocks = new List<Flock>();
        Mesh _wingMesh, _birdWing;
        System.Random _rnd = new System.Random(523);
        float R(float a, float b) => a + (float)_rnd.NextDouble() * (b - a);

        public int ButterflyCount => _butterflies.Sum(g => g.list.Count);
        public int DuckCount => _ducks.Count;
        public int BirdCount => _flocks.Sum(f => f.list.Count);
        /// <summary>Ground-level spots with life (butterfly groups, ducks) for the "always something in sight" check.</summary>
        public IEnumerable<Vector3> LifeSpots => _butterflies.Select(g => g.centre).Concat(_ducks.Select(d => d.home));

        static readonly Color[] WingColors =
        {
            new Color(1f, 0.78f, 0.25f), new Color(1f, 1f, 0.95f), new Color(0.55f, 0.75f, 1f), new Color(1f, 0.55f, 0.3f), new Color(0.95f, 0.7f, 0.95f),
        };

        static readonly Vector2[] ButterflySpots =
        {
            new Vector2(198, 134), new Vector2(160, 120), new Vector2(240, 112), new Vector2(170, 190), new Vector2(118, 236),
            new Vector2(112, 166), new Vector2(205, 205), new Vector2(285, 160), new Vector2(300, 205),
        };
        static readonly Vector2[] DuckSpots =
        {
            new Vector2(262, 300), new Vector2(300, 300), new Vector2(330, 330), new Vector2(232, 140), new Vector2(205, 243), new Vector2(255, 178),
        };

        void Awake() => Instance = this;
        void OnDestroy() { if (Instance == this) Instance = null; }

        void Start()
        {
            _wingMesh = WingMesh(0.09f, 0.07f);
            _birdWing = WingMesh(0.45f, 0.16f);
            var creatureHomes = CreatureDefs.All.Where(d => d.homes != null).SelectMany(d => d.homes).ToList();
            foreach (var s in ButterflySpots) AddButterflies(s, _rnd.Next(2, 4));
            foreach (var s in DuckSpots) AddDucks(s, _rnd.Next(1, 3));
            // fill the gaps: anywhere on dry land more than ~35 m from any life gets a butterfly group
            var fishSpots = FishSchools.Instance ? FishSchools.Instance.Schools.Select(sc => new Vector2(sc.centre.x, sc.centre.z)).ToList() : new List<Vector2>();
            float lo = PlayerController.BoundaryInset + 6f, hi = WorldLayout.Size - PlayerController.BoundaryInset - 6f;
            for (float x = lo; x <= hi; x += 22f)
                for (float z = lo; z <= hi; z += 22f)
                {
                    var p = new Vector2(x, z);
                    if (WorldLayout.WaterSurface(x, z, out _) || WorldLayout.RegionAt(x, z) == WorldLayout.Region.Edge) continue;
                    if (Vector2.Distance(p, WorldLayout.Cabin) < 14f) continue;
                    float near = Mathf.Min(
                        creatureHomes.Count > 0 ? creatureHomes.Min(h => Vector2.Distance(h, p)) : 999f,
                        LifeSpots.Select(l => Vector2.Distance(new Vector2(l.x, l.z), p)).DefaultIfEmpty(999f).Min(),
                        fishSpots.Select(f => Vector2.Distance(f, p)).DefaultIfEmpty(999f).Min());
                    if (near > 28f) AddButterflies(p, 2);
                }
            // birds: three small flocks circling over the valley
            AddFlock(new Vector2(150, 150), 30f, 34f, 5);
            AddFlock(new Vector2(240, 270), 38f, 40f, 4);
            AddFlock(new Vector2(120, 270), 26f, 36f, 3);
            Debug.Log($"[Ambient] {_butterflies.Count} butterfly groups ({ButterflyCount}), {DuckCount} ducks, {_flocks.Count} bird flocks ({BirdCount})");
        }

        static Mesh WingMesh(float len, float wid)
        {
            // a rounded wing: fan of triangles from the hinge (local x out along the wing)
            var m = new Mesh { name = "Wing" };
            const int N = 7;
            var v = new List<Vector3> { Vector3.zero }; var tri = new List<int>();
            for (int i = 0; i <= N; i++)
            {
                float a = Mathf.Lerp(-1.2f, 1.2f, i / (float)N);
                float r = len * (0.55f + 0.45f * Mathf.Cos(a * 0.9f));
                v.Add(new Vector3(Mathf.Cos(a * 0.65f) * r, 0, Mathf.Sin(a) * wid));
                if (i > 0) { tri.Add(0); tri.Add(i); tri.Add(i + 1); }
            }
            m.SetVertices(v); m.SetTriangles(tri, 0);
            m.SetNormals(Enumerable.Repeat(Vector3.up, v.Count).ToList());
            m.RecalculateBounds();
            return m;
        }

        Transform Part(Transform parent, Mesh mesh, Material mat, Vector3 pos, Vector3 scale, Color? tint = null)
        {
            var go = new GameObject("Part");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos; go.transform.localScale = scale;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            if (tint.HasValue) { var mpb = new MaterialPropertyBlock(); mpb.SetColor("_BaseColor", tint.Value); mr.SetPropertyBlock(mpb); }
            return go.transform;
        }

        static Mesh _sphere;
        static Mesh Sphere => _sphere ? _sphere : (_sphere = Resources.GetBuiltinResource<Mesh>("New-Sphere.fbx"));

        void AddButterflies(Vector2 at, int n)
        {
            var g = new Group { centre = new Vector3(at.x, Creature.GroundHeight(at.x, at.y), at.y) };
            g.root = new GameObject("Butterflies"); g.root.transform.SetParent(transform, false);
            var col = WingColors[_rnd.Next(WingColors.Length)];
            for (int i = 0; i < n; i++)
            {
                var f = new Flyer { phase = R(0, 6.28f), speed = R(0.9f, 1.5f), hgt = R(0.5f, 1.4f) };
                f.t = new GameObject("Butterfly").transform; f.t.SetParent(g.root.transform, false);
                f.t.position = g.centre + new Vector3(R(-2, 2), f.hgt, R(-2, 2));
                Part(f.t, Sphere, wing, Vector3.zero, new Vector3(0.018f, 0.018f, 0.07f), new Color(0.15f, 0.12f, 0.1f));
                f.wl = Part(f.t, _wingMesh, wing, Vector3.zero, Vector3.one, col);
                f.wr = Part(f.t, _wingMesh, wing, Vector3.zero, new Vector3(-1, 1, 1), col);
                f.target = f.t.position;
                g.list.Add(f);
            }
            _butterflies.Add(g);
        }

        void AddDucks(Vector2 at, int n)
        {
            for (int i = 0; i < n; i++)
            {
                float x = at.x + R(-3, 3), z = at.y + R(-3, 3);
                if (!Creature.SwimmablePoint(ref x, ref z, out float level)) continue;
                var d = new Duck { home = new Vector3(x, level, z), level = level, phase = R(0, 6.28f), speed = R(0.25f, 0.45f) };
                d.t = new GameObject("Duck").transform; d.t.SetParent(transform, false);
                d.t.position = d.home; d.t.rotation = Quaternion.Euler(0, R(0, 360), 0);
                bool white = _rnd.NextDouble() < 0.3;
                Part(d.t, Sphere, duckBody, new Vector3(0, 0.08f, 0), new Vector3(0.3f, 0.2f, 0.46f), white ? new Color(0.95f, 0.95f, 0.92f) : new Color(0.52f, 0.4f, 0.3f));
                Part(d.t, Sphere, duckBody, new Vector3(0, 0.16f, -0.2f), new Vector3(0.16f, 0.12f, 0.16f), white ? new Color(0.95f, 0.95f, 0.92f) : new Color(0.45f, 0.35f, 0.27f));  // tail
                Part(d.t, Sphere, duckHead, new Vector3(0, 0.28f, 0.17f), new Vector3(0.15f, 0.15f, 0.16f), white ? new Color(0.95f, 0.95f, 0.92f) : new Color(0.12f, 0.45f, 0.28f));
                Part(d.t, Sphere, beak, new Vector3(0, 0.26f, 0.27f), new Vector3(0.07f, 0.035f, 0.1f));
                d.target = d.home;
                _ducks.Add(d);
            }
        }

        void AddFlock(Vector2 at, float radius, float height, int n)
        {
            var fl = new Flock { centre = new Vector3(at.x, 0, at.y), radius = radius, height = height, angle = R(0, 6.28f), speed = R(0.12f, 0.18f) };
            fl.root = new GameObject("Birds").transform; fl.root.SetParent(transform, false);
            for (int i = 0; i < n; i++)
            {
                var f = new Flyer { phase = R(0, 6.28f) };
                f.t = new GameObject("Bird").transform; f.t.SetParent(fl.root, false);
                Part(f.t, Sphere, bird, Vector3.zero, new Vector3(0.08f, 0.08f, 0.3f));
                f.wl = Part(f.t, _birdWing, bird, Vector3.zero, Vector3.one);
                f.wr = Part(f.t, _birdWing, bird, Vector3.zero, new Vector3(-1, 1, 1));
                f.target = new Vector3(R(-3, 3), R(-1.5f, 1.5f), -i * 1.6f + R(-0.5f, 0.5f));   // loose V behind the leader
                fl.list.Add(f);
            }
            _flocks.Add(fl);
        }

        void Update()
        {
            var cam = Camera.main;
            if (!cam) return;
            var cp = cam.transform.position;
            float dt = Time.deltaTime, t = Time.time;
            var env = EnvController.Instance;
            bool day = !env || (env.timeOfDay > 6.2f && env.timeOfDay < 18.6f);
            bool indoors = EnvController.Indoors;
            var player = PlayerController.Instance ? PlayerController.Instance.transform.position : cp;

            foreach (var g in _butterflies)
            {
                bool on = day && !indoors && (g.centre - cp).sqrMagnitude < 90f * 90f && (Weather.Instance == null || Weather.Instance.RainAmount < 0.5f);
                if (g.root.activeSelf != on) g.root.SetActive(on);
                if (!on) continue;
                foreach (var f in g.list)
                {
                    var p = f.t.position;
                    if ((f.target - p).sqrMagnitude < 0.09f)
                    {
                        var c = g.centre + new Vector3(R(-3.5f, 3.5f), 0, R(-3.5f, 3.5f));
                        c.y = Creature.GroundHeight(c.x, c.z) + R(0.35f, 1.6f);
                        f.target = c;
                    }
                    var dir = (f.target - p).normalized;
                    // fluttery: a wobble on top of the straight line
                    var wob = new Vector3(Mathf.Sin(t * 3.1f + f.phase), Mathf.Sin(t * 4.3f + f.phase * 2f) * 0.8f, Mathf.Cos(t * 2.7f + f.phase)) * 0.6f;
                    p += (dir * f.speed + wob) * dt;
                    f.t.position = p;
                    if (dir.sqrMagnitude > 0.01f) f.t.rotation = Quaternion.Slerp(f.t.rotation, Quaternion.LookRotation(new Vector3(dir.x, 0, dir.z) + Vector3.forward * 0.001f), 4f * dt);
                    float flap = Mathf.Sin(t * 22f + f.phase) * 65f;
                    f.wl.localRotation = Quaternion.Euler(0, 0, flap); f.wr.localRotation = Quaternion.Euler(0, 0, -flap);
                }
            }

            foreach (var d in _ducks)
            {
                bool on = !indoors && (d.home - cp).sqrMagnitude < 110f * 110f;
                if (d.t.gameObject.activeSelf != on) d.t.gameObject.SetActive(on);
                if (!on) continue;
                var p = d.t.position;
                var away = p - player; away.y = 0;
                float speed = d.speed;
                if (away.magnitude < 6f)
                {
                    // she is close: paddle away (stays on the water)
                    var q = p + away.normalized * 4f;
                    float x = q.x, z = q.z;
                    if (WorldLayout.WaterSurface(x, z, out float lv) && lv > Creature.GroundHeight(x, z) + 0.25f) d.target = new Vector3(x, d.level, z);
                    speed = 1.4f;
                }
                else if ((d.target - p).sqrMagnitude < 0.25f)
                {
                    float x = d.home.x + R(-6, 6), z = d.home.z + R(-6, 6);
                    if (WorldLayout.WaterSurface(x, z, out float lv) && lv > Creature.GroundHeight(x, z) + 0.25f) d.target = new Vector3(x, d.level, z);
                }
                var dir = d.target - p; dir.y = 0;
                if (dir.sqrMagnitude > 0.01f)
                {
                    d.t.rotation = Quaternion.RotateTowards(d.t.rotation, Quaternion.LookRotation(dir), 90f * dt);
                    p += d.t.forward * speed * dt;
                }
                p.y = d.level - 0.04f + Mathf.Sin(t * 1.7f + d.phase) * 0.015f;
                d.t.position = p;
            }

            foreach (var fl in _flocks)
            {
                bool on = !indoors;
                if (fl.root.gameObject.activeSelf != on) fl.root.gameObject.SetActive(on);
                if (!on) continue;
                fl.angle += fl.speed * dt;
                float gy = Creature.GroundHeight(fl.centre.x, fl.centre.z);
                var lead = fl.centre + new Vector3(Mathf.Cos(fl.angle), 0, Mathf.Sin(fl.angle)) * fl.radius;
                lead.y = gy + fl.height + Mathf.Sin(fl.angle * 3f) * 3f;
                var fwd = new Vector3(-Mathf.Sin(fl.angle), 0, Mathf.Cos(fl.angle));
                var rot = Quaternion.LookRotation(fwd) * Quaternion.Euler(0, 0, -12f);   // banking into the turn
                foreach (var f in fl.list)
                {
                    f.t.position = lead + rot * f.target;
                    f.t.rotation = rot;
                    // flap a few beats, then glide
                    float c = Mathf.Repeat(t * 0.5f + f.phase, 1f);
                    float flap = c < 0.45f ? Mathf.Sin(t * 14f + f.phase) * 35f : 8f;
                    f.wl.localRotation = Quaternion.Euler(0, 0, flap); f.wr.localRotation = Quaternion.Euler(0, 0, -flap);
                }
            }
        }
    }
}
