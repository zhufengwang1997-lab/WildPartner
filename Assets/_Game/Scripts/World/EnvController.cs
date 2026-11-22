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
            K(8,   "#FFE2B0", 1.6f,  "#6FA6E8", "#F7D6D8", "#C8D2E8", "#9AB0D0"),
            K(12,  "#FFF1D6", 2.0f,  "#5E9AE6", "#EEDDE6", "#CFDDF0", "#A4ACD8"),
            K(16,  "#FFD399", 1.9f,  "#6E9EE0", "#FBD3C2", "#D6D8EC", "#9C98CE"),
            K(17,  "#FFBE7E", 1.6f,  "#6A92D8", "#FFC6A8", "#DCCFE0", "#968CC4"),
            K(18,  "#FF8A50", 1.0f,  "#4C5EA8", "#FF9A70", "#C88A90", "#8A7A9E"),
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

        void Awake()
        {
            Instance = this;
            RenderSettings.fog = true;
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

            RenderSettings.ambientSkyColor = amb * (1 - 0.2f * rain);
            RenderSettings.ambientEquatorColor = Color.Lerp(amb, fog, 0.5f) * 0.9f;
            RenderSettings.ambientGroundColor = Color.Lerp(amb, new Color(0.42f, 0.36f, 0.62f), 0.35f) * 0.5f;   // blue-purple shadow fill
            RenderSettings.fogColor = fog;
            RenderSettings.fogStartDistance = Mathf.Lerp(140f, 40f, rain);   // light aerial haze only (PRD-补充 §3)
            RenderSettings.fogEndDistance = Mathf.Lerp(1100f, 380f, rain) * (night ? 0.8f : 1f);

            Shader.SetGlobalColor(ZenithId, zen);
            Shader.SetGlobalColor(HorizonId, hor);
            Shader.SetGlobalColor(GroundId, Color.Lerp(hor, fog, 0.6f) * 0.8f);
            Shader.SetGlobalColor(SunColId, night ? Color.black : sunCol * (1 - rain));
            Vector3 toSun = sun ? -sun.transform.forward : Vector3.up;
            float starF = Mathf.Clamp01(night ? 1 - Mathf.Abs(nightT - 0.5f) * 1.2f : 0) * (1 - rain);
            Shader.SetGlobalVector(SunDirId, new Vector4(toSun.x, toSun.y, toSun.z, starF));
        }

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
