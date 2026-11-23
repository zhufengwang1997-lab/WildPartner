using System;
using UnityEngine;
using UnityEngine.Rendering;
using WildPartner.Core;

namespace WildPartner.World
{
    /// <summary>
    /// Day/night cycle (one day = 8 real minutes, FR-G05): sun direction and colour, ambient trilight,
    /// fog, sky gradient (global properties read by WildPartner/Sky) and night stars. Also owns the global
    /// wind parameters for the Toon shader (current + previous frame time for motion vectors).
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public class EnvController : MonoBehaviour
    {
        public static EnvController Instance { get; private set; }

        [Header("Time")]
        public float dayLengthSeconds = 480f;
        [Range(0, 24)] public float timeOfDay = 9f;
        public bool paused;

        [Header("Scene refs")]
        public Light sun;
        public Weather weather;

        [Header("Wind")]
        public Vector2 windDirection = new Vector2(0.8f, 0.6f);
        public float windStrength = 1f;

        public event Action<int> HourChanged;
        public bool IsNight => timeOfDay < 5.5f || timeOfDay > 19f;
        public bool IsDuskOrDawn => (timeOfDay > 5f && timeOfDay < 8f) || (timeOfDay > 17f && timeOfDay < 20f);
        /// <summary>When set (benchmark scenes), time and wind phase are frozen for deterministic frames.</summary>
        public static bool Deterministic;

        int _lastHour = -1;
        float _windTime, _windTimePrev;

        static readonly int ZenithId = Shader.PropertyToID("_WP_SkyZenith");
        static readonly int HorizonId = Shader.PropertyToID("_WP_SkyHorizon");
        static readonly int GroundId = Shader.PropertyToID("_WP_SkyGround");
        static readonly int SunColId = Shader.PropertyToID("_WP_SunColor");
        static readonly int SunDirId = Shader.PropertyToID("_WP_SunDir");
        static readonly int WindId = Shader.PropertyToID("_WP_Wind");
        static readonly int WindTimeId = Shader.PropertyToID("_WP_WindTime");
        static readonly int WindTimePrevId = Shader.PropertyToID("_WP_WindTimePrev");

        // Key frames: hour, sun colour, sun intensity, sky zenith, sky horizon, fog colour, ambient sky.
        struct Key { public float h; public Color sun; public float sunI; public Color zen; public Color hor; public Color fog; public Color amb; }
        static readonly Key[] Keys =
        {
            K(0,   "#8FA6FF", 0.18f, "#0B1230", "#24305A", "#1C2546", "#202A52"),
            K(4.5f,"#8FA6FF", 0.15f, "#101838", "#2C3866", "#26305A", "#26305A"),
            K(6,   "#FF9A5C", 0.9f,  "#5C7BC8", "#FFB08A", "#D8A0A0", "#7E7FA8"),
            K(8,   "#FFE0AA", 1.9f,  "#3F86E6", "#F7D6D0", "#B4C8EA", "#8FA6D0"),
            K(12,  "#FFF0D2", 2.3f,  "#2C76E8", "#DCE4F0", "#A8C6EC", "#94A2D6"),
            K(16,  "#FFC98A", 2.5f,  "#2F70DE", "#F6D6C2", "#A4C2E8", "#8A88CC"),
            K(17,  "#FFB070", 2.2f,  "#3A6AD2", "#FCC6A4", "#B4BEE0", "#8680C0"),
            K(18,  "#FF8A50", 1.3f,  "#4C5EA8", "#FF9A70", "#B88A98", "#7E709C"),
            K(19.5f,"#9AA8FF",0.25f, "#1E2650", "#5A4C80", "#3A3A66", "#3A3E70"),
            K(24,  "#8FA6FF", 0.18f, "#0B1230", "#24305A", "#1C2546", "#202A52"),
        };

        static Key K(float h, string sun, float i, string z, string hor, string fog, string amb)
        {
            ColorUtility.TryParseHtmlString(sun, out var s); ColorUtility.TryParseHtmlString(z, out var zz);
            ColorUtility.TryParseHtmlString(hor, out var hh); ColorUtility.TryParseHtmlString(fog, out var f);
            ColorUtility.TryParseHtmlString(amb, out var a);
            return new Key { h = h, sun = s, sunI = i, zen = zz, hor = hh, fog = f, amb = a };
        }

        static readonly int FogColId = Shader.PropertyToID("_WP_FogColor");
        static readonly int FogSunId = Shader.PropertyToID("_WP_FogSunColor");
        static readonly int FogParamsId = Shader.PropertyToID("_WP_FogParams");
        static readonly int FogParams2Id = Shader.PropertyToID("_WP_FogParams2");
        /// <summary>Height-fog density multiplier (settings / quality); 0 turns the haze off.</summary>
        public static float FogAmount = 1f;
        /// <summary>True while the player is inside the cabin (set by CabinInterior).</summary>
        public static bool Indoors;
        /// <summary>Sky colour and daylight for the cabin windows.</summary>
        public static Color OutdoorSky = Color.white, OutdoorLight = Color.white;
        /// <summary>Near-camera dither fade for Toon objects (m): hidden below x, fully visible above y.</summary>
        public static Vector2 NearFade = new Vector2(0.6f, 2.4f);
        static readonly int NearFadeId = Shader.PropertyToID("_WP_NearFade");

        void Awake()
        {
            Instance = this;
            RenderSettings.fog = false;   // the HeightFog renderer feature does aerial perspective (画面整改 4)
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.ambientMode = AmbientMode.Trilight;
        }

        void OnDestroy() { if (Instance == this) Instance = null; }

        void Update()
        {
            if (!paused && !Deterministic)
                timeOfDay = Mathf.Repeat(timeOfDay + Time.deltaTime * 24f / dayLengthSeconds, 24f);
            Apply();
            int h = Mathf.FloorToInt(timeOfDay);
            if (h != _lastHour) { _lastHour = h; HourChanged?.Invoke(h); }
        }

        public void Apply()
        {
            Key a = Keys[0], b = Keys[1];
            for (int i = 0; i < Keys.Length - 1; i++)
                if (timeOfDay >= Keys[i].h && timeOfDay <= Keys[i + 1].h) { a = Keys[i]; b = Keys[i + 1]; break; }
            float t = Mathf.InverseLerp(a.h, b.h, timeOfDay);
            float rain = weather ? weather.RainAmount : 0f;

            Color sunCol = Color.Lerp(a.sun, b.sun, t);
            float sunI = Mathf.Lerp(a.sunI, b.sunI, t) * (1 - 0.55f * rain);
            Color zen = Color.Lerp(Color.Lerp(a.zen, b.zen, t), new Color(0.45f, 0.5f, 0.58f), rain * 0.7f);
            Color hor = Color.Lerp(Color.Lerp(a.hor, b.hor, t), new Color(0.6f, 0.63f, 0.68f), rain * 0.7f);
            Color fog = Color.Lerp(Color.Lerp(a.fog, b.fog, t), new Color(0.55f, 0.6f, 0.66f), rain * 0.6f);
            Color amb = Color.Lerp(a.amb, b.amb, t);

            // Sun path: rises in the east (6h), sets in the west (18h); at night the light becomes the moon.
            bool night = timeOfDay < 6f || timeOfDay > 18.5f;
            float dayT = Mathf.InverseLerp(6f, 18.5f, timeOfDay);
            float nightT = timeOfDay > 18.5f ? Mathf.InverseLerp(18.5f, 30f, timeOfDay) : Mathf.InverseLerp(-5.5f, 6f, timeOfDay);
            float elev = night ? Mathf.Sin(nightT * Mathf.PI) * 50f + 8f : Mathf.Sin(dayT * Mathf.PI) * 62f + 4f;
            float azim = night ? Mathf.Lerp(90f, 270f, nightT) : Mathf.Lerp(90f, 270f, dayT);
            if (sun)
            {
                sun.transform.rotation = Quaternion.Euler(elev, azim - 90f + 30f, 0);
                sun.color = sunCol;
                sun.intensity = sunI;
                sun.shadowStrength = night ? 0.6f : 0.9f;
            }

            // lower fill than before: lit faces pop, shade stays blue-purple instead of grey (画面整改 4)
            RenderSettings.ambientSkyColor = amb * 0.85f * (1 - 0.2f * rain);
            RenderSettings.ambientEquatorColor = Color.Lerp(amb, fog, 0.4f) * 0.75f;
            RenderSettings.ambientGroundColor = Color.Lerp(amb, new Color(0.40f, 0.34f, 0.62f), 0.45f) * 0.45f;
            RenderSettings.fog = false;
            RenderSettings.fogColor = fog;

            // height fog: thick in the valley, thin above it; warm towards the sun, cool blue elsewhere
            Color fogSun = Color.Lerp(sunCol, Color.white, 0.25f) * Mathf.Clamp01(sunI / 1.5f);
            Shader.SetGlobalColor(FogColId, Color.Lerp(fog, hor, 0.25f));
            Shader.SetGlobalColor(FogSunId, new Color(fogSun.r, fogSun.g, fogSun.b, night ? 0f : 0.75f * (1 - rain)));
            float density = (night ? 0.0032f : 0.0034f) * (1 + rain * 2.2f) * FogAmount;
            Shader.SetGlobalVector(FogParamsId, new Vector4(density, 0.04f, 26f, Mathf.Lerp(0.74f, 0.95f, rain)));
            Shader.SetGlobalVector(FogParams2Id, new Vector4(Mathf.Lerp(30f, 8f, rain), 0.85f, 0, 0));

            // indoors the camera is often 1–2 m from the witch: only dither what is really in the lens
            Shader.SetGlobalVector(NearFadeId, Indoors ? new Vector4(0.15f, 0.55f, 0, 0) : new Vector4(NearFade.x, NearFade.y, 0, 0));
            Shader.SetGlobalColor(ZenithId, zen);
            Shader.SetGlobalColor(HorizonId, hor);
            Shader.SetGlobalColor(GroundId, Color.Lerp(hor, fog, 0.6f) * 0.8f);
            Shader.SetGlobalColor(SunColId, night ? Color.black : sunCol * (1 - rain));
            Vector3 toSun = sun ? -sun.transform.forward : Vector3.up;
            float starF = Mathf.Clamp01(night ? 1 - Mathf.Abs(nightT - 0.5f) * 1.2f : 0) * (1 - rain);
            Shader.SetGlobalVector(SunDirId, new Vector4(toSun.x, toSun.y, toSun.z, starF));

            // what the windows of the cabin show (试玩反馈 1123b-3)
            OutdoorSky = Color.Lerp(hor, zen, 0.35f);
            OutdoorLight = (night ? new Color(0.45f, 0.55f, 0.85f) * 0.25f : sunCol * Mathf.Clamp01(sunI / 1.8f)) * (1 - 0.5f * rain);
            if (Indoors)
            {
                // inside the cabin: no sun, no valley haze; a warm low fill, the fire and lamps do the rest
                if (sun) { sun.intensity = 0f; sun.shadows = LightShadows.None; }
                Color warm = new Color(0.42f, 0.30f, 0.22f) * (night ? 0.55f : 0.8f);
                RenderSettings.ambientSkyColor = warm;
                RenderSettings.ambientEquatorColor = warm * 0.85f;
                RenderSettings.ambientGroundColor = warm * 0.6f;
                Shader.SetGlobalVector(FogParamsId, Vector4.zero);
            }
            else if (sun) sun.shadows = LightShadows.Soft;
        }

        /// <summary>Benchmarks: restart the wind clock so every capture sees the same grass pose.</summary>
        public void ResetWindClock() { _windTime = 0f; _windTimePrev = 0f; }

        void LateUpdate()
        {
            // Wind for the Toon shader. Previous time is stored so the motion-vector pass can
            // reconstruct last frame's vertex position exactly (FR-G07).
            _windTimePrev = _windTime;
            if (!Deterministic) _windTime += Time.deltaTime;
            float rain = weather ? weather.RainAmount : 0f;
            var d = windDirection.normalized;
            float gust = 0.75f + 0.25f * Mathf.Sin(_windTime * 0.21f) * Mathf.Sin(_windTime * 0.37f + 1);
            Shader.SetGlobalVector(WindId, new Vector4(d.x, d.y, windStrength * gust * (1 + rain * 0.8f), 0));
            Shader.SetGlobalFloat(WindTimeId, _windTime);
            Shader.SetGlobalFloat(WindTimePrevId, _windTimePrev);
        }

        public string ClockText()
        {
            int h = Mathf.FloorToInt(timeOfDay);
            int m = Mathf.FloorToInt((timeOfDay - h) * 60);
            string part = timeOfDay < 5 ? "深夜" : timeOfDay < 8 ? "清晨" : timeOfDay < 11 ? "上午" : timeOfDay < 14 ? "中午" :
                          timeOfDay < 17 ? "下午" : timeOfDay < 19.5f ? "黄昏" : "夜晚";
            return $"{part} {h:00}:{m:00}";
        }
    }
}
