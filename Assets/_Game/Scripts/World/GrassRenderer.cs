using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace WildPartner.World
{
    /// <summary>
    /// Dense short grass around the camera (画面整改 1). The ground is split into 8 m chunks on a fixed world grid;
    /// every frame the chunks within <see cref="farRadius"/> are submitted with Graphics.RenderMeshPrimitives
    /// (Unity frustum-culls them by their bounds). Near chunks use a 18-blade clump per 0.33 m cell, far chunks a
    /// wider 9-blade clump per 0.66 m cell. Blade placement is computed in the vertex shader from the cell hash,
    /// the terrain height texture and the baked grass map, so no per-blade data lives on the CPU or GPU.
    /// </summary>
    [ExecuteAlways]
    public class GrassRenderer : MonoBehaviour
    {
        public Material material;
        public Texture2D grassMap;     // R density, G height, B dryness (linear)
        public Texture2D groundTint;   // sRGB, 0.5 = neutral; also tints the terrain
        public float cellSize = 0.33f;
        public int cellsPerChunk = 24;
        public float nearRadius = 22f, farRadius = 64f;
        public int nearBlades = 18, farBlades = 9;
        [Tooltip("Quality scale: 1 = full, 0.5 = half the far radius")]
        public static float Quality = 1f;
        public static GrassRenderer Instance { get; private set; }

        Terrain _terrain;
        Texture2D _height;
        Mesh _near, _far;
        readonly Dictionary<long, MaterialPropertyBlock> _blocks = new Dictionary<long, MaterialPropertyBlock>();
        readonly Dictionary<long, Vector2> _chunkHeights = new Dictionary<long, Vector2>();

        static readonly int ChunkId = Shader.PropertyToID("_Chunk");
        static readonly int WidthId = Shader.PropertyToID("_BladeWidth");
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
            if (_near) DestroyImmediate(_near);
            if (_far) DestroyImmediate(_far);
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
            if (!_near) _near = BuildClump(nearBlades, 0.05f, 1234);
            if (!_far) _far = BuildClump(farBlades, 0.08f, 4321);
            Shader.SetGlobalTexture(HeightTexId, _height);
            if (grassMap) Shader.SetGlobalTexture(MapId, grassMap);
            if (groundTint) Shader.SetGlobalTexture(TintId, groundTint);
            Shader.SetGlobalVector(SizeId, new Vector4(td.size.x, td.size.y, res, _terrain.transform.position.y));
            Shader.SetGlobalVector(PlayerId, new Vector4(0, -999, 0, 0.9f));
        }

        /// <summary>One clump: blades scattered in a disc of radius 0.75 cells, 3 segments + tip each.</summary>
        static Mesh BuildClump(int blades, float halfWidth, int seed)
        {
            var rnd = new System.Random(seed);
            float R() => (float)rnd.NextDouble();
            var pos = new List<Vector3>(); var side = new List<Vector3>(); var uv = new List<Vector2>(); var uv1 = new List<Vector2>(); var tri = new List<int>();
            float[] ts = { 0f, 0.38f, 0.72f };
            for (int b = 0; b < blades; b++)
            {
                float r = Mathf.Sqrt(R()) * 0.75f, a = R() * Mathf.PI * 2;
                var off = new Vector2(Mathf.Cos(a) * r, Mathf.Sin(a) * r);
                float yaw = R() * Mathf.PI * 2;
                var sv = new Vector3(Mathf.Cos(yaw), 0.15f + R() * 0.35f, Mathf.Sin(yaw));   // y = lean
                float w = halfWidth * (0.7f + R() * 0.6f);
                var r1 = new Vector2((b + R()) / blades, R());                              // density threshold, height var
                int v0 = pos.Count;
                foreach (var t in ts)
                {
                    foreach (var s in new[] { -1f, 1f })
                    {
                        pos.Add(new Vector3(off.x, t, off.y)); side.Add(sv); uv.Add(new Vector2(s * w, t)); uv1.Add(r1);
                    }
                }
                pos.Add(new Vector3(off.x, 1f, off.y)); side.Add(sv); uv.Add(new Vector2(0, 1)); uv1.Add(r1);
                for (int k = 0; k < 2; k++)
                {
                    int i = v0 + k * 2;
                    tri.AddRange(new[] { i, i + 2, i + 1, i + 1, i + 2, i + 3 });
                }
                tri.AddRange(new[] { v0 + 4, v0 + 6, v0 + 5 });
            }
            var m = new Mesh { name = $"GrassClump{blades}", hideFlags = HideFlags.DontSave };
            m.SetVertices(pos); m.SetNormals(side); m.SetUVs(0, uv); m.SetUVs(1, uv1); m.SetTriangles(tri, 0);
            m.bounds = new Bounds(Vector3.zero, Vector3.one * 1000);
            return m;
        }

        void Update()
        {
            if (!Application.isPlaying) return;
            var p = Player.PlayerController.Instance;
            if (p) Shader.SetGlobalVector(PlayerId, new Vector4(p.transform.position.x, p.transform.position.y, p.transform.position.z, 0.9f));
            Submit(null, Camera.main ? Camera.main.transform.position : transform.position);
        }

        /// <summary>Queue the chunks around <paramref name="centre"/> for this frame (camera null = all cameras).</summary>
        public void Submit(Camera cam, Vector3 centre)
        {
            if (!_terrain || !material || !_near) { Setup(); if (!_near) return; }
            float chunk = cellSize * cellsPerChunk;
            float far = farRadius * Mathf.Clamp(Quality, 0.3f, 1.5f);
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
            int count = 0;
            for (int z = cz - r; z <= cz + r; z++)
                for (int x = cx - r; x <= cx + r; x++)
                {
                    if (x < 0 || z < 0 || (x + 1) * chunk > size || (z + 1) * chunk > size) continue;
                    var mid = new Vector2((x + 0.5f) * chunk, (z + 0.5f) * chunk);
                    float d = Vector2.Distance(mid, new Vector2(centre.x, centre.z)) - chunk * 0.7f;
                    if (d > far) continue;
                    bool near = d < nearRadius;
                    long key = ((long)x << 20) ^ ((long)z << 1) ^ (near ? 1 : 0);
                    if (!_blocks.TryGetValue(key, out var mpb))
                    {
                        mpb = new MaterialPropertyBlock();
                        float cell = near ? cellSize : cellSize * 2;
                        int n = near ? cellsPerChunk : cellsPerChunk / 2;
                        mpb.SetVector(ChunkId, new Vector4(x * chunk, z * chunk, cell, n));
                        mpb.SetFloat(WidthId, near ? 1f : 1.6f);
                        _blocks[key] = mpb;
                    }
                    var hr = ChunkHeights(x, z, chunk);
                    rp.matProps = mpb;
                    rp.worldBounds = new Bounds(new Vector3(mid.x, (hr.x + hr.y) * 0.5f, mid.y), new Vector3(chunk + 2, hr.y - hr.x + 3, chunk + 2));
                    int cells = near ? cellsPerChunk * cellsPerChunk : (cellsPerChunk / 2) * (cellsPerChunk / 2);
                    Graphics.RenderMeshPrimitives(rp, near ? _near : _far, 0, cells);
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
