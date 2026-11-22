using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using WildPartner.Core;
using WildPartner.Creatures;
using WildPartner.Player;
using WildPartner.World;

namespace WildPartner.UI
{
    /// <summary>
    /// Minimap (top-left, always on while playing) and the big map (M). Both draw the same pre-baked texture
    /// (Resources/Map/WorldMap, made by MapBaker in the editor): no camera renders the scene for the map at
    /// runtime, so the map costs a few UI quads, not a second scene pass (试玩反馈 3, see DECISIONS).
    ///   Minimap: circle ~18% of screen height, player-centred, rotates with the camera (camera facing = up),
    ///            compass letters ride the rim, landmarks past the edge are pinned to the rim. Uncaught pets are
    ///            never shown.
    ///   Big map: north up, every landmark with its name, the player, and where each caught pet was found.
    /// </summary>
    public class MapUI : MonoBehaviour
    {
        const float ViewRadius = 45f;   // metres from the player to the minimap rim

        struct Landmark { public string name, glyph; public Vector2 pos; public Color color; }

        static Landmark[] _landmarks;
        static Landmark[] Landmarks => _landmarks ??= new[]
        {
            new Landmark { name = "木屋", glyph = "屋", pos = WorldLayout.House, color = new Color(0.86f, 0.45f, 0.25f) },
            new Landmark { name = "石阵", glyph = "石", pos = WorldLayout.StoneCircle, color = new Color(0.55f, 0.52f, 0.62f) },
            new Landmark { name = "木桥", glyph = "桥", pos = WorldLayout.Bridge, color = new Color(0.68f, 0.48f, 0.3f) },
            new Landmark { name = "瀑布", glyph = "瀑", pos = new Vector2(WorldLayout.RiverIn[2].x, WorldLayout.RiverIn[2].y), color = new Color(0.3f, 0.58f, 0.85f) },
            new Landmark { name = "瞭望塔", glyph = "塔", pos = WorldLayout.Watchtower, color = new Color(0.5f, 0.38f, 0.62f) },
            new Landmark { name = "湖心岛", glyph = "岛", pos = new Vector2(WorldLayout.Islands[0].x, WorldLayout.Islands[0].y), color = new Color(0.35f, 0.62f, 0.42f) },
        };

        GameFlow _flow;
        RectTransform _canvasRt, _objective;
        Texture2D _mapTex;

        // minimap
        RectTransform _mini, _rotor, _miniMap, _miniArrow;
        readonly List<RectTransform> _miniIcons = new List<RectTransform>();
        readonly RectTransform[] _compass = new RectTransform[4];

        // big map
        RectTransform _big, _bigMap, _bigArrow, _bigPets;

        static Sprite _circle, _arrow;
        static Sprite Circle => _circle ??= MakeCircle();
        static Sprite Arrow => _arrow ??= MakeArrow();

        public static bool Exists => Resources.Load<Texture2D>("Map/WorldMap") != null;

        public void Build(GameFlow flow, Canvas canvas, RectTransform hud, RectTransform objective)
        {
            _flow = flow;
            _canvasRt = (RectTransform)canvas.transform;
            _objective = objective;
            _mapTex = Resources.Load<Texture2D>("Map/WorldMap");
            if (!_mapTex) Debug.LogWarning("[Map] Resources/Map/WorldMap missing: run WildPartner > Bake World Map");
            BuildMini(hud);
            BuildBig(canvas.transform);
            _flow.StateChanged += OnState;
            OnState(_flow.State);
        }

        // ------------------------------------------------------------------ minimap
        void BuildMini(RectTransform hud)
        {
            _mini = UIKit.Rect(hud, "Minimap", new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(36, -30), new Vector2(194, 194));
            var rim = UIKit.Rect(_mini, "Rim", Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(10, 10));
            Img(rim, Circle, UIKit.ParchmentFrame);
            var mask = UIKit.Stretch(_mini, "Mask");
            Img(mask, Circle, new Color(0.93f, 0.87f, 0.72f));
            mask.gameObject.AddComponent<Mask>().showMaskGraphic = true;
            _rotor = UIKit.Rect(mask, "Rotor", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            _miniMap = UIKit.Rect(_rotor, "Map", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.one * 100);
            var ri = _miniMap.gameObject.AddComponent<RawImage>(); ri.texture = _mapTex; ri.raycastTarget = false;
            // soft inner shade so the edge reads as a lens, not a cut-out
            var vign = UIKit.Stretch(mask, "Shade");
            Img(vign, Circle, new Color(0.25f, 0.16f, 0.1f, 0f));

            var overlay = UIKit.Stretch(_mini, "Overlay");
            foreach (var lm in Landmarks) _miniIcons.Add(Icon(overlay, lm, 26, false));
            string[] dirs = { "北", "东", "南", "西" };
            for (int k = 0; k < 4; k++)
            {
                var c = UIKit.Rect(overlay, dirs[k], new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(26, 26));
                Img(c, Circle, k == 0 ? new Color(0.62f, 0.2f, 0.18f, 0.92f) : new Color(0.2f, 0.13f, 0.1f, 0.78f));
                UIKit.Label(c, dirs[k], 16, TextAnchor.MiddleCenter, new Color(1f, 0.95f, 0.85f));
                _compass[k] = c;
            }
            _miniArrow = UIKit.Rect(overlay, "Player", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(26, 26));
            Img(_miniArrow, Arrow, Color.white);
        }

        // ------------------------------------------------------------------ big map
        void BuildBig(Transform canvas)
        {
            _big = UIKit.Stretch(canvas, "BigMap");
            var dim = UIKit.Panel(_big, new Color(0.08f, 0.06f, 0.05f, 0.62f)); dim.sprite = null;
            var card = UIKit.Rect(_big, "Card", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(900, 1010));
            UIKit.Box(card, new Color(0.95f, 0.89f, 0.74f, 0.98f), UIKit.ParchmentFrame, 1.2f);
            var title = UIKit.Rect(card, "Title", new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), new Vector2(0, -12), new Vector2(0, 64));
            UIKit.Label(title, "荒原地图", 40, TextAnchor.MiddleCenter, UIKit.Ink);
            _bigMap = UIKit.Rect(card, "Map", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -6), new Vector2(840, 840));
            var ri = _bigMap.gameObject.AddComponent<RawImage>(); ri.texture = _mapTex; ri.raycastTarget = false;
            var frame = UIKit.Rect(_bigMap, "Frame", Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(8, 8));
            var fi = frame.gameObject.AddComponent<Image>(); fi.sprite = UIKit.Ring; fi.type = Image.Type.Sliced; fi.color = UIKit.ParchmentFrame; fi.raycastTarget = false;

            float sc = 840f / WorldLayout.Size;
            foreach (var lm in Landmarks)
            {
                var ic = Icon(_bigMap, lm, 34, true);
                ic.anchoredPosition = (lm.pos - Vector2.one * WorldLayout.Size * 0.5f) * sc;
            }
            _bigPets = UIKit.Stretch(_bigMap, "Pets");
            // compass rose (north is always up on the big map)
            var rose = UIKit.Rect(_bigMap, "North", new Vector2(1, 1), new Vector2(1, 1), new Vector2(0.5f, 0.5f), new Vector2(-46, -46), new Vector2(44, 44));
            Img(rose, Circle, new Color(0.62f, 0.2f, 0.18f, 0.92f));
            UIKit.Label(rose, "北", 24, TextAnchor.MiddleCenter, new Color(1f, 0.95f, 0.85f));
            var up = UIKit.Rect(rose, "Up", new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0.5f, 0), new Vector2(0, -2), new Vector2(22, 22));
            Img(up, Arrow, new Color(0.62f, 0.2f, 0.18f, 0.92f));

            _bigArrow = UIKit.Rect(_bigMap, "Player", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(36, 36));
            Img(_bigArrow, Arrow, Color.white);
            var foot = UIKit.Rect(card, "Foot", new Vector2(0, 0), new Vector2(1, 0), new Vector2(0.5f, 0), new Vector2(0, 10), new Vector2(0, 44));
            UIKit.Label(foot, "白色箭头是你 · 头像是伙伴被找到的地方 · 按 M 或 Esc 关闭", 22, TextAnchor.MiddleCenter, UIKit.Ink);
            _big.gameObject.SetActive(false);
        }

        void RefreshBig()
        {
            foreach (Transform c in _bigPets) Destroy(c.gameObject);
            float sc = 840f / WorldLayout.Size;
            foreach (var e in _flow.Save.caught)
            {
                var def = CreatureDefs.Get(e.id);
                if (def == null) continue;
                var p = UIKit.Rect(_bigPets, def.id, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                    (new Vector2(e.position.x, e.position.z) - Vector2.one * WorldLayout.Size * 0.5f) * sc, new Vector2(58, 58));
                var ring = UIKit.Rect(p, "Ring", Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(6, 6));
                Img(ring, Circle, new Color(0.45f, 0.3f, 0.2f, 0.95f));
                Img(UIKit.Stretch(p, "Back"), Circle, Color.Lerp(def.journalColor, Color.white, 0.4f));
                var tex = Resources.Load<Texture2D>("Icons/" + def.model);
                var inner = UIKit.Rect(p, "Icon", Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(-6, -6));
                if (tex) { var ri = inner.gameObject.AddComponent<RawImage>(); ri.texture = tex; ri.raycastTarget = false; }
                else UIKit.Label(inner, def.name.Substring(0, 1), 26, TextAnchor.MiddleCenter, UIKit.Ink);
                // pet names sit to the right so they never collide with the landmark names below the icons
                var name = UIKit.Rect(p, "Name", new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(0, 0.5f), new Vector2(4, 0), new Vector2(160, 30));
                var t = UIKit.Label(name, def.name, 20, TextAnchor.MiddleLeft, UIKit.Ink);
                t.gameObject.AddComponent<Outline>().effectColor = new Color(1f, 0.96f, 0.86f, 0.9f);
            }
            _bigArrow.SetAsLastSibling();
        }

        void OnState(GameFlow.FlowState s)
        {
            bool open = s == GameFlow.FlowState.Map;
            _big.gameObject.SetActive(open);
            if (open) { RefreshBig(); LateUpdate(); }
        }

        // ------------------------------------------------------------------ per frame
        void LateUpdate()
        {
            var pc = PlayerController.Instance;
            var cam = Camera.main;
            if (!pc || !cam || _flow == null) return;
            float S = WorldLayout.Size;
            Vector3 p = pc.transform.position;
            float phi = pc.transform.eulerAngles.y;

            if (_flow.State == GameFlow.FlowState.Map)
            {
                float sc = 840f / S;
                _bigArrow.anchoredPosition = new Vector2(p.x - S * 0.5f, p.z - S * 0.5f) * sc;
                _bigArrow.localEulerAngles = new Vector3(0, 0, -phi);
                return;
            }
            if (!_mini.gameObject.activeInHierarchy) return;

            // size: 18% of the screen height; the objective box sits right below
            float d = Mathf.Round(_canvasRt.rect.height * 0.18f);
            _mini.sizeDelta = new Vector2(d, d);
            if (_objective) _objective.anchoredPosition = new Vector2(36, -30 - d - 18);
            float r = d * 0.5f, s = r / ViewRadius;

            float psi = cam.transform.eulerAngles.y;
            _rotor.localEulerAngles = new Vector3(0, 0, psi);
            _miniMap.sizeDelta = Vector2.one * S * s;
            _miniMap.anchoredPosition = -new Vector2(p.x - S * 0.5f, p.z - S * 0.5f) * s;
            _miniArrow.localEulerAngles = new Vector3(0, 0, psi - phi);

            float a = psi * Mathf.Deg2Rad, ca = Mathf.Cos(a), sa = Mathf.Sin(a);
            Vector2 Rot(Vector2 v) => new Vector2(v.x * ca - v.y * sa, v.x * sa + v.y * ca);
            var lms = Landmarks;
            for (int i = 0; i < lms.Length; i++)
            {
                Vector2 v = Rot((lms[i].pos - new Vector2(p.x, p.z)) * s);
                float lim = r - 15f;
                bool outside = v.magnitude > lim;
                if (outside) v = v.normalized * lim;
                _miniIcons[i].anchoredPosition = v;
                _miniIcons[i].localScale = Vector3.one * (outside ? 0.78f : 1f);
            }
            Vector2[] dirs = { Vector2.up, Vector2.right, Vector2.down, Vector2.left };
            for (int k = 0; k < 4; k++) _compass[k].anchoredPosition = Rot(dirs[k]) * (r + 2f);
        }

        // ------------------------------------------------------------------ helpers
        static RectTransform Icon(RectTransform parent, Landmark lm, float size, bool named)
        {
            var c = UIKit.Rect(parent, lm.name, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(size, size));
            // children draw over their parent: ring first, then the coloured dot, then the glyph
            var ring = UIKit.Rect(c, "Ring", Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(4, 4));
            Img(ring, Circle, new Color(1f, 0.96f, 0.86f, 0.95f));
            var dot = UIKit.Stretch(c, "Dot");
            Img(dot, Circle, lm.color);
            UIKit.Label(c, lm.glyph, Mathf.RoundToInt(size * 0.6f), TextAnchor.MiddleCenter, Color.white);
            if (named)
            {
                var n = UIKit.Rect(c, "Name", new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0.5f, 1), new Vector2(0, -2), new Vector2(180, 32));
                var t = UIKit.Label(n, lm.name, 24, TextAnchor.UpperCenter, UIKit.Ink);
                t.gameObject.AddComponent<Outline>().effectColor = new Color(1f, 0.96f, 0.86f, 0.9f);
            }
            return c;
        }

        static Image Img(RectTransform rt, Sprite sp, Color c)
        {
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = sp; img.color = c; img.raycastTarget = false;
            return img;
        }

        static Sprite MakeCircle()
        {
            const int n = 128;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "UI_Circle" };
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = x + 0.5f - n * 0.5f, dy = y + 0.5f - n * 0.5f;
                    float a = Mathf.Clamp01(n * 0.5f - 0.5f - Mathf.Sqrt(dx * dx + dy * dy));
                    px[y * n + x] = new Color32(255, 255, 255, (byte)(a * 255));
                }
            tex.SetPixels32(px); tex.Apply(false, true);
            return Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100);
        }

        /// <summary>Map pointer: a white arrowhead with a dark outline, pointing up (+y).</summary>
        static Sprite MakeArrow()
        {
            const int n = 64;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "UI_Arrow" };
            var px = new Color32[n * n];
            Vector2 tip = new Vector2(32, 60), l = new Vector2(8, 6), rr = new Vector2(56, 6), notch = new Vector2(32, 20);
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    var q = new Vector2(x + 0.5f, y + 0.5f);
                    // concave quad: inside = inside both triangles (tip, r, notch) or (tip, notch, l)
                    float d1 = Mathf.Max(Mathf.Max(Side(tip, rr, q), Side(rr, notch, q)), Side(notch, tip, q));
                    float d2 = Mathf.Max(Mathf.Max(Side(tip, notch, q), Side(notch, l, q)), Side(l, tip, q));
                    float d = Mathf.Min(d1, d2);
                    float fill = Mathf.Clamp01(0.5f - (d + 3f));      // white body, 3 px inset
                    float edge = Mathf.Clamp01(0.5f - d);             // dark outline
                    var c = Color.Lerp(new Color(0.2f, 0.1f, 0.14f, edge), new Color(1, 1, 1, 1), fill);
                    px[y * n + x] = c;
                }
            tex.SetPixels32(px); tex.Apply(false, true);
            return Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100);
        }

        /// <summary>Signed distance to the line a→b: negative on its right, i.e. inside a clockwise outline.</summary>
        static float Side(Vector2 a, Vector2 b, Vector2 q)
        {
            Vector2 e = (b - a).normalized;
            return e.x * (q.y - a.y) - e.y * (q.x - a.x);
        }
    }
}
