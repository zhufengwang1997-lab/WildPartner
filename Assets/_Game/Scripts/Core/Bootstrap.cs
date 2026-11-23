using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Unity.Cinemachine;
using Zfw.QualityTiers;

namespace WildPartner.Core
{
    /// <summary>
    /// Start-up: compat table -> tier (command line > saved manual choice > default High) -> post-processing
    /// per tier. Also switches between the slowly orbiting menu camera and the gameplay camera.
    /// Command line: -tier=low|medium|high|ultra
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class Bootstrap : MonoBehaviour
    {
        public Volume volume;
        public CinemachineCamera menuCamera;
        public CinemachineCamera playerCamera;
        public Transform menuPivot;
        public float menuOrbitSpeed = 2.5f;

        public static string BuildCommit { get; private set; } = "dev";

        void Awake()
        {
            var commit = Resources.Load<TextAsset>("build_commit");
            if (commit) BuildCommit = commit.text.Trim();
            // 11-24: the menu flyover camera (scene priority 20) was live on the first frame, so the brain blended
            // from high above the valley down into the cabin through the terrain. Switch it off before the brain
            // ever sees it: the game opens directly on the witch in bed.
            if (menuCamera) menuCamera.gameObject.SetActive(false);
            string[] args = Environment.GetCommandLineArgs();
            QualityTiers.LoadCompat();
            string tierArg = Array.Find(args, a => a.StartsWith("-tier=", StringComparison.Ordinal));
            if (tierArg != null && Enum.TryParse(tierArg.Substring(6), true, out Tier forced)) QualityTiers.SetCommandLine(forced);
            else if (!string.IsNullOrEmpty(SaveSystem.Settings.tier) && Enum.TryParse(SaveSystem.Settings.tier, out Tier saved)) QualityTiers.Set(saved, manual: true);
            else QualityTiers.Set(Tier.High, manual: false);
            QualityTiers.TierChanged += ApplyPost;
            ApplyPost(QualityTiers.Current);
            // 超分阶段 step 1: temporal anti-aliasing (saved choice, -aa= on the command line); a tier switch swaps the
            // URP asset, so re-apply; every camera cut drops the temporal history (IN-08)
            Rendering.WildPartnerTier.Apply();
            Rendering.AntiAliasing.LoadSaved();
            Rendering.UpscalerSettings.Init();
            Rendering.UpscalerInputsPass.Init();
            Rendering.GrdSettings.Init();
            Zfw.PerfRunner.PerfRunner.UpscalerInfo = () => Zfw.Upscaling.UpscalerHost.Active == null ? "off"
                : $"{Zfw.Upscaling.UpscalerHost.Active.Name}/{Zfw.Upscaling.UpscalerHost.Quality} {Zfw.Upscaling.UpscalerHost.RenderSize.x}x{Zfw.Upscaling.UpscalerHost.RenderSize.y}";
            Zfw.PerfRunner.PerfRunner.UpscaleMs = Rendering.UpscaleBench.UpscaleMs;
            QualityTiers.TierChanged += ReapplyAA;
            Player.PlayerController.CameraCut += Rendering.AntiAliasing.ResetHistory;
            if (!FindFirstObjectByType<Rendering.DebugOverlay>()) new GameObject("DebugOverlay").AddComponent<Rendering.DebugOverlay>();
            Debug.Log($"[Boot] WildPartner commit={BuildCommit} tier={QualityTiers.Current} ({QualityTiers.Mode}) api={SystemInfo.graphicsDeviceType} gpu={SystemInfo.graphicsDeviceName}");
        }

        void OnDestroy()
        {
            QualityTiers.TierChanged -= ApplyPost;
            QualityTiers.TierChanged -= ReapplyAA;
            Player.PlayerController.CameraCut -= Rendering.AntiAliasing.ResetHistory;
        }

        static void ReapplyAA(Tier _) { Rendering.AntiAliasing.Apply(); Rendering.WildPartnerTier.Apply(); Rendering.GrdSettings.Apply(); }

        void ApplyPost(Tier _)
        {
            var s = QualityTiers.Settings;
            if (!s || !volume || !volume.profile) return;
            if (volume.profile.TryGet(out Bloom b))
            {
                b.active = s.bloom;
                b.maxIterations.Override(Mathf.Clamp(s.bloomIterations, 2, 8));
            }
        }

        void Update()
        {
            var flow = GameFlow.Instance;
            // 11-24: the rotating valley flyover behind the main menu is removed (it clipped through terrain);
            // the menu now sits over the normal game camera.
            if (menuCamera) menuCamera.Priority = 0;
        }
    }
}
