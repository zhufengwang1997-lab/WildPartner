using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Zfw.QualityTiers;

namespace WildPartner.EditorTools
{
    /// <summary>
    /// Idempotent project configuration as code (adapted from MineEmbers). Re-run any time:
    ///   tools/unity.sh WildPartner.EditorTools.ProjectSetup.Run
    /// Creates four URP assets + TierSettings (the shared QualityTiers package), quality levels and
    /// player settings (Metal on Mac, D3D12 on Windows).
    /// </summary>
    public static class ProjectSetup
    {
        const string TierDir = "Assets/_Game/Settings/Tiers";
        const string BaseAsset = "Assets/Settings/PC_RPAsset.asset";
        const string ResourcesDir = "Assets/Resources";

        struct TierSpec
        {
            public Tier tier; public float renderScale; public int shadowRes; public float shadowDistance;
            public int cascades; public float particles; public bool softParticles;
            public bool bloom; public int bloomIter; public bool fog;
        }

        static readonly TierSpec[] Specs =
        {
            new TierSpec { tier = Tier.Low,    renderScale = 1f, shadowRes = 1024, shadowDistance = 60,  cascades = 1, particles = 0.4f, softParticles = false, bloom = false, bloomIter = 0, fog = true },
            new TierSpec { tier = Tier.Medium, renderScale = 1f, shadowRes = 2048, shadowDistance = 90,  cascades = 2, particles = 0.7f, softParticles = true,  bloom = true,  bloomIter = 3, fog = true },
            new TierSpec { tier = Tier.High,   renderScale = 1f, shadowRes = 4096, shadowDistance = 110, cascades = 4, particles = 1.0f, softParticles = true,  bloom = true,  bloomIter = 5, fog = true },
            new TierSpec { tier = Tier.Ultra,  renderScale = 1f, shadowRes = 4096, shadowDistance = 160, cascades = 4, particles = 1.0f, softParticles = true,  bloom = true,  bloomIter = 6, fog = true },
        };

        [MenuItem("WildPartner/Project Setup")]
        public static void Run()
        {
            ConfigurePlayer();
            var assets = CreateTierPipelines();
            var catalog = CreateTierSettings(assets);
            ConfigureQualityLevels(assets);
            GraphicsSettings.defaultRenderPipeline = assets[(int)Tier.High];
            foreach (var p in new[] { "Assets/Settings/Mobile_RPAsset.asset", "Assets/Settings/Mobile_Renderer.asset" })
                if (File.Exists(p)) AssetDatabase.DeleteAsset(p);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[ProjectSetup] done: 4 tiers, catalog={AssetDatabase.GetAssetPath(catalog)}");
        }

        static void ConfigurePlayer()
        {
            PlayerSettings.companyName = "zfw";
            PlayerSettings.productName = "WildPartner";
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Standalone, "com.zfw.wildpartner");
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.runInBackground = true;
            PlayerSettings.enableFrameTimingStats = true;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultScreenWidth = 1600;
            PlayerSettings.defaultScreenHeight = 900;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneWindows64, new[] { GraphicsDeviceType.Direct3D12 });
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneOSX, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneOSX, new[] { GraphicsDeviceType.Metal });
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, ScriptingImplementation.Mono2x);
        }

        static UniversalRenderPipelineAsset[] CreateTierPipelines()
        {
            Directory.CreateDirectory(TierDir);
            var result = new UniversalRenderPipelineAsset[Specs.Length];
            foreach (var s in Specs)
            {
                string path = $"{TierDir}/URP_{s.tier}.asset";
                if (!File.Exists(path)) AssetDatabase.CopyAsset(BaseAsset, path);
                var asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(path);
                var so = new SerializedObject(asset);
                so.FindProperty("m_RenderScale").floatValue = s.renderScale;
                so.FindProperty("m_MSAA").intValue = 1; // AA comes from the upscaler stage (IN-01)
                so.FindProperty("m_MainLightShadowmapResolution").intValue = s.shadowRes;
                so.FindProperty("m_ShadowDistance").floatValue = s.shadowDistance;
                so.FindProperty("m_ShadowCascadeCount").intValue = s.cascades;
                so.FindProperty("m_RequireDepthTexture").boolValue = true;
                so.FindProperty("m_RequireOpaqueTexture").boolValue = false;
                so.FindProperty("m_SoftShadowQuality").intValue = s.tier == Tier.Low ? 1 : s.tier == Tier.Medium ? 2 : 3;
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(asset);
                result[(int)s.tier] = asset;
            }
            return result;
        }

        static TierCatalog CreateTierSettings(UniversalRenderPipelineAsset[] assets)
        {
            Directory.CreateDirectory(ResourcesDir);
            string catPath = $"{ResourcesDir}/TierCatalog.asset";
            var cat = AssetDatabase.LoadAssetAtPath<TierCatalog>(catPath);
            if (!cat) { cat = ScriptableObject.CreateInstance<TierCatalog>(); AssetDatabase.CreateAsset(cat, catPath); }
            foreach (var s in Specs)
            {
                string path = $"{TierDir}/Tier_{s.tier}.asset";
                var t = AssetDatabase.LoadAssetAtPath<TierSettings>(path);
                if (!t) { t = ScriptableObject.CreateInstance<TierSettings>(); AssetDatabase.CreateAsset(t, path); }
                t.tier = s.tier;
                t.pipeline = assets[(int)s.tier];
                t.maxShadowedPointLights = 2;
                t.shadowResolution = 256;
                t.particleDensity = s.particles;
                t.softParticles = s.softParticles;
                t.bloom = s.bloom;
                t.bloomIterations = s.bloomIter;
                t.fog = s.fog;
                EditorUtility.SetDirty(t);
                switch (s.tier)
                {
                    case Tier.Low: cat.low = t; break;
                    case Tier.Medium: cat.medium = t; break;
                    case Tier.High: cat.high = t; break;
                    default: cat.ultra = t; break;
                }
            }
            EditorUtility.SetDirty(cat);
            return cat;
        }

        static void ConfigureQualityLevels(UniversalRenderPipelineAsset[] assets)
        {
            var qs = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/QualitySettings.asset")[0];
            var so = new SerializedObject(qs);
            var levels = so.FindProperty("m_QualitySettings");
            int proto = levels.arraySize - 1;
            while (levels.arraySize < Specs.Length) levels.InsertArrayElementAtIndex(proto);
            while (levels.arraySize > Specs.Length) levels.DeleteArrayElementAtIndex(0);
            for (int i = 0; i < Specs.Length; i++)
            {
                var e = levels.GetArrayElementAtIndex(i);
                e.FindPropertyRelative("name").stringValue = Specs[i].tier.ToString();
                e.FindPropertyRelative("customRenderPipeline").objectReferenceValue = assets[i];
                e.FindPropertyRelative("vSyncCount").intValue = 0;
                e.FindPropertyRelative("softParticles").boolValue = Specs[i].softParticles;
                e.FindPropertyRelative("lodBias").floatValue = i == 0 ? 0.7f : i == 1 ? 1f : i == 2 ? 1.5f : 2f;
            }
            so.FindProperty("m_CurrentQuality").intValue = (int)Tier.High;
            var perPlatform = so.FindProperty("m_PerPlatformDefaultQuality");
            for (int i = 0; i < perPlatform.arraySize; i++)
                perPlatform.GetArrayElementAtIndex(i).FindPropertyRelative("second").intValue = (int)Tier.High;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
