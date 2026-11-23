using System;
using UnityEngine;

namespace WildPartner.World
{
    /// <summary>
    /// Clear / rain switching (FR-G05). Durations are random. (Rain used to be forced every few minutes for the
    /// rain-only koi; since 1123d-1 the koi is fished in any weather, and the moon butterfly needs a clear night,
    /// so sleeping into the night is what guarantees the weather now: see <see cref="SleptIntoNight"/>.) Rain is a GPU
    /// particle system that follows the camera; rain drops are transparent and have no motion vectors,
    /// which is why stage 2 writes them into the Reactive Mask (IN-06).
    /// </summary>
    public class Weather : MonoBehaviour
    {
        public static Weather Instance { get; private set; }
        public ParticleSystem rain;
        public float minSpell = 50f, maxSpell = 110f;
        public float maxDryMinutes = 3f;
        public float fadeSeconds = 6f;

        public bool IsRaining { get; private set; }
        public float RainAmount { get; private set; }
        public event Action<bool> Changed;
        public static bool Deterministic;

        float _timer;
        float _dry;
        System.Random _rng = new System.Random(WorldLayout.Seed);

        void Awake() { Instance = this; _timer = 70f; }
        void Start() { if (rain) Rendering.ReactiveMaskSources.Register(rain.GetComponent<ParticleSystemRenderer>()); }   // IN-06
        void OnDestroy() { if (Instance == this) Instance = null; }

        public void Set(bool raining, bool instant = false)
        {
            if (IsRaining != raining) Changed?.Invoke(raining);
            IsRaining = raining;
            if (raining) _dry = 0;
            _timer = Mathf.Lerp(minSpell, maxSpell, (float)_rng.NextDouble()) * (raining ? 0.8f : 1f);
            if (instant) RainAmount = raining ? 1 : 0;
        }

        void Update()
        {
            if (!Deterministic)
            {
                _timer -= Time.deltaTime;
                if (!IsRaining) _dry += Time.deltaTime;
                if (_timer <= 0) Set(!IsRaining && _rng.NextDouble() < 0.5);
            }
            RainAmount = Mathf.MoveTowards(RainAmount, IsRaining ? 1 : 0, Time.deltaTime / fadeSeconds);

            if (rain)
            {
                var cam = Camera.main;
                if (cam) rain.transform.position = cam.transform.position + Vector3.up * 12f + cam.transform.forward * 6f;
                var em = rain.emission;
                var tier = Zfw.QualityTiers.QualityTiers.Settings;
                em.rateOverTime = EnvController.Indoors ? 0f : 3500f * RainAmount * (tier ? tier.particleDensity : 1f);   // a roof overhead in the cabin
                if (RainAmount > 0.01f && !rain.isPlaying) rain.Play();
                if (RainAmount <= 0.01f && rain.isPlaying) rain.Stop();
            }
        }

        public string Label => IsRaining ? "下雨" : "晴";

        /// <summary>1123d-3: sleeping in the bed into the night. At least 70 % clear; after two rainy nights in a row the
        /// third is clear for sure. A clear night then stays clear for <see cref="ClearHoldSeconds"/> so there is time to
        /// reach the island. Returns whether it rains.</summary>
        public bool SleptIntoNight(ref int rainyInRow, double roll = -1)
        {
            if (roll < 0) roll = _rng.NextDouble();
            bool rain = rainyInRow < 2 && roll < 0.3;
            rainyInRow = rain ? rainyInRow + 1 : 0;
            Set(rain, true);
            if (!rain) _timer = Mathf.Max(_timer, ClearHoldSeconds);
            Debug.Log($"[Weather] slept into the night: {(rain ? "rain" : "clear")} (rainy nights in a row {rainyInRow})");
            return rain;
        }
        public const float ClearHoldSeconds = 300f;
    }
}
