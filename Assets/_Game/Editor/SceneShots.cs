using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using WildPartner.Creatures;
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

        /// <summary>Keeps a close-up's original camera distance in metres on the shrunk map (the landmark model
        /// did not shrink): moves pos away from look by 1/F, then re-grounds it.</summary>
        static Vector3 Far(Vector3 pos, Vector3 look)
        {
            var p = look + (pos - look) / WorldLayout.F;
            var t = Terrain.activeTerrain;
            p.y = Mathf.Max(p.y, t.SampleHeight(p) + (pos.y - t.SampleHeight(pos)));
            return p;
        }

        static Vector3 G(float x, float z, float up)
        {
            var t = Terrain.activeTerrain;
            x *= WorldLayout.K; z *= WorldLayout.K;
            return new Vector3(x, t.SampleHeight(new Vector3(x, 0, z)) + up, z);
        }

        /// <summary>Close-ups of the rigged cat's clips sampled at chosen times, in the real scene lighting
        /// (animation check for 画面整改 8 without entering Play mode).</summary>
        static void CatPoses(Camera cam, EnvController env, RenderTexture rt, Texture2D tex)
        {
            PoseShots(cam, env, rt, tex, "cat", W(WorldLayout.PlayerStart + WorldLayout.CabinFacing * 1.6f, 0f),   // on the dirt path, not in tall grass
                      new Vector3(-WorldLayout.CabinFacing.y, 0, WorldLayout.CabinFacing.x), 1f,
                      new[] { ("Idle", 2.1f), ("Walk", 0.2f), ("Run", 0.13f), ("SitLick", 0.5f), ("SitLick", 1.2f), ("SitLick", 3.2f), ("BellyUp", 1.0f), ("Rub", 0.45f), ("Point", 0.5f) });
            var deer = CreatureDefs.Get("white_deer");
            PoseShots(cam, env, rt, tex, "white_deer", W(deer.anchors[0], 0f), new Vector3(1, 0, 0.3f), 2.3f,
                      new[] { ("Idle", 1.5f), ("Walk", 0.3f), ("Run", 0.2f), ("GrazeDown", 0.6f), ("Graze", 0.4f), ("Graze", 1.1f) });
            var rabbit = CreatureDefs.Get("moon_rabbit");
            PoseShots(cam, env, rt, tex, "moon_rabbit", W(rabbit.anchors[0], 0f), new Vector3(1, 0, 0.3f), 1.0f,
                      new[] { ("Idle", 0.7f), ("Idle", 1.6f), ("Walk", 0.12f), ("Walk", 0.25f), ("Run", 0.2f), ("Run", 0.36f) });
            PoseShots(cam, env, rt, tex, "moon_butterfly", W(WorldLayout.PlayerStart + WorldLayout.CabinFacing * 1.6f, 0f) + Vector3.up * 0.9f,
                      new Vector3(1, 0, 0.6f), 0.9f, new[] { ("Idle", 0.0f), ("Idle", 0.12f), ("Run", 0.1f) });
            PoseShots(cam, env, rt, tex, "ink_koi", W(WorldLayout.PlayerStart + WorldLayout.CabinFacing * 1.6f, 0f) + Vector3.up * 0.3f,
                      new Vector3(1, 0, 0.6f), 0.8f, new[] { ("Walk", 0.0f), ("Walk", 0.3f), ("Run", 0.2f) });
            PoseShots(cam, env, rt, tex, "witch", W(WorldLayout.PlayerStart + WorldLayout.CabinFacing * 1.6f, 0f),
                      new Vector3(-WorldLayout.CabinFacing.y, 0, WorldLayout.CabinFacing.x), 2.6f,
                      new[] { ("Idle", 1.0f), ("Walk", 0.3f), ("SneakIdle", 1.0f), ("Sneak", 0.4f), ("Sneak", 0.9f), ("Capture", 0.5f) });
        }

        /// <summary>Witch, cat and rabbit standing in meadow grass (试玩反馈 5: the grass should half-hide the small
        /// animals but not the witch's legs) -> Logs/shots/14_grass_pets.png</summary>
        static void GrassPets(Camera cam, EnvController env, RenderTexture rt, Texture2D tex)
        {
            var spot = new Vector2(400, 470) * WorldLayout.K;
            var fwd = new Vector3(-0.8f, 0, 0.6f).normalized;
            var right = Vector3.Cross(Vector3.up, fwd);
            var witchAt = W(spot, 0);
            var cast = new System.Collections.Generic.List<GameObject>();
            var pushers = new Vector4[6];
            void Put(string id, Vector3 at, Vector3 face, string clipName, float time, int slot)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(id == "witch" ? "Assets/_Game/Prefabs/Player.prefab" : $"Assets/_Game/Prefabs/Creatures/{id}.prefab");
                if (!prefab) return;
                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                go.transform.SetPositionAndRotation(at, Quaternion.LookRotation(face));
                var anim = go.GetComponentInChildren<Animator>();
                var clip = anim ? System.Array.Find(anim.runtimeAnimatorController.animationClips, c => c.name == clipName) : null;
                if (clip) clip.SampleAnimation(anim.gameObject, time);
                foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>())
                {
                    var m = new Mesh(); smr.BakeMesh(m, true);
                    var b = new GameObject("baked", typeof(MeshFilter), typeof(MeshRenderer));
                    b.transform.SetPositionAndRotation(smr.transform.position, smr.transform.rotation);
                    b.GetComponent<MeshFilter>().sharedMesh = m;
                    b.GetComponent<MeshRenderer>().sharedMaterials = smr.sharedMaterials;
                    smr.enabled = false; cast.Add(b);
                }
                cast.Add(go);
                if (slot >= 0) pushers[slot] = new Vector4(at.x, at.y, at.z, 0.75f);
                else Shader.SetGlobalVector("_WP_PlayerPos", new Vector4(at.x, at.y, at.z, 0.9f));
            }
            Put("witch", witchAt, fwd, "Idle", 1f, -1);
            Put("cat", W(spot + new Vector2(right.x, right.z) * 1.1f + new Vector2(fwd.x, fwd.z) * 0.6f, 0), fwd - right * 0.6f, "Idle", 2.1f, 0);
            Put("moon_rabbit", W(spot - new Vector2(right.x, right.z) * 1.2f + new Vector2(fwd.x, fwd.z) * 1.3f, 0), right, "Idle", 0.7f, 1);
            Shader.SetGlobalVectorArray("_WP_GrassPushers", pushers);
            env.timeOfDay = 10.5f; env.Apply();
            var pos = witchAt - fwd * 3.6f + right * 0.6f + Vector3.up * 1.5f;
            cam.transform.position = pos;
            cam.transform.rotation = Quaternion.LookRotation(witchAt + fwd * 1.0f + Vector3.up * 0.35f - pos);
            cam.targetTexture = rt;
            var grass = Object.FindFirstObjectByType<GrassRenderer>();
            if (grass) { grass.Setup(); grass.Submit(cam, pos); }
            cam.Render();
            if (grass) grass.Submit(cam, pos);
            cam.Render();
            RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0); tex.Apply();
            RenderTexture.active = null;
            File.WriteAllBytes("Logs/shots/14_grass_pets.png", tex.EncodeToPNG());
            foreach (var go in cast) Object.DestroyImmediate(go);
            Shader.SetGlobalVectorArray("_WP_GrassPushers", new Vector4[6]);
            Shader.SetGlobalVector("_WP_PlayerPos", new Vector4(0, -999, 0, 0.9f));
        }

        /// <summary>Close-ups of a rigged creature prefab sampled at given clip times -> Logs/shots/&lt;id&gt;_&lt;clip&gt;.png</summary>
        static void PoseShots(Camera cam, EnvController env, RenderTexture rt, Texture2D tex, string id, Vector3 at, Vector3 facing, float k,
                              (string, float)[] poses)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(id == "witch" ? "Assets/_Game/Prefabs/Player.prefab" : $"Assets/_Game/Prefabs/Creatures/{id}.prefab");
            if (!prefab) return;
            var cat = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            var anim = cat.GetComponentInChildren<Animator>();
            if (!anim) { Object.DestroyImmediate(cat); return; }
            var clips = anim.runtimeAnimatorController.animationClips;
            cat.transform.SetPositionAndRotation(at, Quaternion.LookRotation(facing));
            env.timeOfDay = 16.3f; env.Apply();
            int shot = 0;
            foreach (var (clipName, time) in poses)
            {
                shot++;
                var clip = System.Array.Find(clips, c => c.name == clipName);
                if (!clip) continue;
                clip.SampleAnimation(anim.gameObject, time);
                // batch mode does not skin before an editor-side Render: bake the posed mesh into a stand-in
                var baked = new System.Collections.Generic.List<GameObject>();
                foreach (var smr in cat.GetComponentsInChildren<SkinnedMeshRenderer>())
                {
                    var m = new Mesh(); smr.BakeMesh(m, true);
                    var go = new GameObject("baked", typeof(MeshFilter), typeof(MeshRenderer));
                    go.transform.SetPositionAndRotation(smr.transform.position, smr.transform.rotation);
                    go.GetComponent<MeshFilter>().sharedMesh = m;
                    go.GetComponent<MeshRenderer>().sharedMaterials = smr.sharedMaterials;
                    smr.enabled = false; baked.Add(go);
                }
                var right = cat.transform.right;
                var pos = at + (right * 0.95f + cat.transform.forward * 0.9f + Vector3.up * 0.42f) * k;
                cam.transform.position = pos;
                cam.transform.rotation = Quaternion.LookRotation(at + Vector3.up * 0.22f * k - pos);
                cam.targetTexture = rt;
                // animation check: no grass in front of the paws and no near-camera dither on the model
                Shader.SetGlobalVector("_WP_NearFade", Vector4.zero);
                cam.Render();
                cam.Render();
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0); tex.Apply();
                RenderTexture.active = null;
                File.WriteAllBytes($"Logs/shots/{id}_{shot}_{clipName}.png", tex.EncodeToPNG());
                foreach (var go in baked) Object.DestroyImmediate(go);
                foreach (var smr in cat.GetComponentsInChildren<SkinnedMeshRenderer>()) smr.enabled = true;
            }
            Object.DestroyImmediate(cat);
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
                new View { name = "01_start", pos = W(WorldLayout.PlayerStart - WorldLayout.CabinFacing * 3.4f, 2.1f), look = W(WorldLayout.PlayerStart + WorldLayout.CabinFacing * 30f, -0.5f), hour = 16.5f },
                new View { name = "02_cabin", pos = W(WorldLayout.Cabin + WorldLayout.CabinFacing * 95f + new Vector2(12, -30), 4f), look = W(WorldLayout.House, 6f), hour = 16f },
                new View { name = "03_meadow", pos = G(420, 470, 6), look = G(320, 544, 2), hour = 11f },
                new View { name = "04_lake", pos = G(640, 700, 10), look = G(812, 826, 0), hour = 17.5f },
                new View { name = "05_bridge", pos = Far(G(720, 640, 5), G(748, 664, 0)), look = G(748, 664, 0), hour = 13f },
                new View { name = "06_forest", pos = G(545, 748, 1.8f), look = G(522, 805, 2.4f), hour = 15f },
                new View { name = "07_overview", pos = new Vector3(180, 150, -50), look = new Vector3(180, 30, 195), hour = 12f },
                new View { name = "08_night", pos = W(WorldLayout.RampTop - WorldLayout.CabinFacing * 2f, 2.2f), look = W(WorldLayout.House, 2.5f), hour = 22f },
                new View { name = "09_waterfall", pos = G(345, 768, 7), look = G(258, 884, 3), hour = 12f },
                new View { name = "10_moonflower", pos = G(841, 825, 1.6f), look = G(846, 830, 1.0f), hour = 19.5f },
                new View { name = "11_stones", pos = Far(G(298, 524, 2.5f), G(320, 544, 1.5f)), look = G(320, 544, 1.5f), hour = 15f },
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
                // which plants stand closest in front of the lens (helps tune viewpoints and clearings)
                var near = new System.Text.StringBuilder();
                foreach (Transform grp in veg.transform)
                    foreach (Transform it in grp)
                    {
                        var d = it.position - v.pos; d.y = 0;
                        if (d.magnitude < 22f && Vector3.Dot(d.normalized, cam.transform.forward) > 0.5f && !grp.name.StartsWith("Grass") && !grp.name.StartsWith("Flowers"))
                            near.Append($" {grp.name}@{d.magnitude:0.0}m");
                    }
                Debug.Log($"[Shots] {v.name} near props:{near}");
            }
            GrassPets(cam, env, rt, tex);
            CatPoses(cam, env, rt, tex);
            cam.targetTexture = null;
            Debug.Log($"[World] shots written: {views.Length}");
        }
    }
}
