using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using WildPartner.Core;
using WildPartner.Creatures;
using WildPartner.Player;
using WildPartner.World;
using Zfw.QualityTiers;

namespace WildPartner.UI
{
    /// <summary>One settings row; other modules (e.g. the upscaler in stage 2) register extra rows.</summary>
    public class SettingRow
    {
        public string label;
        public Func<string> value;
        public Action<int> step;
    }

    public static class SettingsRegistry
    {
        public static readonly List<SettingRow> Rows = new List<SettingRow>();
        public static event Action Changed;
        public static void Add(SettingRow r) { Rows.Add(r); Changed?.Invoke(); }
    }

    /// <summary>
    /// All screens, built in code on a Screen Space Overlay canvas. Overlay canvases are drawn by Unity
    /// after the camera stack at full display resolution, so UI stays sharp whatever the render scale
    /// (IN-07, verified in stage 2).
    /// </summary>
    public class GameUI : MonoBehaviour
    {
        GameFlow _flow;
        Canvas _canvas;
        RectTransform _hud, _menu, _pause, _settings, _journal, _ending;
        RectTransform _alertRoot;
        Text _objective, _clock, _prompt, _toast, _hint, _endingText, _journalBody;
        Image _progressFill, _progressBg, _alertFill;
        RectTransform _journalGrid, _settingsList;
        Button _continueBtn, _firstMenuBtn;
        FlowStateMemo _returnFromSettings;
        readonly Queue<string> _toasts = new Queue<string>();
        float _toastTimer;
        string[] _hints; int _hintIndex; float _hintTimer;
        enum FlowStateMemo { Menu, Pause }

        void Start()
        {
            _flow = GameFlow.Instance;
            BuildCanvas();
            BuildHud();
            BuildMenu();
            BuildPause();
            BuildSettings();
            BuildJournal();
            BuildEnding();
            _flow.StateChanged += OnState;
            _flow.Toast += t => _toasts.Enqueue(t);
            _flow.Hints += h => { _hints = h; _hintIndex = 0; _hintTimer = 0; };
            RegisterBaseSettings();
            OnState(_flow.State);
        }

        void BuildCanvas()
        {
            var go = new GameObject("UI Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            go.transform.SetParent(transform, false);
            _canvas = go.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 10;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            if (!FindFirstObjectByType<EventSystem>())
            {
                var es = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
                es.transform.SetParent(transform, false);
            }
        }

        RectTransform Screen(string name, Color bg)
        {
            var rt = UIKit.Stretch(_canvas.transform, name);
            if (bg.a > 0) UIKit.Panel(rt, bg).sprite = null;
            rt.gameObject.SetActive(false);
            return rt;
        }

        RectTransform Card(Transform parent, Vector2 size)
        {
            var rt = UIKit.Rect(parent, "Card", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, size);
            UIKit.Box(rt, UIKit.Paper, UIKit.ParchmentFrame, 1.2f);
            return rt;
        }

        // ------------------------------------------------------------------ HUD
        Image _stamina;
        RawImage _petIcon; Text _petText; string _petShown; int _petCount = -1;

        void BuildHud()
        {
            _hud = Screen("HUD", Color.clear);
            var obj = UIKit.Rect(_hud, "Objective", new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(36, -30), new Vector2(760, 60));
            UIKit.Box(obj, UIKit.Plum, UIKit.PlumFrame);
            var star = UIKit.Label(obj, "✦", 26, TextAnchor.MiddleLeft, UIKit.Gold, "Icon");
            star.rectTransform.offsetMin = new Vector2(20, 0);
            _objective = UIKit.Label(obj, "", 26, TextAnchor.MiddleLeft, new Color(1f, 0.97f, 0.92f));
            _objective.rectTransform.offsetMin = new Vector2(54, 0);
            UIKit.Shade(_objective);
            gameObject.AddComponent<MapUI>().Build(_flow, _canvas, _hud, obj);   // minimap + M big map; moves the objective below the minimap

            var clock = UIKit.Rect(_hud, "Clock", new Vector2(1, 1), new Vector2(1, 1), new Vector2(1, 1), new Vector2(-36, -30), new Vector2(300, 60));
            UIKit.Box(clock, UIKit.Plum, UIKit.PlumFrame);
            _clock = UIKit.Label(clock, "", 24, TextAnchor.MiddleCenter, new Color(1f, 0.97f, 0.92f));
            UIKit.Shade(_clock);

            // 1123c-B2: the pet at her side, its picture and what it can do (J switches)
            var pet = UIKit.Rect(_hud, "Pet", new Vector2(1, 1), new Vector2(1, 1), new Vector2(1, 1), new Vector2(-36, -102), new Vector2(420, 96));
            UIKit.Box(pet, UIKit.Plum, UIKit.PlumFrame);
            var petIcon = new GameObject("Icon", typeof(RectTransform), typeof(RawImage));
            petIcon.transform.SetParent(pet, false);
            var pir = (RectTransform)petIcon.transform;
            pir.anchorMin = pir.anchorMax = pir.pivot = new Vector2(0, 0.5f); pir.anchoredPosition = new Vector2(10, 0); pir.sizeDelta = new Vector2(78, 78);
            _petIcon = petIcon.GetComponent<RawImage>();
            _petText = UIKit.Label(pet, "", 20, TextAnchor.MiddleLeft, new Color(1f, 0.97f, 0.92f));
            _petText.rectTransform.offsetMin = new Vector2(98, 6); _petText.rectTransform.offsetMax = new Vector2(-10, -6);
            _petText.horizontalOverflow = HorizontalWrapMode.Wrap;
            UIKit.Shade(_petText);

            // 1123c-B4: climbing stamina, shown while it is not full
            var st = UIKit.Rect(_hud, "Stamina", new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 150), new Vector2(260, 16));
            UIKit.Panel(st, new Color(0.12f, 0.08f, 0.18f, 0.75f));
            var stFill = UIKit.Rect(st, "Fill", Vector2.zero, new Vector2(1, 1), new Vector2(0, 0.5f), Vector2.zero, Vector2.zero);
            stFill.offsetMin = new Vector2(2, 2); stFill.offsetMax = new Vector2(-2, -2);
            _stamina = UIKit.Panel(stFill, new Color(0.62f, 0.9f, 0.55f));
            _stamina.type = Image.Type.Filled; _stamina.fillMethod = Image.FillMethod.Horizontal;
            if (!_stamina.sprite) _stamina.sprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 4, 4), Vector2.one * 0.5f);
            st.gameObject.SetActive(false);

            var prompt = UIKit.Rect(_hud, "Prompt", new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 190), new Vector2(560, 92));
            _prompt = UIKit.Label(prompt, "", 28, TextAnchor.UpperCenter, Color.white);
            var shadow = _prompt.gameObject.AddComponent<Outline>(); shadow.effectColor = new Color(0, 0, 0, 0.6f);
            var bar = UIKit.Rect(prompt, "Bar", new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 6), new Vector2(420, 16));
            _progressBg = UIKit.Box(bar, new Color(0.12f, 0.08f, 0.2f, 0.7f), UIKit.PlumFrame, 0.3f);
            var fill = UIKit.Rect(bar, "Fill", Vector2.zero, new Vector2(0, 1), new Vector2(0, 0.5f), Vector2.zero, Vector2.zero);
            _progressFill = UIKit.Panel(fill, new Color(0.75f, 0.6f, 1f));
            var alert = _alertRoot = UIKit.Rect(prompt, "Alert", new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, -12), new Vector2(420, 8));
            UIKit.Panel(alert, new Color(0, 0, 0, 0.4f));
            var af = UIKit.Rect(alert, "Fill", Vector2.zero, new Vector2(0, 1), new Vector2(0, 0.5f), Vector2.zero, Vector2.zero);
            _alertFill = UIKit.Panel(af, new Color(1f, 0.45f, 0.3f));

            var toast = UIKit.Rect(_hud, "Toast", new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -110), new Vector2(900, 60));
            _toast = UIKit.Label(toast, "", 30, TextAnchor.MiddleCenter, Color.white);
            var o2 = _toast.gameObject.AddComponent<Outline>(); o2.effectColor = new Color(0.2f, 0.1f, 0.3f, 0.8f); o2.effectDistance = new Vector2(2, -2);

            var hint = UIKit.Rect(_hud, "Hint", new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 60), new Vector2(900, 56));
            UIKit.Box(hint, UIKit.Plum, UIKit.PlumFrame);
            _hint = UIKit.Label(hint, "", 26, TextAnchor.MiddleCenter, new Color(1, 0.96f, 0.85f));
            UIKit.Shade(_hint);
        }

        // ------------------------------------------------------------------ main menu
        void BuildMenu()
        {
            _menu = Screen("MainMenu", Color.clear);
            // plum glass card behind title and buttons so they read over the bright valley (画面整改 6)
            var back = UIKit.Rect(_menu, "MenuBack", new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(96, 30), new Vector2(860, 720));
            UIKit.Box(back, new Color(0.14f, 0.09f, 0.22f, 0.62f), UIKit.PlumFrame, 1.6f);
            var title = UIKit.Rect(_menu, "Title", new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(140, 230), new Vector2(900, 200));
            var t = UIKit.Label(title, "荒原伙伴", 110, TextAnchor.MiddleLeft, new Color(1f, 0.95f, 0.84f));
            var o = t.gameObject.AddComponent<Outline>(); o.effectColor = new Color(0.25f, 0.15f, 0.4f, 0.8f); o.effectDistance = new Vector2(3, -3);
            var sub = UIKit.Rect(_menu, "Sub", new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(146, 130), new Vector2(900, 60));
            var st = UIKit.Label(sub, "WildPartner · 和小伙伴们一起回到山崖上的木屋", 28, TextAnchor.MiddleLeft, new Color(1, 0.97f, 0.9f));
            st.gameObject.AddComponent<Outline>().effectColor = new Color(0.2f, 0.1f, 0.3f, 0.7f);

            var list = UIKit.VList(_menu, "Buttons", new Vector2(360, 380));
            list.anchorMin = list.anchorMax = new Vector2(0, 0.5f); list.pivot = new Vector2(0, 0.5f);
            list.anchoredPosition = new Vector2(140, -130);
            _continueBtn = UIKit.Button(list, "继续", () => _flow.Continue(), new Vector2(340, 64));
            _firstMenuBtn = UIKit.Button(list, "新游戏", () => _flow.NewGame(), new Vector2(340, 64));
            UIKit.Button(list, "设置", () => OpenSettings(FlowStateMemo.Menu), new Vector2(340, 64));
            UIKit.Button(list, "退出", () => _flow.Quit(), new Vector2(340, 64));

            var credit = UIKit.Rect(_menu, "Credit", new Vector2(1, 0), new Vector2(1, 0), new Vector2(1, 0), new Vector2(-30, 20), new Vector2(900, 40));
            UIKit.Label(credit, "Unity 6.3 · URP · 本项目使用 AI 辅助开发（Claude Code）", 18, TextAnchor.LowerRight, new Color(1, 1, 1, 0.75f));
        }

        void BuildPause()
        {
            _pause = Screen("Pause", UIKit.Dim);
            var card = Card(_pause, new Vector2(460, 470));
            var title = UIKit.Rect(card, "T", new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), new Vector2(0, -20), new Vector2(0, 70));
            UIKit.Label(title, "暂停", 40, TextAnchor.MiddleCenter, UIKit.Ink);
            var list = UIKit.VList(card, "List", new Vector2(360, 340));
            list.anchoredPosition = new Vector2(0, -30);
            UIKit.Button(list, "继续游戏", () => _flow.Resume(), new Vector2(340, 62));
            UIKit.Button(list, "设置", () => OpenSettings(FlowStateMemo.Pause), new Vector2(340, 62));
            UIKit.Button(list, "回到主菜单", () => _flow.BackToMenu(), new Vector2(340, 62));
            UIKit.Button(list, "退出游戏", () => _flow.Quit(), new Vector2(340, 62));
        }

        // ------------------------------------------------------------------ settings
        void BuildSettings()
        {
            _settings = Screen("Settings", UIKit.Dim);
            var card = Card(_settings, new Vector2(760, 760));
            var title = UIKit.Rect(card, "T", new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), new Vector2(0, -16), new Vector2(0, 70));
            UIKit.Label(title, "设置（立即生效）", 38, TextAnchor.MiddleCenter, UIKit.Ink);
            _settingsList = UIKit.VList(card, "Rows", new Vector2(680, 560), 10);
            _settingsList.anchoredPosition = new Vector2(0, -10);
            var back = UIKit.Rect(card, "BackRow", new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 24), new Vector2(300, 64));
            var b = UIKit.Button(back, "返回", CloseSettings, new Vector2(300, 64));
            b.transform.localPosition = Vector3.zero;
            SettingsRegistry.Changed += RebuildSettingsRows;
        }

        void RebuildSettingsRows()
        {
            if (!_settingsList) return;
            foreach (Transform c in _settingsList) Destroy(c.gameObject);
            foreach (var r in SettingsRegistry.Rows) UIKit.Cycler(_settingsList, r.label, r.value, r.step, new Vector2(680, 56));
        }

        void RegisterBaseSettings()
        {
            string[] tierNames = { "自动", "低", "中", "高", "极高" };
            SettingsRegistry.Add(new SettingRow
            {
                label = "画质档位",
                value = () => QualityTiers.Mode == SelectionMode.Manual ? tierNames[(int)QualityTiers.Current + 1] : $"自动（{tierNames[(int)QualityTiers.Current + 1]}）",
                step = d =>
                {
                    int cur = QualityTiers.Mode == SelectionMode.Manual ? (int)QualityTiers.Current + 1 : 0;
                    cur = (cur + d + 5) % 5;
                    if (cur == 0) { QualityTiers.Set(QualityTiers.Current, manual: false); SaveSystem.Settings.tier = ""; }
                    else { QualityTiers.Set((Tier)(cur - 1), manual: true); SaveSystem.Settings.tier = ((Tier)(cur - 1)).ToString(); }
                    SaveSystem.SaveSettings();
                },
            });
            SettingsRegistry.Add(new SettingRow
            {
                label = "鼠标灵敏度",
                value = () => SaveSystem.Settings.mouseSensitivity.ToString("0.0"),
                step = d => { SaveSystem.Settings.mouseSensitivity = Mathf.Clamp(SaveSystem.Settings.mouseSensitivity + d * 0.1f, 0.2f, 3f); SaveSystem.SaveSettings(); },
            });
            SettingsRegistry.Add(new SettingRow
            {
                label = "反转 Y 轴",
                value = () => SaveSystem.Settings.invertY ? "开" : "关",
                step = d => { SaveSystem.Settings.invertY = !SaveSystem.Settings.invertY; SaveSystem.SaveSettings(); },
            });
            SettingsRegistry.Add(new SettingRow
            {
                label = "音量",
                value = () => Mathf.RoundToInt(SaveSystem.Settings.masterVolume * 100) + "%",
                step = d => { SaveSystem.Settings.masterVolume = Mathf.Clamp01(SaveSystem.Settings.masterVolume + d * 0.1f); AudioListener.volume = SaveSystem.Settings.masterVolume; SaveSystem.SaveSettings(); },
            });
            AudioListener.volume = SaveSystem.Settings.masterVolume;
        }

        void OpenSettings(FlowStateMemo from)
        {
            _returnFromSettings = from;
            RebuildSettingsRows();
            _menu.gameObject.SetActive(false);
            _pause.gameObject.SetActive(false);
            _settings.gameObject.SetActive(true);
            Select(_settingsList.childCount > 0 ? _settingsList.GetChild(0).GetComponentInChildren<Button>() : null);
        }

        void CloseSettings()
        {
            _settings.gameObject.SetActive(false);
            OnState(_flow.State);
        }

        // ------------------------------------------------------------------ journal
        void BuildJournal()
        {
            _journal = Screen("Journal", UIKit.Dim);
            var card = Card(_journal, new Vector2(1500, 820));
            var title = UIKit.Rect(card, "T", new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), new Vector2(0, -14), new Vector2(0, 70));
            UIKit.Label(title, "伙伴图鉴", 42, TextAnchor.MiddleCenter, UIKit.Ink);
            _journalGrid = UIKit.Rect(card, "Grid", new Vector2(0, 0), new Vector2(1, 1), new Vector2(0.5f, 0.5f), new Vector2(0, -20), new Vector2(-60, -180));
            var h = _journalGrid.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 18; h.childControlWidth = true; h.childControlHeight = true; h.childForceExpandWidth = true; h.childForceExpandHeight = true;
            var foot = UIKit.Rect(card, "Foot", new Vector2(0, 0), new Vector2(1, 0), new Vector2(0.5f, 0), new Vector2(0, 18), new Vector2(0, 50));
            _journalBody = UIKit.Label(foot, "", 22, TextAnchor.MiddleCenter, UIKit.Ink);
        }

        void RefreshJournal()
        {
            foreach (Transform c in _journalGrid) Destroy(c.gameObject);
            foreach (var def in CreatureDefs.All)
            {
                var entry = _flow.Save.caught.Find(e => e.id == def.id);
                bool got = entry != null;
                var cell = UIKit.Rect(_journalGrid, def.id, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
                UIKit.Panel(cell, got ? new Color(1, 1, 1, 0.85f) : new Color(0.85f, 0.82f, 0.78f, 0.85f));

                var icon = UIKit.Rect(cell, "Icon", new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -20), new Vector2(200, 200));
                var tex = Resources.Load<Texture2D>("Icons/" + def.model);
                if (tex)
                {
                    var ri = icon.gameObject.AddComponent<RawImage>();
                    ri.texture = tex;
                    ri.color = got ? Color.white : new Color(0.12f, 0.1f, 0.16f, 0.9f);   // silhouette when unknown
                }
                else
                {
                    UIKit.Panel(icon, got ? def.journalColor : new Color(0.2f, 0.18f, 0.24f));
                    UIKit.Label(icon, got ? def.name.Substring(0, 1) : "?", 90, TextAnchor.MiddleCenter, got ? UIKit.Ink : Color.white);
                }

                var name = UIKit.Rect(cell, "Name", new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), new Vector2(0, -236), new Vector2(0, 50));
                UIKit.Label(name, got ? def.name : "？？？", 32, TextAnchor.MiddleCenter, UIKit.Ink);
                var body = UIKit.Rect(cell, "Body", Vector2.zero, new Vector2(1, 1), new Vector2(0.5f, 0.5f), new Vector2(0, -150), new Vector2(-30, -330));
                string text = got
                    ? $"{def.description}\n\n<color=#6a4ca8>捕捉于：游戏时间 {FormatHour(entry.gameHour)}，第 {GameFlow.FormatTime(entry.playTime)}</color>"
                    : $"<b>线索</b>\n{def.hint}\n\n<color=#6a4ca8>出没：{def.ConditionText}</color>";
                UIKit.Label(body, text, 22, TextAnchor.UpperLeft, UIKit.Ink);
            }
            _journalBody.text = $"已收集 {_flow.CaughtCount}/{_flow.Total}    ·    按 Tab 或 Esc 关闭";
        }

        static string FormatHour(float h) { int hh = Mathf.FloorToInt(h); int mm = Mathf.FloorToInt((h - hh) * 60); return $"{hh:00}:{mm:00}"; }

        // ------------------------------------------------------------------ ending
        void BuildEnding()
        {
            _ending = Screen("Ending", new Color(0.05f, 0.03f, 0.1f, 0.35f));
            var card = Card(_ending, new Vector2(900, 460));
            card.anchoredPosition = new Vector2(0, -200);
            card.anchorMin = card.anchorMax = new Vector2(0.5f, 0.5f);
            _endingText = UIKit.Label(card, "", 30, TextAnchor.UpperCenter, UIKit.Ink);
            _endingText.rectTransform.offsetMin = new Vector2(30, 110);
            _endingText.rectTransform.offsetMax = new Vector2(-30, -30);
            var row = UIKit.Rect(card, "Btn", new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 26), new Vector2(340, 64));
            UIKit.Button(row, "返回主菜单", () => _flow.BackToMenu(), new Vector2(340, 64)).transform.localPosition = Vector3.zero;
        }

        // ------------------------------------------------------------------ state
        void OnState(GameFlow.FlowState s)
        {
            if (!_hud) return;
            _hud.gameObject.SetActive(s == GameFlow.FlowState.Playing);
            _menu.gameObject.SetActive(s == GameFlow.FlowState.MainMenu);
            _pause.gameObject.SetActive(s == GameFlow.FlowState.Paused);
            _journal.gameObject.SetActive(s == GameFlow.FlowState.Journal);
            _ending.gameObject.SetActive(s == GameFlow.FlowState.Ending);
            _settings.gameObject.SetActive(false);
            if (s == GameFlow.FlowState.MainMenu)
            {
                bool canContinue = SaveSystem.HasSave;
                _continueBtn.gameObject.SetActive(canContinue);
                Select(canContinue ? _continueBtn : _firstMenuBtn);
            }
            else if (s == GameFlow.FlowState.Paused) Select(_pause.GetComponentInChildren<Button>());
            else if (s == GameFlow.FlowState.Journal) RefreshJournal();
            else if (s == GameFlow.FlowState.Ending)
            {
                _endingText.text = $"<size=44><b>伙伴们都回家了</b></size>\n\n五位伙伴围在篝火旁，山谷里的夜风温柔地吹过。\n\n用时：{GameFlow.FormatTime(_flow.Save.playTime)}";
                Select(_ending.GetComponentInChildren<Button>());
            }
        }

        static void Select(Selectable s)
        {
            if (s && EventSystem.current) EventSystem.current.SetSelectedGameObject(s.gameObject);
        }

        void Update()
        {
            if (_flow == null) return;
            if (_settings.gameObject.activeSelf && GameInput.PausePressed) { CloseSettings(); return; }
            if (_flow.State != GameFlow.FlowState.Playing) return;

            _objective.text = _flow.Objective;
            var env = EnvController.Instance;
            var w = Weather.Instance;
            if (env) _clock.text = $"<color=#FFDB8C>{(env.timeOfDay >= 6f && env.timeOfDay < 18.5f ? "☀" : "☾")}</color>  {env.ClockText()}  ·  {(w ? w.Label : "")}";

            if (_petShown != PetSwitcher.Active)
            {
                _petShown = PetSwitcher.Active;
                var d = CreatureDefs.Get(_petShown);
                _petIcon.texture = d != null ? Resources.Load<Texture2D>("Icons/" + d.model) : null;
                int n = _flow.CaughtCount;
                _petText.text = d == null ? "" : $"<b>{d.name}</b>  <size=16><color=#D8C8FF>{(n > 1 ? "J 换伙伴" : "")}</color></size>\n<size=17>{PetSwitcher.Ability(d.id)}</size>";
            }
            if (_flow.CaughtCount != _petCount) { _petCount = _flow.CaughtCount; _petShown = null; }

            var pcS = WildPartner.Player.PlayerController.Instance;
            if (pcS)
            {
                float f = pcS.Stamina / WildPartner.Player.PlayerController.StaminaMax;
                _stamina.transform.parent.gameObject.SetActive(pcS.Climbing || f < 0.999f);
                _stamina.fillAmount = f;
                _stamina.color = f < 0.25f ? new Color(0.95f, 0.45f, 0.35f) : new Color(0.62f, 0.9f, 0.55f);
            }

            // capture prompt
            var cand = CaptureSystem.Candidate;
            var target = CaptureSystem.Target;
            bool show = cand != null;
            string interact = WildPartner.World.CabinInterior.Prompt ?? Creature.RidePrompt;
            _prompt.transform.parent.gameObject.SetActive(show || interact != null);
            if (!show && interact != null)
            {
                _prompt.text = interact;   // cabin door / bed / book (试玩反馈 1123b-3), riding the deer (1123b-8)
                _progressBg.gameObject.SetActive(false);
                _alertRoot.gameObject.SetActive(false);
            }
            if (show)
            {
                _alertRoot.gameObject.SetActive(true);
                var c = target ? target : cand;
                _prompt.text = target ? $"正在捕捉 {c.Def.name}…" : $"按住 E 捕捉 {c.Def.name}";
                _progressFill.rectTransform.anchorMax = new Vector2(CaptureSystem.Progress, 1);
                _progressBg.gameObject.SetActive(target != null || CaptureSystem.Progress > 0);
                _alertFill.rectTransform.anchorMax = new Vector2(c.AlertLevel, 1);
            }

            // toasts
            _toastTimer -= Time.deltaTime;
            if (_toastTimer <= 0)
            {
                if (_toasts.Count > 0) { _toast.text = _toasts.Dequeue(); _toastTimer = 3.5f; }
                else _toast.text = "";
            }

            // intro hints
            if (_hints != null && _hintIndex < _hints.Length)
            {
                _hint.transform.parent.gameObject.SetActive(true);
                _hint.text = _hints[_hintIndex];
                _hintTimer += Time.deltaTime;
                if (_hintTimer > 6f) { _hintTimer = 0; _hintIndex++; }
            }
            else _hint.transform.parent.gameObject.SetActive(false);
        }
    }
}
