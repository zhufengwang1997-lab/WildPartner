using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace WildPartner.EditorTools
{
    /// <summary>
    /// Renderer features for the look (画面整改 4), idempotent, run by the world builder:
    ///   * HeightFog — FullScreenPassRendererFeature with WildPartner/HeightFog before post-processing
    ///   * SSAO — applied after opaques so every shader (Toon, terrain, grass) gets contact shadows
    /// All quality tiers share PC_Renderer, so one renderer covers them all.
    /// </summary>
    public static class RenderFeatures
    {
        const string RendererPath = "Assets/Settings/PC_Renderer.asset";

        public static void Ensure()
        {
            var data = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
            if (!data) { Debug.LogError("[Look] renderer data missing"); return; }

            // SSAO: after opaque, stronger, a bit wider so trunks, rocks and the house "sit" on the ground
            var ssao = data.rendererFeatures.FirstOrDefault(f => f && f.GetType().Name == "ScreenSpaceAmbientOcclusion");
            if (ssao)
            {
                var so = new SerializedObject(ssao);
                so.FindProperty("m_Settings.AfterOpaque").boolValue = true;
                so.FindProperty("m_Settings.Intensity").floatValue = 0.9f;
                so.FindProperty("m_Settings.Radius").floatValue = 0.55f;
                so.FindProperty("m_Settings.DirectLightingStrength").floatValue = 0.35f;
                so.FindProperty("m_Settings.Falloff").floatValue = 60f;
                so.ApplyModifiedPropertiesWithoutUndo();
                ssao.SetActive(true);
            }

            var fog = data.rendererFeatures.OfType<FullScreenPassRendererFeature>().FirstOrDefault(f => f.name == "HeightFog");
            if (!fog)
            {
                fog = ScriptableObject.CreateInstance<FullScreenPassRendererFeature>();
                fog.name = "HeightFog";
                AssetDatabase.AddObjectToAsset(fog, data);
                AssetDatabase.TryGetGUIDAndLocalFileIdentifier(fog, out _, out long localId);
                var so = new SerializedObject(data);
                var list = so.FindProperty("m_RendererFeatures");
                list.InsertArrayElementAtIndex(list.arraySize);
                list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = fog;
                var map = so.FindProperty("m_RendererFeatureMap");
                map.InsertArrayElementAtIndex(map.arraySize);
                map.GetArrayElementAtIndex(map.arraySize - 1).longValue = localId;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            string matPath = $"{AssetSetup.MatDir}/M_HeightFog.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (!mat) { mat = new Material(Shader.Find("WildPartner/HeightFog")); AssetDatabase.CreateAsset(mat, matPath); }
            fog.passMaterial = mat;
            fog.passIndex = 0;
            fog.injectionPoint = FullScreenPassRendererFeature.InjectionPoint.BeforeRenderingPostProcessing;
            fog.requirements = ScriptableRenderPassInput.Depth;
            fog.fetchColorBuffer = true;
            fog.SetActive(true);
            EditorUtility.SetDirty(fog);
            EditorUtility.SetDirty(data);
            AssetDatabase.SaveAssets();
            Debug.Log("[Look] renderer features: SSAO after-opaque + HeightFog");
        }
    }
}
