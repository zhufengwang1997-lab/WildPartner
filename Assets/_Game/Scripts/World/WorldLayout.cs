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
        public const float Size = 1024f;
        public const float HeightScale = 160f;
        public const float LakeLevel = 27f;
        public const float PlayableInset = 70f;
        public const int Seed = 20261122;

        // Landmarks
        public static readonly Vector2 Cabin = new Vector2(862, 322);
        public const float CabinPlateau = 50f;
        public const float CabinRadius = 44f;
        public static readonly Vector2 Campfire = new Vector2(846, 296);
        public static readonly Vector2 StoneCircle = new Vector2(320, 544);
        public static readonly Vector2 Watchtower = new Vector2(150, 452);
        public static readonly Vector2 PlayerStart = new Vector2(840, 305);
        public const float PlayerStartYaw = 300f;

        // Lake (ellipse) + islands
        public static readonly Vector2 LakeCentre = new Vector2(812, 826);
        public static readonly Vector2 LakeRadii = new Vector2(190, 105);
        public static readonly Vector3[] Islands = { new Vector3(846, 830, 42), new Vector3(735, 800, 13) };

        // River: x, z, water level. Steep level drops are the waterfalls (NW highland).
        public static readonly Vector3[] RiverIn =
        {
            new Vector3(215, 960, 64), new Vector3(245, 905, 63), new Vector3(258, 884, 52),
            new Vector3(285, 842, 51), new Vector3(300, 822, 41), new Vector3(338, 794, 40),
            new Vector3(366, 778, 32), new Vector3(450, 736, 30.6f), new Vector3(560, 698, 29.2f),
            new Vector3(640, 684, 28f), new Vector3(700, 700, LakeLevel),
        };
        public static readonly Vector3[] RiverOut =
        {
            new Vector3(722, 742, LakeLevel), new Vector3(748, 664, 26.9f), new Vector3(744, 600, 26.6f),
            new Vector3(745, 528, 26.2f), new Vector3(722, 468, 25.8f), new Vector3(684, 425, 25.4f),
            new Vector3(660, 405, 25.2f),
        };
        public static readonly Vector2 Pond = new Vector2(655, 400);
        public const float PondRadius = 26f;
        public const float RiverHalfWidth = 6.5f;
        public static readonly Vector2 Bridge = new Vector2(748, 664);

        // Paths (dirt): polylines between landmarks.
        public static readonly Vector2[][] Paths =
        {
            new[] { new Vector2(838, 300), new Vector2(800, 330), new Vector2(770, 360), new Vector2(700, 380), new Vector2(600, 420), new Vector2(480, 470), new Vector2(380, 520), new Vector2(330, 544) },
            new[] { new Vector2(330, 544), new Vector2(260, 520), new Vector2(200, 480), new Vector2(160, 455) },
            new[] { new Vector2(330, 544), new Vector2(380, 610), new Vector2(470, 650), new Vector2(560, 650), new Vector2(660, 640), new Vector2(748, 664), new Vector2(790, 700) },
            new[] { new Vector2(480, 470), new Vector2(500, 380), new Vector2(470, 280), new Vector2(420, 200) },
            new[] { new Vector2(560, 650), new Vector2(540, 760), new Vector2(500, 850) },
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
            if (x < 330 && z > 800) return Region.Highland;
            // Autumn forest: north band + lake shores
            if (z > 700 && x > 330 && x < 1000 && lake < 1.9f) return Region.AutumnForest;
            if (z > 760 && x > 330) return Region.AutumnForest;
            // Deep green forest: west and south
            float n = Mathf.PerlinNoise(x * 0.004f + 3, z * 0.004f + 7) * 60;
            if (x < 230 + n || z < 200 + n || (x > 900 && z < 640)) return Region.DeepForest;
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
                if (d < best) { best = d; level = Mathf.Lerp(pts[i].z, pts[i + 1].z, t); }
            }
        }

        public static float PathDistance(float x, float z)
        {
            var p = new Vector2(x, z);
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
