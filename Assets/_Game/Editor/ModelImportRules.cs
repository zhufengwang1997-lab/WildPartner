using UnityEditor;
using UnityEngine;

namespace WildPartner.EditorTools
{
    /// <summary>Import rules for everything under Assets/_Game/Models (materials are created by AssetSetup).</summary>
    public class ModelImportRules : AssetPostprocessor
    {
        void OnPreprocessModel()
        {
            if (!assetPath.StartsWith("Assets/_Game/Models")) return;
            var m = (ModelImporter)assetImporter;
            m.materialImportMode = ModelImporterMaterialImportMode.None;
            m.importBlendShapes = false;
            m.importCameras = false;
            m.importLights = false;
            bool rigged = assetPath.Contains("/Rigged/");
            m.importAnimation = rigged;
            m.animationType = rigged ? ModelImporterAnimationType.Generic : ModelImporterAnimationType.None;
            if (rigged) m.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            m.importNormals = ModelImporterNormals.Import;
            m.bakeAxisConversion = true;
            m.useFileScale = true;
            m.globalScale = 1f;
            m.isReadable = assetPath.Contains("/Generated/");   // world builder reads cliff/rock meshes for colliders
            m.meshCompression = ModelImporterMeshCompression.Off;
            m.optimizeMeshPolygons = true;
            m.optimizeMeshVertices = true;
        }

        // Blender FBX exports leave a 90 degree X rotation on mesh nodes. Placement code sets rotations on
        // prefab roots, which would wipe it out (bridge on its side, characters lying down), so bake every
        // mesh node's local rotation into its vertices and reset the node to identity.
        void OnPostprocessModel(GameObject root)
        {
            if (!assetPath.StartsWith("Assets/_Game/Models")) return;
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
            {
                var t = mf.transform;
                if (t.childCount > 0 || t.localRotation == Quaternion.identity || !mf.sharedMesh) continue;
                var q = t.localRotation;
                var mesh = mf.sharedMesh;
                var v = mesh.vertices; var n = mesh.normals; var tg = mesh.tangents;
                for (int i = 0; i < v.Length; i++) v[i] = q * v[i];
                for (int i = 0; i < n.Length; i++) n[i] = q * n[i];
                for (int i = 0; i < tg.Length; i++) { var d = q * new Vector3(tg[i].x, tg[i].y, tg[i].z); tg[i] = new Vector4(d.x, d.y, d.z, tg[i].w); }
                mesh.vertices = v;
                if (n.Length > 0) mesh.normals = n;
                if (tg.Length > 0) mesh.tangents = tg;
                mesh.RecalculateBounds();
                t.localRotation = Quaternion.identity;
            }
        }

        // Rigged characters (tools/blender/rig_*.py): clips are named "<Rig>|<Action>" -> "<Action>"; loops loop.
        void OnPreprocessAnimation()
        {
            if (!assetPath.Contains("/_Game/Models/Rigged/")) return;
            var m = (ModelImporter)assetImporter;
            var clips = m.defaultClipAnimations;
            foreach (var c in clips)
            {
                int bar = c.name.LastIndexOf('|');
                if (bar >= 0) c.name = c.name.Substring(bar + 1);
                c.loopTime = LoopClips.Contains(c.name);
                c.lockRootRotation = c.lockRootHeightY = c.lockRootPositionXZ = false;
            }
            // foot-contact events (AssetSetup.BakeStepEvents writes <model>_steps.json next to the FBX)
            string stepsPath = assetPath.Substring(0, assetPath.Length - 4) + "_steps.json";
            if (System.IO.File.Exists(stepsPath))
            {
                var steps = JsonUtility.FromJson<StepTable>(System.IO.File.ReadAllText(stepsPath));
                foreach (var c in clips)
                {
                    var entry = steps.clips.Find(e => e.clip == c.name);
                    if (entry == null) continue;
                    c.events = entry.times.ConvertAll(t => new AnimationEvent { functionName = "OnFootstep", time = t }).ToArray();
                }
            }
            m.clipAnimations = clips;
        }

        [System.Serializable] public class StepEntry { public string clip; public System.Collections.Generic.List<float> times = new System.Collections.Generic.List<float>(); }
        [System.Serializable] public class StepTable { public System.Collections.Generic.List<StepEntry> clips = new System.Collections.Generic.List<StepEntry>(); }

        static readonly System.Collections.Generic.HashSet<string> LoopClips = new System.Collections.Generic.HashSet<string>
        { "Idle", "Walk", "Run", "Sneak", "Ride", "Hop", "Swim", "Flap", "Fall", "Eat", "SitLoop", "LieLoop", "RubLoop", "SitLick", "BellyUp", "Rub", "Graze", "SneakIdle", "Capture", "Point", "Climb" };

        void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith("Assets/_Game")) return;
            var t = (TextureImporter)assetImporter;
            if (assetPath.Contains("/Textures/Water/WaterNormal"))
            {
                // 1123c-C2: tiling normal maps for WildPartner/Water
                t.textureType = TextureImporterType.NormalMap;
                t.wrapMode = TextureWrapMode.Repeat;
                t.mipmapEnabled = true;
                t.anisoLevel = 4;
                return;
            }
            if (assetPath.EndsWith("Generated/Palette.png"))
            {
                // Swatch atlas: mips would bleed neighbouring swatches together in the distance.
                t.mipmapEnabled = false;
                t.filterMode = FilterMode.Bilinear;
                t.textureCompression = TextureImporterCompression.Uncompressed;
                t.wrapMode = TextureWrapMode.Clamp;
            }
            else if (assetPath.Contains("/Models/"))
            {
                t.maxTextureSize = 2048;
                t.mipmapEnabled = true;
                t.anisoLevel = 4;
            }
        }
    }
}
