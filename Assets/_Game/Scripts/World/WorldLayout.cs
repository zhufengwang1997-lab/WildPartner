using System.Collections.Generic;
using UnityEngine;

namespace WildPartner.World
{
    /// <summary>
    /// Single source of truth for the map layout, traced from docs/reference/world_map.webp (north up).
    /// World X = east, Z = north, terrain covers [0, Size] x [0, Size]. Image pixel (px, py) of the
    /// 1024 px reference maps to world (px, Size - py). Used by the editor world builder and at runtime
    /// (creature habitats, journal hints, ending trigger).
    /// </summary>
    public static class WorldLayout
    {
        /// <summary>Playable world edge length. Shrunk from 600 m to 360 m on 2026-11-23 (试玩反馈 4): every
        /// landmark keeps its place on the reference map, distances shrink by F; models keep their real size.</summary>
        public const float Size = 360f;
        /// <summary>Shrink factor against the original 600 m layout, for distances tuned in metres back then.</summary>
        public const float F = Size / 600f;
        /// <summary>Design coordinates are pixels of the 1024 px reference map; K maps them onto the world (PRD-补充 §2).</summary>
        public const float K = Size / 1024f;
        static Vector2 M(float x, float y) => new Vector2(x * K, y * K);
        static Vector3 R(float x, float y, float level) => new Vector3(x * K, y * K, level);
        public const float HeightScale = 160f;
        public const float LakeLevel = 27f;
        public const float PlayableInset = 42f * F;
        public const int Seed = 20261122;

        // Landmarks
        public static readonly Vector2 Cabin = M(862, 322);              // centre of the cliff-top plateau (SE)
        public const float CabinPlateau = 50f;
        public const float CabinRadius = 22f;
        /// <summary>From the house door out over the valley (WNW); the ramp leaves the plateau this way.</summary>
        public static readonly Vector2 CabinFacing = new Vector2(-0.926f, 0.377f);
        public static readonly Vector2 House = Cabin - CabinFacing * 6f;
        public static readonly Vector2 RampTop = Cabin + CabinFacing * 19f + new Vector2(0.377f, 0.926f) * 4f;
        public static readonly Vector2 RampBottom = RampTop + new Vector2(-40, 18);
        public static readonly Vector2 Campfire = Cabin + new Vector2(-3, -11);
        public static readonly Vector2 StoneCircle = M(320, 544);
        public static readonly Vector2 Watchtower = M(150, 452);
        public static readonly Vector2 PlayerStart = House + CabinFacing * 6.5f;
        /// <summary>Front door of the cabin model (door plane 2.31 m out from the cabin centre, 试玩反馈 1123b-3).</summary>
        public static readonly Vector2 CabinDoor = House + CabinFacing * 2.31f;
        /// <summary>The cabin interior is a separate room outside the map, below the level of the rim hills (no terrain,
        /// heightfield collider, sun or haze reaches it; nothing inside the valley can see it).</summary>
        public static readonly Vector3 InteriorOrigin = new Vector3(-70f, -10f, House.y);
        public static readonly float PlayerStartYaw = Mathf.Atan2(CabinFacing.x, CabinFacing.y) * Mathf.Rad2Deg;

        // Lake (ellipse) + islands
        public static readonly Vector2 LakeCentre = M(812, 826);
        public static readonly Vector2 LakeRadii = M(190, 105);
        public static readonly Vector3[] Islands = { new Vector3(846 * K, 830 * K, 42 * K), new Vector3(735 * K, 800 * K, 13 * K) };

        // River: x, z, water level. Steep level drops are the waterfalls (NW highland).
        public static readonly Vector3[] RiverIn =
        {
            R(215, 960, 64), R(245, 905, 63), R(258, 884, 52),
            R(285, 842, 51), R(300, 822, 41), R(338, 794, 40),
            R(366, 778, 32), R(450, 736, 30.6f), R(560, 698, 29.2f),
            R(640, 684, 28f), R(700, 700, LakeLevel), R(738, 738, LakeLevel),   // last point inside the lake: no cut-off river end
        };
        public static readonly Vector3[] RiverOut =
        {
            R(722, 742, LakeLevel), R(748, 664, 26.9f), R(744, 600, 26.6f),
            R(745, 528, 26.2f), R(722, 468, 25.8f), R(684, 425, 25.4f),
            R(660, 405, 25.2f),
        };
        public static readonly Vector2 Pond = M(655, 400);
        public const float PondRadius = 26f * K;
        public const float RiverHalfWidth = 5f;
        public static readonly Vector2 Bridge = M(748, 664);

        // Paths (dirt): polylines between landmarks.
        public static readonly Vector2[][] Paths =
        {
            new[] { House + CabinFacing * 4f, RampTop, RampBottom, M(700, 380), M(600, 420), M(480, 470), M(380, 520), M(330, 544) },
            new[] { M(330, 544), M(260, 520), M(200, 480), M(160, 455) },
            new[] { M(330, 544), M(380, 610), M(470, 650), M(560, 650), M(660, 640), M(748, 664), M(790, 700) },
            new[] { M(480, 470), M(500, 380), M(470, 280), M(420, 200) },
            new[] { M(560, 650), M(540, 760), M(500, 850) },
        };

        public enum Region { Meadow, AutumnForest, DeepForest, Riverbank, Lakeshore, Highland, Cabin, Edge }

        /// <summary>Coarse biome classification used for planting and creature habitats.</summary>
        public static Region RegionAt(float x, float z)
        {
            float edge = Mathf.Min(Mathf.Min(x, Size - x), Mathf.Min(z, Size - z));
            if (edge < PlayableInset - 10) return Region.Edge;
            if (Vector2.Distance(new Vector2(x, z), Cabin) < CabinRadius + 10) return Region.Cabin;
            float lake = LakeDistance(x, z);
            if (lake < 1.25f) return Region.Lakeshore;
            if (RiverDistance(x, z, out _) < RiverHalfWidth + 14) return Region.Riverbank;
            if (x < 330 * K && z > 800 * K) return Region.Highland;
            // Autumn forest: north band + lake shores
            if (z > 700 * K && x > 330 * K && x < 1000 * K && lake < 1.9f) return Region.AutumnForest;
            if (z > 760 * K && x > 330 * K) return Region.AutumnForest;
            // Deep green forest: west and south
            float n = Mathf.PerlinNoise(x * 0.007f / F + 3, z * 0.007f / F + 7) * 35 * F;
            if (x < 230 * K + n || z < 200 * K + n || (x > 900 * K && z < 640 * K)) return Region.DeepForest;
            return Region.Meadow;
        }

        /// <summary>Normalised elliptical distance from the lake centre (1 = shoreline).</summary>
        public static float LakeDistance(float x, float z)
        {
            var d = new Vector2(x, z) - LakeCentre;
            float a = Mathf.Atan2(d.y, d.x);
            float wobble = 1 + 0.12f * Mathf.Sin(a * 3 + 1) + 0.07f * Mathf.Sin(a * 5 + 2);
            return new Vector2(d.x / LakeRadii.x, d.y / LakeRadii.y).magnitude / wobble;
        }

        /// <summary>Distance to the river centre line (both branches + pond); outputs the water level there.</summary>
        public static float RiverDistance(float x, float z, out float waterLevel)
        {
            var p = new Vector2(x, z);
            float best = float.MaxValue; waterLevel = LakeLevel;
            Segments(RiverIn, p, ref best, ref waterLevel);
            Segments(RiverOut, p, ref best, ref waterLevel);
            float pond = Vector2.Distance(p, Pond) - PondRadius + RiverHalfWidth;
            if (pond < best) { best = Mathf.Max(pond, 0); waterLevel = 25.2f; }
            return best;
        }

        static void Segments(Vector3[] pts, Vector2 p, ref float best, ref float level)
        {
            for (int i = 0; i < pts.Length - 1; i++)
            {
                var a = new Vector2(pts[i].x, pts[i].y);
                var b = new Vector2(pts[i + 1].x, pts[i + 1].y);
                var ab = b - a;
                float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
                float d = Vector2.Distance(p, a + ab * t);
                if (d < best)
                {
                    best = d;
                    // same profile as the water ribbon: waterfalls stay level, then drop over the end of the segment
                    float drop = pts[i].z - pts[i + 1].z;
                    float lt = drop > 3f ? Mathf.SmoothStep(0, 1, Mathf.InverseLerp(0.55f, 0.95f, t)) : t;
                    level = Mathf.Lerp(pts[i].z, pts[i + 1].z, lt);
                }
            }
        }

        public static float PathDistance(float x, float z)
        {
            // domain warp: the polylines become gently meandering trails (画面整改 1: no ruler-straight bands)
            var p = new Vector2(x + (Mathf.PerlinNoise(x * 0.025f + 7.1f, z * 0.025f + 1.3f) - 0.5f) * 7f,
                                z + (Mathf.PerlinNoise(x * 0.025f + 3.7f, z * 0.025f + 9.2f) - 0.5f) * 7f);
            float best = float.MaxValue;
            foreach (var path in Paths)
                for (int i = 0; i < path.Length - 1; i++)
                {
                    var ab = path[i + 1] - path[i];
                    float t = Mathf.Clamp01(Vector2.Dot(p - path[i], ab) / ab.sqrMagnitude);
                    best = Mathf.Min(best, Vector2.Distance(p, path[i] + ab * t));
                }
            return best;
        }

        public static string RegionName(Region r) => r switch
        {
            Region.Meadow => "中央草甸",
            Region.AutumnForest => "秋色林",
            Region.DeepForest => "深绿林",
            Region.Riverbank => "河边",
            Region.Lakeshore => "湖畔",
            Region.Highland => "瀑布高地",
            Region.Cabin => "悬崖木屋",
            _ => "荒野边缘",
        };

        public static IEnumerable<Vector3> AllRiverPoints()
        {
            foreach (var p in RiverIn) yield return p;
            foreach (var p in RiverOut) yield return p;
        }
    }
}
