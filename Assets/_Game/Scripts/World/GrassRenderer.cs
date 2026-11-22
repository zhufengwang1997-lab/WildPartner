using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace WildPartner.World
{
    /// <summary>
    /// Lawn grass around the camera (画面整改 1, rebuilt for 试玩反馈 5). The ground is split into 7 m chunks on a
    /// fixed world grid; each frame the chunks within <see cref="lod2Radius"/> are submitted with
    /// Graphics.RenderMeshPrimitives (GPU instancing: one instance = one tuft on a 0.25 m cell; Unity frustum-culls
    /// each chunk by its bounds). A tuft is up to <see cref="Blades"/> thin tapering blades (4 segments + tip,
    /// 1–3 cm wide, 30–70 cm tall, curving outwards with a drooping tip) plus a few loose single blades.
    /// LOD without seams: the blades are sorted by their thinning threshold, and the three LOD meshes are prefixes
    /// of that list (all / <see cref="Lod1Blades"/> / <see cref="Lod2Blades"/>). The shader thins blades with
    /// distance in exactly that order and widens the survivors, so by the time a chunk switches to a smaller
    /// mesh the blades it drops are already zero-sized. Placement comes from the cell hash, the terrain height
    /// texture and the baked grass map in the vertex shader: no per-blade data on the CPU.
    /// </summary>
    [ExecuteAlways]
    public class GrassRenderer : MonoBehaviour
    {
        public const int Blades = 24, Lod1Blades = 10, Lod2Blades = 4;

        public Material material;
        public Texture2D grassMap;     // R density, G height, B dryness (linear)
        public Texture2D groundTint;   // sRGB, 0.5 = neutral; also tints the terrain
        public float cell = 0.25f;
        public int cellsPerChunkSide = 28;
        public float lod0Radius = 12f, lod1Radius = 28f, lod2Radius = 60f;
        [Tooltip("Quality scale: 1 = full, 0.5 = half the far radius")]
        public static float Quality = 1f;
        public static GrassRenderer Instance { get; private set; }

        Terrain _terrain;
        Texture2D _height;
        Mesh[] _lods;
        readonly Dictionary<long, MaterialPropertyBlock> _blocks = new Dictionary<long, MaterialPropertyBlock>();
        readonly Dictionary<long, Vector2> _chunkHeights = new Dictionary<long, Vector2>();

        static readonly int ChunkId = Shader.PropertyToID("_Chunk");
        static readonly int LodId = Shader.PropertyToID("_WP_GrassLod");
        static readonly int HeightTexId = Shader.PropertyToID("_WP_GrassHeight");
        static readonly int MapId = Shader.PropertyToID("_WP_GrassMap");
        static readonly int TintId = Shader.PropertyToID("_WP_GroundTint");
        static readonly int SizeId = Shader.PropertyToID("_WP_TerrainSize");
        static readonly int PlayerId = Shader.PropertyToID("_WP_PlayerPos");

        public int LastChunkCount { get; private set; }

        void OnEnable()
        {
            Instance = this;
            Setup();
        }

        void OnDisable()
        {
            if (Instance == this) Instance = null;
            if (_height) DestroyImmediate(_height);
            if (_lods != null) foreach (var m in _lods) if (m) DestroyImmediate(m);
            _lods = null;
            _blocks.Clear(); _chunkHeights.Clear();
        }

        public void Setup()
        {
            _terrain = Terrain.activeTerrain ? Terrain.activeTerrain : FindFirstObjectByType<Terrain>();
            if (!_terrain || !material) return;
            var td = _terrain.terrainData;
            int res = td.heightmapResolution;
            if (!_height)
            {
                var h = td.GetHeights(0, 0, res, res);
                var data = new float[res * res];
                for (int z = 0; z < res; z++) for (int x = 0; x < res; x++) data[z * res + x] = h[z, x];
                _height = new Texture2D(res, res, TextureFormat.RFloat, false, true) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "GrassHeight" };
                _height.SetPixelData(data, 0);
                _height.Apply(false, true);
            }
            if (_lods == null)
            {
                var all = BuildTuft(20261123);
                _lods = new[] { Prefix(all, Blades), Prefix(all, Lod1Blades), Prefix(all, Lod2Blades) };
            }
            Shader.SetGlobalTexture(HeightTexId, _height);
            if (grassMap) Shader.SetGlobalTexture(MapId, grassMap);
            if (groundTint) Shader.SetGlobalTexture(TintId, groundTint);
            Shader.SetGlobalVector(SizeId, new Vector4(td.size.x, td.size.y, res, _terrain.transform.position.y));
            Shader.SetGlobalVector(PlayerId, new Vector4(0, -999, 0, 0.9f));
        }

        struct BladeDef { public Vector2 off, facing; public float lean, width, heightVar; public bool single; }

        /// <summary>
        /// One tuft: 16 blades from a ~5 cm base fanning outwards, plus 8 loose single blades further out in the
        /// cell, shuffled together so thinning removes tuft and loose blades evenly. Returned in thinning order.
        /// </summary>
        static List<BladeDef> BuildTuft(int seed)
        {
            var rnd = new System.Random(seed);
            float R() => (float)rnd.NextDouble();
            var list = new List<BladeDef>();
            for (int b = 0; b < Blades; b++)
            {
                bool single = b % 3 == 2;                                         // 8 of 24 are loose blades
                float r = single ? 0.3f + R() * 0.8f : Mathf.Sqrt(R()) * 0.2f;   // in cells
                float a = R() * Mathf.PI * 2;
                var off = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
                // tuft blades curve away from the tuft centre; loose blades pick any direction
                float fa = single ? R() * Mathf.PI * 2 : a + (R() - 0.5f) * 1.6f;
                list.Add(new BladeDef
                {
                    off = off,
                    facing = new Vector2(Mathf.Cos(fa), Mathf.Sin(fa)),
                    lean = single ? 0.2f + R() * 0.35f : 0.25f + R() * 0.5f,
                    width = 0.007f + R() * 0.008f,         // half width: 1.4–3 cm blades
                    heightVar = single ? 0.75f + R() * 0.3f : 0.55f + R() * 0.45f,
                    single = single,
                });
            }
            // shuffle (Fisher–Yates) but keep the first few blades as tuft blades so even LOD2 reads as a tuft
            for (int i = list.Count - 1; i > 0; i--) { int j = rnd.Next(i + 1); (list[i], list[j]) = (list[j], list[i]); }
            for (int i = 0; i < Lod2Blades; i++)
                if (list[i].single)
                    for (int j = Lod2Blades; j < list.Count; j++)
                        if (!list[j].single) { (list[i], list[j]) = (list[j], list[i]); break; }
            return list;
        }

        static readonly float[] Rows = { 0f, 0.28f, 0.52f, 0.74f };   // 4 segments, tip at t = 1

        static Mesh Prefix(List<BladeDef> blades, int count)
        {
            var pos = new List<Vector3>(); var side = new List<Vector3>(); var uv = new List<Vector2>(); var uv1 = new List<Vector2>(); var tri = new List<int>();
            for (int b = 0; b < count; b++)
            {
                var d = blades[b];
                var sv = new Vector3(d.facing.y, d.lean, -d.facing.x);                     // side vector (facing rotated -90°), y = lean
                var r1 = new Vector2((b + 0.5f) / Blades, d.heightVar);                       // thinning threshold, height var
                int v0 = pos.Count;
                foreach (var t in Rows)
                    foreach (var s in new[] { -1f, 1f })
                    {
                        pos.Add(new Vector3(d.off.x, t, d.off.y)); side.Add(sv); uv.Add(new Vector2(s * d.width, t)); uv1.Add(r1);
                    }
                pos.Add(new Vector3(d.off.x, 1f, d.off.y)); side.Add(sv); uv.Add(new Vector2(0, 1)); uv1.Add(r1);
                for (int k = 0; k < Rows.Length - 1; k++)
                {
                    int i = v0 + k * 2;
                    tri.AddRange(new[] { i, i + 2, i + 1, i + 1, i + 2, i + 3 });
                }
                int last = v0 + (Rows.Length - 1) * 2;
                tri.AddRange(new[] { last, last + 2, last + 1 });
            }
            var m = new Mesh { name = $"GrassTuft{count}", hideFlags = HideFlags.DontSave };
            m.SetVertices(pos); m.SetNormals(side); m.SetUVs(0, uv); m.SetUVs(1, uv1); m.SetTriangles(tri, 0);
            m.bounds = new Bounds(Vector3.zero, Vector3.one * 1000);
            return m;
        }

        static readonly int PushersId = Shader.PropertyToID("_WP_GrassPushers");
        readonly Vector4[] _pushers = new Vector4[6];
        Creatures.Creature[] _creatures;

        void Update()
        {
            if (!Application.isPlaying) return;
            var p = Player.PlayerController.Instance;
            if (p) Shader.SetGlobalVector(PlayerId, new Vector4(p.transform.position.x, p.transform.position.y, p.transform.position.z, 0.9f));
            // animals part the grass too, so a rabbit or cat in 50 cm grass is half hidden, not lost (试玩反馈 5)
            _creatures ??= FindObjectsByType<Creatures.Creature>(FindObjectsSortMode.None);
            for (int i = 0; i < _pushers.Length; i++)
            {
                var c = i < _creatures.Length ? _creatures[i] : null;
                bool on = c && c.isActiveAndEnabled && c.Current != Creatures.Creature.State.Hidden;
                var cp = on ? c.transform.position : Vector3.down * 999;
                _pushers[i] = new Vector4(cp.x, cp.y, cp.z, on ? 0.75f : 0f);
            }
            Shader.SetGlobalVectorArray(PushersId, _pushers);
            Submit(null, Camera.main ? Camera.main.transform.position : transform.position);
        }

        /// <summary>Queue the chunks around <paramref name="centre"/> for this frame (camera null = all cameras).</summary>
        public void Submit(Camera cam, Vector3 centre)
        {
            if (!_terrain || !material || _lods == null) { Setup(); if (_lods == null) return; }
            float q = Mathf.Clamp(Quality, 0.3f, 1.5f);
            float r0 = lod0Radius * Mathf.Min(q, 1f), r1 = lod1Radius * q, far = lod2Radius * q;
            Shader.SetGlobalVector(LodId, new Vector4(r0, r1, (float)Lod1Blades / Blades, (float)Lod2Blades / Blades));
            Shader.SetGlobalVector("_WP_GrassFade", new Vector4(far * 0.72f, far, 0, 0));
            float chunk = cell * cellsPerChunkSide;
            int r = Mathf.CeilToInt(far / chunk) + 1;
            int cx = Mathf.FloorToInt(centre.x / chunk), cz = Mathf.FloorToInt(centre.z / chunk);
            float size = _terrain.terrainData.size.x;
            var rp = new RenderParams(material)
            {
                camera = cam,
                shadowCastingMode = ShadowCastingMode.Off,
                receiveShadows = true,
                layer = gameObject.layer,
                motionVectorMode = MotionVectorGenerationMode.Object,
            };
            int count = 0, cells = cellsPerChunkSide * cellsPerChunkSide;
            var c2 = new Vector2(centre.x, centre.z);
            for (int z = cz - r; z <= cz + r; z++)
                for (int x = cx - r; x <= cx + r; x++)
                {
                    if (x < 0 || z < 0 || (x + 1) * chunk > size || (z + 1) * chunk > size) continue;
                    // nearest point of the chunk to the camera (exact, so the LOD choice matches the shader's thinning)
                    var nearest = new Vector2(Mathf.Clamp(c2.x, x * chunk, (x + 1) * chunk), Mathf.Clamp(c2.y, z * chunk, (z + 1) * chunk));
                    float d = Vector2.Distance(nearest, c2);
                    if (d > far) continue;
                    int lod = d < r0 ? 0 : d < r1 ? 1 : 2;
                    long key = ((long)x << 20) ^ z;
                    if (!_blocks.TryGetValue(key, out var mpb))
                    {
                        mpb = new MaterialPropertyBlock();
                        mpb.SetVector(ChunkId, new Vector4(x * chunk, z * chunk, cell, cellsPerChunkSide));
                        _blocks[key] = mpb;
                    }
                    var hr = ChunkHeights(x, z, chunk);
                    var mid = new Vector2((x + 0.5f) * chunk, (z + 0.5f) * chunk);
                    rp.matProps = mpb;
                    rp.worldBounds = new Bounds(new Vector3(mid.x, (hr.x + hr.y) * 0.5f, mid.y), new Vector3(chunk + 2, hr.y - hr.x + 3, chunk + 2));
                    Graphics.RenderMeshPrimitives(rp, _lods[lod], 0, cells);
                    count++;
                }
            LastChunkCount = count;
        }

        Vector2 ChunkHeights(int x, int z, float chunk)
        {
            long key = ((long)x << 20) ^ z;
            if (_chunkHeights.TryGetValue(key, out var v)) return v;
            float lo = float.MaxValue, hi = float.MinValue;
            for (int j = 0; j <= 4; j++)
                for (int i = 0; i <= 4; i++)
                {
                    float h = _terrain.SampleHeight(new Vector3((x + i / 4f) * chunk, 0, (z + j / 4f) * chunk)) + _terrain.transform.position.y;
                    lo = Mathf.Min(lo, h); hi = Mathf.Max(hi, h);
                }
            v = new Vector2(lo, hi + 0.6f);
            _chunkHeights[key] = v;
            return v;
        }
    }
}
