using System;
using UnityEngine;

namespace WildPartner.World
{
    /// <summary>
    /// Clear / rain switching (FR-G05). Durations are random, but rain is guaranteed at least once every
    /// <see cref="maxDryMinutes"/> of play so the rain-only creature can always be found. Rain is a GPU
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
                if (_timer <= 0) Set(!IsRaining && (_rng.NextDouble() < 0.5 || _dry > maxDryMinutes * 60f));
                else if (!IsRaining && _dry > maxDryMinutes * 60f) Set(true);
            }
            RainAmount = Mathf.MoveTowards(RainAmount, IsRaining ? 1 : 0, Time.deltaTime / fadeSeconds);

            if (rain)
            {
                var cam = Camera.main;
                if (cam) rain.transform.position = cam.transform.position + Vector3.up * 12f + cam.transform.forward * 6f;
                var em = rain.emission;
                var tier = Zfw.QualityTiers.QualityTiers.Settings;
                em.rateOverTime = 3500f * RainAmount * (tier ? tier.particleDensity : 1f);
                if (RainAmount > 0.01f && !rain.isPlaying) rain.Play();
                if (RainAmount <= 0.01f && rain.isPlaying) rain.Stop();
            }
        }

        public string Label => IsRaining ? "下雨" : "晴";
    }
}
