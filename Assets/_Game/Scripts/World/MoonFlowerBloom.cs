using UnityEngine;
using WildPartner.Core;
using WildPartner.Creatures;
using WildPartner.Player;

namespace WildPartner.World
{
    /// <summary>
    /// 试玩反馈 1123d-3: the moonflower on the lake island, where the moon butterfly rests. On a clear night it opens
    /// and glows a soft blue-violet (emission, a point light, a halo seen from across the lake and a few drifting motes);
    /// by day or in the rain it is closed and dark. Also the two "find it" cues: the first clear night she comes to the
    /// lake shore a line on screen, and the cat (when it is the pet at her side) stops, looks towards the island and
    /// meows.
    /// </summary>
    public class MoonFlowerBloom : MonoBehaviour
    {
        public static MoonFlowerBloom Instance { get; private set; }
        /// <summary>0 closed .. 1 open and glowing.</summary>
        public float Bloom { get; private set; }
        public bool ClearNight { get; private set; }
        /// <summary>World position of the flower head (the butterfly's perch).</summary>
        public Vector3 Top { get; private set; }
        public static event System.Action ShoreHint, CatCue;
        /// <summary>Smoke test: as if she had never been to the shore on a clear night.</summary>
        public void ResetCues() { _catCooldown = 0f; if (GameFlow.Instance != null) GameFlow.Instance.Save.moonHintShown = false; }

        Renderer[] _rs;
        MaterialPropertyBlock _mpb;
        Light _light;
        Transform _haloNear, _haloFar;
        Material _haloNearMat, _haloFarMat;
        ParticleSystem _motes;
        Vector3 _scale;
        float _catCooldown;

        void Awake() => Instance = this;
        void OnDestroy() { if (Instance == this) Instance = null; }

        void Start()
        {
            _rs = GetComponentsInChildren<Renderer>();
            _mpb = new MaterialPropertyBlock();
            _scale = transform.localScale;
            var b = new Bounds(transform.position, Vector3.zero);
            foreach (var r in _rs) b.Encapsulate(r.bounds);
            Top = new Vector3(transform.position.x, b.max.y, transform.position.z);
            _light = GetComponentInChildren<Light>();
            if (_light)
            {
                var nl = _light.GetComponent<NightLight>(); if (nl) nl.enabled = false;   // driven here instead
                _light.color = new Color(0.62f, 0.56f, 1f); _light.range = 10f;
                // above the flower, not beside the butterfly: right next to it the light burns the wings white
                _light.transform.position = Top + Vector3.up * 1.8f;
            }
            // the butterfly sits on the flower head
            var def = CreatureDefs.Get("moon_butterfly");
            if (def != null) def.hover = Top.y - Creature.GroundHeight(Top.x, Top.z) + 0.04f;

            _haloNear = Halo("Halo_Near", 2.6f, out _haloNearMat);
            _haloFar = Halo("Halo_Far", 8f, out _haloFarMat);

            var go = new GameObject("MoonMotes", typeof(ParticleSystem));
            go.transform.position = Top;
            _motes = go.GetComponent<ParticleSystem>();
            _motes.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var m = _motes.main; m.simulationSpace = ParticleSystemSimulationSpace.World; m.playOnAwake = false;
            m.startLifetime = 6f; m.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.14f); m.startSpeed = 0f;
            m.startColor = new Color(0.72f, 0.66f, 1f, 0.9f); m.maxParticles = 60;
            var sh = _motes.shape; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = 2.6f;
            var vel = _motes.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(-0.1f, 0.1f); vel.y = new ParticleSystem.MinMaxCurve(0.04f, 0.16f); vel.z = new ParticleSystem.MinMaxCurve(-0.1f, 0.1f);
            var col = _motes.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                      new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, 0.3f), new GradientAlphaKey(0, 1) });
            col.color = g;
            var pr = go.GetComponent<ParticleSystemRenderer>(); pr.sharedMaterial = FxMat.Additive;
            _motes.Play();
        }

        Transform Halo(string name, float size, out Material mat)
        {
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Destroy(q.GetComponent<Collider>());
            q.name = name;
            q.transform.position = Top + Vector3.up * 0.1f;
            q.transform.localScale = Vector3.one * size;
            mat = new Material(FxMat.Additive);
            var r = q.GetComponent<MeshRenderer>(); r.sharedMaterial = mat; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return q.transform;
        }

        void Update()
        {
            var env = EnvController.Instance;
            var weather = Weather.Instance;
            float hour = env ? env.timeOfDay : 12f;
            ClearNight = CreatureDefs.Get("moon_butterfly").ConditionMet(hour, weather && weather.IsRaining);
            Bloom = Mathf.MoveTowards(Bloom, ClearNight ? 1f : 0f, Time.deltaTime / 3f);
            float b = Mathf.SmoothStep(0, 1, Bloom), pulse = 0.9f + 0.1f * Mathf.Sin(Time.time * 1.3f);

            transform.localScale = new Vector3(_scale.x * Mathf.Lerp(0.8f, 1.05f, b), _scale.y * Mathf.Lerp(0.85f, 1.05f, b), _scale.z * Mathf.Lerp(0.8f, 1.05f, b));
            foreach (var r in _rs)
            {
                r.GetPropertyBlock(_mpb);
                _mpb.SetFloat("_Emission", Mathf.Lerp(0.1f, 0.7f, b) * pulse);
                r.SetPropertyBlock(_mpb);
            }
            if (_light) { _light.enabled = b > 0.01f; _light.intensity = 1.6f * b * pulse; }
            var cam = Camera.main;
            if (cam)
            {
                _haloNear.rotation = _haloFar.rotation = cam.transform.rotation;
                float dist = Vector3.Distance(cam.transform.position, Top);
                float far = Mathf.InverseLerp(10f, 40f, dist);    // the big halo is for the far shore
                float near = Mathf.InverseLerp(5f, 12f, dist);   // up close the flower itself glows, no veil over it
                _haloNearMat.SetColor("_BaseColor", new Color(0.62f, 0.55f, 1f, 0.4f * b * pulse * near));
                _haloFarMat.SetColor("_BaseColor", new Color(0.5f, 0.45f, 1f, 0.22f * b * pulse * far));
            }
            var em = _motes.emission; em.rateOverTime = 8f * b;

            Cues();
        }

        /// <summary>The first clear night on the lake shore: a line on screen. The cat at her side looks across and meows.</summary>
        void Cues()
        {
            var player = PlayerController.Instance;
            var flow = GameFlow.Instance;
            if (!player || flow == null || flow.State != GameFlow.FlowState.Playing || Bloom < 0.6f) return;
            if (flow.Save.HasCaught("moon_butterfly")) return;
            var p = player.transform.position;
            float lake = WorldLayout.LakeDistance(p.x, p.z);
            bool shore = lake > 0.9f && lake < 1.6f && !player.Swimming;
            if (!shore) return;
            if (!flow.Save.moonHintShown)
            {
                flow.Save.moonHintShown = true;
                flow.ShowToast("湖心岛上亮着一点蓝紫色的光……月光花开了，也许月影蝶就停在那里。");
                ShoreHint?.Invoke();
            }
            _catCooldown -= Time.deltaTime;
            if (_catCooldown <= 0f && PetSwitcher.Active == "cat")
            {
                var cat = PetSwitcher.Pet("cat");
                if (cat && cat.PetShown)
                {
                    cat.LookAndMeow(Top, 3f);
                    _catCooldown = 40f;
                    CatCue?.Invoke();
                    Debug.Log("[Moon] the cat looks towards the island and meows");
                }
                else _catCooldown = 1f;   // cat not out: look again in a second, not every frame (Pet() searches the scene)
            }
        }
    }
}
