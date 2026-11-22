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

        static Vector3 G(float x, float z, float up)
        {
            var t = Terrain.activeTerrain;
            return new Vector3(x, t.SampleHeight(new Vector3(x, 0, z)) + up, z);
        }

        public static void Run()
        {
            EditorSceneManager.OpenScene("Assets/_Game/Scenes/World.unity");
            Directory.CreateDirectory("Logs/shots");
            var veg = Object.FindFirstObjectByType<VegetationSpawner>();
            veg.Spawn();
            var env = Object.FindFirstObjectByType<EnvController>();
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            Shader.SetGlobalVector("_WP_Wind", new Vector4(0.8f, 0.6f, 1, 0));

            var cam = Camera.main;
            var brain = cam.GetComponent<Unity.Cinemachine.CinemachineBrain>();
            if (brain) brain.enabled = false;
            var views = new[]
            {
                new View { name = "01_start", pos = G(845.6f, 301.7f, 3.2f), look = G(840, 305, 1.4f) + new Vector3(-10, 0, 6), hour = 9.5f },
                new View { name = "02_cabin", pos = G(835, 300, 3), look = G(870, 328, 3), hour = 16f },
                new View { name = "03_meadow", pos = G(420, 470, 6), look = G(320, 544, 2), hour = 11f },
                new View { name = "04_lake", pos = G(640, 700, 10), look = G(812, 826, 0), hour = 17.5f },
                new View { name = "05_bridge", pos = G(720, 640, 5), look = G(748, 664, 0), hour = 13f },
                new View { name = "06_forest", pos = G(520, 820, 3), look = G(560, 790, 3), hour = 10f },
                new View { name = "07_overview", pos = new Vector3(512, 360, -120), look = new Vector3(512, 30, 520), hour = 12f },
                new View { name = "08_night", pos = G(842, 300, 2.2f), look = G(866, 324, 3f), hour = 22f },
                new View { name = "09_waterfall", pos = G(345, 768, 7), look = G(258, 884, 3), hour = 12f },
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
                cam.Render();
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
