using System;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using Zfw.QualityTiers;

namespace WildPartner.Rendering
{
    /// <summary>
    /// 画质分级扩展: the knobs this game adds on top of the package's generic tier settings (lights, particles, bloom,
    /// fog, URP asset = shadow map size / distance / cascades). A tier asset may carry one of these in
    /// <see cref="TierSettings.extensions"/>; when it does not, the table in <see cref="Defaults"/> is used, so the four
    /// tiers always differ in the same documented way (docs/画质分级.md).
    /// </summary>
    [Serializable]
    public class WildPartnerTier : TierExtension
    {
        [Tooltip("Grass draw distance and density scale (GrassRenderer.Quality): 1 = 60 m far ring.")]
        public float grassQuality = 1f;
        [Tooltip("QualitySettings.lodBias: higher keeps the detailed tree LODs further away.")]
        public float lodBias = 1f;
        public bool ssao = true;
        [Tooltip("Lake reflection probe cube size.")]
        public int waterProbeResolution = 128;
        [Tooltip("Default anti-aliasing when the player has not picked one.")]
        public AAMode defaultAA = AAMode.STP;

        public static WildPartnerTier Defaults(Tier t) => t switch
        {
            Tier.Low => new WildPartnerTier { grassQuality = 0.55f, lodBias = 0.7f, ssao = false, waterProbeResolution = 64, defaultAA = AAMode.TAA },
            Tier.Medium => new WildPartnerTier { grassQuality = 0.8f, lodBias = 1f, ssao = false, waterProbeResolution = 64, defaultAA = AAMode.STP },
            Tier.High => new WildPartnerTier { grassQuality = 1f, lodBias = 1.3f, ssao = true, waterProbeResolution = 128, defaultAA = AAMode.STP },
            _ => new WildPartnerTier { grassQuality = 1.25f, lodBias = 1.8f, ssao = true, waterProbeResolution = 256, defaultAA = AAMode.STP },
        };

        /// <summary>The active tier's values (asset extension if present, else the table).</summary>
        public static WildPartnerTier Current
        {
            get
            {
                var s = QualityTiers.Settings;
                return (s ? s.Get<WildPartnerTier>() : null) ?? Defaults(QualityTiers.Current);
            }
        }

        /// <summary>Push the active tier's values into the game (called on start and on every tier change).</summary>
        public static void Apply()
        {
            var w = Current;
            World.GrassRenderer.Quality = w.grassQuality;
            QualitySettings.lodBias = w.lodBias;
            SetSsao(w.ssao);
            World.WaterProbe.Resolution = w.waterProbeResolution;
            Debug.Log($"[Tier] {QualityTiers.Current}: grass {w.grassQuality}, lodBias {w.lodBias}, SSAO {(w.ssao ? "on" : "off")}, water probe {w.waterProbeResolution}, default AA {w.defaultAA}");
        }

        /// <summary>SSAO lives on the renderer data shared by the four URP assets: toggled there.</summary>
        public static void SetSsao(bool on)
        {
            foreach (var f in Features()) if (f && f.name.Contains("AmbientOcclusion")) f.SetActive(on);
        }

        public static System.Collections.Generic.List<ScriptableRendererFeature> Features()
        {
            var r = new System.Collections.Generic.List<ScriptableRendererFeature>();
            var asset = AntiAliasing.Asset;
            if (!asset) return r;
            foreach (var d in asset.rendererDataList)
                if (d) r.AddRange(d.rendererFeatures);
            return r;
        }
    }
}
