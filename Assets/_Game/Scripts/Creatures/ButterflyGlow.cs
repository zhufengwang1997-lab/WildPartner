using UnityEngine;
using WildPartner.Rendering;
using WildPartner.World;

namespace WildPartner.Creatures
{
    /// <summary>
    /// 试玩反馈 1123e-3: the moon butterfly glows only at night, and leaves stardust instead of a light trail.
    ///  - Day: plain wing colours, no glow, no particles.
    ///  - Dusk it brightens, at night the wing pattern glows a soft blue-violet and fine glowing motes fall from
    ///    behind it as it flaps (more the faster it flies): they sink a little, wander, shrink and fade. Dawn it dims.
    ///    Everything eases over the hours - no switching.
    ///  - On a rainy night the glow is weaker.
    /// The stardust has no motion vectors and is registered for the upscaler's reactive mask (IN-06).
    /// On every moon butterfly (the companion and the wild one on the moonflower).
    /// </summary>
    public class ButterflyGlow : MonoBehaviour
    {
        /// <summary>0 day .. 1 full night glow, rain included (smoke test).</summary>
        public float Glow { get; private set; }
        public ParticleSystem Dust => _dust;
        public int DustAlive => _dust ? _dust.particleCount : 0;

        static readonly int EmissionId = Shader.PropertyToID("_Emission"), TintId = Shader.PropertyToID("_EmissionTint");
        static readonly Color Tint = new Color(0.42f, 0.42f, 1.2f);   // blue-violet; the albedo is already pale blue, so keep red low or it reads white
        const float NightEmission = 0.42f;   // 0.9 blew out to white under bloom (1123e showcase)

        Renderer[] _rs;
        Creature _c;
        Light _ownLight; float _ownBase;
        MaterialPropertyBlock _mpb;
        ParticleSystem _dust;
        Vector3 _lastPos;
        float _speed, _carry;
        /// <summary>Optional pet light (Creature sets it for the companion).</summary>
        public Light petLight;

        /// <summary>How much "night" it is at this hour: up from 17:30 to 19:30, down from 5:00 to 6:30.</summary>
        public static float Nightness(float hour)
        {
            if (hour >= 12f) return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(17.5f, 19.5f, hour));
            return 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(5f, 6.5f, hour));
        }

        void Start()
        {
            _rs = GetComponentsInChildren<Renderer>(true);
            // the prefab's own point light ("Glow", AssetSetup) was on all day and burnt the wings white in the sun:
            // it now follows the night glow like everything else
            foreach (var l in GetComponentsInChildren<Light>(true)) if (l.name == "Glow") { _ownLight = l; _ownBase = l.intensity; }
            _mpb = new MaterialPropertyBlock();
            _lastPos = transform.position;

            var go = new GameObject("MoonDust", typeof(ParticleSystem));
            go.transform.SetParent(transform, false);
            _dust = go.GetComponent<ParticleSystem>();
            _dust.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var m = _dust.main;
            m.simulationSpace = ParticleSystemSimulationSpace.World; m.playOnAwake = false; m.loop = true;
            m.startLifetime = new ParticleSystem.MinMaxCurve(1.2f, 2.4f);
            m.startSize = new ParticleSystem.MinMaxCurve(0.018f, 0.055f);
            m.startSpeed = new ParticleSystem.MinMaxCurve(0.02f, 0.12f);
            m.gravityModifier = 0.025f;                                     // sinks a little
            m.maxParticles = 220;
            m.startColor = new ParticleSystem.MinMaxGradient(new Color(0.55f, 0.6f, 1f, 0.55f), new Color(0.95f, 0.85f, 1f, 1f));
            var em = _dust.emission; em.enabled = false;                    // emitted by hand, by flight speed
            var sh = _dust.shape; sh.enabled = true; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = 0.05f;
            var noise = _dust.noise; noise.enabled = true; noise.strength = 0.18f; noise.frequency = 0.9f; noise.scrollSpeed = 0.4f; noise.quality = ParticleSystemNoiseQuality.Low;
            var sz = _dust.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0, 1f, 1, 0.15f));
            var col = _dust.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(new Color(0.7f, 0.6f, 1f), 1) },
                      new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, 0.08f), new GradientAlphaKey(0.6f, 0.5f), new GradientAlphaKey(0, 1) });
            col.color = g;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = FxMat.Additive; r.renderMode = ParticleSystemRenderMode.Billboard;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
            ReactiveMaskSources.Register(r);                                // IN-06: no motion vectors, into the reactive mask
            _dust.Play();
        }

        void OnDestroy() { if (_dust) ReactiveMaskSources.Unregister(_dust.GetComponent<Renderer>()); }

        void LateUpdate()
        {
            var env = EnvController.Instance;
            var weather = Weather.Instance;
            float hour = env ? env.timeOfDay : 12f;
            float target = Nightness(hour) * (1f - 0.55f * (weather ? weather.RainAmount : 0f));
            if (EnvController.Indoors) target = Mathf.Max(target, 0.6f);    // indoors by the fire it still glows softly
            // the hour already changes slowly; this only smooths jumps (sleeping, a forced time in tests)
            Glow = Mathf.MoveTowards(Glow, target, Time.deltaTime / 2.5f);

            foreach (var rr in _rs)
            {
                if (!rr || rr is ParticleSystemRenderer || rr is TrailRenderer) continue;
                rr.GetPropertyBlock(_mpb);
                _mpb.SetFloat(EmissionId, NightEmission * Glow);
                _mpb.SetColor(TintId, Tint);
                rr.SetPropertyBlock(_mpb);
            }
            var cr = _c ? _c : (_c = GetComponent<Creature>());
            bool visible = cr && cr.Current != Creature.State.Hidden && (cr.Current != Creature.State.Companion || cr.PetShown);
            if (petLight) { petLight.enabled = visible && Glow > 0.02f; petLight.intensity = 1.6f * Glow; }
            if (_ownLight) { _ownLight.enabled = visible && Glow > 0.02f; _ownLight.intensity = _ownBase * 0.5f * Glow; _ownLight.color = new Color(0.55f, 0.55f, 1f); }

            // stardust: more the faster it flies; a little even hovering (the wings still beat)
            float dt = Mathf.Max(Time.deltaTime, 1e-4f);
            _speed = Mathf.Lerp(_speed, Vector3.Distance(transform.position, _lastPos) / dt, 1f - Mathf.Exp(-6f * dt));
            _lastPos = transform.position;
            if (!_dust || !visible || Glow < 0.03f) { _carry = 0f; return; }
            float rate = (5f + Mathf.Min(_speed, 6f) * 9f) * Glow;
            _carry += rate * dt;
            int n = Mathf.FloorToInt(_carry);
            if (n <= 0) return;
            _carry -= n;
            var tail = transform.position - transform.forward * 0.07f + Vector3.down * 0.02f;
            var ep = new ParticleSystem.EmitParams { position = tail, applyShapeToPosition = true };
            for (int i = 0; i < n; i++)
            {
                ep.startSize = Random.Range(0.018f, 0.055f) * Mathf.Lerp(0.6f, 1f, Glow);
                var c = Color.Lerp(new Color(0.55f, 0.6f, 1f), new Color(1f, 0.88f, 1f), Random.value);
                c.a = Random.Range(0.45f, 1f) * Glow;
                ep.startColor = c;
                _dust.Emit(ep, 1);
            }
        }
    }
}
