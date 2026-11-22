using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace WildPartner.UI
{
    /// <summary>
    /// Tiny code-only UGUI toolkit (no prefabs to break). Text uses a dynamic OS font so Chinese renders
    /// without shipping a font: PingFang SC on macOS, Microsoft YaHei on Windows.
    /// </summary>
    public static class UIKit
    {
        static Font _font;
        public static Font Font => _font ??= Font.CreateDynamicFontFromOSFont(
            new[] { "PingFang SC", "Microsoft YaHei UI", "Microsoft YaHei", "Hiragino Sans GB", "Noto Sans CJK SC", "Arial Unicode MS", "Arial" }, 32);

        public static readonly Color Ink = new Color(0.16f, 0.13f, 0.2f);
        public static readonly Color Paper = new Color(0.98f, 0.95f, 0.88f, 0.94f);
        public static readonly Color Accent = new Color(0.45f, 0.3f, 0.7f);
        public static readonly Color Dim = new Color(0.08f, 0.06f, 0.12f, 0.55f);

        public static RectTransform Rect(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = anchorMin; rt.anchorMax = anchorMax; rt.pivot = pivot;
            rt.anchoredPosition = pos; rt.sizeDelta = size;
            return rt;
        }

        public static RectTransform Stretch(Transform parent, string name)
        {
            var rt = Rect(parent, name, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            return rt;
        }

        public static Image Panel(RectTransform rt, Color c)
        {
            var img = rt.gameObject.AddComponent<Image>();
            img.color = c;
            return img;
        }

        public static Text Label(Transform parent, string text, int size, TextAnchor align, Color color, string name = "Label")
        {
            var rt = Stretch(parent, name);
            var t = rt.gameObject.AddComponent<Text>();
            t.font = Font; t.text = text; t.fontSize = size; t.alignment = align; t.color = color;
            t.horizontalOverflow = HorizontalWrapMode.Wrap; t.verticalOverflow = VerticalWrapMode.Overflow;
            t.supportRichText = true; t.raycastTarget = false;
            return t;
        }

        public static Button Button(Transform parent, string text, UnityAction onClick, Vector2 size)
        {
            var rt = Rect(parent, "Btn_" + text, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, size);
            var img = Panel(rt, new Color(1, 1, 1, 0.9f));
            var b = rt.gameObject.AddComponent<Button>();
            var colors = b.colors;
            colors.normalColor = new Color(0.97f, 0.94f, 0.88f, 0.92f);
            colors.highlightedColor = new Color(0.86f, 0.8f, 0.98f, 1f);
            colors.selectedColor = new Color(0.8f, 0.72f, 0.98f, 1f);
            colors.pressedColor = new Color(0.7f, 0.6f, 0.95f, 1f);
            b.colors = colors;
            b.targetGraphic = img;
            b.onClick.AddListener(onClick);
            b.navigation = new Navigation { mode = Navigation.Mode.Vertical };
            Label(rt, text, 26, TextAnchor.MiddleCenter, Ink);
            var le = rt.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = size.x; le.preferredHeight = size.y;
            return b;
        }

        /// <summary>"&lt; value &gt;" selector — works with mouse, keyboard and gamepad.</summary>
        public static Button Cycler(Transform parent, string label, Func<string> value, Action<int> step, Vector2 size)
        {
            var row = Rect(parent, "Row_" + label, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, size);
            var le = row.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = size.x; le.preferredHeight = size.y;
            var lt = Label(row, label, 24, TextAnchor.MiddleLeft, Ink);
            lt.rectTransform.anchorMax = new Vector2(0.45f, 1);
            lt.rectTransform.offsetMin = new Vector2(16, 0);

            var btnRt = Rect(row, "Value", new Vector2(0.45f, 0), new Vector2(1, 1), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            var img = Panel(btnRt, new Color(1, 1, 1, 0.9f));
            var b = btnRt.gameObject.AddComponent<Button>();
            var colors = b.colors;
            colors.highlightedColor = new Color(0.86f, 0.8f, 0.98f, 1f);
            colors.selectedColor = new Color(0.8f, 0.72f, 0.98f, 1f);
            b.colors = colors;
            b.targetGraphic = img;
            b.navigation = new Navigation { mode = Navigation.Mode.Vertical };
            var vt = Label(btnRt, "", 24, TextAnchor.MiddleCenter, Accent);
            void Refresh() => vt.text = $"‹  {value()}  ›";
            b.onClick.AddListener(() => { step(+1); Refresh(); });
            var cyc = btnRt.gameObject.AddComponent<CyclerInput>();
            cyc.onStep = d => { step(d); Refresh(); };
            cyc.refresh = Refresh;
            Refresh();
            return b;
        }

        public static RectTransform VList(Transform parent, string name, Vector2 size, float spacing = 12)
        {
            var rt = Rect(parent, name, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, size);
            var v = rt.gameObject.AddComponent<VerticalLayoutGroup>();
            v.spacing = spacing; v.childAlignment = TextAnchor.MiddleCenter;
            v.childControlHeight = false; v.childControlWidth = false; v.childForceExpandHeight = false; v.childForceExpandWidth = false;
            return rt;
        }
    }

    /// <summary>Left/right on keyboard or d-pad steps a cycler while it is selected.</summary>
    public class CyclerInput : MonoBehaviour, UnityEngine.EventSystems.IMoveHandler
    {
        public Action<int> onStep;
        public Action refresh;
        public void OnMove(UnityEngine.EventSystems.AxisEventData e)
        {
            if (e.moveDir == UnityEngine.EventSystems.MoveDirection.Left) { onStep?.Invoke(-1); e.Use(); }
            else if (e.moveDir == UnityEngine.EventSystems.MoveDirection.Right) { onStep?.Invoke(+1); e.Use(); }
        }
        void OnEnable() => refresh?.Invoke();
    }
}
