using System.IO;
using UnityEditor;
using UnityEngine;
using Zfw.GpuCaps;

namespace WildPartner.EditorTools
{
    /// <summary>Writes a GpuCaps JSON report from inside WildPartner (stage-0 acceptance).
    ///   tools/unity.sh WildPartner.EditorTools.CapsTool.WriteReport   -> docs/caps/</summary>
    public static class CapsTool
    {
        [MenuItem("WildPartner/Write GpuCaps Report")]
        public static void WriteReport()
        {
            var r = GpuCaps.Current;
            string path = GpuCaps.Write(r, Path.GetFullPath("docs/caps"));
            Debug.Log($"[Caps] {r.api} {r.gpu.name} source={r.source} -> {path}");
        }
    }
}
