using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using WildPartner.Creatures;
using WildPartner.Player;

namespace WildPartner.EditorTools
{
    /// <summary>
    /// Builds materials and prefabs from the imported models (idempotent):
    ///   tools/unity.sh WildPartner.EditorTools.AssetSetup.Run
    /// Every creature/player prefab = logic on the root + model under a child named "Visual", so a new
    /// model can replace the Visual without touching code (PRD 4.2). Missing models get a magenta
    /// placeholder capsule so it is obvious what still needs replacing.
    /// </summary>
    public static class AssetSetup
    {
        public const string Gen = "Assets/_Game/Models/Generated";
        public const string Chars = "Assets/_Game/Models/Characters";
        public const string Props = "Assets/_Game/Models/Props";
        public const string MatDir = "Assets/_Game/Materials";
        public const string EnvPrefabs = "Assets/_Game/Prefabs/Env";
        public const string CreaturePrefabs = "Assets/_Game/Prefabs/Creatures";

        static Shader Toon => Shader.Find("WildPartner/Toon");

        [MenuItem("WildPartner/Asset Setup")]
        public static void Run()
        {
            Directory.CreateDirectory(MatDir);
            Directory.CreateDirectory(EnvPrefabs);
            Directory.CreateDirectory(CreaturePrefabs);
            Directory.CreateDirectory("Assets/Resources/Icons");
            AssetDatabase.Refresh();

            var palette = AssetDatabase.LoadAssetAtPath<Texture2D>($"{Gen}/Palette.png");
            var world = ToonMat("M_World", palette);
            var foliage = ToonMat("M_Foliage", palette, m => { m.EnableKeyword("_WIND"); m.SetFloat("_Wind", 1); m.SetFloat("_WindStrength", 0.22f); m.SetFloat("_Translucency", 0.35f); m.SetColor("_RimColor", new Color(1, 0.95f, 0.75f, 0.25f)); });
            var grass = ToonMat("M_Grass", palette, m => { m.EnableKeyword("_WIND"); m.SetFloat("_Wind", 1); m.SetFloat("_WindStrength", 0.12f); m.SetFloat("_WindFrequency", 1.8f); m.SetFloat("_Cull", 0); m.SetFloat("_RampSteps", 2); m.SetFloat("_Translucency", 0.3f); m.SetFloat("_ShadowStrength", 0.6f); m.SetFloat("_UpNormal", 1f); });
            var banner = ToonMat("M_Banner", palette, m => { m.EnableKeyword("_WIND"); m.SetFloat("_Wind", 1); m.SetFloat("_WindStrength", 0.35f); m.SetFloat("_WindFrequency", 2.5f); m.SetFloat("_Cull", 0); });
            var glow = ToonMat("M_Glow", palette, m => m.SetFloat("_Emission", 2.5f));
            var cloud = ToonMat("M_Cloud", palette, m => { m.SetColor("_ShadowColor", new Color(0.75f, 0.78f, 0.92f)); m.SetFloat("_RampSteps", 2); m.SetFloat("_ShadowStrength", 0); m.SetFloat("_Emission", 0.25f); });
            var placeholder = ToonMat("M_Placeholder", null, m => { m.SetColor("_BaseColor", new Color(1f, 0.1f, 0.8f)); m.SetFloat("_Emission", 0.3f); });

            var water = WaterMat("M_Water_Lake", 4f, 0.05f, 0.1f, 0.15f);
            WaterMat("M_Water_River", 2f, 0.9f, 0.35f, 0.08f);
            WaterMat("M_Waterfall", 1.5f, 3.5f, 0.75f, 0.0f);
            var sky = new Material(Shader.Find("WildPartner/Sky"));
            SaveMat(sky, "M_Sky");

            // ---- environment prefabs -------------------------------------------------------------
            var windAssets = new HashSet<string> { "Tree_Broadleaf_A", "Tree_Broadleaf_B", "Tree_Autumn_A", "Tree_Autumn_B", "Tree_Birch_A", "Tree_Pine_A", "Tree_Pine_B", "Bush_A", "Bush_B" };
            var grassAssets = new HashSet<string> { "Grass_A", "Grass_B", "Flowers_White", "Flowers_Orange", "Flowers_Purple" };
            foreach (var path in Directory.GetFiles(Gen, "*.fbx"))
            {
                string name = Path.GetFileNameWithoutExtension(path);
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                var mat = windAssets.Contains(name) ? foliage : grassAssets.Contains(name) ? grass : name == "FlagPole" ? banner :
                          name == "Cloud_A" ? cloud : world;
                var go = (GameObject)PrefabUtility.InstantiatePrefab(model);
                foreach (var r in go.GetComponentsInChildren<MeshRenderer>())
                {
                    r.sharedMaterial = mat;
                    if (grassAssets.Contains(name) || name == "Mushrooms") r.shadowCastingMode = ShadowCastingMode.Off;
                    if (name == "SnowMountains" || name == "Cloud_A") { r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false; }
                }
                ConfigureLods(go, name);
                AddColliders(go, name);
                if (name == "LampPost") AddLamp(go, new Vector3(0.7f, 2.7f, 0), 7f);
                PrefabUtility.SaveAsPrefabAsset(go, $"{EnvPrefabs}/{name}.prefab");
                Object.DestroyImmediate(go);
            }

            // Tripo props (door/window/furniture) with their own textures
            foreach (var path in Directory.GetFiles(Props, "*.fbx"))
            {
                string name = Path.GetFileNameWithoutExtension(path);
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>($"{Props}/{name}_BaseColor.png");
                var mat = ToonMat("M_" + name, tex, m => { m.SetFloat("_RampSteps", 3); if (name == "WallLamp" || name == "Fireplace") m.SetFloat("_Emission", 0.15f); });
                var go = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(path));
                foreach (var r in go.GetComponentsInChildren<MeshRenderer>()) r.sharedMaterial = mat;
                PrefabUtility.SaveAsPrefabAsset(go, $"{EnvPrefabs}/{name}.prefab");
                Object.DestroyImmediate(go);
            }

            // ---- creatures + player --------------------------------------------------------------
            foreach (var def in CreatureDefs.All) BuildCreature(def, placeholder);
            BuildPlayer(placeholder);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            IconRenderer.RenderAll();
            Debug.Log("[Setup] materials + prefabs done");
        }

        static Material ToonMat(string name, Texture tex, System.Action<Material> cfg = null)
        {
            string path = $"{MatDir}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!m) { m = new Material(Toon); AssetDatabase.CreateAsset(m, path); }
            m.shader = Toon;
            m.SetTexture("_BaseMap", tex);
            m.SetColor("_BaseColor", Color.white);
            cfg?.Invoke(m);
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }

        static Material WaterMat(string name, float depth, float flow, float streak, float wave)
        {
            string path = $"{MatDir}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!m) { m = new Material(Shader.Find("WildPartner/Water")); AssetDatabase.CreateAsset(m, path); }
            m.SetFloat("_DepthRange", depth);
            m.SetFloat("_FlowSpeed", flow);
            m.SetFloat("_StreakAmount", streak);
            m.SetFloat("_WaveScale", wave);
            EditorUtility.SetDirty(m);
            return m;
        }

        static void SaveMat(Material m, string name)
        {
            string path = $"{MatDir}/{name}.mat";
            if (File.Exists(path)) AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(m, path);
        }

        static void ConfigureLods(GameObject go, string name)
        {
            var lg = go.GetComponent<LODGroup>();
            var r0 = FindLod(go, name, 0);
            if (!r0) return;
            if (!lg) lg = go.AddComponent<LODGroup>();
            var list = new List<LOD>();
            float[] heights = name.StartsWith("Tree") ? new[] { 0.22f, 0.07f, 0.012f }
                            : name.StartsWith("Bush") ? new[] { 0.12f, 0.025f }
                            : name.StartsWith("Cliff") || name == "Watchtower" || name == "Cabin" ? new[] { 0.08f, 0.004f }
                            : name.StartsWith("Rock") || name.StartsWith("StandingStone") ? new[] { 0.06f, 0.012f }
                            : name.StartsWith("Grass") || name.StartsWith("Flowers") || name == "Mushrooms" ? new[] { 0.035f }
                            : name == "SnowMountains" || name == "Cloud_A" ? new[] { 0.0001f }
                            : new[] { 0.01f };
            for (int i = 0; i < heights.Length; i++)
            {
                var r = FindLod(go, name, i);
                if (!r) break;
                list.Add(new LOD(heights[i], new Renderer[] { r }));
            }
            lg.SetLODs(list.ToArray());
            lg.fadeMode = LODFadeMode.None;
            lg.RecalculateBounds();
        }

        static Renderer FindLod(GameObject go, string name, int i)
        {
            foreach (var r in go.GetComponentsInChildren<MeshRenderer>(true))
                if (r.name.EndsWith($"_LOD{i}")) return r;
            return null;
        }

        static void AddColliders(GameObject go, string name)
        {
            if (name.StartsWith("Tree"))
            {
                var c = go.AddComponent<CapsuleCollider>();
                c.radius = 0.45f; c.height = 5f; c.center = new Vector3(0, 2.5f, 0);
            }
            else if (name.StartsWith("Rock") || name.StartsWith("Cliff") || name.StartsWith("StandingStone") || name == "Cabin" ||
                     name == "Watchtower" || name == "Bridge" || name == "Altar" || name == "Campfire" || name == "Crate" ||
                     name == "Fence" || name == "LogBench")
            {
                var lod0 = FindLod(go, name, 0);
                var mf = lod0 ? lod0.GetComponent<MeshFilter>() : null;
                if (mf)
                {
                    var mc = lod0.gameObject.AddComponent<MeshCollider>();
                    mc.sharedMesh = mf.sharedMesh;
                    mc.convex = name.StartsWith("Rock") || name == "Crate";
                }
            }
            else if (name == "LampPost" || name == "FlagPole")
            {
                var c = go.AddComponent<CapsuleCollider>();
                c.radius = 0.15f; c.height = 3f; c.center = new Vector3(0, 1.5f, 0);
            }
        }

        static void AddLamp(GameObject go, Vector3 pos, float range)
        {
            var l = new GameObject("Light", typeof(Light)).GetComponent<Light>();
            l.transform.SetParent(go.transform, false);
            l.transform.localPosition = pos;
            l.type = LightType.Point; l.range = range; l.intensity = 2.5f;
            l.color = new Color(1f, 0.75f, 0.45f);
            l.shadows = LightShadows.None;
            l.gameObject.AddComponent<World.NightLight>();
        }

        // ------------------------------------------------------------------------------------------
        static GameObject Visual(string model, string texName, Material placeholder, out bool isPlaceholder, System.Action<Material> cfg = null)
        {
            string fbx = $"{Chars}/{model}.fbx";
            if (!File.Exists(fbx)) fbx = $"{Gen}/{model}.fbx";
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(fbx);
            GameObject v;
            if (asset)
            {
                v = (GameObject)PrefabUtility.InstantiatePrefab(asset);
                PrefabUtility.UnpackPrefabInstance(v, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>($"{Chars}/{texName}_BaseColor.png");
                Material mat;
                if (tex) mat = ToonMat("M_" + model, tex, m => { m.SetFloat("_RampSteps", 3); m.SetColor("_RimColor", new Color(1, 0.95f, 0.9f, 0.35f)); cfg?.Invoke(m); });
                else mat = AssetDatabase.LoadAssetAtPath<Material>($"{MatDir}/M_World.mat");
                foreach (var r in v.GetComponentsInChildren<MeshRenderer>()) r.sharedMaterial = mat;
                isPlaceholder = false;
            }
            else
            {
                v = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                Object.DestroyImmediate(v.GetComponent<Collider>());
                v.GetComponent<MeshRenderer>().sharedMaterial = placeholder;
                v.transform.localScale = new Vector3(0.5f, 0.4f, 0.5f);
                v.transform.localPosition = new Vector3(0, 0.4f, 0);
                isPlaceholder = true;
            }
            v.name = "Visual";
            // Tripo + generated models face -Z after FBX import; game convention is +Z forward.
            if (!isPlaceholder) v.transform.localRotation = Quaternion.Euler(0, 180, 0);
            return v;
        }

        static void BuildCreature(CreatureDef def, Material placeholder)
        {
            var root = new GameObject(def.id);
            var vis = Visual(def.model, def.model, placeholder, out bool ph, m =>
            {
                if (def.id == "moon_butterfly") m.SetFloat("_Emission", 0.3f);
            });
            vis.transform.SetParent(root.transform, false);
            var c = root.AddComponent<Creature>();
            c.defId = def.id;
            var anim = root.AddComponent<ProceduralAnimator>();
            anim.style = def.anim;
            anim.scale = def.animScale;
            anim.visual = vis.transform;
            anim.stepLength = def.id == "white_deer" ? 1.6f : def.id == "cat" ? 0.3f : 0.6f;
            var col = root.AddComponent<SphereCollider>();
            col.isTrigger = true; col.radius = 0.6f; col.center = new Vector3(0, 0.4f, 0);
            if (def.movement == Movement.Ground)
            {
                var agent = root.AddComponent<NavMeshAgent>();
                agent.radius = def.id == "white_deer" ? 0.5f : def.id == "cat" ? 0.2f : 0.3f;
                agent.height = def.id == "white_deer" ? 1.6f : def.id == "cat" ? 0.45f : 0.6f;
                agent.speed = def.wanderSpeed;
                agent.angularSpeed = 400;
                agent.acceleration = 14;
                agent.autoBraking = true;
                agent.enabled = false; // enabled after placing on the NavMesh
            }
            if (def.id == "moon_butterfly")
            {
                var l = new GameObject("Glow", typeof(Light)).GetComponent<Light>();
                l.transform.SetParent(root.transform, false);
                l.transform.localPosition = new Vector3(0, 0.3f, 0);
                l.type = LightType.Point; l.range = 4; l.intensity = 1.5f; l.color = new Color(0.6f, 0.7f, 1f);
                l.shadows = LightShadows.None;
            }
            PrefabUtility.SaveAsPrefabAsset(root, $"{CreaturePrefabs}/{def.id}.prefab");
            Object.DestroyImmediate(root);
            if (ph) Debug.LogWarning($"[Setup] {def.id}: model {def.model} missing -> placeholder capsule");
        }

        public const string Rigged = "Assets/_Game/Models/Rigged";
        public const string AnimDir = "Assets/_Game/Animation";

        /// <summary>Blender-rigged model (tools/blender/rig_*.py) as "Visual" with an Animator driven by "Speed"
        /// (Idle 0 / Walk 1.64 / Run 3.2 m/s). Null when the rigged FBX does not exist yet.</summary>
        static GameObject RiggedVisual(string model, string texName)
        {
            string fbx = $"{Rigged}/{model}.fbx";
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(fbx);
            if (!asset) return null;
            var v = (GameObject)PrefabUtility.InstantiatePrefab(asset);
            PrefabUtility.UnpackPrefabInstance(v, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            v.name = "Visual";
            v.transform.localRotation = Quaternion.Euler(0, 180, 0);
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>($"{Chars}/{texName}_BaseColor.png");
            var mat = ToonMat("M_" + model, tex, m => { m.SetFloat("_RampSteps", 3); m.SetColor("_RimColor", new Color(1, 0.95f, 0.9f, 0.35f)); });
            foreach (var r in v.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                r.sharedMaterial = mat;
                r.updateWhenOffscreen = false;
                r.skinnedMotionVectors = true;
            }
            var animator = v.GetComponent<Animator>() ?? v.AddComponent<Animator>();
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
            animator.runtimeAnimatorController = LocomotionController(model, fbx);
            return v;
        }

        [System.Serializable] public class Speeds { public float Walk = 1f, Run = 3.2f; }
        public static Speeds ClipSpeeds(string model)
        {
            string p = $"{Rigged}/{model}_speeds.json";
            return File.Exists(p) ? JsonUtility.FromJson<Speeds>(File.ReadAllText(p)) : new Speeds();
        }

        static UnityEditor.Animations.AnimatorController LocomotionController(string model, string fbx)
        {
            Directory.CreateDirectory(AnimDir);
            string path = $"{AnimDir}/{model}.controller";
            AssetDatabase.DeleteAsset(path);
            var ctrl = UnityEditor.Animations.AnimatorController.CreateAnimatorControllerAtPath(path);
            ctrl.AddParameter("Speed", AnimatorControllerParameterType.Float);
            AnimationClip Clip(string n)
            {
                foreach (var o in AssetDatabase.LoadAllAssetsAtPath(fbx))
                    if (o is AnimationClip c && c.name == n) return c;
                Debug.LogWarning($"[Setup] {model}: clip {n} missing");
                return null;
            }
            var sm = ctrl.layers[0].stateMachine;
            var state = ctrl.CreateBlendTreeInController("Locomotion", out var tree, 0);
            tree.blendType = UnityEditor.Animations.BlendTreeType.Simple1D;
            tree.blendParameter = "Speed";
            tree.useAutomaticThresholds = false;
            tree.AddChild(Clip("Idle"), 0f);
            var sp = ClipSpeeds(model);
            tree.AddChild(Clip("Walk"), sp.Walk);   // thresholds = the clips' own ground speeds -> no foot skating
            tree.AddChild(Clip("Run"), sp.Run);
            sm.defaultState = state;
            EditorUtility.SetDirty(ctrl);
            return ctrl;
        }

        static void BuildPlayer(Material placeholder)
        {
            var root = new GameObject("Player");
            root.tag = "Player";
            var vis = RiggedVisual("Witch", "Witch") ?? Visual("Witch", "Witch", placeholder, out _);
            bool rigged = vis.GetComponentInChildren<Animator>() != null;
            vis.transform.SetParent(root.transform, false);
            var cc = root.AddComponent<CharacterController>();
            cc.height = 1.6f; cc.radius = 0.35f; cc.center = new Vector3(0, 0.82f, 0); cc.stepOffset = 0.45f; cc.slopeLimit = 50;
            var pc = root.AddComponent<PlayerController>();
            pc.visual = vis.transform;
            if (rigged) pc.animRunSpeed = ClipSpeeds("Witch").Run;
            if (!rigged)   // fallback when there is no Blender-rigged model yet
            {
                var anim = root.AddComponent<ProceduralAnimator>();
                anim.style = ProceduralAnimator.Style.Walker;
                anim.visual = vis.transform;
                anim.stepLength = 0.75f;
            }
            var target = new GameObject("CameraTarget");
            target.transform.SetParent(root.transform, false);
            target.transform.localPosition = new Vector3(0, 1.45f, 0);
            root.AddComponent<CaptureSystem>();
            PrefabUtility.SaveAsPrefabAsset(root, "Assets/_Game/Prefabs/Player.prefab");
            Object.DestroyImmediate(root);
        }
    }
}
