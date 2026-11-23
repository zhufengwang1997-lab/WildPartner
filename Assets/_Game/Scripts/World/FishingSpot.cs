using System.Collections.Generic;
using UnityEngine;

namespace WildPartner.World
{
    /// <summary>
    /// 试玩反馈 1123d-1: one fishing spot - a small wooden jetty built by WorldBuilder (by the wooden bridge, and at the
    /// pool below the waterfall). She stands at <see cref="stand"/> facing <see cref="yaw"/> and the float lands at
    /// <see cref="cast"/>. Marked from afar by a lantern on the jetty and, on the water, slow violet rings and a few
    /// rising motes where the koi live.
    /// </summary>
    public class FishingSpot : MonoBehaviour
    {
        public string label;
        public Vector3 stand;
        public float yaw;
        public Vector3 cast;
        /// <summary>The jetty's wood (the rod is made of it too).</summary>
        public Material wood;

        public static readonly List<FishingSpot> All = new List<FishingSpot>();
        ParticleSystem _rings, _motes;

        void OnEnable() { if (!All.Contains(this)) All.Add(this); }
        void OnDisable() => All.Remove(this);

        void Start()
        {
            _rings = MakePs("FishSpot_Rings", FxMat.Ring, true);
            var m = _rings.main;
            m.startLifetime = 3.2f; m.startSize = new ParticleSystem.MinMaxCurve(0.6f, 1.0f); m.startSpeed = 0f;
            m.startColor = new Color(0.82f, 0.72f, 1f, 0.55f); m.maxParticles = 8;
            var em = _rings.emission; em.rateOverTime = 0.75f;
            var sh = _rings.shape; sh.enabled = true; sh.shapeType = ParticleSystemShapeType.Circle; sh.radius = 0.8f; sh.rotation = new Vector3(90, 0, 0);
            var sz = _rings.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 0.4f, 1, 3.2f));
            var col = _rings.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                      new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, 0.15f), new GradientAlphaKey(0, 1) });
            col.color = g;
            _rings.transform.position = cast + Vector3.up * 0.04f;
            _rings.Play();

            _motes = MakePs("FishSpot_Motes", FxMat.Additive, false);
            m = _motes.main;
            m.startLifetime = 4f; m.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.11f); m.startSpeed = 0f;
            m.startColor = new Color(0.75f, 0.6f, 1f, 0.8f); m.maxParticles = 30; m.gravityModifier = -0.012f;
            em = _motes.emission; em.rateOverTime = 3.5f;
            sh = _motes.shape; sh.enabled = true; sh.shapeType = ParticleSystemShapeType.Circle; sh.radius = 1.4f; sh.rotation = new Vector3(90, 0, 0);
            var vel = _motes.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(-0.08f, 0.08f); vel.y = new ParticleSystem.MinMaxCurve(0.08f, 0.2f); vel.z = new ParticleSystem.MinMaxCurve(-0.08f, 0.08f);
            col = _motes.colorOverLifetime; col.enabled = true;
            g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                      new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, 0.25f), new GradientAlphaKey(0, 1) });
            col.color = g;
            _motes.transform.position = cast + Vector3.up * 0.12f;
            _motes.Play();
        }

        ParticleSystem MakePs(string name, Material mat, bool flat)
        {
            var go = new GameObject(name, typeof(ParticleSystem));
            go.transform.SetParent(transform, false);
            var ps = go.GetComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main; main.simulationSpace = ParticleSystemSimulationSpace.World; main.playOnAwake = false;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = mat;
            r.renderMode = flat ? ParticleSystemRenderMode.HorizontalBillboard : ParticleSystemRenderMode.Billboard;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return ps;
        }

        /// <summary>A burst of rings where the float lands / the fish struggles.</summary>
        public void Splash(Vector3 p, int n = 2)
        {
            if (!_rings) return;
            var ep = new ParticleSystem.EmitParams { position = p + Vector3.up * 0.04f, applyShapeToPosition = false };
            _rings.Emit(ep, n);
        }
    }
}
