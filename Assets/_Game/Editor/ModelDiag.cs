using UnityEditor;
using UnityEngine;

namespace WildPartner.EditorTools
{
    /// <summary>Logs root/child rotations and bounds of imported models (orientation self-check).</summary>
    public static class ModelDiag
    {
        public static void Reimport()
        {
            AssetDatabase.ImportAsset("Assets/_Game/Models", ImportAssetOptions.ImportRecursive | ImportAssetOptions.ForceUpdate);
            Run();
        }

        public static void Run()
        {
            foreach (var p in new[] { "Assets/_Game/Models/Characters/Witch.fbx", "Assets/_Game/Models/Characters/WhiteDeer.fbx", "Assets/_Game/Models/Generated/Bridge.fbx", "Assets/_Game/Models/Generated/Cabin.fbx", "Assets/_Game/Models/Generated/Owl.fbx" })
            {
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(p);
                if (!go) { Debug.Log($"[Models] missing {p}"); continue; }
                var b = new Bounds(); bool first = true;
                foreach (var r in go.GetComponentsInChildren<Renderer>()) { if (first) { b = r.bounds; first = false; } else b.Encapsulate(r.bounds); }
                Debug.Log($"[Models] {p} root={go.transform.localEulerAngles} bounds={b.size}");
                foreach (Transform c in go.transform) Debug.Log($"[Models]   child {c.name} rot={c.localEulerAngles} pos={c.localPosition}");
            }
            var player = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Game/Prefabs/Player.prefab");
            foreach (Transform c in player.GetComponentsInChildren<Transform>()) Debug.Log($"[Models] player {c.name} rot={c.localEulerAngles}");
        }
    }
}
