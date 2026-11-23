using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using WildPartner.Core;

namespace WildPartner.Rendering
{
    /// <summary>
    /// 11-24: display mode (borderless full screen / exclusive full screen / window) and resolution, saved in
    /// settings.json and re-applied at the next launch; F1 toggles borderless full screen and window.
    /// Every change is logged with the new render size, and the temporal history is dropped once the new
    /// size is live (the upscaler host re-initialises itself on the size change, TC-03), so nothing from the
    /// old resolution is blended into the first frames at the new one.
    /// </summary>
    public class DisplaySettings : MonoBehaviour
    {
        public static DisplaySettings Instance { get; private set; }
        /// <summary>Short on-screen notice ("全屏窗口" / "窗口"), shown by the UI.</summary>
        public static event Action<string> Notice;
        /// <summary>Count of applied display changes (smoke test).</summary>
        public static int Changes { get; private set; }

        public static readonly FullScreenMode[] Modes = Application.platform == RuntimePlatform.WindowsPlayer
            ? new[] { FullScreenMode.FullScreenWindow, FullScreenMode.ExclusiveFullScreen, FullScreenMode.Windowed }
            : new[] { FullScreenMode.FullScreenWindow, FullScreenMode.Windowed };   // exclusive is Windows-only

        public static string Label(FullScreenMode m) => m switch
        {
            FullScreenMode.FullScreenWindow => "全屏窗口",
            FullScreenMode.ExclusiveFullScreen => "独占全屏",
            FullScreenMode.MaximizedWindow => "全屏窗口",
            _ => "窗口",
        };

        int _w, _h; FullScreenMode _mode;

        public static void Init()
        {
            if (Instance) return;
            var go = new GameObject("DisplaySettings");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<DisplaySettings>();
            // the command line (benchmarks, smoke test, Windows test scripts) wins over the saved choice
            var args = Environment.GetCommandLineArgs();
            bool cli = args.Any(a => a == "-screen-width" || a == "-screen-height" || a == "-screen-fullscreen" || a == "-popupwindow" || a == "-window-mode");
            var s = SaveSystem.Settings;
            if (!cli && s.displayMode >= 0)
            {
                var mode = (FullScreenMode)s.displayMode;
                if (!Modes.Contains(mode)) mode = FullScreenMode.FullScreenWindow;
                var (w, h) = s.resolutionW > 0 ? (s.resolutionW, s.resolutionH) : Native();
                if (mode != FullScreenMode.Windowed) (w, h) = Clamp(w, h);
                Screen.SetResolution(w, h, mode);
                Debug.Log($"[Display] 启动时应用存档：{Label(mode)} {w}x{h}");
            }
        }

        /// <summary>Resolutions the display supports (distinct sizes, largest first).</summary>
        public static List<Vector2Int> Resolutions()
        {
            var list = Screen.resolutions.Select(r => new Vector2Int(r.width, r.height)).Distinct()
                .OrderByDescending(v => v.x * v.y).ThenByDescending(v => v.x).ToList();
            var (nw, nh) = Native();
            if (!list.Contains(new Vector2Int(nw, nh))) list.Insert(0, new Vector2Int(nw, nh));
            return list;
        }

        public static (int, int) Native()
        {
            var r = Screen.currentResolution;
            int w = Display.main.systemWidth > 0 ? Display.main.systemWidth : r.width;
            int h = Display.main.systemHeight > 0 ? Display.main.systemHeight : r.height;
            return (w, h);
        }

        static (int, int) Clamp(int w, int h)
        {
            var (nw, nh) = Native();
            return (Mathf.Min(w, nw), Mathf.Min(h, nh));
        }

        public static FullScreenMode CurrentMode => Screen.fullScreenMode == FullScreenMode.MaximizedWindow ? FullScreenMode.FullScreenWindow : Screen.fullScreenMode;

        /// <summary>Apply and save a mode / resolution.</summary>
        public static void Set(FullScreenMode mode, int w, int h, bool save = true)
        {
            if (mode != FullScreenMode.Windowed) (w, h) = Clamp(w, h);
            Debug.Log($"[Display] 切换：{Label(CurrentMode)} {Screen.width}x{Screen.height} → {Label(mode)} {w}x{h}");
            Screen.SetResolution(w, h, mode);
            if (save)
            {
                SaveSystem.Settings.displayMode = (int)mode;
                SaveSystem.Settings.resolutionW = w; SaveSystem.Settings.resolutionH = h;
                SaveSystem.SaveSettings();
            }
        }

        public static void SetMode(FullScreenMode mode)
        {
            if (mode == FullScreenMode.Windowed)
            {
                // a window a bit smaller than the screen; the saved window size if there is one
                var (nw, nh) = Native();
                int w = SaveSystem.Settings.displayMode == (int)FullScreenMode.Windowed && SaveSystem.Settings.resolutionW > 0 ? SaveSystem.Settings.resolutionW : 0;
                int h = w > 0 ? SaveSystem.Settings.resolutionH : 0;
                if (w <= 0 || w >= nw || h >= nh)
                {
                    var fit = Resolutions().FirstOrDefault(r => r.x <= nw * 0.8f && r.y <= nh * 0.8f);
                    if (fit == default) fit = new Vector2Int(Mathf.RoundToInt(nw * 0.75f), Mathf.RoundToInt(nh * 0.75f));
                    w = fit.x; h = fit.y;
                }
                Set(mode, w, h);
            }
            else
            {
                var (nw, nh) = Native();
                Set(mode, nw, nh);
            }
        }

        /// <summary>F1: borderless full screen ↔ window.</summary>
        public static void Toggle()
        {
            var next = CurrentMode == FullScreenMode.Windowed ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed;
            SetMode(next);
            Notice?.Invoke($"显示模式：{Label(next)}（F1 切换）");
        }

        void Start() { _w = Screen.width; _h = Screen.height; _mode = CurrentMode; }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb != null && kb.f1Key.wasPressedThisFrame) Toggle();
            if (Screen.width != _w || Screen.height != _h || CurrentMode != _mode)
            {
                _w = Screen.width; _h = Screen.height; _mode = CurrentMode;
                StartCoroutine(AfterChange());
            }
        }

        IEnumerator AfterChange()
        {
            // the upscaler host re-initialises at the start of the next rendered frame; drop the history after it
            yield return null; yield return null;
            AntiAliasing.ResetHistory();
            Changes++;
            var host = Zfw.Upscaling.UpscalerHost.Active;
            var rs = Zfw.Upscaling.UpscalerHost.IsUpscaling ? Zfw.Upscaling.UpscalerHost.RenderSize : new Vector2Int(Screen.width, Screen.height);
            Debug.Log($"[Display] 已生效：{Label(CurrentMode)} 显示 {Screen.width}x{Screen.height}，渲染 {rs.x}x{rs.y}（{(host != null ? host.Name + "/" + Zfw.Upscaling.UpscalerHost.Quality : "原生")}），历史帧已重置");
        }
    }
}
