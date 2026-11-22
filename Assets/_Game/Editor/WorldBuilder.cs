using System;
using System.Collections.Generic;
using System.IO;
using Unity.AI.Navigation;
using Unity.Cinemachine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using WildPartner.Audio;
using WildPartner.Core;
using WildPartner.Creatures;
using WildPartner.Player;
using WildPartner.UI;
using WildPartner.World;
using Random = System.Random;

namespace WildPartner.EditorTools
{
    /// <summary>
    /// Generates the whole game scene from <see cref="WorldLayout"/> with a fixed seed (FR-G01/G02):
    ///   tools/unity.sh WildPartner.EditorTools.WorldBuilder.Build
    /// Terrain (~1 km², meadow / sparse forest / river valley), water, landmarks, vegetation placement
    /// list, lights, post-processing, cameras, gameplay systems and the NavMesh. Re-running gives an
    /// identical scene.
    /// </summary>
    public static partial class WorldBuilder
    {
        const string ScenePath = "Assets/_Game/Scenes/World.unity";
        const string TerrainDir = "Assets/_Game/Settings/Terrain";
        const float S = WorldLayout.Size;
        const int HeightRes = 1025;
        const int SplatRes = 1024;

        static Random _rng;
        static TerrainData _td;

        [MenuItem("WildPartner/Build World")]
        public static void Build()
        {
            var t0 = DateTime.Now;
            RenderFeatures.Ensure();
            _rng = new Random(WorldLayout.Seed);
            Directory.CreateDirectory(TerrainDir);
            Directory.CreateDirectory("Assets/_Game/Scenes");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var terrain = BuildTerrain();
            BuildWater();
            var landmarks = new GameObject("Landmarks").transform;
            var campfire = BuildCabin(landmarks);
            BuildInterior(landmarks);   // 试玩反馈 1123b-3: the room behind the front door
            BuildStoneCircle(landmarks);
            Place("Watchtower", landmarks, WorldLayout.Watchtower, 30, 1f);
            BuildBridge(landmarks);
            var isl = WorldLayout.Islands[0];
            Place("MoonFlower", landmarks, new Vector2(isl.x, isl.y), 200, 1.1f, -0.05f);   // the moon butterfly's perch
            BuildBackground(landmarks);
            var vegetation = BuildVegetation();
            BuildGrass(vegetation);

            // ---- lights, sky, post -----------------------------------------------------------------
            var sunGo = new GameObject("Sun", typeof(Light));
            var sun = sunGo.GetComponent<Light>();
            sun.type = LightType.Directional; sun.shadows = LightShadows.Soft; sun.intensity = 1.8f;
            sunGo.AddComponent<UniversalAdditionalLightData>();
            RenderSettings.sun = sun;
            RenderSettings.skybox = AssetDatabase.LoadAssetAtPath<Material>($"{AssetSetup.MatDir}/M_Sky.mat");
            var volume = BuildVolume();

            // ---- systems --------------------------------------------------------------------------
            var systems = new GameObject("Systems");
            var env = systems.AddComponent<EnvController>();
            env.sun = sun;
            var weatherGo = new GameObject("Weather");
            weatherGo.transform.SetParent(systems.transform);
            var weather = weatherGo.AddComponent<Weather>();
            weather.rain = BuildRain(weatherGo.transform);
            env.weather = weather;

            // ---- player + cameras -----------------------------------------------------------------
            var playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Game/Prefabs/Player.prefab");
            var player = (GameObject)PrefabUtility.InstantiatePrefab(playerPrefab);
            var ps = WorldLayout.PlayerStart;
            player.transform.SetPositionAndRotation(new Vector3(ps.x, Height(ps.x, ps.y) + 0.1f, ps.y), Quaternion.Euler(0, WorldLayout.PlayerStartYaw, 0));
            var cams = BuildCameras(player.transform);
            player.GetComponent<PlayerController>().vcam = cams.player;

            // ---- creatures ------------------------------------------------------------------------
            var creatures = new GameObject("Creatures").transform;
            foreach (var def in CreatureDefs.All)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{AssetSetup.CreaturePrefabs}/{def.id}.prefab");
                for (int i = 0; i < def.Count; i++)   // several of a kind (试玩反馈 1123b-6/7/8): one per home
                {
                    var c = (GameObject)PrefabUtility.InstantiatePrefab(prefab, creatures);
                    var a = def.homes != null ? def.homes[i] : def.anchors[0];
                    c.transform.position = new Vector3(a.x, Height(a.x, a.y), a.y);
                    c.GetComponent<Creature>().instance = i;
                    if (def.Count > 1) c.name = $"{def.id}_{i}";
                }
            }

            // ordinary fish schools (试玩反馈 1123b-7): scenery, not creatures
            var fishGo = new GameObject("FishSchools");
            var fishC = fishGo.AddComponent<FishSchools>();
            fishC.silver = FishMat("M_Fish_Silver", new Color(0.72f, 0.76f, 0.8f));
            fishC.gold = FishMat("M_Fish_Gold", new Color(0.93f, 0.8f, 0.5f));

            var flow = systems.AddComponent<GameFlow>();
            flow.player = player.GetComponent<PlayerController>();
            flow.environment = env;
            flow.weather = weather;
            flow.campfire = campfire;
            var boot = systems.AddComponent<Bootstrap>();
            boot.volume = volume;
            boot.menuCamera = cams.menu;
            boot.playerCamera = cams.player;
            boot.menuPivot = cams.menuPivot;
            systems.AddComponent<GameUI>();
            systems.AddComponent<ProceduralAudio>();

            // ---- navmesh (vegetation colliders spawned temporarily so trunks are obstacles) --------
            BakeNavMesh(vegetation);

            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();
            int count = 0; foreach (var b in vegetation.batches) count += b.items.Count;
            Debug.Log($"[World] built {ScenePath}: vegetation={count} in {(DateTime.Now - t0).TotalSeconds:0}s");
            MapBaker.Bake();   // the minimap texture must follow every layout change (试玩反馈 3)
        }

        // =========================================================================================
        // Height field
        // =========================================================================================
        static float Fbm(float x, float z, int oct = 4)
        {
            float a = 0.5f, f = 1, s = 0, n = 0;
            for (int i = 0; i < oct; i++) { s += a * Mathf.PerlinNoise(x * f + 17.3f * i, z * f + 9.1f * i); n += a; a *= 0.5f; f *= 2.03f; }
            return s / n;
        }

        static float SegDist(Vector2 p, Vector2 a, Vector2 b, out float t)
        {
            var ab = b - a;
            t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
            return Vector2.Distance(p, a + ab * t);
        }

        static readonly Vector2 RampTop = WorldLayout.RampTop;
        static readonly Vector2 RampBottom = WorldLayout.RampBottom;
        const float K = WorldLayout.K;

        static float BaseHeight(float x, float z)
        {
            // valley floor: broad swells (~120 m) + small hillocks (~30 m), never a flat plane (画面整改 2)
            float h = 31f + (Fbm(x * 0.008f / WorldLayout.F, z * 0.008f / WorldLayout.F) - 0.5f) * 10f + (Fbm(x * 0.034f + 40, z * 0.034f + 10, 3) - 0.5f) * 4.5f;
            h += Mathf.SmoothStep(0, 1, Mathf.InverseLerp(380 * WorldLayout.F, 560 * WorldLayout.F, z)) * 6f * Fbm(x * 0.017f / WorldLayout.F + 5, z * 0.017f / WorldLayout.F);
            // NW highland (waterfall source)
            float dh = Vector2.Distance(new Vector2(x, z), new Vector2(165, 955) * K) / (220f * K * 1.25f);
            h += (1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(0.55f, 1.0f, dh))) * 36f;
            return h;
        }

        /// <summary>World-edge hills: wooded slopes rising to the rim, rock only where it gets steep.</summary>
        static float EdgeRise(float x, float z)
        {
            float e = Mathf.Min(Mathf.Min(x, S - x), Mathf.Min(z, S - z));
            float t = 1 - Mathf.Clamp01((e - 4f) / 55f);   // 70 m on the 600 m map; a little narrower on the 360 m map
            return (t * t * (26f + Fbm(x * 0.025f, z * 0.025f) * 26f) + t * 6f) * 0.75f;   // lower rim on the 360 m map: same height over a shorter run turned the slopes into bare rock
        }

        public static float Height(float x, float z)
        {
            var p = new Vector2(x, z);
            float h = BaseHeight(x, z);

            // stone circle: gentle flat clearing
            float ds = Vector2.Distance(p, WorldLayout.StoneCircle);
            h = Mathf.Lerp(BaseHeight(WorldLayout.StoneCircle.x, WorldLayout.StoneCircle.y), h, Mathf.SmoothStep(0, 1, Mathf.InverseLerp(12, 28, ds)));

            h += EdgeRise(x, z);

            // cabin: a rocky cliff-top plateau (SE). Steep faces towards the valley; behind it the ground climbs
            // into the edge hills so the plateau reads as a spur of the mountain, not a table.
            float dc = Vector2.Distance(p, WorldLayout.Cabin);
            float ang = Mathf.Atan2(z - WorldLayout.Cabin.y, x - WorldLayout.Cabin.x);
            float rim = WorldLayout.CabinRadius + Mathf.Sin(ang * 3 + 0.7f) * 2.2f + Mathf.Sin(ang * 7 + 2f) * 1.2f;
            float plateau = 1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(rim - 1.5f, rim + 5f, dc));
            float top = WorldLayout.CabinPlateau + (Fbm(x * 0.06f, z * 0.06f, 2) - 0.5f) * 0.8f;
            // shoulder behind the plateau (away from the valley) so it joins the hills
            var back = -WorldLayout.CabinFacing;
            var rel = p - WorldLayout.Cabin;
            float along = Vector2.Dot(rel, back), side = Mathf.Abs(Vector2.Dot(rel, new Vector2(-back.y, back.x)));
            float shoulder = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(0, 18, along)) * (1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(14, 30, side)));
            float hill = Mathf.Max(h, top + Mathf.Max(along - 18, 0) * 0.35f);
            h = Mathf.Lerp(h, hill, shoulder * (1 - plateau));
            h = Mathf.Lerp(h, top, plateau);
            // ramp down the cliff side to the meadow
            float dr = SegDist(p, RampTop, RampBottom, out float rt);
            float rampH = Mathf.Lerp(WorldLayout.CabinPlateau, BaseHeight(RampBottom.x, RampBottom.y), Mathf.SmoothStep(0, 1, rt));
            float rampMask = (1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(3f, 7f, dr))) * Mathf.SmoothStep(0, 1, Mathf.InverseLerp(-0.05f, 0.05f, rt)) *
                             (1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(0.95f, 1.1f, rt)));
            if (dr < 7f && rt > 0.001f) h = Mathf.Lerp(h, rampH, rampMask);

            // lake basin + islands
            float L = WorldLayout.LakeDistance(x, z);
            if (L < 1.4f)
            {
                float bed = WorldLayout.LakeLevel - 7f + Fbm(x * 0.03f, z * 0.03f) * 3f;
                foreach (var isl in WorldLayout.Islands)
                {
                    float di = Vector2.Distance(p, new Vector2(isl.x, isl.y)) / isl.z;
                    bed = Mathf.Max(bed, Mathf.Lerp(WorldLayout.LakeLevel + 2.5f + Fbm(x * 0.1f, z * 0.1f) * 1.2f, bed, Mathf.SmoothStep(0, 1, Mathf.InverseLerp(0.4f, 1.1f, di))));
                }
                // steeper shore: a narrow beach instead of wide flats that read as sand bars at grazing angles
                float shoreT = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(0.88f, 1.16f, L));
                h = Mathf.Lerp(bed, Mathf.Max(h, WorldLayout.LakeLevel + 1.3f), shoreT);
            }

            // river channel: cut down into the valley, bed below the water, banks always above it
            float d = WorldLayout.RiverDistance(x, z, out float wl);
            float half = WorldLayout.RiverHalfWidth;
            if (d < half + 14f)
            {
                float bankH = Mathf.Max(Mathf.Min(h, wl + 2.2f + Mathf.Max(d - half, 0) * 0.35f), wl + 0.7f + Mathf.Max(d - half, 0) * 0.1f);
                float bed = wl - 2.0f + Mathf.Clamp01(d / half) * 1.3f;
                float cut = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(half - 0.6f, half + 5f, d));
                float hRiver = Mathf.Lerp(bed, Mathf.Lerp(bankH, h, Mathf.SmoothStep(0, 1, Mathf.InverseLerp(half + 4f, half + 14f, d))), cut);
                // where the river runs into the lake, its raised banks would leave sand flats just above the
                // lake surface (the "pink patches", 画面整改 7): fade the channel out inside the basin
                float inLake = 1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(1.1f, 1.4f, L));
                h = Mathf.Lerp(hRiver, Mathf.Min(h, hRiver), inLake);
            }
            return h;
        }

        // =========================================================================================
        // Terrain
        // =========================================================================================
        static Terrain BuildTerrain()
        {
            _td = new TerrainData { heightmapResolution = HeightRes };
            _td.size = new Vector3(S, WorldLayout.HeightScale, S);
            AssetDatabase.DeleteAsset($"{TerrainDir}/WorldTerrain.asset");
            AssetDatabase.CreateAsset(_td, $"{TerrainDir}/WorldTerrain.asset");
            var heights = new float[HeightRes, HeightRes];
            for (int zi = 0; zi < HeightRes; zi++)
                for (int xi = 0; xi < HeightRes; xi++)
                {
                    float x = xi * S / (HeightRes - 1), z = zi * S / (HeightRes - 1);
                    heights[zi, xi] = Mathf.Clamp01(Height(x, z) / WorldLayout.HeightScale);
                }
            _td.SetHeights(0, 0, heights);

            // layers
            var layers = new[]
            {
                Layer("Grass", new Color(0.5f, 0.64f, 0.3f), new Color(0.38f, 0.53f, 0.24f), 7f, 0),
                Layer("ForestFloor", new Color(0.33f, 0.52f, 0.24f), new Color(0.27f, 0.42f, 0.2f), 6f, 1),
                Layer("Dirt", new Color(0.74f, 0.6f, 0.45f), new Color(0.63f, 0.5f, 0.38f), 4f, 2),
                Layer("Rock", new Color(0.58f, 0.56f, 0.6f), new Color(0.46f, 0.44f, 0.5f), 9f, 3),
            };
            _td.terrainLayers = layers;
            _td.alphamapResolution = SplatRes;
            var a = new float[SplatRes, SplatRes, 4];
            for (int zi = 0; zi < SplatRes; zi++)
                for (int xi = 0; xi < SplatRes; xi++)
                {
                    float x = (xi + 0.5f) * S / SplatRes, z = (zi + 0.5f) * S / SplatRes;
                    float slope = _td.GetSteepness(x / S, z / S);
                    var region = WorldLayout.RegionAt(x, z);
                    float forest = region == WorldLayout.Region.DeepForest || region == WorldLayout.Region.AutumnForest ||
                                   region == WorldLayout.Region.Highland || region == WorldLayout.Region.Edge ? 1 : 0;
                    float n = Fbm(x * 0.03f, z * 0.03f, 2);
                    float w0 = 1 - forest, w1 = forest * (0.7f + 0.3f * n), w2 = 0, w3 = 0;
                    float path = WorldLayout.PathDistance(x, z);
                    float edgeN = (Fbm(x * 0.3f + 11, z * 0.3f + 5, 2) - 0.5f) * 1.6f;   // ragged edge, matches the grass map
                    float pathW = 1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(0.9f + edgeN, 2.3f + edgeN, path));
                    float river = WorldLayout.RiverDistance(x, z, out _);
                    float bank = 1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(WorldLayout.RiverHalfWidth + 0.5f, WorldLayout.RiverHalfWidth + 3f, river));
                    float lake = 1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(1.0f, 1.08f, WorldLayout.LakeDistance(x, z)));
                    float yard = 1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(6f, 11f, Vector2.Distance(new Vector2(x, z), WorldLayout.Campfire)));
                    float circle = (1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(6.5f + n * 2, 9.5f + n * 2, Vector2.Distance(new Vector2(x, z), WorldLayout.StoneCircle)))) * 0.75f;
                    w2 = Mathf.Max(Mathf.Max(pathW, bank * 0.9f), Mathf.Max(lake * 0.9f, Mathf.Max(yard, circle)));
                    w3 = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(40f, 52f, slope + n * 6f));
                    if (river < WorldLayout.RiverHalfWidth + 16f) w3 *= 0.2f;   // river cut banks stay green, not grey
                    float rest = (1 - w3) * (1 - w2);
                    w0 *= rest; w1 *= rest; w2 *= (1 - w3);
                    float sum = w0 + w1 + w2 + w3 + 1e-5f;
                    a[zi, xi, 0] = w0 / sum; a[zi, xi, 1] = w1 / sum; a[zi, xi, 2] = w2 / sum; a[zi, xi, 3] = w3 / sum;
                }
            _td.SetAlphamaps(0, 0, a);
            EditorUtility.SetDirty(_td);
            AssetDatabase.SaveAssets();

            var go = Terrain.CreateTerrainGameObject(_td);
            go.name = "Terrain";
            var t = go.GetComponent<Terrain>();
            var mat = AssetDatabase.LoadAssetAtPath<Material>($"{AssetSetup.MatDir}/M_Terrain.mat");
            if (!mat) { mat = new Material(Shader.Find("WildPartner/ToonTerrain")); AssetDatabase.CreateAsset(mat, $"{AssetSetup.MatDir}/M_Terrain.mat"); }
            t.materialTemplate = mat;
            t.drawTreesAndFoliage = false;
            t.heightmapPixelError = 4;
            t.basemapDistance = 2000;
            t.drawInstanced = false;
            t.shadowCastingMode = ShadowCastingMode.On;
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.NavigationStatic | StaticEditorFlags.BatchingStatic);
            return t;
        }

        static TerrainLayer Layer(string name, Color a, Color b, float tile, int seed)
        {
            string texPath = $"{TerrainDir}/T_{name}.png";
            var rnd = new Random(seed + 100);
            int N = 256;
            var tex = new Texture2D(N, N, TextureFormat.RGB24, true);
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    // tileable noise: sample on a torus
                    float u = x / (float)N * Mathf.PI * 2, v = y / (float)N * Mathf.PI * 2;
                    float n = Mathf.PerlinNoise(Mathf.Cos(u) * 2 + 10 + seed, Mathf.Sin(u) * 2 + Mathf.Cos(v) * 2) * 0.6f +
                              Mathf.PerlinNoise(Mathf.Cos(u) * 6 + seed, Mathf.Sin(v) * 6 + Mathf.Sin(u) * 3) * 0.4f;
                    var c = Color.Lerp(b, a, Mathf.SmoothStep(0.3f, 0.7f, n));
                    if (name == "Grass" && rnd.NextDouble() < 0.02) c *= 1.12f;
                    if (name == "Dirt" && rnd.NextDouble() < 0.015) c = Color.Lerp(c, new Color(0.9f, 0.85f, 0.75f), 0.5f);
                    if (name == "Rock") c *= 0.92f + 0.16f * Mathf.PerlinNoise(Mathf.Cos(u) * 3 + seed, Mathf.Sin(v) * 3 + Mathf.Sin(u) * 2);
                    tex.SetPixel(x, y, c);
                }
            tex.Apply();
            File.WriteAllBytes(texPath, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(texPath);
            var layer = new TerrainLayer
            {
                diffuseTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath),
                tileSize = new Vector2(tile, tile),
            };
            AssetDatabase.CreateAsset(layer, $"{TerrainDir}/L_{name}.terrainlayer");
            return layer;
        }

        static float TerrainH(float x, float z) => _td.GetInterpolatedHeight(x / S, z / S);

        // =========================================================================================
        // Water
        // =========================================================================================
        static void BuildWater()
        {
            var root = new GameObject("Water").transform;
            var lakeMat = AssetDatabase.LoadAssetAtPath<Material>($"{AssetSetup.MatDir}/M_Water_Lake.mat");
            var riverMat = AssetDatabase.LoadAssetAtPath<Material>($"{AssetSetup.MatDir}/M_Water_River.mat");
            var fallMat = AssetDatabase.LoadAssetAtPath<Material>($"{AssetSetup.MatDir}/M_Waterfall.mat");

            // lake: one plane at lake level (terrain hides it everywhere outside the basin)
            var c = WorldLayout.LakeCentre;
            // only over the basin: the old full rectangle also flooded low valley ground outside it, leaving
            // sand bars poking through the surface (画面整改 7)
            MeshObject("Lake", root, Grid(new Vector3(c.x, WorldLayout.LakeLevel + 0.15f, c.y), WorldLayout.LakeRadii.x * 1.45f, WorldLayout.LakeRadii.y * 1.6f, 48,
                (x, z) => WorldLayout.LakeDistance(x, z) < 1.22f || TerrainH(x, z) < WorldLayout.LakeLevel - 0.3f && WorldLayout.LakeDistance(x, z) < 1.6f), lakeMat);
            MeshObject("Pond", root, Grid(new Vector3(WorldLayout.Pond.x, 25.35f, WorldLayout.Pond.y), WorldLayout.PondRadius + 4, WorldLayout.PondRadius + 4, 8), lakeMat);

            Ribbon(root, WorldLayout.RiverIn, riverMat, fallMat, "RiverIn");
            Ribbon(root, WorldLayout.RiverOut, riverMat, fallMat, "RiverOut");
        }

        static Mesh Grid(Vector3 centre, float rx, float rz, int n, Func<float, float, bool> keep = null)
        {
            var m = new Mesh { name = "WaterGrid" };
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
            for (int j = 0; j <= n; j++)
                for (int i = 0; i <= n; i++)
                {
                    float x = (i / (float)n * 2 - 1) * rx, z = (j / (float)n * 2 - 1) * rz;
                    v.Add(new Vector3(x, 0, z)); uv.Add(new Vector2(x * 0.05f, z * 0.05f));
                }
            for (int j = 0; j < n; j++)
                for (int i = 0; i < n; i++)
                {
                    int a = j * (n + 1) + i, b = a + 1, cc = a + n + 1, d = cc + 1;
                    if (keep != null)
                    {
                        var mid = (v[a] + v[d]) * 0.5f;
                        if (!keep(centre.x + mid.x, centre.z + mid.z)) continue;
                    }
                    t.AddRange(new[] { a, cc, b, b, cc, d });
                }
            m.SetVertices(v); m.SetUVs(0, uv); m.SetTriangles(t, 0); m.RecalculateNormals(); m.RecalculateBounds();
            m.bounds = new Bounds(m.bounds.center, m.bounds.size + Vector3.up);
            var go = new GameObject("tmp"); go.transform.position = centre;
            UnityEngine.Object.DestroyImmediate(go);
            // bake the centre into the vertices so the GameObject can sit at the origin
            for (int i = 0; i < v.Count; i++) v[i] += centre;
            m.SetVertices(v); m.RecalculateBounds();
            return m;
        }

        static void Ribbon(Transform root, Vector3[] pts, Material river, Material fall, string name)
        {
            // resample the polyline every ~2 m (Catmull-Rom in xz, linear water level)
            var samples = new List<Vector3>();
            for (int i = 0; i < pts.Length - 1; i++)
            {
                Vector3 p0 = pts[Mathf.Max(i - 1, 0)], p1 = pts[i], p2 = pts[i + 1], p3 = pts[Mathf.Min(i + 2, pts.Length - 1)];
                float len = Vector2.Distance(new Vector2(p1.x, p1.y), new Vector2(p2.x, p2.y));
                int steps = Mathf.Max(2, Mathf.CeilToInt(len / 2f));
                for (int s = 0; s < steps; s++)
                {
                    float t = s / (float)steps;
                    var xz = CatmullRom(new Vector2(p0.x, p0.y), new Vector2(p1.x, p1.y), new Vector2(p2.x, p2.y), new Vector2(p3.x, p3.y), t);
                    // waterfalls: keep level flat, then drop over the last 25% of the segment
                    float drop = p1.z - p2.z;
                    float lt = drop > 3f ? Mathf.SmoothStep(0, 1, Mathf.InverseLerp(0.55f, 0.95f, t)) : t;
                    samples.Add(new Vector3(xz.x, Mathf.Lerp(p1.z, p2.z, lt), xz.y));
                }
            }
            var last = pts[pts.Length - 1];
            samples.Add(new Vector3(last.x, last.z, last.y));

            // split into calm and falling pieces
            var calm = new List<int>(); var falls = new List<int>();
            var verts = new List<Vector3>(); var uvs = new List<Vector2>();
            float dist = 0;
            var nearFall = new float[samples.Count];
            for (int i = 1; i < samples.Count; i++)
                if (Mathf.Abs(samples[i].y - samples[i - 1].y) > 0.6f)
                    for (int k = -5; k <= 5; k++)
                    {
                        int j = Mathf.Clamp(i + k, 0, samples.Count - 1);
                        nearFall[j] = Mathf.Max(nearFall[j], 1 - Mathf.Abs(k) / 6f);
                    }
            for (int i = 0; i < samples.Count; i++)
            {
                Vector3 a = samples[Mathf.Max(i - 1, 0)], b = samples[Mathf.Min(i + 1, samples.Count - 1)];
                var dir = new Vector2(b.x - a.x, b.z - a.z).normalized;
                float w = Mathf.Lerp(WorldLayout.RiverHalfWidth + 6f, WorldLayout.RiverHalfWidth + 0.8f, nearFall[i]);
                var side = new Vector3(-dir.y, 0, dir.x) * w;
                var p = samples[i] + Vector3.up * 0.18f;
                if (i > 0) dist += Vector3.Distance(samples[i], samples[i - 1]);
                verts.Add(p - side); verts.Add(p + side);
                uvs.Add(new Vector2(0, dist / 8f)); uvs.Add(new Vector2(1, dist / 8f));
                if (i > 0)
                {
                    int i0 = (i - 1) * 2;
                    bool steep = Mathf.Abs(samples[i].y - samples[i - 1].y) > 0.6f;
                    var list = steep ? falls : calm;
                    list.AddRange(new[] { i0, i0 + 2, i0 + 1, i0 + 1, i0 + 2, i0 + 3 });
                    if (steep && (i + 1 >= samples.Count || Mathf.Abs(samples[i + 1].y - samples[i].y) <= 0.6f))
                        Mist(root, samples[i]);
                }
            }
            var m = new Mesh { name = name, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            m.SetVertices(verts); m.SetUVs(0, uvs);
            m.subMeshCount = 2;
            m.SetTriangles(calm, 0); m.SetTriangles(falls, 1);
            m.RecalculateNormals(); m.RecalculateBounds();
            var go = MeshObject(name, root, m, river);
            go.GetComponent<MeshRenderer>().sharedMaterials = new[] { river, fall };
        }

        static Vector2 CatmullRom(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float t)
        {
            float t2 = t * t, t3 = t2 * t;
            return 0.5f * (2 * p1 + (-p0 + p2) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t2 + (-p0 + 3 * p1 - 3 * p2 + p3) * t3);
        }

        static GameObject MeshObject(string name, Transform parent, Mesh mesh, Material mat)
        {
            string path = $"{TerrainDir}/{name}.asset";
            AssetDatabase.CreateAsset(mesh, path);
            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(parent, false);
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.motionVectorGenerationMode = MotionVectorGenerationMode.Camera;
            return go;
        }

        static void Mist(Transform root, Vector3 at)
        {
            var go = new GameObject("WaterfallMist", typeof(ParticleSystem));
            go.transform.SetParent(root, false);
            go.transform.position = at + Vector3.up * 0.5f;
            var ps = go.GetComponent<ParticleSystem>();
            var main = ps.main;
            main.startLifetime = 2.5f; main.startSpeed = 1.2f; main.startSize = new ParticleSystem.MinMaxCurve(1.5f, 3.5f);
            main.startColor = new Color(1, 1, 1, 0.35f); main.maxParticles = 120; main.simulationSpace = ParticleSystemSimulationSpace.World;
            var em = ps.emission; em.rateOverTime = 25;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = 3f;
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                      new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(0.5f, 0.3f), new GradientAlphaKey(0, 1) });
            col.color = g;
            go.GetComponent<ParticleSystemRenderer>().sharedMaterial = ParticleMat("M_Mist", new Color(1, 1, 1, 0.5f), true);
            em.rateOverTime = 40; main.startSize = new ParticleSystem.MinMaxCurve(2f, 5f); main.maxParticles = 200;

            // white droplets thrown up where the water lands (画面整改 7)
            var sgo = new GameObject("WaterfallSplash", typeof(ParticleSystem));
            sgo.transform.SetParent(root, false);
            sgo.transform.SetPositionAndRotation(at + Vector3.up * 0.2f, Quaternion.Euler(-90, 0, 0));
            var sp = sgo.GetComponent<ParticleSystem>();
            var sm = sp.main;
            sm.startLifetime = new ParticleSystem.MinMaxCurve(0.6f, 1.1f); sm.startSpeed = new ParticleSystem.MinMaxCurve(2.5f, 5f);
            sm.startSize = new ParticleSystem.MinMaxCurve(0.15f, 0.45f); sm.gravityModifier = 1.1f;
            sm.startColor = new Color(1, 1, 1, 0.85f); sm.maxParticles = 300; sm.simulationSpace = ParticleSystemSimulationSpace.World;
            var se = sp.emission; se.rateOverTime = 120;
            var ss = sp.shape; ss.shapeType = ParticleSystemShapeType.Cone; ss.angle = 28; ss.radius = 2.2f;
            var sc = sp.colorOverLifetime; sc.enabled = true;
            var sg = new Gradient();
            sg.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                       new[] { new GradientAlphaKey(0.9f, 0), new GradientAlphaKey(0.6f, 0.6f), new GradientAlphaKey(0, 1) });
            sc.color = sg;
            sgo.GetComponent<ParticleSystemRenderer>().sharedMaterial = ParticleMat("M_Splash", new Color(1, 1, 1, 0.9f), true);
        }

        static Material FishMat(string name, Color c)
        {
            string path = $"{AssetSetup.MatDir}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!m) { m = new Material(Shader.Find("WildPartner/Toon")); AssetDatabase.CreateAsset(m, path); }
            m.SetColor("_BaseColor", c);
            m.SetFloat("_Cull", 0f);
            m.SetColor("_RimColor", new Color(1f, 1f, 1f, 0.35f));   // a glint along the back
            m.SetFloat("_Emission", 0.35f);                          // reads through the water tint
            m.SetColor("_ShadowColor", new Color(0.6f, 0.66f, 0.8f));
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }

        static Material ParticleMat(string name, Color c, bool soft)
        {
            string path = $"{AssetSetup.MatDir}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m) return m;
            m = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
            m.SetFloat("_Surface", 1); m.SetFloat("_Blend", 0);
            m.SetOverrideTag("RenderType", "Transparent");
            m.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha); m.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha); m.SetInt("_ZWrite", 0);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)RenderQueue.Transparent;
            m.SetColor("_BaseColor", c);
            m.SetTexture("_BaseMap", SoftDot());
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        static Texture2D SoftDot()
        {
            string path = $"{TerrainDir}/T_SoftDot.png";
            if (!File.Exists(path))
            {
                var t = new Texture2D(64, 64, TextureFormat.RGBA32, false);
                for (int y = 0; y < 64; y++) for (int x = 0; x < 64; x++)
                {
                    float d = Vector2.Distance(new Vector2(x, y), new Vector2(31.5f, 31.5f)) / 32f;
                    t.SetPixel(x, y, new Color(1, 1, 1, Mathf.Clamp01(1 - d) * Mathf.Clamp01(1 - d)));
                }
                t.Apply();
                File.WriteAllBytes(path, t.EncodeToPNG());
                AssetDatabase.ImportAsset(path);
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        // =========================================================================================
        // Landmarks
        // =========================================================================================
        static GameObject Place(string prefab, Transform parent, Vector2 xz, float yaw, float scale, float yOffset = 0)
        {
            var p = AssetDatabase.LoadAssetAtPath<GameObject>($"{AssetSetup.EnvPrefabs}/{prefab}.prefab");
            if (!p) { Debug.LogWarning($"[World] missing prefab {prefab}"); return null; }
            var go = (GameObject)PrefabUtility.InstantiatePrefab(p, parent);
            go.transform.SetPositionAndRotation(new Vector3(xz.x, TerrainH(xz.x, xz.y) + yOffset, xz.y), Quaternion.Euler(0, yaw, 0));
            go.transform.localScale = Vector3.one * scale;
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.NavigationStatic);
            return go;
        }

        /// <summary>Yaw that points a model's local -Z (its front, see AssetSetup) along dir.</summary>
        static float FaceYaw(Vector2 dir) => Mathf.Atan2(-dir.x, -dir.y) * Mathf.Rad2Deg;

        static Transform BuildCabin(Transform parent)
        {
            var root = new GameObject("CliffCabin").transform;
            root.SetParent(parent, false);
            var c = WorldLayout.Cabin;
            Vector2 toRamp = WorldLayout.CabinFacing;
            var rampDir = (RampTop - c).normalized;
            float yaw = FaceYaw(toRamp);
            var cc = WorldLayout.House;
            Place("Cabin", root, cc, yaw, 1f, -0.15f);   // Tripo cabin: door, windows, chimney are part of the model
            // yard dressing in the cabin's frame: x = to the cabin's right (seen from outside), y = out of the door
            Vector2 side = new Vector2(-toRamp.y, toRamp.x);
            Vector2 L(float x, float y) => cc + side * x + toRamp * y;
            Place("Cauldron", root, L(-4.5f, 6.5f), yaw + 25, 1f);
            Place("Planter", root, L(5.2f, 5.5f), yaw, 1f);
            Place("Planter", root, L(5.2f, 7.6f), yaw, 1f);
            Place("Chest", root, L(4.6f, 1.6f), yaw - 75, 1f);
            Place("Woodpile", root, L(-5.0f, 0.5f), yaw + 90, 1f);
            // 试玩反馈 1123b-1: the stump used to sit in the middle of the yard path. Now it grows in the grass beside it:
            // first spot ≥ 3.2 m from any path, base sunk into the grass, mushrooms and a few flowers at its foot
            {
                Vector2 spot = L(-5.5f, 11f);
                for (float y = 8f; y <= 13f && WorldLayout.PathDistance(spot.x, spot.y) < 3.2f; y += 0.5f)
                    for (float x = -3.5f; x >= -8f; x -= 0.5f)
                    {
                        var q = L(x, y);
                        if (WorldLayout.PathDistance(q.x, q.y) >= 3.2f && Vector2.Distance(q, L(-4.5f, 6.5f)) > 2.5f && Vector2.Distance(q, L(-6.5f, 9.5f)) > 1.8f) { spot = q; break; }
                    }
                Place("Stump", root, spot, 40, 1f, -0.14f);
                Place("Mushrooms", root, spot + side * 0.55f + toRamp * 0.35f, 120, 0.9f, -0.02f);
                Place("Mushrooms", root, spot - side * 0.5f - toRamp * 0.45f, 260, 0.7f, -0.02f);
                Place("Flowers_White", root, spot + toRamp * 0.9f - side * 0.3f, 30, 0.8f);
                Place("Flowers_Purple", root, spot - toRamp * 0.8f + side * 0.6f, 200, 0.7f);
            }
            var perp = new Vector2(-rampDir.y, rampDir.x);
            Place("Signpost", root, RampTop - rampDir * 2.5f + perp * 3.2f, yaw + 20, 1f);

            var fire = Place("Campfire", root, WorldLayout.Campfire, 0, 1.2f);
            for (int i = 0; i < 3; i++)
            {
                float a = i / 3f * Mathf.PI * 2 + 0.4f;
                var p = WorldLayout.Campfire + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 3.4f;
                Place("Stump", root, p, i * 97f, 0.75f);   // Tripo stumps as seats
            }
            var fireLight = new GameObject("FireLight", typeof(Light));
            fireLight.transform.SetParent(fire.transform, false);
            fireLight.transform.localPosition = new Vector3(0, 1.2f, 0);
            var fl = fireLight.GetComponent<Light>();
            fl.type = LightType.Point; fl.range = 14; fl.intensity = 4f; fl.color = new Color(1f, 0.6f, 0.3f); fl.shadows = LightShadows.None;
            var nl = fireLight.AddComponent<NightLight>(); nl.alwaysOn = true; nl.flicker = true;
            Flames(fire.transform);

            Place("FlagPole", root, L(-6.5f, 9.5f), yaw, 1f);
            Place("LampPost", root, RampTop - rampDir * 1.5f - perp * 3.4f, yaw + 90, 1f);
            Place("LampPost", root, L(3.2f, 4.8f), yaw - 90, 1f);

            // fence ring along the plateau edge, leaving a gap at the ramp
            float rampAng = Mathf.Atan2(rampDir.y, rampDir.x);
            float R = WorldLayout.CabinRadius - 4f;
            int n = Mathf.RoundToInt(2 * Mathf.PI * R / 3f);
            for (int i = 0; i < n; i++)
            {
                float a = i / (float)n * Mathf.PI * 2;
                if (Mathf.Abs(Mathf.DeltaAngle(a * Mathf.Rad2Deg, rampAng * Mathf.Rad2Deg)) < 12f) continue;
                var p = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * R;
                var tangent = new Vector2(-Mathf.Sin(a), Mathf.Cos(a));
                Place("Fence", root, p, Mathf.Atan2(-tangent.y, tangent.x) * Mathf.Rad2Deg, 1f, -0.1f);
            }
            // rock faces: two rings of big boulders sunk into the cliff so the plateau sides read as grey crags
            // (女巫木屋参考.png). The back (towards the hills) and the ramp stay open.
            for (int ring = 0; ring < 2; ring++)
            {
                int count = ring == 0 ? 30 : 24;
                for (int i = 0; i < count; i++)
                {
                    float a = (i + (ring == 0 ? 0 : 0.5f)) / count * Mathf.PI * 2;
                    var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                    if (Vector2.Dot(dir, WorldLayout.CabinFacing) < -0.55f) continue;                       // back shoulder
                    if (Mathf.Abs(Mathf.DeltaAngle(a * Mathf.Rad2Deg, rampAng * Mathf.Rad2Deg)) < 20f) continue;
                    // 画面整改 (cliff round 2): break the even "stone wall" — gaps, mixed sizes and heights, tilted
                    // stones, some stretched tall, bushes tucked on ledges and at the foot
                    if (_rng.NextDouble() < 0.22) continue;
                    float rr = WorldLayout.CabinRadius + (ring == 0 ? 0.8f + (float)_rng.NextDouble() * 1.4f : 6f + (float)_rng.NextDouble() * 5f);   // ring 0 on the rim, ring 1 down the slope
                    var p = c + dir * rr;
                    float big = (float)_rng.NextDouble();
                    float sc = (ring == 0 ? 1.7f : 2.2f) + big * big * 2.2f;
                    float topY = WorldLayout.CabinPlateau - (ring == 0 ? 0.8f : 7f) - (float)_rng.NextDouble() * (ring == 0 ? 2.5f : 4f);
                    var rock = Place(_rng.NextDouble() < 0.7 ? "Rock_A" : "Rock_B", root, p, _rng.Next(360), sc);
                    if (rock)
                    {
                        var pos = rock.transform.position; pos.y = ring == 0 ? Mathf.Min(topY - 2.6f * sc, TerrainH(p.x, p.y) + 0.4f * sc) : Mathf.Min(topY - 2.6f * sc, TerrainH(p.x, p.y) - 0.35f * sc); rock.transform.position = pos;   // never hovering over the slope
                        rock.transform.rotation = Quaternion.Euler(((float)_rng.NextDouble() - 0.5f) * 24f, _rng.Next(360), ((float)_rng.NextDouble() - 0.5f) * 24f);
                        rock.transform.localScale = new Vector3(sc, sc * (0.8f + (float)_rng.NextDouble() * 0.6f), sc);
                    }
                    for (int b = 0; b < 2; b++) if (_rng.NextDouble() < 0.6)   // bushes on the slope below the crag
                    {
                        var bp = c + dir * (rr + 2f + (float)_rng.NextDouble() * 7f) + new Vector2(-dir.y, dir.x) * ((float)_rng.NextDouble() - 0.5f) * 4f;
                        Place(_rng.NextDouble() < 0.5 ? "Bush_A" : "Bush_B", root, bp, _rng.Next(360), 1.2f + (float)_rng.NextDouble() * 0.9f, -0.2f);
                    }
                }
            }
            // wooded flanks: a loose ring of broadleaf trees and bushes part-way down the slope, so the plateau
            // reads as a wooded crag with rock showing through instead of a bare mound
            for (int i = 0; i < 44; i++)
            {
                float a = (i + (float)_rng.NextDouble() * 0.6f) / 44f * Mathf.PI * 2;
                var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                if (Vector2.Dot(dir, WorldLayout.CabinFacing) < -0.55f) continue;
                if (Mathf.Abs(Mathf.DeltaAngle(a * Mathf.Rad2Deg, rampAng * Mathf.Rad2Deg)) < 14f) continue;
                var p = c + dir * (WorldLayout.CabinRadius + 4f + (float)_rng.NextDouble() * 22f);
                if (_rng.NextDouble() < 0.6)
                    Place(_rng.NextDouble() < 0.5 ? "Tree_Broadleaf_A" : "Tree_Broadleaf_B", root, p, _rng.Next(360), 0.75f + (float)_rng.NextDouble() * 0.4f, -0.4f);
                for (int b = 0; b < 2; b++)
                    Place(_rng.NextDouble() < 0.5 ? "Bush_A" : "Bush_B", root, p + new Vector2((float)_rng.NextDouble() - 0.5f, (float)_rng.NextDouble() - 0.5f) * 6f,
                          _rng.Next(360), 1.3f + (float)_rng.NextDouble() * 1.0f, -0.25f);
            }
            return fire.transform;
        }

        static void AttachProp(string prefab, Transform parent, Vector3 pos, float yaw, float scale)
        {
            var p = AssetDatabase.LoadAssetAtPath<GameObject>($"{AssetSetup.EnvPrefabs}/{prefab}.prefab");
            if (!p) return;
            var go = (GameObject)PrefabUtility.InstantiatePrefab(p, parent);
            go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0, yaw, 0));
            go.transform.localScale = Vector3.one * scale;
        }

        static void Flames(Transform fire)
        {
            var go = new GameObject("Flames", typeof(ParticleSystem));
            go.transform.SetParent(fire, false);
            go.transform.localPosition = new Vector3(0, 0.2f, 0);
            go.transform.localRotation = Quaternion.Euler(-90, 0, 0);
            var ps = go.GetComponent<ParticleSystem>();
            var main = ps.main;
            main.startLifetime = 0.8f; main.startSpeed = 1.6f; main.startSize = new ParticleSystem.MinMaxCurve(0.35f, 0.7f);
            main.startColor = new Color(1f, 0.6f, 0.2f, 0.9f); main.maxParticles = 80;
            var em = ps.emission; em.rateOverTime = 40;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 12; sh.radius = 0.3f;
            var sz = ps.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1, AnimationCurve.Linear(0, 1, 1, 0));
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(new Color(1, 0.85f, 0.4f), 0), new GradientColorKey(new Color(1, 0.3f, 0.1f), 1) },
                      new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(0, 1) });
            col.color = g;
            var mat = ParticleMat("M_Flame", Color.white, false);
            go.GetComponent<ParticleSystemRenderer>().sharedMaterial = mat;
        }

        static void BuildStoneCircle(Transform parent)
        {
            var root = new GameObject("StoneCircle").transform;
            root.SetParent(parent, false);
            var c = WorldLayout.StoneCircle;
            Place("StandingStone", root, c, 15, 1.6f, -0.35f);   // Tripo: the whole ring (9.7 m) in one model
        }

        static void BuildBridge(Transform parent)
        {
            var b = WorldLayout.Bridge;
            var a0 = WorldLayout.RiverOut[0]; var a1 = WorldLayout.RiverOut[1];
            var flow = new Vector2(a1.x - a0.x, a1.y - a0.y).normalized;
            var across = new Vector2(-flow.y, flow.x);
            float yaw = FaceYaw(across);   // Tripo bridge: deck runs along local Z
            WorldLayout.RiverDistance(b.x, b.y, out float wl);
            var p = AssetDatabase.LoadAssetAtPath<GameObject>($"{AssetSetup.EnvPrefabs}/Bridge.prefab");
            var go = (GameObject)PrefabUtility.InstantiatePrefab(p, parent);
            // deck (3.2 m above the pillar feet in the model) level with the banks at both ends
            const float scale = 1.2f, deck = 3.2f * scale;
            var e0 = b + across * 7.5f * scale; var e1 = b - across * 7.5f * scale;
            float bank = Mathf.Max((TerrainH(e0.x, e0.y) + TerrainH(e1.x, e1.y)) * 0.5f, wl + 1.2f);
            go.transform.SetPositionAndRotation(new Vector3(b.x, bank - deck + 0.1f, b.y), Quaternion.Euler(0, yaw, 0));
            go.transform.localScale = new Vector3(1.2f, 1, 1.2f);
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.NavigationStatic);
        }

        // Far mountains (画面整改 5): four concentric ridge rings around the valley instead of the old cone
        // "pyramids". Each ring is one mesh + one FarRidge material; nearer rings are darker and crisper,
        // farther ones paler and bluer, the last two carry snow. Queues are ordered farthest-first so nearer
        // silhouettes overlap farther ones (they don't write depth; see FarRidge.shader).
        const float RidgeK = 0.72f;   // rings pulled in with the 360 m map
        static readonly (float radius, float baseH, float amp, float freq, Color rock, float fade, float snow, float seed)[] RidgeLayers =
        {
            (1750f * RidgeK, 260f, 420f, 5f,  new Color(0.52f, 0.60f, 0.74f), 0.72f, 520f, 11f),
            (1250f * RidgeK, 170f, 330f, 7f,  new Color(0.42f, 0.50f, 0.64f), 0.52f, 360f, 23f),
            ( 880f * RidgeK, 100f, 210f, 9f,  new Color(0.32f, 0.42f, 0.50f), 0.34f, 255f, 37f),
            ( 620f * RidgeK,  55f, 105f, 13f, new Color(0.24f, 0.36f, 0.36f), 0.20f, 9999f, 53f),
        };

        static void BuildBackground(Transform parent)
        {
            var root = new GameObject("Background").transform;
            root.SetParent(parent, false);
            var shader = Shader.Find("WildPartner/FarRidge");
            var centre = new Vector3(S * 0.5f, 0, S * 0.5f);
            for (int li = 0; li < RidgeLayers.Length; li++)
            {
                var L = RidgeLayers[li];
                var mesh = RidgeMesh(L.radius, L.baseH, L.amp, L.freq, L.seed);
                string mpath = $"{AssetSetup.Gen}/FarRidge_{li}.asset";
                AssetDatabase.DeleteAsset(mpath);
                AssetDatabase.CreateAsset(mesh, mpath);
                string matPath = $"{AssetSetup.MatDir}/M_FarRidge_{li}.mat";
                var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
                if (!mat) { mat = new Material(shader); AssetDatabase.CreateAsset(mat, matPath); }
                mat.shader = shader;
                mat.SetColor("_RockColor", L.rock);
                mat.SetFloat("_Fade", L.fade);
                mat.SetFloat("_SnowLine", L.snow);
                mat.SetVector("_BaseFade", new Vector4(0, L.baseH * 0.9f, 0, 0));
                mat.renderQueue = 2600 + li;
                EditorUtility.SetDirty(mat);

                var go = new GameObject($"FarRidge_{li}");
                go.transform.SetParent(root, false);
                go.transform.position = centre;
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = mat;
                r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
                r.lightProbeUsage = LightProbeUsage.Off; r.reflectionProbeUsage = ReflectionProbeUsage.Off;
                r.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            }
        }

        static float RidgeNoise(float a, float freq, float seed)
        {
            // seamless around the ring: sample 2D Perlin on a circle, a few ridged octaves
            float h = 0, amp = 1, norm = 0, f = freq;
            for (int o = 0; o < 4; o++)
            {
                float p = Mathf.PerlinNoise(seed + o * 7.3f + Mathf.Cos(a) * f * 0.16f, seed * 0.7f + o * 3.1f + Mathf.Sin(a) * f * 0.16f);
                float rdg = 1 - Mathf.Abs(p * 2 - 1);
                h += rdg * rdg * amp; norm += amp; amp *= 0.5f; f *= 2.1f;
            }
            return h / norm;
        }

        static Mesh RidgeMesh(float radius, float baseH, float amp, float freq, float seed)
        {
            const int seg = 1024;
            var hs = new float[seg + 1];
            for (int i = 0; i <= seg; i++)
            {
                float a = i / (float)seg * Mathf.PI * 2;
                // broad massifs (low frequency) times sharper ridges
                float massif = Mathf.PerlinNoise(seed * 1.3f + Mathf.Cos(a) * 1.6f, seed + Mathf.Sin(a) * 1.6f);
                hs[i] = baseH + amp * Mathf.Pow(RidgeNoise(a, freq, seed), 1.6f) * Mathf.Lerp(0.45f, 1.25f, massif);
            }
            hs[seg] = hs[0];
            float step = Mathf.PI * 2 * radius / seg;
            var v = new List<Vector3>(); var n = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
            // three rows per column: foot (-40 m), shoulder (55 % up, pushed outward) and crest, so each ridge
            // has a lit and a shaded face rather than reading as a flat card
            for (int i = 0; i <= seg; i++)
            {
                float a = i / (float)seg * Mathf.PI * 2;
                var radial = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                var tangent = new Vector3(-Mathf.Sin(a), 0, Mathf.Cos(a));
                float h = hs[i];
                float slope = (hs[(i + 5) % seg] - hs[(i - 5 + seg) % seg]) / (10 * step);   // wide window: no per-column banding
                var nrm = (-radial * 0.8f - tangent * slope * 1.6f + Vector3.up * 0.35f).normalized;
                float u = i * step;
                v.Add(radial * (radius + 60) + Vector3.up * -40); n.Add((-radial + Vector3.up * 0.2f).normalized); uv.Add(new Vector2(u, 0));
                v.Add(radial * (radius + 25) + Vector3.up * (h * 0.55f)); n.Add(Vector3.Lerp(nrm, -radial, 0.4f).normalized); uv.Add(new Vector2(u, 0.55f));
                v.Add(radial * radius + Vector3.up * h); n.Add(nrm); uv.Add(new Vector2(u, 1));
            }
            for (int i = 0; i < seg; i++)
            {
                int c = i * 3, d = c + 3;
                for (int r = 0; r < 2; r++)
                {
                    t.Add(c + r); t.Add(c + r + 1); t.Add(d + r + 1);
                    t.Add(c + r); t.Add(d + r + 1); t.Add(d + r);
                }
            }
            var m = new Mesh { name = "FarRidge", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            m.SetVertices(v); m.SetNormals(n); m.SetUVs(0, uv); m.SetTriangles(t, 0);
            m.RecalculateBounds();
            return m;
        }

        // =========================================================================================
        // Vegetation
        // =========================================================================================
        static readonly Dictionary<WorldLayout.Region, (string, float)[]> TreeMix = new Dictionary<WorldLayout.Region, (string, float)[]>
        {
            // 2026-11-23: the user's new tree models are true to species again (broadleaf oak, white birch, golden
            // maple, pine), so birch is back and the autumn forest uses the maple instead of a recoloured broadleaf
            { WorldLayout.Region.DeepForest, new[] { ("Tree_Broadleaf_A", .32f), ("Tree_Broadleaf_B", .34f), ("Tree_Pine_A", .14f), ("Tree_Pine_B", .12f), ("Tree_Birch_A", .08f) } },
            { WorldLayout.Region.AutumnForest, new[] { ("Tree_Autumn_A", .38f), ("Tree_Autumn_B", .3f), ("Tree_Birch_A", .14f), ("Tree_Broadleaf_A", .1f), ("Tree_Pine_A", .08f) } },
            { WorldLayout.Region.Highland, new[] { ("Tree_Pine_A", .3f), ("Tree_Pine_B", .2f), ("Tree_Autumn_B", .15f), ("Tree_Birch_A", .1f), ("Tree_Broadleaf_B", .25f) } },
            { WorldLayout.Region.Edge, new[] { ("Tree_Pine_A", .5f), ("Tree_Pine_B", .5f) } },
            { WorldLayout.Region.Lakeshore, new[] { ("Tree_Autumn_A", .3f), ("Tree_Autumn_B", .25f), ("Tree_Birch_A", .25f), ("Tree_Pine_A", .2f) } },
            { WorldLayout.Region.Meadow, new[] { ("Tree_Broadleaf_A", .45f), ("Tree_Broadleaf_B", .3f), ("Tree_Birch_A", .25f) } },
            { WorldLayout.Region.Riverbank, new[] { ("Tree_Broadleaf_A", .35f), ("Tree_Broadleaf_B", .25f), ("Tree_Birch_A", .4f) } },
            { WorldLayout.Region.Cabin, new[] { ("Tree_Pine_A", .5f), ("Tree_Broadleaf_B", .4f) } },
        };

        static readonly Dictionary<WorldLayout.Region, float> TreeDensity = new Dictionary<WorldLayout.Region, float>
        {
            { WorldLayout.Region.DeepForest, .82f }, { WorldLayout.Region.AutumnForest, .75f }, { WorldLayout.Region.Highland, .55f },
            { WorldLayout.Region.Edge, .85f }, { WorldLayout.Region.Lakeshore, .3f }, { WorldLayout.Region.Meadow, .025f },
            { WorldLayout.Region.Riverbank, .05f }, { WorldLayout.Region.Cabin, .03f },
        };

        static readonly Dictionary<WorldLayout.Region, float> GrassDensity = new Dictionary<WorldLayout.Region, float>
        {
            { WorldLayout.Region.Meadow, .8f }, { WorldLayout.Region.Riverbank, .55f }, { WorldLayout.Region.Cabin, .6f },
            { WorldLayout.Region.Lakeshore, .4f }, { WorldLayout.Region.Highland, .3f }, { WorldLayout.Region.AutumnForest, .15f },
            { WorldLayout.Region.DeepForest, .12f }, { WorldLayout.Region.Edge, .05f },
        };

        static readonly Vector3[] ViewClearings = { new Vector3(420, 470, 16), new Vector3(640, 700, 30), new Vector3(545, 748, 14), new Vector3(720, 640, 10), new Vector3(345, 768, 10), new Vector3(298, 524, 10) };   // x, z (map units), radius (m)

        static bool Blocked(float x, float z, float pathClear, float waterClear)
        {
            if (x < 15 || z < 15 || x > S - 15 || z > S - 15) return true;
            if (UnderWater(x, z, 0.35f + waterClear * 0.3f)) return true;
            if (WorldLayout.RiverDistance(x, z, out _) < WorldLayout.RiverHalfWidth + waterClear) return true;
            if (WorldLayout.LakeDistance(x, z) < 1.0f + waterClear * 0.01f) return true;
            if (WorldLayout.PathDistance(x, z) < pathClear) return true;
            var p = new Vector2(x, z);
            if (Vector2.Distance(p, WorldLayout.StoneCircle) < 19) return true;
            if (Vector2.Distance(p, WorldLayout.Watchtower) < 7) return true;
            if (Vector2.Distance(p, WorldLayout.Bridge) < 12) return true;
            if (Vector2.Distance(p, WorldLayout.Campfire) < 9) return true;
            if (Vector2.Distance(p, WorldLayout.House) < 9) return true;
            if (Vector2.Distance(p, WorldLayout.Cabin) < WorldLayout.CabinRadius + 1f) return true;   // the yard is dressed by hand
            if (SegDist(p, RampTop, RampBottom, out _) < 6) return true;
            return false;
        }

        static bool UnderWater(float x, float z, float margin)
        {
            float h = TerrainH(x, z);
            if (WorldLayout.RiverDistance(x, z, out float wl) < WorldLayout.RiverHalfWidth + 18f && h < wl + 0.18f + margin) return true;
            if (WorldLayout.LakeDistance(x, z) < 1.5f && h < WorldLayout.LakeLevel + 0.15f + margin) return true;
            if (Vector2.Distance(new Vector2(x, z), WorldLayout.Pond) < WorldLayout.PondRadius + 12 && h < 25.35f + margin) return true;
            return false;
        }

        static string Pick((string, float)[] mix)
        {
            double r = _rng.NextDouble(), acc = 0;
            foreach (var (n, w) in mix) { acc += w; if (r <= acc) return n; }
            return mix[mix.Length - 1].Item1;
        }

        static VegetationSpawner BuildVegetation()
        {
            var go = new GameObject("Vegetation");
            var sp = go.AddComponent<VegetationSpawner>();
            var batches = new Dictionary<string, VegetationSpawner.Batch>();
            void Add(string prefab, float x, float z, float yaw, float scale, float sink = 0.15f)
            {
                if (!batches.TryGetValue(prefab, out var b))
                {
                    b = new VegetationSpawner.Batch { prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{AssetSetup.EnvPrefabs}/{prefab}.prefab") };
                    batches[prefab] = b;
                }
                b.items.Add(new Vector4(x, TerrainH(x, z) - sink, z, VegetationSpawner.Pack(yaw, scale)));
            }
            float J(float cell) => ((float)_rng.NextDouble() - 0.5f) * cell * 0.9f;

            // ---- trees (画面整改 3): big trees, closed canopy in the woods, groups of 3–5 in the open
            float TreeScale(string n)
            {
                float r = (float)_rng.NextDouble();
                return n.StartsWith("Tree_Broadleaf") ? 1.2f + r * 0.45f : n.StartsWith("Tree_Autumn") ? 1.15f + r * 0.4f :
                       n.StartsWith("Tree_Birch") ? 1.0f + r * 0.3f : 0.9f + r * 0.4f;
            }
            bool TreeOk(float px, float pz, float pathClear)
            {
                if (Blocked(px, pz, pathClear, 3f)) return false;
                foreach (var c in ViewClearings)   // small clearings at the scenic viewpoints (SceneShots) so no trunk sits in the lens
                    if (Vector2.Distance(new Vector2(px, pz), new Vector2(c.x, c.y) * WorldLayout.K) < c.z) return false;
                if (Vector2.Distance(new Vector2(px, pz), WorldLayout.Cabin + WorldLayout.CabinFacing * 95f + new Vector2(12, -30)) < 14f) return false;   // 02_cabin viewpoint
                var reg = WorldLayout.RegionAt(px, pz);
                return _td.GetSteepness(px / S, pz / S) <= (reg == WorldLayout.Region.Edge ? 50f : 44f);
            }
            var placedTrees = new List<Vector2>();
            void Tree(string n, float px, float pz, float scale)
            {
                Add(n, px, pz, (float)_rng.NextDouble() * 360f, scale, 0.3f);
                placedTrees.Add(new Vector2(px, pz));
                // understory: bushes / ferns at the foot of the tree
                if (_rng.NextDouble() < 0.55)
                {
                    float a = (float)_rng.NextDouble() * Mathf.PI * 2, r = 2.2f + (float)_rng.NextDouble() * 2.2f;
                    float bx = px + Mathf.Cos(a) * r, bz = pz + Mathf.Sin(a) * r;
                    if (!Blocked(bx, bz, 2f, 1f)) Add(_rng.NextDouble() < 0.6 ? "Bush_A" : "Bush_B", bx, bz, (float)_rng.NextDouble() * 360f, 0.55f + (float)_rng.NextDouble() * 0.6f);
                }
            }
            // closed woods: jittered grid
            const float tc = 6.5f;
            for (float z = tc / 2; z < S; z += tc)
                for (float x = tc / 2; x < S; x += tc)
                {
                    float px = x + J(tc), pz = z + J(tc);
                    var region = WorldLayout.RegionAt(px, pz);
                    if (region == WorldLayout.Region.Meadow || region == WorldLayout.Region.Riverbank || region == WorldLayout.Region.Cabin) continue;
                    if (_rng.NextDouble() > TreeDensity[region] || !TreeOk(px, pz, 4.5f)) continue;
                    var n = Pick(TreeMix[region]);
                    Tree(n, px, pz, TreeScale(n));
                }
            // open ground: groups of 3–5 trees (one big, the rest smaller) with bushes and a rock, sight lines left open
            const float gcell = 28f;   // tighter groups on the smaller map (试玩反馈 4: denser, not emptier)
            for (float z = gcell / 2; z < S; z += gcell)
                for (float x = gcell / 2; x < S; x += gcell)
                {
                    float cx = x + J(gcell), cz = z + J(gcell);
                    var region = WorldLayout.RegionAt(cx, cz);
                    if (region != WorldLayout.Region.Meadow && region != WorldLayout.Region.Riverbank && region != WorldLayout.Region.Cabin) continue;
                    if (_rng.NextDouble() > 0.62 || WorldLayout.PathDistance(cx, cz) < 9f) continue;
                    int k = 3 + _rng.Next(3);
                    for (int t = 0; t < k; t++)
                    {
                        float a = (float)_rng.NextDouble() * Mathf.PI * 2, r = t == 0 ? 0 : 3.5f + (float)_rng.NextDouble() * 5f;
                        float px = cx + Mathf.Cos(a) * r, pz = cz + Mathf.Sin(a) * r;
                        if (!TreeOk(px, pz, 6f)) continue;
                        var n = Pick(TreeMix[region]);
                        Tree(n, px, pz, TreeScale(n) * (t == 0 ? 1.15f : 0.8f));
                    }
                    for (int b = 0; b < 2 + _rng.Next(3); b++)
                    {
                        float a = (float)_rng.NextDouble() * Mathf.PI * 2, r = 4f + (float)_rng.NextDouble() * 5f;
                        float px = cx + Mathf.Cos(a) * r, pz = cz + Mathf.Sin(a) * r;
                        if (!Blocked(px, pz, 2.5f, 1f)) Add(_rng.NextDouble() < 0.6 ? "Bush_A" : "Bush_B", px, pz, (float)_rng.NextDouble() * 360f, 0.7f + (float)_rng.NextDouble() * 0.7f);
                    }
                    if (_rng.NextDouble() < 0.6)
                    {
                        float a = (float)_rng.NextDouble() * Mathf.PI * 2;
                        float px = cx + Mathf.Cos(a) * 6f, pz = cz + Mathf.Sin(a) * 6f;
                        if (!Blocked(px, pz, 2.5f, 1f)) Add("Rock_A", px, pz, (float)_rng.NextDouble() * 360f, 0.5f + (float)_rng.NextDouble() * 0.5f, 0.4f);
                    }
                }
            // path-side vignettes (±15 m of the paths, PRD-补充 §2): every ~30 m, alternating sides, a framing tree with
            // a bush, a rock and a flower patch, so walking the path is a sequence of composed views
            foreach (var path in WorldLayout.Paths)
            {
                float carry = 12f; int sideSign = 1;
                for (int i = 0; i < path.Length - 1; i++)
                {
                    var a0 = path[i]; var a1 = path[i + 1];
                    float len = Vector2.Distance(a0, a1);
                    var dir = (a1 - a0) / len; var nrm = new Vector2(-dir.y, dir.x);
                    for (float u = carry; u < len; u += 20f + (float)_rng.NextDouble() * 8f)
                    {
                        var at = a0 + dir * u;
                        if (Vector2.Distance(at, WorldLayout.Cabin) < WorldLayout.CabinRadius + 20) continue;
                        sideSign = -sideSign;
                        var tp = at + nrm * sideSign * (6.5f + (float)_rng.NextDouble() * 4f);
                        var region = WorldLayout.RegionAt(tp.x, tp.y);
                        if (TreeOk(tp.x, tp.y, 5.5f))
                        {
                            string n = region == WorldLayout.Region.AutumnForest || region == WorldLayout.Region.Lakeshore ? "Tree_Autumn_A" : "Tree_Broadleaf_A";
                            Tree(n, tp.x, tp.y, TreeScale(n) * 1.1f);
                        }
                        var bp = at + nrm * sideSign * 3.6f + dir * 2.5f;
                        if (!Blocked(bp.x, bp.y, 2.4f, 1f)) Add("Bush_A", bp.x, bp.y, (float)_rng.NextDouble() * 360f, 0.8f + (float)_rng.NextDouble() * 0.4f);
                        var rp = at - nrm * sideSign * 4.5f - dir * 3f;
                        if (_rng.NextDouble() < 0.5 && !Blocked(rp.x, rp.y, 2.6f, 1f)) Add("Rock_A", rp.x, rp.y, (float)_rng.NextDouble() * 360f, 0.35f + (float)_rng.NextDouble() * 0.35f, 0.3f);
                    }
                    carry = 0;
                }
            }
            // bushes
            const float bc = 9f;
            for (float z = bc / 2; z < S; z += bc)
                for (float x = bc / 2; x < S; x += bc)
                {
                    float px = x + J(bc), pz = z + J(bc);
                    var region = WorldLayout.RegionAt(px, pz);
                    float d = region == WorldLayout.Region.Meadow ? .03f : region == WorldLayout.Region.Riverbank ? .25f : region == WorldLayout.Region.Cabin ? .1f : .22f;
                    if (_rng.NextDouble() > d || Blocked(px, pz, 2.5f, 1.5f) || _td.GetSteepness(px / S, pz / S) > 35f) continue;
                    Add(_rng.NextDouble() < 0.6 ? "Bush_A" : "Bush_B", px, pz, (float)_rng.NextDouble() * 360f, 0.7f + (float)_rng.NextDouble() * 0.6f);
                }
            // rocks
            const float rc = 20f;
            for (float z = rc / 2; z < S; z += rc)
                for (float x = rc / 2; x < S; x += rc)
                {
                    float px = x + J(rc), pz = z + J(rc);
                    var region = WorldLayout.RegionAt(px, pz);
                    float d = region == WorldLayout.Region.Riverbank || region == WorldLayout.Region.Highland ? .55f : .18f;
                    if (_rng.NextDouble() > d || Blocked(px, pz, 2.5f, 0.5f)) continue;
                    Add(_rng.NextDouble() < 0.55 ? "Rock_A" : "Rock_B", px, pz, (float)_rng.NextDouble() * 360f, 0.5f + (float)_rng.NextDouble() * 1.0f, 0.4f);
                }
            // rock outcrops on the edge hills (Tripo boulders, scaled up)
            for (float t = 0; t < S * 4; t += 22f)
            {
                float side = Mathf.Floor(t / S), u = t % S;
                float inset = 8f + (float)_rng.NextDouble() * 26f;
                Vector2 p = side == 0 ? new Vector2(u, inset) : side == 1 ? new Vector2(S - inset, u) : side == 2 ? new Vector2(S - u, S - inset) : new Vector2(inset, S - u);
                if (_rng.NextDouble() < 0.35) continue;
                Add("Rock_A", p.x, p.y, (float)_rng.NextDouble() * 360f, 2.2f + (float)_rng.NextDouble() * 2.6f, 1.8f);
            }
            // (the lawn itself is GrassRenderer; no grass prefabs any more)
            // flower clusters: dense along path edges, scattered patches in the meadow
            string Flower() { double r = _rng.NextDouble(); return r < 0.5 ? "Flowers_White" : r < 0.78 ? "Flowers_Orange" : "Flowers_Purple"; }
            void Cluster(float cx, float cz, int n, float radius, Func<string> pick, float smin, float smax, float sink = 0.04f)
            {
                for (int k = 0; k < n; k++)
                {
                    float a = (float)_rng.NextDouble() * Mathf.PI * 2, rr = Mathf.Sqrt((float)_rng.NextDouble()) * radius;
                    float px = cx + Mathf.Cos(a) * rr, pz = cz + Mathf.Sin(a) * rr;
                    if (Blocked(px, pz, 1.3f, 0.4f)) continue;
                    Add(pick(), px, pz, (float)_rng.NextDouble() * 360f, smin + (float)_rng.NextDouble() * (smax - smin), sink);
                }
            }
            const float fc = 5f;
            for (float z = fc / 2; z < S; z += fc)
                for (float x = fc / 2; x < S; x += fc)
                {
                    float px = x + J(fc), pz = z + J(fc);
                    var region = WorldLayout.RegionAt(px, pz);
                    if (region == WorldLayout.Region.Edge) continue;
                    float pd = WorldLayout.PathDistance(px, pz);
                    float patch = Mathf.PerlinNoise(px * 0.025f + 40, pz * 0.025f + 40);
                    bool open = region == WorldLayout.Region.Meadow || region == WorldLayout.Region.Cabin || region == WorldLayout.Region.Riverbank;
                    float chance = pd > 1.8f && pd < 6f ? 0.6f : open && patch > 0.52f ? 0.35f : open ? 0.06f : 0.04f;
                    if (_rng.NextDouble() > chance || Blocked(px, pz, 1.6f, 0.5f)) continue;
                    var kind = Flower();
                    Cluster(px, pz, 3 + _rng.Next(5), 1.1f, () => _rng.NextDouble() < 0.8 ? kind : Flower(), 0.75f, 1.2f);
                }
            // small stones along the paths
            const float sc = 7f;
            for (float z = sc / 2; z < S; z += sc)
                for (float x = sc / 2; x < S; x += sc)
                {
                    float px = x + J(sc), pz = z + J(sc);
                    float pd = WorldLayout.PathDistance(px, pz);
                    if (pd < 1.6f || pd > 4f || _rng.NextDouble() > 0.35f || Blocked(px, pz, 1.4f, 0.5f)) continue;
                    Cluster(px, pz, 1 + _rng.Next(3), 0.8f, () => "Rock_B", 0.18f, 0.45f, 0.08f);
                }
            // mushrooms and bushes at the foot of trees (understory)
            var trees = new List<Vector4>();
            foreach (var kv in batches) if (kv.Key.StartsWith("Tree")) trees.AddRange(kv.Value.items);
            foreach (var t in trees)
            {
                double r = _rng.NextDouble();
                float a = (float)_rng.NextDouble() * Mathf.PI * 2;
                var at = new Vector2(t.x + Mathf.Cos(a) * 1.4f, t.z + Mathf.Sin(a) * 1.4f);
                if (r < 0.22) Cluster(at.x, at.y, 1 + _rng.Next(2), 0.6f, () => "Mushrooms", 0.8f, 1.5f, 0.02f);
                else if (r < 0.32) Cluster(at.x, at.y, 2 + _rng.Next(3), 0.7f, Flower, 0.7f, 1.0f);
            }
            sp.batches = new List<VegetationSpawner.Batch>(batches.Values);
            sp.batches.Sort((a, b) => string.CompareOrdinal(a.prefab.name, b.prefab.name));
            return sp;
        }

        // =========================================================================================
        // Ground maps + grass (画面整改 1)
        // =========================================================================================
        const int MapRes = 1024;

        /// <summary>
        /// Bakes the two 1 m/texel maps shared by the terrain and the grass: a ground tint (hand-painted look:
        /// big sunlit and shaded patches, cooler darker forest floor and tree shade, lush river banks) and the
        /// grass map (R density, G height, B dryness). Then adds the GrassRenderer.
        /// </summary>
        static void BuildGrass(VegetationSpawner veg)
        {
            var shade = new float[MapRes * MapRes];
            foreach (var b in veg.batches)
            {
                if (!b.prefab || !b.prefab.name.StartsWith("Tree")) continue;
                foreach (var it in b.items)
                {
                    float sc = Mathf.Floor(it.w / 1000f) / 100f;
                    float rad = (b.prefab.name.StartsWith("Tree_Pine") ? 2.6f : 3.6f) * sc;
                    int x0 = Mathf.FloorToInt(it.x - rad), x1 = Mathf.CeilToInt(it.x + rad), z0 = Mathf.FloorToInt(it.z - rad), z1 = Mathf.CeilToInt(it.z + rad);
                    for (int z = Mathf.Max(z0, 0); z <= Mathf.Min(z1, MapRes - 1); z++)
                        for (int x = Mathf.Max(x0, 0); x <= Mathf.Min(x1, MapRes - 1); x++)
                        {
                            float d = Vector2.Distance(new Vector2(x + 0.5f, z + 0.5f), new Vector2(it.x, it.z)) / rad;
                            if (d < 1) shade[z * MapRes + x] = Mathf.Max(shade[z * MapRes + x], 1 - d * d);
                        }
                }
            }
            var tint = new Color32[MapRes * MapRes];
            var map = new Color32[MapRes * MapRes];
            var cabin = WorldLayout.House;
            for (int zi = 0; zi < MapRes; zi++)
                for (int xi = 0; xi < MapRes; xi++)
                {
                    float x = (xi + 0.5f) * S / MapRes, z = (zi + 0.5f) * S / MapRes;
                    var p = new Vector2(x, z);
                    var region = WorldLayout.RegionAt(x, z);
                    bool forest = region == WorldLayout.Region.DeepForest || region == WorldLayout.Region.AutumnForest || region == WorldLayout.Region.Edge;
                    float sh = shade[zi * MapRes + xi];

                    // ---- tint
                    float sun = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(0.36f, 0.64f, Fbm(x * 0.016f + 3, z * 0.016f + 9, 3)));
                    var c = Color.Lerp(new Color(0.41f, 0.45f, 0.56f), new Color(0.58f, 0.56f, 0.43f), sun);
                    float dryN = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(0.55f, 0.72f, Fbm(x * 0.035f + 7, z * 0.035f + 2, 2)));
                    if (!forest) c = Color.Lerp(c, new Color(0.62f, 0.56f, 0.38f), dryN * 0.35f);
                    if (forest) c *= new Color(0.82f, 0.88f, 0.98f);
                    if (region == WorldLayout.Region.AutumnForest || region == WorldLayout.Region.Lakeshore)   // fallen-leaf patches
                        c = Color.Lerp(c, new Color(0.66f, 0.47f, 0.32f), Mathf.SmoothStep(0, 1, Mathf.InverseLerp(0.45f, 0.7f, Fbm(x * 0.09f + 3, z * 0.09f + 8, 2))) * 0.55f);
                    float river = WorldLayout.RiverDistance(x, z, out _);
                    float lush = 1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(WorldLayout.RiverHalfWidth + 2, WorldLayout.RiverHalfWidth + 14, river));
                    lush = Mathf.Max(lush, 1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(1.05f, 1.3f, WorldLayout.LakeDistance(x, z))));
                    c = Color.Lerp(c, new Color(0.40f, 0.52f, 0.44f), lush * 0.5f);
                    c = Color.Lerp(c, new Color(0.34f, 0.40f, 0.56f), sh * 0.55f);
                    c.r += (Fbm(x * 0.21f, z * 0.21f, 2) - 0.5f) * 0.05f;
                    tint[zi * MapRes + xi] = new Color(Mathf.Clamp01(c.r), Mathf.Clamp01(c.g), Mathf.Clamp01(c.b), 1);

                    // ---- grass map
                    float dens = region == WorldLayout.Region.Meadow ? 1f : region == WorldLayout.Region.Cabin ? 0.95f : region == WorldLayout.Region.Riverbank ? 0.95f :
                                 region == WorldLayout.Region.Lakeshore ? 0.85f : region == WorldLayout.Region.Highland ? 0.8f : region == WorldLayout.Region.Edge ? 0.45f : 0.62f;
                    float edgeN = (Fbm(x * 0.3f + 11, z * 0.3f + 5, 2) - 0.5f) * 1.6f;
                    float pd = WorldLayout.PathDistance(x, z);
                    dens *= Mathf.SmoothStep(0, 1, Mathf.InverseLerp(1.1f + edgeN, 2.9f + edgeN, pd));
                    if (UnderWater(x, z, 0.05f)) dens = 0;
                    if (_td.GetSteepness(x / S, z / S) > (river < WorldLayout.RiverHalfWidth + 16f ? 60f : 42f)) dens = 0;
                    if (Vector2.Distance(p, cabin) < 5.5f) dens = 0;
                    if (Vector2.Distance(p, WorldLayout.Campfire) < 3.2f) dens *= 0.1f;
                    if (Vector2.Distance(p, WorldLayout.StoneCircle) < 7f) dens *= 0.55f;
                    dens *= 1 - sh * 0.3f;
                    float height = Mathf.Lerp(0.3f, 0.85f, Fbm(x * 0.05f + 21, z * 0.05f + 13, 2));
                    height = Mathf.Lerp(height, 0.9f, lush * 0.7f);
                    height *= Mathf.Lerp(0.45f, 1f, Mathf.InverseLerp(1.5f, 5f, pd));
                    if (Vector2.Distance(p, WorldLayout.Cabin) < WorldLayout.CabinRadius) height *= 0.6f;   // mown yard
                    float dry = forest ? 0.05f : dryN * 0.8f + (region == WorldLayout.Region.Highland ? 0.3f : 0);
                    map[zi * MapRes + xi] = new Color(dens, Mathf.Clamp01(height), Mathf.Clamp01(dry), 1);
                }
            var tintTex = SaveMap("T_GroundTint", tint);
            var mapTex = SaveMap("T_GrassMap", map);

            var go = new GameObject("Grass");
            var gr = go.AddComponent<GrassRenderer>();
            var mat = AssetDatabase.LoadAssetAtPath<Material>($"{AssetSetup.MatDir}/M_GrassBlades.mat");
            if (!mat) { mat = new Material(Shader.Find("WildPartner/Grass")); AssetDatabase.CreateAsset(mat, $"{AssetSetup.MatDir}/M_GrassBlades.mat"); }
            // colours live here (not only as shader defaults) so re-running the builder applies tweaks
            // 试玩反馈 5: dark green roots, bright yellow-green tips, 30–70 cm blades; far colour = the terrain's grass layer
            mat.SetColor("_BaseColor", new Color(0.14f, 0.34f, 0.08f)); mat.SetColor("_TipColor", new Color(0.54f, 0.84f, 0.22f));
            mat.SetColor("_DryColor", new Color(0.78f, 0.75f, 0.38f)); mat.SetColor("_ShadowColor", new Color(0.45f, 0.52f, 0.80f));
            mat.SetColor("_FarColor", new Color(0.40f, 0.62f, 0.20f));
            mat.SetVector("_HeightRange", new Vector4(0.3f, 0.62f));
            EditorUtility.SetDirty(mat);
            gr.material = mat; gr.grassMap = mapTex; gr.groundTint = tintTex;
            var tm = AssetDatabase.LoadAssetAtPath<Material>($"{AssetSetup.MatDir}/M_Terrain.mat");
            if (tm) { tm.SetColor("_ShadowColor", new Color(0.47f, 0.53f, 0.74f)); tm.SetTexture("_GroundTint", tintTex); tm.SetFloat("_WorldSize", S); EditorUtility.SetDirty(tm); }
        }

        static Texture2D SaveMap(string name, Color32[] px)
        {
            string path = $"{TerrainDir}/{name}.png";
            var tex = new Texture2D(MapRes, MapRes, TextureFormat.RGBA32, false, true);
            tex.SetPixels32(px); tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path);
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            imp.sRGBTexture = false; imp.mipmapEnabled = false; imp.textureCompression = TextureImporterCompression.Uncompressed;
            imp.wrapMode = TextureWrapMode.Clamp; imp.filterMode = FilterMode.Bilinear; imp.isReadable = false;
            imp.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        // =========================================================================================
        // Post, rain, cameras, navmesh
        // =========================================================================================
        static Volume BuildVolume()
        {
            string path = "Assets/_Game/Settings/WorldVolume.asset";
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
            if (profile) AssetDatabase.DeleteAsset(path);
            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, path);
            var tm = profile.Add<Tonemapping>(true); tm.mode.Override(TonemappingMode.Neutral);
            var bloom = profile.Add<Bloom>(true); bloom.threshold.Override(0.95f); bloom.intensity.Override(0.45f); bloom.scatter.Override(0.7f); bloom.tint.Override(new Color(1f, 0.92f, 0.82f));
            var ca = profile.Add<ColorAdjustments>(true); ca.postExposure.Override(0.05f); ca.contrast.Override(18f); ca.saturation.Override(4f);
            var smh = profile.Add<ShadowsMidtonesHighlights>(true);
            smh.shadows.Override(new Vector4(0.9f, 0.93f, 1.15f, -0.04f)); smh.highlights.Override(new Vector4(1.04f, 1.0f, 0.94f, 0.02f));
            var st = profile.Add<SplitToning>(true); st.shadows.Override(new Color(0.46f, 0.45f, 0.68f)); st.highlights.Override(new Color(1f, 0.93f, 0.84f)); st.balance.Override(10f);   // milder: strong orange highlights turned the blue sky lavender-grey
            var wb = profile.Add<WhiteBalance>(true); wb.temperature.Override(-2f); wb.tint.Override(-3f);
            var vig = profile.Add<Vignette>(true); vig.intensity.Override(0.24f); vig.smoothness.Override(0.45f); vig.color.Override(new Color(0.12f, 0.08f, 0.22f));
            foreach (var c in profile.components) AssetDatabase.AddObjectToAsset(c, profile);
            AssetDatabase.SaveAssets();

            var go = new GameObject("PostProcess", typeof(Volume));
            var v = go.GetComponent<Volume>();
            v.isGlobal = true; v.sharedProfile = profile; v.priority = 1;
            return v;
        }

        static ParticleSystem BuildRain(Transform parent)
        {
            var go = new GameObject("Rain", typeof(ParticleSystem));
            go.transform.SetParent(parent, false);
            var ps = go.GetComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.playOnAwake = false;
            main.loop = true;
            main.startLifetime = 1.1f;
            main.startSpeed = 0;
            main.startSize3D = true;
            main.startSizeX = 0.025f; main.startSizeY = 0.7f; main.startSizeZ = 1;
            main.startColor = new Color(0.78f, 0.85f, 1f, 0.55f);
            main.maxParticles = 9000;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var vel = ps.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(1.5f); vel.y = new ParticleSystem.MinMaxCurve(-22f); vel.z = new ParticleSystem.MinMaxCurve(0.8f);
            var em = ps.emission; em.rateOverTime = 0;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(60, 1, 60);
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Stretch;
            r.velocityScale = 0.03f; r.lengthScale = 1f;
            r.sharedMaterial = ParticleMat("M_Rain", new Color(0.8f, 0.88f, 1f, 0.6f), false);
            r.shadowCastingMode = ShadowCastingMode.Off;
            return ps;
        }

        struct Cams { public CinemachineCamera player, menu; public Transform menuPivot; }

        static Cams BuildCameras(Transform player)
        {
            var camGo = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
            camGo.tag = "MainCamera";
            var cam = camGo.GetComponent<Camera>();
            cam.nearClipPlane = 0.2f; cam.farClipPlane = 2500f; cam.fieldOfView = 55;
            var data = camGo.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.None;
            camGo.AddComponent<CinemachineBrain>();

            var target = player.Find("CameraTarget");
            var vGo = new GameObject("PlayerCam");
            var vcam = vGo.AddComponent<CinemachineCamera>();
            vcam.Follow = target; vcam.LookAt = target;
            vcam.Lens = new LensSettings { FieldOfView = 52, NearClipPlane = 0.15f, FarClipPlane = 2500f };
            vcam.Priority = 10;
            var orbit = vGo.AddComponent<CinemachineOrbitalFollow>();
            orbit.OrbitStyle = CinemachineOrbitalFollow.OrbitStyles.Sphere;
            // 画面整改 6: closer and lower than before, the witch about a quarter to a third of the frame height
            // and a little left of centre, so the view ahead (path, landscape) gets the larger part of the frame
            orbit.Radius = 5.4f;
            orbit.HorizontalAxis.Value = WorldLayout.PlayerStartYaw;
            orbit.VerticalAxis.Range = new Vector2(-12, 65);
            orbit.VerticalAxis.Value = 7f;
            orbit.RadialAxis.Range = new Vector2(0.65f, 1.9f);
            orbit.RadialAxis.Value = 1f;
            orbit.TrackerSettings.PositionDamping = new Vector3(0.35f, 0.3f, 0.35f);
            var comp = vGo.AddComponent<CinemachineRotationComposer>();
            comp.Damping = new Vector2(0.25f, 0.2f);
            var compo = comp.Composition;
            compo.ScreenPosition = new Vector2(-0.12f, 0f);
            comp.Composition = compo;
            // smooth pull-in on terrain, rocks, trunks and walls instead of popping; a little lift when blocked
            var deocc = vGo.AddComponent<CinemachineDeoccluder>();
            deocc.CollideAgainst = 1; // Default layer: terrain, rocks, buildings, trunks
            deocc.MinimumDistanceFromTarget = 0.8f;
            var avoid = deocc.AvoidObstacles;
            avoid.Enabled = true;
            avoid.CameraRadius = 0.35f;
            avoid.Strategy = CinemachineDeoccluder.ObstacleAvoidance.ResolutionStrategy.PullCameraForward;
            avoid.SmoothingTime = 0.4f;
            avoid.Damping = 0.6f;
            avoid.DampingWhenOccluded = 0.15f;
            avoid.MinimumOcclusionTime = 0.05f;
            deocc.AvoidObstacles = avoid;

            var pivot = new GameObject("MenuPivot").transform;
            pivot.position = new Vector3(S * 0.5f, 30, S * 0.5f);   // valley centre
            var mGo = new GameObject("MenuCam");
            mGo.transform.SetParent(pivot, false);
            mGo.transform.localPosition = new Vector3(0, 58, -235 * WorldLayout.F);
            var mcam = mGo.AddComponent<CinemachineCamera>();
            mcam.Lens = new LensSettings { FieldOfView = 40, NearClipPlane = 0.5f, FarClipPlane = 3000f };
            var lookAt = new GameObject("MenuLookAt").transform;
            lookAt.SetParent(pivot, false);
            lookAt.localPosition = new Vector3(0, 0, 30);
            mcam.LookAt = lookAt;
            mGo.AddComponent<CinemachineHardLookAt>();
            mcam.Priority = 20;
            return new Cams { player = vcam, menu = mcam, menuPivot = pivot };
        }

        static void BakeNavMesh(VegetationSpawner veg)
        {
            veg.Spawn();
            var go = new GameObject("Navigation");
            var surface = go.AddComponent<NavMeshSurface>();
            surface.collectObjects = CollectObjects.All;
            surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            surface.overrideVoxelSize = true;
            surface.voxelSize = 0.3f;
            surface.overrideTileSize = true;
            surface.tileSize = 128;
            surface.layerMask = ~0;
            surface.BuildNavMesh();
            string path = "Assets/_Game/Settings/Terrain/WorldNavMesh.asset";
            if (File.Exists(path)) AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(surface.navMeshData, path);
            veg.Clear();
            Debug.Log("[World] navmesh baked");
        }
    }
}
