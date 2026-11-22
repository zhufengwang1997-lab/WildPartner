using System.Collections.Generic;
using UnityEngine;

namespace WildPartner.World
{
    /// <summary>
    /// Marks the waterfall's transparent pieces (sheet, pool foam, mist, splash) — 试玩反馈 1123c-A4. They have no
    /// motion vectors, so the upscaler (stage 2) must write <see cref="All"/> into its reactive mask instead of
    /// reprojecting them. The component on the sheet also plays the looping roar: 3D, loud up close, gone past ~70 m.
    /// </summary>
    public class WaterfallSurface : MonoBehaviour
    {
        /// <summary>Every live waterfall renderer, for the upscaler's reactive mask pass.</summary>
        public static readonly List<Renderer> All = new List<Renderer>();
        [Tooltip("Plays the waterfall sound from here")] public bool sound;
        [Range(0, 1)] public float volume = 0.9f;
        static AudioClip _roar;
        Renderer _r;

        void OnEnable() { _r = GetComponent<Renderer>(); if (_r && !All.Contains(_r)) All.Add(_r); }
        void OnDisable() { if (_r) All.Remove(_r); }

        void Start()
        {
            if (!sound) return;
            var s = gameObject.AddComponent<AudioSource>();
            s.clip = _roar ? _roar : (_roar = MakeRoar());
            s.loop = true; s.volume = volume; s.spatialBlend = 1f; s.dopplerLevel = 0f;
            s.rolloffMode = AudioRolloffMode.Custom; s.minDistance = 3f; s.maxDistance = 70f;
            // loudness: full within ~4 m, -12 dB at 20 m, silent by 70 m
            s.SetCustomCurve(AudioSourceCurveType.CustomRolloff, new AnimationCurve(
                new Keyframe(0f, 1f), new Keyframe(0.06f, 1f), new Keyframe(0.28f, 0.28f), new Keyframe(0.6f, 0.07f), new Keyframe(1f, 0f)));
            s.time = Random.value * 5f;
            s.Play();
        }

        /// <summary>Brown noise rumble + a hissing top, slowly breathing; 8 s seamless loop.</summary>
        static AudioClip MakeRoar()
        {
            const int Rate = 44100; int n = Rate * 8, fade = Rate / 2;
            var d = new float[n];
            var rng = new System.Random(31);
            float brown = 0, lp = 0, hp = 0, prev = 0;
            for (int i = 0; i < n; i++)
            {
                float w = (float)rng.NextDouble() * 2 - 1;
                brown = Mathf.Clamp(brown + w * 0.02f, -1, 1) * 0.998f;
                lp += 0.18f * (w - lp);
                hp = 0.9f * (hp + lp - prev); prev = lp;
                float tt = i / (float)Rate;
                float breathe = 0.85f + 0.15f * Mathf.Sin(tt * 1.7f) * Mathf.Sin(tt * 0.61f + 1f);
                d[i] = (brown * 1.6f + lp * 0.35f + hp * 0.25f) * 0.55f * breathe;
            }
            for (int i = 0; i < fade; i++) { float t = i / (float)fade; d[i] = d[i] * t + d[n - fade + i] * (1 - t); }
            var c = AudioClip.Create("waterfall", n - fade, 1, Rate, false);
            var trimmed = new float[n - fade]; System.Array.Copy(d, trimmed, n - fade);
            c.SetData(trimmed, 0);
            return c;
        }
    }
}
