using UnityEngine;

namespace WildPartner.World
{
    /// <summary>Runtime particle materials (URP Particles/Unlit) with a soft round dot texture — without a texture
    /// the particles draw as hard white squares.</summary>
    public static class FxMat
    {
        static Texture2D _dot;
        static Material _alpha, _add, _ring;
        static Texture2D _ringTex;

        /// <summary>A thin soft ring (water ripples, drawn flat on the surface).</summary>
        public static Material Ring
        {
            get
            {
                if (_ring) return _ring;
                const int n = 128;
                _ringTex = new Texture2D(n, n, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, name = "FxRing" };
                var px = new Color32[n * n];
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        float dx = (x + 0.5f) / n * 2 - 1, dy = (y + 0.5f) / n * 2 - 1, r = Mathf.Sqrt(dx * dx + dy * dy);
                        float a = Mathf.Exp(-Mathf.Pow((r - 0.82f) / 0.07f, 2)) + 0.35f * Mathf.Exp(-Mathf.Pow((r - 0.62f) / 0.05f, 2));
                        px[y * n + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(a) * 255));
                    }
                _ringTex.SetPixels32(px); _ringTex.Apply(true);
                _ring = Make(false);
                if (_ring) { _ring.name = "FxRing"; _ring.SetTexture("_BaseMap", _ringTex); }
                return _ring;
            }
        }

        public static Texture2D Dot
        {
            get
            {
                if (_dot) return _dot;
                const int n = 64;
                _dot = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "FxDot" };
                var px = new Color32[n * n];
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        float dx = (x + 0.5f) / n * 2 - 1, dy = (y + 0.5f) / n * 2 - 1;
                        float a = Mathf.Clamp01(1 - Mathf.Sqrt(dx * dx + dy * dy)); a = a * a * (3 - 2 * a);
                        px[y * n + x] = new Color32(255, 255, 255, (byte)(a * 255));
                    }
                _dot.SetPixels32(px); _dot.Apply();
                return _dot;
            }
        }

        /// <summary>Alpha-blended soft dots (spray, mist).</summary>
        public static Material Alpha => _alpha ? _alpha : (_alpha = Make(false));
        /// <summary>Additive soft dots (magic sparkles).</summary>
        public static Material Additive => _add ? _add : (_add = Make(true));

        static Material Make(bool additive)
        {
            var sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (!sh) return null;
            var m = new Material(sh) { name = additive ? "FxAdd" : "FxAlpha" };
            m.SetFloat("_Surface", 1); m.SetFloat("_Blend", additive ? 2 : 0);
            m.SetFloat("_SrcBlend", (float)(additive ? UnityEngine.Rendering.BlendMode.One : UnityEngine.Rendering.BlendMode.SrcAlpha));
            m.SetFloat("_DstBlend", (float)(additive ? UnityEngine.Rendering.BlendMode.One : UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha));
            m.SetFloat("_ZWrite", 0); m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); m.renderQueue = 3000;
            m.SetTexture("_BaseMap", Dot);
            return m;
        }
    }
}
