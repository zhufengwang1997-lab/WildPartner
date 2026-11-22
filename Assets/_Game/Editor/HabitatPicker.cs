using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using WildPartner.World;

namespace WildPartner.EditorTools
{
    /// <summary>
    /// 试玩反馈 1123c-B5: picks creature homes and fish-school centres from the built world (terrain heights, slopes,
    /// water, nav mesh) and prints them as C# arrays to paste into CreatureDefs / FishSchools. Deterministic.
    ///     tools/unity.sh WildPartner.EditorTools.HabitatPicker.Run
    /// </summary>
    public static class HabitatPicker
    {
        static Terrain _t;
        static float H(Vector2 p) => _t.SampleHeight(new Vector3(p.x, 0, p.y)) + _t.transform.position.y;
        static float Slope(Vector2 p) => _t.terrainData.GetSteepness(p.x / WorldLayout.Size, p.y / WorldLayout.Size);
        static bool Dry(Vector2 p) => !(WorldLayout.WaterSurface(p.x, p.y, out float l) && l > H(p) - 0.2f);
        static bool OnNav(Vector2 p) => NavMesh.SamplePosition(new Vector3(p.x, H(p), p.y), out var hit, 0.8f, NavMesh.AllAreas);
        static bool Inside(Vector2 p, float m = 26f) => p.x > m && p.y > m && p.x < WorldLayout.Size - m && p.y < WorldLayout.Size - m;
        static string V(Vector2 p) => $"new Vector2({p.x:0.0}f, {p.y:0.0}f)";

        public static void Run()
        {
            EditorSceneManager.OpenScene("Assets/_Game/Scenes/World.unity");
            _t = Terrain.activeTerrain;
            var rng = new System.Random(1123);
            Vector2 R(float lo, float hi) => new Vector2(Mathf.Lerp(lo, hi, (float)rng.NextDouble()), Mathf.Lerp(lo, hi, (float)rng.NextDouble()));

            // ---- rabbits: 5 groups (3,2,3,2,2 = 12) on open meadow, groups >= 45 m apart, one near the cabin ramp ----
            var groups = new List<Vector2> { new Vector2(205f, 128f) };   // the existing first group below the ramp
            for (int i = 0; i < 20000 && groups.Count < 5; i++)
            {
                var p = R(30, 330);
                if (!Inside(p) || WorldLayout.RegionAt(p.x, p.y) != WorldLayout.Region.Meadow || Slope(p) > 14f || !Dry(p) || !OnNav(p)) continue;
                if (WorldLayout.PathDistance(p.x, p.y) < 4f || groups.Any(g => Vector2.Distance(g, p) < 45f)) continue;
                groups.Add(p);
            }
            int[] sizes = { 3, 2, 3, 2, 2 };
            var rabbits = new List<Vector2>();
            for (int g = 0; g < groups.Count; g++)
            {
                int n = 0;
                for (int k = 0; k < 200 && n < sizes[g]; k++)
                {
                    var p = groups[g] + new Vector2((float)rng.NextDouble() - 0.5f, (float)rng.NextDouble() - 0.5f) * 7f;
                    if (Slope(p) > 18f || !Dry(p) || !OnNav(p) || rabbits.Any(r => Vector2.Distance(r, p) < 2f)) continue;
                    rabbits.Add(p); n++;
                }
            }
            Debug.Log($"[Habitat] rabbits ({rabbits.Count}, groups {string.Join(" ", groups.Select(V))}):\n" + string.Join(",\n", rabbits.Select(V)));

            // ---- deer: 3 in the northern forests (z > 190), >= 40 m apart ----
            var deer = new List<Vector2>();
            for (int i = 0; i < 20000 && deer.Count < 3; i++)
            {
                var p = R(30, 330);
                var reg = WorldLayout.RegionAt(p.x, p.y);
                if (p.y < 190f || !Inside(p) || (reg != WorldLayout.Region.DeepForest && reg != WorldLayout.Region.AutumnForest)) continue;
                if (Slope(p) > 18f || !Dry(p) || !OnNav(p) || deer.Any(d => Vector2.Distance(d, p) < 40f)) continue;
                deer.Add(p);
            }
            Debug.Log("[Habitat] deer:\n" + string.Join(",\n", deer.Select(V)));

            // ---- koi + fish: water at least 0.8 m deep, within 5 m of dry bank (visible from the shore) ----
            bool Shallow(Vector2 p, out float depth)
            {
                depth = 0;
                if (!WorldLayout.WaterSurface(p.x, p.y, out float l)) return false;
                depth = l - H(p);
                if (depth < 0.45f) return false;
                for (int a = 0; a < 12; a++)
                {
                    var q = p + new Vector2(Mathf.Cos(a * 0.5236f), Mathf.Sin(a * 0.5236f)) * 5f;
                    if (Dry(q)) return true;
                }
                return false;
            }
            Vector2 Near(Vector2 c, float rad)
            {
                for (int i = 0; i < 4000; i++)
                {
                    var p = c + new Vector2((float)rng.NextDouble() - 0.5f, (float)rng.NextDouble() - 0.5f) * 2f * rad;
                    if (Inside(p, 20f) && Shallow(p, out _)) return p;
                }
                return new Vector2(float.NaN, float.NaN);
            }
            var rin = WorldLayout.RiverIn; var rout = WorldLayout.RiverOut;
            Vector2 RI(int i) => new Vector2(rin[i].x, rin[i].y);   // (x, z, level)
            Vector2 RO(int i) => new Vector2(rout[i].x, rout[i].y);
            var koi = new[]
            {
                Near((RI(6) + RI(7)) * 0.5f, 10f),          // upper river, below the falls
                Near(WorldLayout.Bridge, 6f),                // middle: under the bridge
                Near(RO(rout.Length - 2), 10f),              // lower river
                Near(WorldLayout.LakeCentre + new Vector2(-WorldLayout.LakeRadii.x * 0.2f, -WorldLayout.LakeRadii.y * 0.92f), 8f),   // lake, south shore
            };
            Debug.Log("[Habitat] koi:\n" + string.Join(",\n", koi.Select(V)));

            var fishSeeds = new List<Vector2>();
            for (int i = 6; i < rin.Length - 1; i++) fishSeeds.Add((RI(i) + RI(i + 1)) * 0.5f);
            for (int i = 1; i < rout.Length - 1; i += 1) fishSeeds.Add(RO(i));
            fishSeeds.Add(WorldLayout.Pond);
            for (int a = 0; a < 6; a++)
                fishSeeds.Add(WorldLayout.LakeCentre + new Vector2(Mathf.Cos(a * 1.047f) * WorldLayout.LakeRadii.x, Mathf.Sin(a * 1.047f) * WorldLayout.LakeRadii.y) * 0.88f);
            var fish = new List<Vector2>();
            foreach (var s in fishSeeds)
            {
                var p = Near(s, 6f);
                if (float.IsNaN(p.x) || fish.Any(f => Vector2.Distance(f, p) < 22f) || koi.Any(k => Vector2.Distance(k, p) < 8f)) continue;
                fish.Add(p);
                if (fish.Count == 10) break;
            }
            Debug.Log("[Habitat] fish:\n" + string.Join(",\n", fish.Select(p => { Shallow(p, out float d); return $"{V(p)} depth {d:0.0}"; })));
        }
    }
}
