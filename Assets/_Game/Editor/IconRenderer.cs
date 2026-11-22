using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using WildPartner.Creatures;

namespace WildPartner.EditorTools
{
    /// <summary>
    /// Renders a 256x256 portrait (transparent background) of each creature's Visual into
    /// Assets/Resources/Icons/. The journal shows it tinted black as a silhouette until captured.
    /// </summary>
    public static class IconRenderer
    {
        public static void RenderAll()
        {
            Directory.CreateDirectory("Assets/Resources/Icons");
            foreach (var def in CreatureDefs.All)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{AssetSetup.CreaturePrefabs}/{def.id}.prefab");
                if (!prefab) continue;
                Render(prefab, $"Assets/Resources/Icons/{def.model}.png");
            }
            AssetDatabase.Refresh();
        }

        static void Render(GameObject prefab, string path)
        {
            var go = Object.Instantiate(prefab, new Vector3(0, -500, 0), Quaternion.Euler(0, 150, 0));
            foreach (var mb in go.GetComponentsInChildren<MonoBehaviour>()) mb.enabled = false;
            var b = new Bounds(go.transform.position, Vector3.zero);
            foreach (var r in go.GetComponentsInChildren<Renderer>()) b.Encapsulate(r.bounds);

            var camGo = new GameObject("IconCam", typeof(Camera));
            var cam = camGo.GetComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0, 0, 0, 0);
            cam.orthographic = true;
            cam.orthographicSize = Mathf.Max(b.extents.y, b.extents.x) * 1.15f;
            cam.transform.position = b.center + new Vector3(0, 0, -10);
            cam.transform.LookAt(b.center);
            cam.nearClipPlane = 0.1f; cam.farClipPlane = 30;
            var data = camGo.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = false;

            var lightGo = new GameObject("IconLight", typeof(Light));
            var l = lightGo.GetComponent<Light>();
            l.type = LightType.Directional; l.intensity = 1.4f; l.color = new Color(1, 0.96f, 0.9f);
            lightGo.transform.rotation = Quaternion.Euler(35, -30, 0);

            var rt = new RenderTexture(256, 256, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(256, 256, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, 256, 256), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            File.WriteAllBytes(path, tex.EncodeToPNG());

            cam.targetTexture = null;
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(tex);
            Object.DestroyImmediate(camGo);
            Object.DestroyImmediate(lightGo);
            Object.DestroyImmediate(go);
        }
    }
}
