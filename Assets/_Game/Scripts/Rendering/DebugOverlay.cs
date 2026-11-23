using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.Universal;

namespace WildPartner.Rendering
{
    /// <summary>
    /// F3 debug overlay (FR-UP-06, first part): frame rate / frame time, display and render resolution, the
    /// anti-aliasing mode with buttons to switch it (F4 cycles), tier and GPU. Drawn with IMGUI after the frame, at full
    /// display resolution (IN-07). The upscaler rows are added in the upscaling stage.
    /// </summary>
    public class DebugOverlay : MonoBehaviour
    {
        public static bool Visible { get; private set; }
        float _ms = 16f, _fps = 60f;
        GUIStyle _box, _label, _button, _active;

        void Awake()
        {
            DontDestroyOnLoad(gameObject);
            Visible = Core.SaveSystem.Settings.debugOverlay;
        }

        void Update()
        {
            var k = Keyboard.current;
            if (Core.GameInput.DebugPressed)
            {
                Visible = !Visible;
                Core.SaveSystem.Settings.debugOverlay = Visible; Core.SaveSystem.SaveSettings();
            }
            if (Visible && k != null && k.f4Key.wasPressedThisFrame) Cycle();
            float dt = Mathf.Max(Time.unscaledDeltaTime, 1e-4f);
            _ms = Mathf.Lerp(_ms, dt * 1000f, 0.05f);
            _fps = 1000f / _ms;
        }

        static void Cycle()
        {
            int n = System.Enum.GetValues(typeof(AAMode)).Length;
            var m = (AAMode)(((int)AntiAliasing.Mode + 1) % n);
            if (!AntiAliasing.Supported(m)) m = (AAMode)(((int)m + 1) % n);
            AntiAliasing.Set(m);
        }

        void Styles()
        {
            if (_box != null) return;
            _box = new GUIStyle(GUI.skin.box) { normal = { background = Tex(new Color(0.08f, 0.06f, 0.14f, 0.82f)) } };
            _label = new GUIStyle(GUI.skin.label) { fontSize = 15, normal = { textColor = new Color(0.93f, 0.9f, 1f) } };
            _button = new GUIStyle(GUI.skin.button) { fontSize = 15 };
            _active = new GUIStyle(_button) { fontStyle = FontStyle.Bold, normal = { textColor = new Color(1f, 0.85f, 0.45f) } };
        }

        static Texture2D Tex(Color c) { var t = new Texture2D(1, 1); t.SetPixel(0, 0, c); t.Apply(); return t; }

        void OnGUI()
        {
            if (!Visible) return;
            Styles();
            float scale = Mathf.Max(1f, Screen.height / 1080f);
            var m = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1));
            var asset = AntiAliasing.Asset;
            float rs = asset ? asset.renderScale : 1f;
            var host = Zfw.Upscaling.UpscalerHost.Active;
            int dw = Screen.width, dh = Screen.height;
            var r = new Rect(14, 160, 470, 420);
            GUI.Box(r, GUIContent.none, _box);
            GUILayout.BeginArea(new Rect(r.x + 12, r.y + 8, r.width - 24, r.height - 16));
            GUILayout.Label($"F3 调试   {_fps:0} fps   {_ms:0.0} ms", _label);
            GUILayout.Label($"显示 {dw}×{dh}   渲染 {Mathf.RoundToInt(dw * rs)}×{Mathf.RoundToInt(dh * rs)}（{rs * 100:0}%）", _label);
            GUILayout.Label(Zfw.Upscaling.UpscalerHost.IsUpscaling
                ? $"超分：{host.Name} · {UpscalerSettings.QualityLabel(UpscalerSettings.Quality)}   mip 偏移 {host.GetMipBias():0.00}   抖动 {host.GetJitter(Time.frameCount):+0.00;-0.00}"
                : "超分：关（原生分辨率）", _label);
            GUILayout.Label($"选择依据：{UpscalerSettings.LastReason}", _label);
            GUILayout.BeginHorizontal();
            foreach (var b in new[] { "", "STP", "FSR1", "RenderScale", UpscalerSettings.Off })
                if (GUILayout.Button(b == "" ? "自动" : UpscalerSettings.Label(b), (Core.SaveSystem.Settings.upscaler ?? "") == b ? _active : _button, GUILayout.Height(28)))
                    UpscalerSettings.Set(b, Core.SaveSystem.Settings.upscalerQuality);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            foreach (var q in new[] { "", "NativeAA", "Quality", "Balanced", "Performance" })
            {
                string lab = q == "" ? "自动" : UpscalerSettings.QualityLabel((Zfw.Upscaling.UpscalerQuality)System.Enum.Parse(typeof(Zfw.Upscaling.UpscalerQuality), q));
                if (GUILayout.Button(lab, (Core.SaveSystem.Settings.upscalerQuality ?? "") == q ? _active : _button, GUILayout.Height(28)))
                    UpscalerSettings.Set(Core.SaveSystem.Settings.upscaler, q);
            }
            GUILayout.EndHorizontal();
            GUILayout.Label($"画质档位 {Zfw.QualityTiers.QualityTiers.Current}   {SystemInfo.graphicsDeviceType}", _label);
            GUILayout.Label($"抗锯齿（F4 切换）：{AntiAliasing.Label(AntiAliasing.Mode)}", _label);
            GUILayout.BeginHorizontal();
            foreach (AAMode mode in System.Enum.GetValues(typeof(AAMode)))
            {
                GUI.enabled = AntiAliasing.Supported(mode);
                if (GUILayout.Button(AntiAliasing.Label(mode), mode == AntiAliasing.Mode ? _active : _button, GUILayout.Height(30))) AntiAliasing.Set(mode);
            }
            GUI.enabled = true;
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label("输入视图", _label, GUILayout.Width(80));
            foreach (InputView v in System.Enum.GetValues(typeof(InputView)))
            {
                string lab = v == InputView.None ? "画面" : v == InputView.MotionVectors ? "运动向量" : "Reactive Mask";
                if (GUILayout.Button(lab, UpscalerInputsPass.View == v ? _active : _button, GUILayout.Height(28))) UpscalerInputsPass.View = v;
            }
            GUILayout.EndHorizontal();
            GUILayout.Label($"Reactive Mask 来源 {UpscalerInputsPass.LastSourceCount} 个（当前后端未消费）  Reversed-Z {(SystemInfo.usesReversedZBuffer ? "是" : "否")}", _label);
            GUILayout.Label(SystemInfo.graphicsDeviceName, _label);
            GUILayout.EndArea();
            GUI.matrix = m;
        }
    }
}
