using UnityEngine;
using WildPartner.Core;
using WildPartner.Creatures;
using WildPartner.Player;
using WildPartner.World;

namespace WildPartner.Audio
{
    /// <summary>
    /// All sound is synthesised at start-up (no third-party audio, so no licence questions — see CREDITS):
    /// wind and rain beds (filtered noise), footsteps (short noise bursts), a capture chime (pentatonic
    /// arpeggio) and a gentle background loop (pentatonic plucks over a soft pad).
    /// </summary>
    public class ProceduralAudio : MonoBehaviour
    {
        const int Rate = 44100;
        AudioSource _wind, _rain, _music, _sfx;
        AudioClip _step, _chime, _fail, _meow;
        System.Random _rng = new System.Random(7);

        void Start()
        {
            _wind = Loop(MakeNoise(8f, 0.02f, 0.25f), 0.25f);
            _rain = Loop(MakeNoise(6f, 0.25f, 0.6f), 0f);
            _music = Loop(MakeMusic(), 0.22f);
            _sfx = gameObject.AddComponent<AudioSource>();
            _sfx.playOnAwake = false;
            _step = MakeStep();
            _chime = MakeChime(new[] { 0, 4, 7, 12, 16 }, 0.11f);
            _fail = MakeChime(new[] { 7, 3, 0 }, 0.12f);
            if (PlayerController.Instance) PlayerController.Instance.Footstep += () => _sfx.PlayOneShot(_step, PlayerController.Instance.Running ? 0.5f : 0.3f);
            CaptureSystem.Captured += _ => _sfx.PlayOneShot(_chime, 0.7f);
            CaptureSystem.Interrupted += _ => _sfx.PlayOneShot(_fail, 0.5f);
            _meow = MakeMeow();
            Creature.Meowed += p =>
            {
                float d = PlayerController.Instance ? Vector3.Distance(p, PlayerController.Instance.transform.position) : 5f;
                _sfx.pitch = 0.95f + (float)_rng.NextDouble() * 0.12f;
                _sfx.PlayOneShot(_meow, Mathf.Lerp(0.55f, 0.15f, d / 25f));
                _sfx.pitch = 1f;
            };
        }

        AudioSource Loop(AudioClip c, float vol)
        {
            var s = gameObject.AddComponent<AudioSource>();
            s.clip = c; s.loop = true; s.volume = vol; s.Play();
            return s;
        }

        void Update()
        {
            var w = Weather.Instance;
            float rain = w ? w.RainAmount : 0;
            _rain.volume = rain * 0.45f;
            _wind.volume = 0.18f + rain * 0.12f;
            bool menu = GameFlow.Instance && GameFlow.Instance.State == GameFlow.FlowState.MainMenu;
            _music.volume = Mathf.MoveTowards(_music.volume, menu ? 0.3f : 0.18f, Time.unscaledDeltaTime * 0.2f);
        }

        // ---------------------------------------------------------------- synthesis
        AudioClip MakeNoise(float seconds, float lp, float gain)
        {
            int n = (int)(Rate * seconds);
            var d = new float[n];
            float y = 0, y2 = 0;
            for (int i = 0; i < n; i++)
            {
                float white = (float)_rng.NextDouble() * 2 - 1;
                y += lp * (white - y);                    // one-pole low-pass
                y2 += lp * 0.5f * (y - y2);
                float swell = 0.6f + 0.4f * Mathf.Sin(i / (float)Rate * 0.9f) * Mathf.Sin(i / (float)Rate * 0.37f + 1);
                d[i] = y2 * gain * 6f * swell;
            }
            // cross-fade the loop seam
            int fade = Rate / 4;
            for (int i = 0; i < fade; i++) { float t = i / (float)fade; d[i] = d[i] * t + d[n - fade + i] * (1 - t); }
            var c = AudioClip.Create("noise", n - fade, 1, Rate, false);
            var trimmed = new float[n - fade]; System.Array.Copy(d, trimmed, n - fade);
            c.SetData(trimmed, 0);
            return c;
        }

        AudioClip MakeStep()
        {
            int n = Rate / 12;
            var d = new float[n];
            float y = 0;
            for (int i = 0; i < n; i++)
            {
                float env = Mathf.Exp(-i / (float)n * 7f);
                y += 0.3f * (((float)_rng.NextDouble() * 2 - 1) - y);
                d[i] = y * env * 0.8f;
            }
            var c = AudioClip.Create("step", n, 1, Rate, false); c.SetData(d, 0); return c;
        }

        /// <summary>"mi-aow": a voiced glide (pitch up then down) through a moving formant, ~0.5 s.</summary>
        AudioClip MakeMeow()
        {
            int n = (int)(Rate * 0.5f);
            var d = new float[n];
            double ph = 0;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)n;
                float f0 = Mathf.Lerp(520f, 760f, Mathf.SmoothStep(0, 1, t / 0.35f)) - Mathf.SmoothStep(0, 1, (t - 0.35f) / 0.65f) * 300f;
                ph += 2 * Mathf.PI * f0 / Rate;
                float formant = Mathf.Lerp(1200f, 2400f, Mathf.Sin(Mathf.PI * Mathf.Clamp01(t * 1.3f)));   // "i" -> "a" -> "ow"
                float s = 0;
                for (int h = 1; h <= 8; h++)
                {
                    float fh = f0 * h, w = Mathf.Exp(-Mathf.Pow((fh - formant) / 700f, 2)) + 0.25f / h;
                    s += w * Mathf.Sin((float)(ph * h));
                }
                float env = Mathf.SmoothStep(0, 1, t / 0.06f) * (1 - Mathf.SmoothStep(0, 1, (t - 0.7f) / 0.3f));
                float breath = ((float)_rng.NextDouble() * 2 - 1) * 0.04f;
                d[i] = (s * 0.22f + breath) * env;
            }
            var c = AudioClip.Create("meow", n, 1, Rate, false); c.SetData(d, 0); return c;
        }

        static float Midi(int note) => 440f * Mathf.Pow(2, (note - 69) / 12f);

        AudioClip MakeChime(int[] steps, float spacing)
        {
            int n = (int)(Rate * (spacing * steps.Length + 1.2f));
            var d = new float[n];
            for (int k = 0; k < steps.Length; k++)
            {
                float f = Midi(76 + steps[k]);
                int start = (int)(k * spacing * Rate);
                for (int i = start; i < n; i++)
                {
                    float t = (i - start) / (float)Rate;
                    d[i] += Mathf.Sin(2 * Mathf.PI * f * t) * Mathf.Exp(-t * 4f) * 0.25f
                          + Mathf.Sin(2 * Mathf.PI * f * 2 * t) * Mathf.Exp(-t * 7f) * 0.08f;
                }
            }
            var c = AudioClip.Create("chime", n, 1, Rate, false); c.SetData(d, 0); return c;
        }

        AudioClip MakeMusic()
        {
            // 16 bars at 72 bpm in C major pentatonic, plucks over a two-chord pad (C - Am - F - G).
            float beat = 60f / 72f;
            int beats = 64;
            int n = (int)(Rate * beat * beats);
            var d = new float[n];
            int[] scale = { 0, 2, 4, 7, 9, 12, 14, 16 };
            int[][] chords = { new[] { 48, 55, 64 }, new[] { 45, 52, 60 }, new[] { 41, 48, 57 }, new[] { 43, 50, 59 } };
            var rng = new System.Random(42);
            for (int bar = 0; bar < 16; bar++)
            {
                var chord = chords[bar % 4];
                int bs = (int)(bar * 4 * beat * Rate);
                int be = Mathf.Min(n, (int)((bar + 1) * 4 * beat * Rate));
                for (int i = bs; i < be; i++)
                {
                    float t = i / (float)Rate;
                    float local = (i - bs) / (float)(be - bs);
                    float env = Mathf.Sin(local * Mathf.PI) * 0.9f + 0.1f;
                    float s = 0;
                    foreach (int note in chord) s += Mathf.Sin(2 * Mathf.PI * Midi(note) * t) + 0.3f * Mathf.Sin(2 * Mathf.PI * Midi(note) * 2.003f * t);
                    d[i] += s * 0.03f * env;
                }
                for (int b = 0; b < 8; b++)   // eighth-note plucks, some rests
                {
                    if (rng.NextDouble() < 0.35) continue;
                    int note = 72 + scale[rng.Next(scale.Length)] - (b % 4 == 0 ? 12 : 0);
                    float f = Midi(note);
                    int st = (int)((bar * 4 + b * 0.5f) * beat * Rate);
                    for (int i = st; i < Mathf.Min(n, st + Rate * 2); i++)
                    {
                        float t = (i - st) / (float)Rate;
                        d[i] += (Mathf.Sin(2 * Mathf.PI * f * t) + 0.2f * Mathf.Sin(4 * Mathf.PI * f * t)) * Mathf.Exp(-t * 3.2f) * 0.09f;
                    }
                }
            }
            var c = AudioClip.Create("music", n, 1, Rate, false); c.SetData(d, 0); return c;
        }
    }
}
