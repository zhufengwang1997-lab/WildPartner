using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using WildPartner.World;

namespace WildPartner.EditorTools
{
    /// <summary>
    /// Renders the World scene from fixed viewpoints into Logs/shots/*.png (visual self-check, no Play mode):
    ///   tools/unity.sh WildPartner.EditorTools.SceneShots.Run
    /// </summary>
    public static class SceneShots
    {
        struct View { public string name; public Vector3 pos; public Vector3 look; public float hour; }

        /// <summary>World metres (no design-map scaling), height above the terrain.</summary>
        static Vector3 W(Vector2 xz, float up)
        {
            var t = Terrain.activeTerrain;
            return new Vector3(xz.x, t.SampleHeight(new Vector3(xz.x, 0, xz.y)) + up, xz.y);
        }

        static Vector3 G(float x, float z, float up)
        {
            var t = Terrain.activeTerrain;
            x *= WorldLayout.K; z *= WorldLayout.K;
            return new Vector3(x, t.SampleHeight(new Vector3(x, 0, z)) + up, z);
        }

        public static void Run()
        {
            EditorSceneManager.OpenScene("Assets/_Game/Scenes/World.unity");
            Directory.CreateDirectory("Logs/shots");
            var veg = Object.FindFirstObjectByType<VegetationSpawner>();
            veg.Spawn();
            var env = Object.FindFirstObjectByType<EnvController>();
            RenderSettings.fog = false;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            Shader.SetGlobalVector("_WP_Wind", new Vector4(0.8f, 0.6f, 1, 0));

            var cam = Camera.main;
            var brain = cam.GetComponent<Unity.Cinemachine.CinemachineBrain>();
            if (brain) brain.enabled = false;
            var views = new[]
            {
                new View { name = "01_start", pos = W(WorldLayout.PlayerStart - WorldLayout.CabinFacing * 3.2f, 2.0f), look = W(WorldLayout.PlayerStart + WorldLayout.CabinFacing * 30f, -4f), hour = 16.5f },
                new View { name = "02_cabin", pos = W(WorldLayout.Cabin + WorldLayout.CabinFacing * 95f + new Vector2(12, -30), 4f), look = W(WorldLayout.House, 6f), hour = 16f },
                new View { name = "03_meadow", pos = G(420, 470, 6), look = G(320, 544, 2), hour = 11f },
                new View { name = "04_lake", pos = G(640, 700, 10), look = G(812, 826, 0), hour = 17.5f },
                new View { name = "05_bridge", pos = G(720, 640, 5), look = G(748, 664, 0), hour = 13f },
                new View { name = "06_forest", pos = G(545, 748, 1.8f), look = G(522, 805, 2.4f), hour = 15f },
                new View { name = "07_overview", pos = new Vector3(300, 230, -70), look = new Vector3(300, 30, 320), hour = 12f },
                new View { name = "08_night", pos = W(WorldLayout.RampTop - WorldLayout.CabinFacing * 2f, 2.2f), look = W(WorldLayout.House, 2.5f), hour = 22f },
                new View { name = "09_waterfall", pos = G(345, 768, 7), look = G(258, 884, 3), hour = 12f },
                new View { name = "10_moonflower", pos = G(841, 825, 1.6f), look = G(846, 830, 1.0f), hour = 19.5f },
                new View { name = "11_stones", pos = G(298, 524, 2.5f), look = G(320, 544, 1.5f), hour = 15f },
                new View { name = "13_sky", pos = G(420, 470, 4), look = G(320, 544, 45), hour = 16f },
                new View { name = "12_yard", pos = W(WorldLayout.RampTop - WorldLayout.CabinFacing * 2f, 2.2f), look = W(WorldLayout.House, 2.0f), hour = 15.5f },
            };
            var rt = new RenderTexture(1600, 900, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
            var tex = new Texture2D(1600, 900, TextureFormat.RGB24, false);
            foreach (var v in views)
            {
                env.timeOfDay = v.hour;
                env.Apply();
                cam.transform.position = v.pos;
                cam.transform.rotation = Quaternion.LookRotation(v.look - v.pos);
                cam.targetTexture = rt;
                var grass = Object.FindFirstObjectByType<GrassRenderer>();
                if (grass) { grass.Setup(); grass.Submit(cam, v.pos); }
                cam.Render();
                if (grass) grass.Submit(cam, v.pos);
                cam.Render();
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0);
                tex.Apply();
                RenderTexture.active = null;
                File.WriteAllBytes($"Logs/shots/{v.name}.png", tex.EncodeToPNG());
            }
            cam.targetTexture = null;
            Debug.Log($"[World] shots written: {views.Length}");
        }
    }
}
