using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Profiling;
using UnityEditorInternal;
using UnityEngine;

namespace WildPartner.EditorTools
{
    /// <summary>
    /// Sums managed (GC) allocations per call path from a profiler capture written by a Development build
    /// (-profiler-enable -profiler-log-file cap.raw). Used to find what allocates every frame in the soak test.
    ///   tools/unity.sh WildPartner.EditorTools.ProfileAllocReport.Run -raw Logs/prof1/cap.raw -report Logs/prof1/alloc.txt
    /// Only the main thread; only the frames the profiler keeps in history (the last ~2000).
    /// </summary>
    public static class ProfileAllocReport
    {
        public static void Run()
        {
            var args = Environment.GetCommandLineArgs();
            string Arg(string k) { int i = Array.IndexOf(args, k); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }
            string raw = Arg("-raw"), outPath = Arg("-report") ?? "Logs/alloc_report.txt";
            ProfilerDriver.profileEditor = false;
            if (!ProfilerDriver.LoadProfile(raw, false)) { Debug.LogError("[Alloc] cannot load " + raw); EditorApplication.Exit(1); return; }
            int first = ProfilerDriver.firstFrameIndex, last = ProfilerDriver.lastFrameIndex;
            var byPath = new Dictionary<string, (double bytes, int calls)>();
            var kids = new List<int>();
            double total = 0; int frames = 0;
            for (int f = first; f <= last; f++)
            {
                using var v = ProfilerDriver.GetHierarchyFrameDataView(f, 0, HierarchyFrameDataView.ViewModes.MergeSamplesWithTheSameName, HierarchyFrameDataView.columnGcMemory, false);
                if (v == null || !v.valid) continue;
                frames++;
                void Walk(int id, string path)
                {
                    var children = new List<int>();
                    v.GetItemChildren(id, children);
                    foreach (var c in children)
                    {
                        string n = v.GetItemName(c);
                        if (n == "GC.Alloc")
                        {
                            double b = v.GetItemColumnDataAsDouble(c, HierarchyFrameDataView.columnGcMemory);
                            int calls = (int)v.GetItemColumnDataAsDouble(c, HierarchyFrameDataView.columnCalls);
                            byPath.TryGetValue(path, out var e);
                            byPath[path] = (e.bytes + b, e.calls + calls);
                            total += b;
                        }
                        else if (v.GetItemColumnDataAsDouble(c, HierarchyFrameDataView.columnGcMemory) > 0) Walk(c, path + " > " + n);
                    }
                }
                Walk(v.GetRootItemID(), "");
            }
            var lines = new List<string> { $"frames {frames} ({first}..{last}), total GC alloc {total / 1024:0.0} KB, {total / Math.Max(frames, 1):0} B/frame", "" };
            foreach (var kv in byPath.OrderByDescending(k => k.Value.bytes).Take(60))
                lines.Add($"{kv.Value.bytes / Math.Max(frames, 1),8:0} B/frame  {kv.Value.calls / (double)Math.Max(frames, 1),6:0.0} allocs/frame  {kv.Key}");
            File.WriteAllLines(outPath, lines);
            Debug.Log("[Alloc] report -> " + outPath);
            EditorApplication.Exit(0);
        }
    }
}
