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
    /// <summary>1123e-4: the three volume categories from the settings menu (master is AudioListener.volume).</summary>
    public static class AudioMix
    {
        public static float Music => SaveSystem.Settings != null ? SaveSystem.Settings.musicVolume : 1f;
        public static float Sfx => SaveSystem.Settings != null ? SaveSystem.Settings.sfxVolume : 1f;
        public static float Ambience => SaveSystem.Settings != null ? SaveSystem.Settings.ambienceVolume : 1f;
    }

    public class ProceduralAudio : MonoBehaviour
    {
        // 1123e-4 mix, loudest to quietest: music > ambience (wind, rain, water, fire) > footsteps.
        // Smoke test compares the effective loudness (clip RMS x level) of each layer; see LoudnessReport.
        // Targets (effective RMS): music ~0.040; wind 0.014, rain 0.016, fire 0.016, waterfall up close 0.030;
        // footsteps walk 0.008, run 0.012, sneak 0.002; meow 0.025, purr 0.020, capture chime 0.035, door 0.025.
        public const float MusicGame = 0.85f, MusicMenu = 0.95f;
        public const float WindLevel = 0.36f, RainLevel = 0.235f, FireLevel = 0.17f, DripLevel = 0.5f;
        public const float StepWalk = 0.12f, StepRun = 0.18f, StepSneak = 0.03f;
        public const float MeowLevel = 0.13f, PurrLevel = 0.14f;
        public const float ChimeLevel = 0.21f, DoorLevel = 0.22f;
        /// <summary>Effective RMS of each layer at its normal level (smoke test): name -> (rms, peak).</summary>
        public static readonly System.Collections.Generic.Dictionary<string, Vector2> LoudnessReport = new System.Collections.Generic.Dictionary<string, Vector2>();
        public static bool Purring { get; private set; }
        AudioSource _purr;
        float _purrVol;
        AudioClip _purrClip;
        Creature _cat; float _nextCatLookup;
        const int Rate = 44100;
        AudioSource _wind, _rain, _music, _sfx, _steps;
        AudioLowPassFilter _rainLp;
        AudioClip _step, _chime, _fail, _meow, _drip, _creak, _thud;
        AudioSource _fire;
        AudioClip[] _grass, _dirt, _wood, _water;
        int _lastVariant = -1;
        float _rainVol, _nextDrip, _fireVol, _musicVol = MusicGame;
        public enum Surface { Grass, Dirt, Wood, Water }
        public static Surface LastSurface { get; private set; }
        System.Random _rng = new System.Random(7);

        void Start()
        {
            _wind = Loop(MakeNoise(8f, 0.02f, 0.25f), WindLevel);
            var rainClip = Resources.Load<AudioClip>("Audio/rain_loop");
            _rain = Loop(rainClip ? rainClip : MakeNoise(6f, 0.25f, 0.6f), 0f);
            _rainLp = _rain.gameObject.AddComponent<AudioLowPassFilter>();   // muffled "on the roof" indoors
            _rainLp.cutoffFrequency = 22000;
            _grass = LoadSet("step_grass_", 6); _dirt = LoadSet("step_dirt_", 6); _wood = LoadSet("step_wood_", 4); _water = LoadSet("step_water_", 4);
            _steps = gameObject.AddComponent<AudioSource>(); _steps.playOnAwake = false;
            _drip = MakeDrip();
            _music = Loop(MakeMusic(), MusicGame);
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
                pc.EnteredWater += () => { if (_water.Length == 0) return; _steps.pitch = 0.7f; Step(_water[0], 0.3f); Step(_water[_water.Length - 1], 0.22f); };
                pc.SwimStroke += () => { if (_water.Length == 0) return; _steps.pitch = 0.75f + (float)_rng.NextDouble() * 0.12f; Step(_water[_rng.Next(_water.Length)], pc.Running ? 0.16f : 0.1f); };
            }
            CaptureSystem.Captured += _ => Sfx(_chime, ChimeLevel);
            CaptureSystem.Interrupted += _ => Sfx(_fail, ChimeLevel * 0.7f);
            _creak = MakeCreak(); _thud = MakeThud();
            _fire = Loop(MakeCrackle(6f), 0f);
            CabinInterior.DoorOpened += () => Sfx(_creak, DoorLevel);
            CabinInterior.DoorClosed += () => Sfx(_thud, DoorLevel * 0.85f);
            // 1123d-1 fishing: whoosh of the cast, the float's plop, the bite (plop + bell), reel clicks
            var whoosh = MakeWhoosh(); var tick = MakeTick(); var bell = MakeChime(new[] { 12, 19 }, 0.07f);
            FishingSystem.CastOut += () => Sfx(whoosh, 0.2f);
            FishingSystem.FloatLanded += () => { if (_water.Length > 0) { _steps.pitch = 1.45f; Step(_water[0], 0.3f); } };
            FishingSystem.Nibble += () => { if (_water.Length > 0) { _steps.pitch = 1.8f; Step(_water[_water.Length - 1], 0.14f); } };
            FishingSystem.Bite += () => { Sfx(bell, 0.22f); if (_water.Length > 0) { _steps.pitch = 1.1f; Step(_water[0], 0.45f); } };
            FishingSystem.Hooked += () => Sfx(whoosh, 0.14f);
            FishingSystem.ReelTick += () => Sfx(tick, 0.1f);
            FishingSystem.Lost += _ => Sfx(_fail, ChimeLevel * 0.65f);
            FishingSystem.Caught += koi => { if (_water.Length > 0) { _steps.pitch = 0.9f; Step(_water[0], 0.45f); } if (!koi) Sfx(bell, 0.12f); };
            _meow = MakeMeow();
            _purrClip = MakePurr();
            _purr = gameObject.AddComponent<AudioSource>(); _purr.clip = _purrClip; _purr.loop = true; _purr.volume = 0f; _purr.playOnAwake = false;
            BuildLoudnessReport();
            Creature.Meowed += p =>
            {
                float d = PlayerController.Instance ? Vector3.Distance(p, PlayerController.Instance.transform.position) : 5f;
                _sfx.pitch = 0.95f + (float)_rng.NextDouble() * 0.12f;
                Sfx(_meow, Mathf.Lerp(MeowLevel, 0.08f, d / 25f));
                _sfx.pitch = 1f;
            };
        }

        static AudioClip MakeWhoosh()
        {
            const int sr = 44100; int n = (int)(sr * 0.45f);
            var d = new float[n]; var r = new System.Random(7); float lp = 0;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)n, env = Mathf.Sin(t * Mathf.PI) * Mathf.Sin(t * Mathf.PI);
                float k = Mathf.Lerp(0.04f, 0.35f, Mathf.Sin(t * Mathf.PI));   // filter opens and closes: a swish
                lp += ((float)r.NextDouble() * 2 - 1 - lp) * k;
                d[i] = lp * env * 0.9f;
            }
            var c = AudioClip.Create("Whoosh", n, 1, sr, false); c.SetData(d, 0); return c;
        }

        static AudioClip MakeTick()
        {
            const int sr = 44100; int n = (int)(sr * 0.04f);
            var d = new float[n];
            for (int i = 0; i < n; i++) { float t = i / (float)sr; d[i] = Mathf.Sin(t * 2 * Mathf.PI * 2400f) * Mathf.Exp(-t * 180f) * 0.6f; }
            var c = AudioClip.Create("Tick", n, 1, sr, false); c.SetData(d, 0); return c;
        }

        /// <summary>A one-shot sound effect through the 音效 volume.</summary>
        void Sfx(AudioClip c, float v) { if (c) _sfx.PlayOneShot(c, v * AudioMix.Sfx); }
        void Step(AudioClip c, float v) { if (c) _steps.PlayOneShot(c, v * AudioMix.Sfx); }

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
            if (set == null || set.Length == 0) { Step(_step, StepWalk); return; }
            int k = _rng.Next(set.Length);
            if (k == _lastVariant && set.Length > 1) k = (k + 1) % set.Length;   // never the same take twice in a row
            _lastVariant = k;
            // running: a little heavier (lower, louder); sneaking: lighter and softer; ±5 % pitch, ±15 % volume
            float basePitch = pc.Running ? 0.95f : pc.Sneaking ? 1.03f : 1f;
            float baseVol = pc.Running ? StepRun : pc.Sneaking ? StepSneak : StepWalk;   // 1123e-4: barely there walking
            _steps.pitch = basePitch * (0.95f + (float)_rng.NextDouble() * 0.1f);
            Step(set[k], baseVol * (0.85f + (float)_rng.NextDouble() * 0.3f));
        }

        void Update()
        {
            var w = Weather.Instance;
            float rain = w ? w.RainAmount : 0;
            // soft, low rain bed: ~4 s fade in/out on top of the weather ramp, kept under the music and footsteps
            _rainVol = Mathf.MoveTowards(_rainVol, rain * RainLevel, Time.unscaledDeltaTime * RainLevel / 4f);
            _rain.volume = _rainVol * AudioMix.Ambience;
            bool inside = PlayerController.Instance && Indoors(PlayerController.Instance.transform.position);
            _rainLp.cutoffFrequency = Mathf.MoveTowards(_rainLp.cutoffFrequency, inside ? 650f : 22000f, Time.unscaledDeltaTime * 30000f);
            if (_rainVol > 0.02f && !inside && Time.time > _nextDrip)   // occasional drops ticking on leaves
            {
                _nextDrip = Time.time + 0.25f + (float)_rng.NextDouble() * 1.4f;
                _sfx.pitch = 0.8f + (float)_rng.NextDouble() * 0.5f;
                _sfx.PlayOneShot(_drip, _rainVol * DripLevel / RainLevel * (0.4f + (float)_rng.NextDouble() * 0.5f) * AudioMix.Ambience);
                _sfx.pitch = 1f;
            }
            _wind.volume = WindLevel * (1f + rain * 0.25f) * (inside ? 0.25f : 1f) * AudioMix.Ambience;
            _fireVol = Mathf.MoveTowards(_fireVol, inside ? FireLevel : 0f, Time.unscaledDeltaTime * 0.4f);
            _fire.volume = _fireVol * AudioMix.Ambience;
            bool menu = GameFlow.Instance && GameFlow.Instance.State == GameFlow.FlowState.MainMenu;
            _musicVol = Mathf.MoveTowards(_musicVol, menu ? MusicMenu : MusicGame, Time.unscaledDeltaTime * 0.2f);
            _music.volume = _musicVol * AudioMix.Music;
            UpdatePurr();
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

        /// <summary>1123e-4: a soft, short, low "mew" like a house cat asking for attention: a gentle rise and fall
        /// around 330-430 Hz (was 520-760), a round low formant, the highs rolled off (no harsh edge), breathy, 0.34 s.</summary>
        AudioClip MakeMeow()
        {
            int n = (int)(Rate * 0.34f);
            var d = new float[n];
            double ph = 0; float lp = 0, lp2 = 0;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)n;
                float f0 = Mathf.Lerp(330f, 430f, Mathf.SmoothStep(0, 1, t / 0.4f)) - Mathf.SmoothStep(0, 1, (t - 0.4f) / 0.6f) * 120f;
                ph += 2 * Mathf.PI * f0 / Rate;
                float formant = Mathf.Lerp(700f, 1250f, Mathf.Sin(Mathf.PI * Mathf.Clamp01(t * 1.15f)));   // "m" -> "e" -> "w"
                float s = 0;
                for (int h = 1; h <= 6; h++)
                {
                    float fh = f0 * h, w = Mathf.Exp(-Mathf.Pow((fh - formant) / 520f, 2)) * 0.8f + 0.55f / (h * h);
                    s += w * Mathf.Sin((float)(ph * h));
                }
                float env = Mathf.SmoothStep(0, 1, t / 0.14f) * (1 - Mathf.SmoothStep(0, 1, (t - 0.55f) / 0.45f));
                float breath = ((float)_rng.NextDouble() * 2 - 1) * 0.05f;
                float x = (s * 0.2f + breath) * env;
                lp += (x - lp) * 0.32f; lp2 += (lp - lp2) * 0.32f;                  // two-pole low-pass (~2.6 kHz)
                d[i] = lp2;
            }
            Normalise(d, 0.5f);
            var c = AudioClip.Create("meow", n, 1, Rate, false); c.SetData(d, 0); return c;
        }

        /// <summary>1123e-4: purring, a 2 s seamless loop: low rumble pulsing ~25 times a second, swelling with
        /// each breath (in, out).</summary>
        AudioClip MakePurr()
        {
            int n = Rate * 2;
            var d = new float[n];
            float lp = 0, lp2 = 0;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float w = (float)_rng.NextDouble() * 2 - 1;
                lp += (w - lp) * 0.03f; lp2 += (lp - lp2) * 0.05f;                    // dark rumble (~150 Hz)
                float pulse = Mathf.Pow(0.5f + 0.5f * Mathf.Sin(2 * Mathf.PI * 25f * t), 3f);
                float breath = 0.55f + 0.45f * Mathf.Pow(Mathf.Sin(Mathf.PI * t), 2f);   // two breaths per loop
                float tone = Mathf.Sin(2 * Mathf.PI * 50f * t) * 0.25f + Mathf.Sin(2 * Mathf.PI * 75f * t) * 0.12f;
                d[i] = (lp2 * 9f + tone) * pulse * breath;
            }
            // seamless: fade the first 50 ms in against the last 50 ms
            int f = Rate / 20;
            for (int i = 0; i < f; i++) { float k = i / (float)f; d[i] = d[i] * k + d[n - f + i] * (1 - k); }
            var trimmed = new float[n - f]; System.Array.Copy(d, trimmed, n - f);
            Normalise(trimmed, 0.6f);
            var c = AudioClip.Create("purr", n - f, 1, Rate, false); c.SetData(trimmed, 0); return c;
        }

        static void Normalise(float[] d, float peak)
        {
            float m = 1e-6f; foreach (var v in d) m = Mathf.Max(m, Mathf.Abs(v));
            for (int i = 0; i < d.Length; i++) d[i] *= peak / m;
        }

        /// <summary>The cat purrs while it rubs against her legs (mood 3).</summary>
        void UpdatePurr()
        {
            if (!_purr) return;
            if (Time.time >= _nextCatLookup)
            {
                _nextCatLookup = Time.time + 1f;
                _cat = null;
                foreach (var c in FindObjectsByType<Creature>(FindObjectsSortMode.None))
                    if (c.defId == "cat" && c.Current == Creature.State.Companion) { _cat = c; break; }
            }
            var pc = PlayerController.Instance;
            bool rub = _cat && _cat.PetShown && _cat.Mood == 3 && pc && Vector3.Distance(_cat.transform.position, pc.transform.position) < 2.5f;
            Purring = rub;
            float target = rub ? PurrLevel : 0f;
            _purrVol = Mathf.MoveTowards(_purrVol, target, Time.unscaledDeltaTime * PurrLevel / 0.6f);
            _purr.volume = _purrVol * AudioMix.Sfx;
            if (_purrVol > 0.001f && !_purr.isPlaying) _purr.Play();
            if (_purrVol <= 0.001f && _purr.isPlaying) _purr.Stop();
        }

        /// <summary>Effective loudness of one layer: RMS of the clip's sounding part x its level.</summary>
        static Vector2 Loudness(AudioClip c, float level)
        {
            if (!c) return Vector2.zero;
            var d = new float[c.samples * c.channels];
            if (!c.GetData(d, 0)) return Vector2.zero;
            double sum = 0; int cnt = 0; float peak = 0, gate = 0;
            foreach (var v in d) gate = Mathf.Max(gate, Mathf.Abs(v));
            gate *= 0.05f;                                               // ignore the silent tail
            foreach (var v in d) { float a = Mathf.Abs(v); peak = Mathf.Max(peak, a); if (a > gate) { sum += v * v; cnt++; } }
            return new Vector2(cnt > 0 ? Mathf.Sqrt((float)(sum / cnt)) * level : 0f, peak * level);
        }

        public static ProceduralAudio Instance { get; private set; }
        void Awake() => Instance = this;

        public void BuildLoudnessReport()
        {
            LoudnessReport.Clear();
            LoudnessReport["音乐"] = Loudness(_music.clip, MusicGame);
            LoudnessReport["风"] = Loudness(_wind.clip, WindLevel);
            LoudnessReport["雨"] = Loudness(_rain.clip, RainLevel);
            LoudnessReport["壁炉"] = Loudness(_fire.clip, FireLevel);
            LoudnessReport["瀑布（近处）"] = Loudness(WaterfallSurface.Roar, WaterfallSurface.Level * 0.9f);
            if (_grass.Length > 0) LoudnessReport["脚步（草地走路）"] = Loudness(_grass[0], StepWalk);
            if (_grass.Length > 0) LoudnessReport["脚步（草地跑步）"] = Loudness(_grass[0], StepRun);
            if (_wood.Length > 0) LoudnessReport["脚步（木地板走路）"] = Loudness(_wood[0], StepWalk);
            if (_dirt.Length > 0) LoudnessReport["脚步（土路走路）"] = Loudness(_dirt[0], StepWalk);
            if (_water.Length > 0) LoudnessReport["脚步（浅水走路）"] = Loudness(_water[0], StepWalk);
            LoudnessReport["猫叫"] = Loudness(_meow, MeowLevel);
            LoudnessReport["呼噜"] = Loudness(_purrClip, PurrLevel);
            LoudnessReport["捕捉成功"] = Loudness(_chime, ChimeLevel);
            LoudnessReport["开门"] = Loudness(_creak, DoorLevel);
            foreach (var kv in LoudnessReport) Debug.Log($"[Audio] {kv.Key}: rms {kv.Value.x:0.0000} peak {kv.Value.y:0.000}");
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
