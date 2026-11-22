using UnityEngine;

namespace WildPartner.World
{
    /// <summary>Lamp / campfire light: fades in at dusk, optional fire flicker.</summary>
    [RequireComponent(typeof(Light))]
    public class NightLight : MonoBehaviour
    {
        public bool alwaysOn;
        public bool flicker;
        Light _l;
        float _base;

        void Awake() { _l = GetComponent<Light>(); _base = _l.intensity; }

        void Update()
        {
            var env = EnvController.Instance;
            float h = env ? env.timeOfDay : 12f;
            float night = Mathf.Clamp01(Mathf.Max(Mathf.InverseLerp(17.5f, 19.5f, h), Mathf.InverseLerp(7f, 5f, h)));
            float rain = Weather.Instance ? Weather.Instance.RainAmount * 0.5f : 0f;
            float on = alwaysOn ? Mathf.Max(0.35f, night) : Mathf.Max(night, rain);
            float f = flicker && !EnvController.Deterministic ? 0.85f + 0.15f * Mathf.PerlinNoise(Time.time * 6f, 0.3f) : 1f;
            _l.intensity = _base * on * f;
            _l.enabled = _l.intensity > 0.02f;
        }
    }
}
