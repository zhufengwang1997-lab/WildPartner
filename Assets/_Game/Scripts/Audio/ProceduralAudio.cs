using UnityEngine;
using WildPartner.Core;
using WildPartner.Creatures;
using WildPartner.Player;
using WildPartner.World;

namespace WildPartner.Audio
{
    /// <summary>
    /// Game audio. Footsteps and the rain bed are CC0 field recordings (Resources/Audio, see docs/CREDITS.md,
    /// processed by tools/audio/process_sfx.py); wind, leaf drips in the rain, the capture chime, the meow and the
    /// background loop are synthesised at start-up.
    /// Footsteps come from animation events on the witch's foot-contact frames and pick their sound from the
    /// ground under her: grass, dirt (terrain splat layer 2), wooden floor (inside the cabin), shallow water.
    /// </summary>
    public class ProceduralAudio : MonoBehaviour
    {
        const int Rate = 44100;
        AudioSource _wind, _rain, _music, _sfx, _steps;
        AudioLowPassFilter _rainLp;
        AudioClip _step, _chime, _fail, _meow, _drip, _creak, _thud;
        AudioSource _fire;
        AudioClip[] _grass, _dirt, _wood, _water;
        int _lastVariant = -1;
        float _rainVol, _nextDrip;
        public enum Surface { Grass, Dirt, Wood, Water }
        public static Surface LastSurface { get; private set; }
        System.Random _rng = new System.Random(7);

        void Start()
        {
            _wind = Loop(MakeNoise(8f, 0.02f, 0.25f), 0.25f);
            var rainClip = Resources.Load<AudioClip>("Audio/rain_loop");
            _rain = Loop(rainClip ? rainClip : MakeNoise(6f, 0.25f, 0.6f), 0f);
            _rainLp = _rain.gameObject.AddComponent<AudioLowPassFilter>();   // muffled "on the roof" indoors
            _rainLp.cutoffFrequency = 22000;
            _grass = LoadSet("step_grass_", 6); _dirt = LoadSet("step_dirt_", 6); _wood = LoadSet("step_wood_", 4); _water = LoadSet("step_water_", 4);
            _steps = gameObject.AddComponent<AudioSource>(); _steps.playOnAwake = false;
            _drip = MakeDrip();
            _music = Loop(MakeMusic(), 0.22f);
            _sfx = gameObject.AddComponent<AudioSource>();
            _sfx.playOnAwake = false;
            _step = MakeStep();
            _chime = MakeChime(new[] { 0, 4, 7, 12, 16 }, 0.11f);
            _fail = MakeChime(new[] { 7, 3, 0 }, 0.12f);
            if (PlayerController.Instance)
            {
                var pc = PlayerController.Instance;
                pc.Footstep += PlayStep;
                // 1123c-B3: a big splash going in, a soft slosh on every stroke
                pc.EnteredWater += () => { if (_water.Length == 0) return; _steps.pitch = 0.7f; _steps.PlayOneShot(_water[0], 0.9f); _steps.PlayOneShot(_water[_water.Length - 1], 0.7f); };
                pc.SwimStroke += () => { if (_water.Length == 0) return; _steps.pitch = 0.75f + (float)_rng.NextDouble() * 0.12f; _steps.PlayOneShot(_water[_rng.Next(_water.Length)], pc.Running ? 0.42f : 0.28f); };
            }
            CaptureSystem.Captured += _ => _sfx.PlayOneShot(_chime, 0.7f);
            CaptureSystem.Interrupted += _ => _sfx.PlayOneShot(_fail, 0.5f);
            _creak = MakeCreak(); _thud = MakeThud();
            _fire = Loop(MakeCrackle(6f), 0f);
            CabinInterior.DoorOpened += () => _sfx.PlayOneShot(_creak, 0.55f);
            CabinInterior.DoorClosed += () => _sfx.PlayOneShot(_thud, 0.5f);
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

        AudioClip[] LoadSet(string prefix, int n)
        {
            var list = new System.Collections.Generic.List<AudioClip>();
            for (int i = 0; i < n; i++) { var c = Resources.Load<AudioClip>("Audio/" + prefix + i); if (c) list.Add(c); }
            return list.ToArray();
        }

        /// <summary>Inside the cabin (wooden floor, rain on the roof, fire crackling).</summary>
        static bool Indoors(Vector3 p) => EnvController.Indoors;

        public static Surface SurfaceAt(Vector3 p)
        {
            if (Indoors(p)) return Surface.Wood;
            if (WorldLayout.RiverDistance(p.x, p.z, out float lvl) < WorldLayout.RiverHalfWidth && p.y < lvl + 0.4f) return Surface.Water;
            if (WorldLayout.LakeDistance(p.x, p.z) < 1.02f && p.y < WorldLayout.LakeLevel + 0.4f) return Surface.Water;
            var t = Terrain.activeTerrain;
            if (t)
            {
                var td = t.terrainData;
                var lp = p - t.transform.position;
                int ax = Mathf.Clamp(Mathf.RoundToInt(lp.x / td.size.x * (td.alphamapWidth - 1)), 0, td.alphamapWidth - 1);
                int az = Mathf.Clamp(Mathf.RoundToInt(lp.z / td.size.z * (td.alphamapHeight - 1)), 0, td.alphamapHeight - 1);
                var a = td.GetAlphamaps(ax, az, 1, 1);
                if (td.alphamapLayers > 2 && a[0, 0, 2] > 0.5f) return Surface.Dirt;
            }
            return Surface.Grass;
        }

        void PlayStep()
        {
            var pc = PlayerController.Instance;
            if (!pc) return;
            var surf = SurfaceAt(pc.transform.position);
            LastSurface = surf;
            var set = surf == Surface.Dirt ? _dirt : surf == Surface.Wood ? _wood : surf == Surface.Water ? _water : _grass;
            if (set == null || set.Length == 0) { _sfx.PlayOneShot(_step, 0.3f); return; }
            int k = _rng.Next(set.Length);
            if (k == _lastVariant && set.Length > 1) k = (k + 1) % set.Length;   // never the same take twice in a row
            _lastVariant = k;
            // running: a little heavier (lower, louder); sneaking: lighter and softer; ±5 % pitch, ±15 % volume
            float basePitch = pc.Running ? 0.95f : pc.Sneaking ? 1.03f : 1f;
            float baseVol = pc.Running ? 0.55f : pc.Sneaking ? 0.16f : 0.36f;
            _steps.pitch = basePitch * (0.95f + (float)_rng.NextDouble() * 0.1f);
            _steps.PlayOneShot(set[k], baseVol * (0.85f + (float)_rng.NextDouble() * 0.3f));
        }

        void Update()
        {
            var w = Weather.Instance;
            float rain = w ? w.RainAmount : 0;
            // soft, low rain bed: ~4 s fade in/out on top of the weather ramp, kept under the music and footsteps
            _rainVol = Mathf.MoveTowards(_rainVol, rain * 0.16f, Time.unscaledDeltaTime * 0.16f / 4f);
            _rain.volume = _rainVol;
            bool inside = PlayerController.Instance && Indoors(PlayerController.Instance.transform.position);
            _rainLp.cutoffFrequency = Mathf.MoveTowards(_rainLp.cutoffFrequency, inside ? 650f : 22000f, Time.unscaledDeltaTime * 30000f);
            if (_rainVol > 0.02f && !inside && Time.time > _nextDrip)   // occasional drops ticking on leaves
            {
                _nextDrip = Time.time + 0.25f + (float)_rng.NextDouble() * 1.4f;
                _sfx.pitch = 0.8f + (float)_rng.NextDouble() * 0.5f;
                _sfx.PlayOneShot(_drip, _rainVol * (0.4f + (float)_rng.NextDouble() * 0.5f));
                _sfx.pitch = 1f;
            }
            _wind.volume = (0.18f + rain * 0.04f) * (inside ? 0.25f : 1f);
            _fire.volume = Mathf.MoveTowards(_fire.volume, inside ? 0.3f : 0f, Time.unscaledDeltaTime * 0.6f);
            bool menu = GameFlow.Instance && GameFlow.Instance.State == GameFlow.FlowState.MainMenu;
            _music.volume = Mathf.MoveTowards(_music.volume, menu ? 0.3f : 0.18f, Time.unscaledDeltaTime * 0.2f);
        }

        // ---------------------------------------------------------------- synthesis
        /// <summary>Wooden door creak (~0.75 s): a stick-slip buzz whose pitch wanders down, through a wood resonance.</summary>
        AudioClip MakeCreak()
        {
            int n = (int)(Rate * 0.75f);
            var d = new float[n];
            float ph = 0, lp = 0, bp1 = 0, bp2 = 0;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)n;
                float f = Mathf.Lerp(190f, 120f, t) * (1 + 0.12f * Mathf.Sin(t * 23f) + 0.05f * Mathf.Sin(t * 71f));
                ph += f / Rate;
                float pulse = Mathf.Pow(Mathf.Repeat(ph, 1f), 6f) * 2 - 0.3f;           // sharp stick-slip pulses
                float noise = ((float)_rng.NextDouble() * 2 - 1) * 0.15f;
                float x = pulse + noise;
                // two resonators (~700 Hz, ~1.6 kHz) for the hollow wooden body
                bp1 += 0.12f * (x - bp1); bp2 += 0.3f * (x - bp2);
                lp += 0.5f * ((bp1 - bp2 * 0.6f) - lp);
                float env = Mathf.Clamp01(t * 12f) * Mathf.Clamp01((1 - t) * 4f) * (0.6f + 0.4f * Mathf.Sin(t * 9f));
                d[i] = lp * env * 0.9f;
            }
            var c = AudioClip.Create("creak", n, 1, Rate, false); c.SetData(d, 0); return c;
        }

        /// <summary>Door shutting: a low wooden knock with a short rattle.</summary>
        AudioClip MakeThud()
        {
            int n = (int)(Rate * 0.4f);
            var d = new float[n];
            float y = 0;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float body = Mathf.Sin(2 * Mathf.PI * 95f * t) * Mathf.Exp(-t * 18f) + Mathf.Sin(2 * Mathf.PI * 210f * t) * Mathf.Exp(-t * 30f) * 0.5f;
                y += 0.25f * (((float)_rng.NextDouble() * 2 - 1) - y);
                float rattle = y * Mathf.Exp(-t * 40f) * 0.5f;
                d[i] = (body + rattle) * 0.8f;
            }
            var c = AudioClip.Create("thud", n, 1, Rate, false); c.SetData(d, 0); return c;
        }

        /// <summary>Fireplace: a soft roar with random crackles and pops (loops).</summary>
        AudioClip MakeCrackle(float seconds)
        {
            int n = (int)(Rate * seconds);
            var d = new float[n];
            float roar = 0, pop = 0, popEnv = 0;
            for (int i = 0; i < n; i++)
            {
                roar += 0.02f * (((float)_rng.NextDouble() * 2 - 1) - roar);
                if (_rng.NextDouble() < 0.00045) popEnv = 0.4f + (float)_rng.NextDouble() * 0.6f;
                pop = ((float)_rng.NextDouble() * 2 - 1) * popEnv;
                popEnv *= 0.993f;
                d[i] = roar * 1.2f + pop * 0.5f;
            }
            int fade = Rate / 20;   // seamless loop: cross-fade the tail into the head
            for (int i = 0; i < fade; i++) { float k = i / (float)fade; d[i] = d[i] * k + d[n - fade + i] * (1 - k); }
            var c = AudioClip.Create("fire", n - fade, 1, Rate, false);
            var o = new float[n - fade]; System.Array.Copy(d, o, n - fade); c.SetData(o, 0); return c;
        }

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

        /// <summary>A tiny water drop on a leaf: a soft band-limited tick with a short resonant "tip".</summary>
        AudioClip MakeDrip()
        {
            int n = Rate / 20;
            var d = new float[n];
            float y = 0, prev = 0;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float env = Mathf.Exp(-t * 90f);
                float white = (float)_rng.NextDouble() * 2 - 1;
                y += 0.45f * (white - y);
                float hp = y - prev; prev = y;   // band-limit: low-pass then high-pass
                d[i] = (hp * 0.6f + Mathf.Sin(2 * Mathf.PI * 2300f * t) * 0.25f * Mathf.Exp(-t * 140f)) * env;
            }
            var c = AudioClip.Create("drip", n, 1, Rate, false); c.SetData(d, 0); return c;
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
