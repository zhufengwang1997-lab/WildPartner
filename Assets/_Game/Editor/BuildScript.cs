using System;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace WildPartner.EditorTools
{
    /// <summary>
    /// Command-line builds. The short git commit is baked into Resources/build_commit.txt so every CSV /
    /// caps report can be traced back to the exact code that produced it.
    ///   Unity -batchmode -quit -projectPath . -executeMethod WildPartner.EditorTools.BuildScript.BuildMac
    ///   Unity -batchmode -quit -projectPath . -executeMethod WildPartner.EditorTools.BuildScript.BuildWindows
    /// Optional: -dev (development build + profiler connection), -out &lt;dir&gt;.
    /// </summary>
    public static class BuildScript
    {
        public static void BuildMac() => Build(BuildTarget.StandaloneOSX, "Mac/WildPartner.app");
        public static void BuildWindows() => Build(BuildTarget.StandaloneWindows64, "Windows/WildPartner.exe");

        [MenuItem("WildPartner/Build/macOS")] static void MenuMac() => BuildMac();
        [MenuItem("WildPartner/Build/Windows (Mono, cross-compiled)")] static void MenuWin() => BuildWindows();

        static void Build(BuildTarget target, string rel)
        {
            string[] args = Environment.GetCommandLineArgs();
            bool dev = Array.IndexOf(args, "-dev") >= 0;
            int oi = Array.IndexOf(args, "-out");
            string root = oi >= 0 && oi + 1 < args.Length ? args[oi + 1] : "Builds";

            string commit = Git("rev-parse --short HEAD");
            if (!string.IsNullOrEmpty(Git("status --porcelain -- Assets Packages ProjectSettings"))) commit += "-dirty";
            Directory.CreateDirectory("Assets/Resources");
            File.WriteAllText("Assets/Resources/build_commit.txt", commit);
            AssetDatabase.ImportAsset("Assets/Resources/build_commit.txt");

            var opts = new BuildPlayerOptions
            {
                scenes = Array.ConvertAll(EditorBuildSettings.scenes, s => s.path),
                locationPathName = Path.Combine(root, rel),
                target = target,
                options = dev ? BuildOptions.Development | BuildOptions.ConnectWithProfiler : BuildOptions.None,
            };
            var report = BuildPipeline.BuildPlayer(opts);
            var s = report.summary;
            Debug.Log($"[Build] {target} {s.result} commit={commit} size={s.totalSize / 1048576} MB time={s.totalTime.TotalSeconds:0}s -> {opts.locationPathName}");
            if (Application.isBatchMode) EditorApplication.Exit(s.result == BuildResult.Succeeded ? 0 : 1);
        }

        static string Git(string a)
        {
            try
            {
                var p = Process.Start(new ProcessStartInfo("git", a) { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true });
                string o = p.StandardOutput.ReadToEnd().Trim();
                p.WaitForExit();
                return o;
            }
            catch { return "nogit"; }
        }
    }
}
