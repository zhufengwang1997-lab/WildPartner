using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using WildPartner.World;

namespace WildPartner.EditorTools
{
    /// <summary>
    /// Bakes the world map texture used by the minimap and the big map (试玩反馈 3):
    ///   tools/unity.sh WildPartner.EditorTools.MapBaker.Bake   (also runs at the end of WorldBuilder.Build)
    /// One orthographic top-down render of the finished scene at noon without haze, then a hand-drawn pass:
    /// colours flattened into soft bands, warmed towards parchment, ink lines along strong edges (shores,
    /// paths, tree crowns), paper grain and a burnt edge. At runtime the map is only a texture: no second camera
    /// renders the scene every frame (that would cost a full extra scene pass and skew the upscaler benchmarks).
    /// </summary>
    public static class MapBaker
    {
        public const string OutPath = "Assets/Resources/Map/WorldMap.png";
        const int RenderRes = 2048, OutRes = 1024;

        [MenuItem("WildPartner/Bake World Map")]
        public static void Bake()
        {
            EditorSceneManager.OpenScene("Assets/_Game/Scenes/World.unity");
            var veg = Object.FindFirstObjectByType<VegetationSpawner>();
            if (veg) veg.Spawn();
            var env = Object.FindFirstObjectByType<EnvController>();
            float fog = EnvController.FogAmount;
            EnvController.FogAmount = 0f;
            if (env) { env.timeOfDay = 12.5f; env.Apply(); }
            Shader.SetGlobalVector("_WP_NearFade", Vector4.zero);   // ortho clip.w is 1: the near-camera dither would eat every tree
            foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
                if (r.name.Contains("Cloud") || r.name.Contains("FarRidge") || r.name.Contains("SnowMountains") || r is ParticleSystemRenderer)
                    r.enabled = false;

            float S = WorldLayout.Size;
            var go = new GameObject("MapCam");
            var cam = go.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = S * 0.5f;
            cam.aspect = 1f;
            cam.nearClipPlane = 1f; cam.farClipPlane = 1000f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.55f, 0.6f, 0.45f);
            go.transform.SetPositionAndRotation(new Vector3(S * 0.5f, 500f, S * 0.5f), Quaternion.Euler(90, 0, 0));   // north (+Z) up
            var rt = new RenderTexture(RenderRes, RenderRes, 24, RenderTextureFormat.ARGB32);
            cam.targetTexture = rt;
            float lodBias = QualitySettings.lodBias;
            QualitySettings.lodBias = 100f;   // the ortho view is "far" for every LODGroup: keep the trees in
            cam.Render(); cam.Render();
            QualitySettings.lodBias = lodBias;
            RenderTexture.active = rt;
            var raw = new Texture2D(RenderRes, RenderRes, TextureFormat.RGB24, false);
            raw.ReadPixels(new Rect(0, 0, RenderRes, RenderRes), 0, 0); raw.Apply();
            RenderTexture.active = null;
            cam.targetTexture = null;
            Object.DestroyImmediate(go);
            EnvController.FogAmount = fog;

            var src = Downsample(raw.GetPixels(), RenderRes, OutRes);
            var outTex = new Texture2D(OutRes, OutRes, TextureFormat.RGB24, false);
            outTex.SetPixels(Stylise(src, OutRes));
            outTex.Apply();
            Directory.CreateDirectory(Path.GetDirectoryName(OutPath));
            File.WriteAllBytes(OutPath, outTex.EncodeToPNG());
            Directory.CreateDirectory("Logs/shots");
            File.WriteAllBytes("Logs/shots/map_raw.png", raw.EncodeToPNG());
            AssetDatabase.ImportAsset(OutPath);
            var imp = (TextureImporter)AssetImporter.GetAtPath(OutPath);
            imp.textureType = TextureImporterType.Default;
            imp.wrapMode = TextureWrapMode.Clamp;
            imp.mipmapEnabled = true;
            imp.sRGBTexture = true;
            imp.maxTextureSize = 1024;
            imp.SaveAndReimport();
            Debug.Log($"[Map] baked {OutPath} ({OutRes}px for {S:0} m)");
        }

        static Color[] Downsample(Color[] px, int n, int m)
        {
            int k = n / m;
            var o = new Color[m * m];
            for (int y = 0; y < m; y++)
                for (int x = 0; x < m; x++)
                {
                    Color s = Color.clear;
                    for (int j = 0; j < k; j++) for (int i = 0; i < k; i++) s += px[(y * k + j) * n + x * k + i];
                    o[y * m + x] = s / (k * k);
                }
            return o;
        }

        static float Hash(int x, int y) { uint h = (uint)(x * 374761393 + y * 668265263); h = (h ^ (h >> 13)) * 1274126177u; return (h & 0xffff) / 65535f; }
        static float VNoise(float x, float y)
        {
            int ix = Mathf.FloorToInt(x), iy = Mathf.FloorToInt(y); float fx = x - ix, fy = y - iy;
            fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy);
            return Mathf.Lerp(Mathf.Lerp(Hash(ix, iy), Hash(ix + 1, iy), fx), Mathf.Lerp(Hash(ix, iy + 1), Hash(ix + 1, iy + 1), fx), fy);
        }

        // HLSL-style smoothstep (Mathf.SmoothStep interpolates between its first two arguments instead)
        static float Smooth(float a, float b, float x) { float t = Mathf.Clamp01((x - a) / (b - a)); return t * t * (3 - 2 * t); }

        static float[] Blur(float[] a, int n, int r)
        {
            var t = new float[n * n]; var o = new float[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float s = 0; int c = 0;
                    for (int k = -r; k <= r; k++) { int xx = x + k; if (xx < 0 || xx >= n) continue; s += a[y * n + xx]; c++; }
                    t[y * n + x] = s / c;
                }
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float s = 0; int c = 0;
                    for (int k = -r; k <= r; k++) { int yy = y + k; if (yy < 0 || yy >= n) continue; s += t[yy * n + x]; c++; }
                    o[y * n + x] = s / c;
                }
            return o;
        }

        static Color[] Stylise(Color[] c, int n)
        {
            var parchment = new Color(0.93f, 0.85f, 0.68f);
            var ink = new Color(0.30f, 0.22f, 0.16f);
            var lum = new float[n * n];
            for (int i = 0; i < c.Length; i++) lum[i] = c[i].r * 0.3f + c[i].g * 0.59f + c[i].b * 0.11f;
            // blur first: the grass texture is high-frequency noise at this scale and inked every pixel
            lum = Blur(Blur(lum, n, 2), n, 2);
            var o = new Color[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    int i = y * n + x;
                    Color col = c[i];
                    // soft colour bands (watercolour-ish), a touch more saturation, warmed towards parchment
                    Color.RGBToHSV(col, out float h, out float s, out float v);
                    v = Mathf.Lerp(v, Mathf.Round(v * 7f) / 7f, 0.45f);
                    col = Color.HSVToRGB(h, Mathf.Clamp01(s * 0.85f), Mathf.Clamp01(v * 1.08f));
                    // the water shader renders near-white from straight above: paint it as watercolour blue
                    if (s < 0.14f && v > 0.82f) col = Color.Lerp(new Color(0.50f, 0.70f, 0.78f), new Color(0.62f, 0.80f, 0.84f), (v - 0.82f) * 5f);
                    col = Color.Lerp(col, parchment, 0.24f);
                    // ink lines on strong luminance edges (Sobel)
                    int xm = Mathf.Max(x - 1, 0), xp = Mathf.Min(x + 1, n - 1), ym = Mathf.Max(y - 1, 0), yp = Mathf.Min(y + 1, n - 1);
                    float gx = lum[ym * n + xp] + 2 * lum[y * n + xp] + lum[yp * n + xp] - lum[ym * n + xm] - 2 * lum[y * n + xm] - lum[yp * n + xm];
                    float gy = lum[yp * n + xm] + 2 * lum[yp * n + x] + lum[yp * n + xp] - lum[ym * n + xm] - 2 * lum[ym * n + x] - lum[ym * n + xp];
                    float edge = Smooth(0.12f, 0.35f, Mathf.Sqrt(gx * gx + gy * gy));
                    col = Color.Lerp(col, ink, edge * 0.45f);
                    // paper grain + fibres
                    float grain = VNoise(x * 0.35f, y * 0.35f) * 0.6f + VNoise(x * 0.04f + 7, y * 0.04f + 3) * 0.4f;
                    col *= Mathf.Lerp(0.92f, 1.04f, grain);
                    // burnt edge
                    float ex = Mathf.Min(x, n - 1 - x) / (float)n, ey = Mathf.Min(y, n - 1 - y) / (float)n;
                    float e = Mathf.Min(ex, ey) + (VNoise(x * 0.05f, y * 0.05f) - 0.5f) * 0.02f;
                    col = Color.Lerp(col * new Color(0.55f, 0.4f, 0.28f), col, Smooth(0.0f, 0.025f, e));
                    col.a = 1;
                    o[i] = col;
                }
            return o;
        }
    }
}
