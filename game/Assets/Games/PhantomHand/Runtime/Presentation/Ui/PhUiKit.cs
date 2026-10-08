using Oculus.Interaction;
using Oculus.Interaction.Surfaces;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Opus.Games.PhantomHand.Presentation
{
    /// <summary>
    /// U4 uGUI helpers: brand palette (same hex values as OpusHud / the Flutter app, docs/design/OPUS_DESIGN_V2.md v3),
    /// millimetre layout (a panel canvas is 1 px = 1 mm, scale 0.001, so a 64 px button is 6.4 cm), and the ISDK poke wiring.
    /// All panels are built in code; layout coordinates are top-left based (x right, y down), in millimetres.
    /// </summary>
    public static class PhUiKit
    {
        public static readonly Color PanelBg = new Color(0.071f, 0.027f, 0.031f, 0.94f);   // #120708
        public static readonly Color PanelLine = new Color(0.200f, 0.086f, 0.102f, 1f);    // #33161A
        public static readonly Color Oxblood = new Color(0.478f, 0.102f, 0.125f, 1f);      // #7A1A20
        public static readonly Color Crimson = new Color(0.659f, 0.141f, 0.173f, 1f);      // #A8242C
        public static readonly Color Ink = new Color(0.925f, 0.894f, 0.890f, 1f);          // #ECE4E3
        public static readonly Color InkDim = new Color(0.690f, 0.640f, 0.645f, 1f);       // dimmer text, still > 4.5:1 on the panel
        public static readonly Color Good = new Color(0.310f, 0.749f, 0.545f, 1f);         // #4FBF8B
        public static readonly Color Warn = new Color(0.878f, 0.643f, 0.231f, 1f);         // #E0A43B
        public static readonly Color Info = new Color(0.357f, 0.608f, 0.902f, 1f);         // #5B9BE6
        public static readonly Color Bad = new Color(0.878f, 0.376f, 0.353f, 1f);          // #E0605A
        public static readonly Color ButtonBg = new Color(0.165f, 0.071f, 0.082f, 1f);

        private static Font _font;
        private static Sprite _ring, _disc;

        public static Font DefaultFont
        {
            get { if (_font == null) _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); return _font; }
        }

        private static Font _devanagari;

        /// <summary>
        /// Font that has Devanagari glyphs (the built-in LegacyRuntime font has none, so Hindi would render as boxes):
        /// the OS dynamic font (Noto Sans Devanagari on Quest/Android, Nirmala UI or Mangal on Windows). Falls back to the
        /// default font when the device has none of them.
        /// </summary>
        public static Font DevanagariFont
        {
            get
            {
                if (_devanagari == null)
                    _devanagari = Font.CreateDynamicFontFromOSFont(new[] { "Noto Sans Devanagari", "Noto Sans Devanagari UI", "Nirmala UI", "Mangal", "Noto Sans" }, 32);
                return _devanagari != null ? _devanagari : DefaultFont;
            }
        }

        public static bool HasDevanagari(string s)
        {
            if (s == null) return false;
            for (int i = 0; i < s.Length; i++) if (s[i] >= 'ऀ' && s[i] <= 'ॿ') return true;
            return false;
        }

        /// <summary>Sets the text and picks the font that can draw it (Devanagari font for Hindi strings).</summary>
        public static void SetText(Text t, string s)
        {
            if (t == null) return;
            var f = HasDevanagari(s) ? DevanagariFont : DefaultFont;
            if (t.font != f) t.font = f;
            t.text = s ?? "";
        }

        // ---- layout ------------------------------------------------------------------------------------------------

        /// <summary>Positions rt by its top-left corner inside its parent; x,y,w,h in millimetres (panel pixels).</summary>
        public static RectTransform Place(RectTransform rt, float x, float y, float w, float h)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(x, -y);
            rt.sizeDelta = new Vector2(w, h);
            return rt;
        }

        public static void ClearChildren(Transform t)
        {
            for (int i = t.childCount - 1; i >= 0; i--)
            {
                var c = t.GetChild(i).gameObject;
                c.SetActive(false);
                if (Application.isPlaying) Object.Destroy(c); else Object.DestroyImmediate(c);
            }
        }

        public static Image Box(Transform parent, string name, float x, float y, float w, float h, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            Place(go.GetComponent<RectTransform>(), x, y, w, h);
            var img = go.GetComponent<Image>(); img.color = color; img.raycastTarget = false;
            return img;
        }

        public static Text Label(Transform parent, string name, string text, int size, Color color, float x, float y, float w, float h,
                                 TextAnchor align = TextAnchor.UpperLeft, FontStyle style = FontStyle.Normal)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            Place(go.GetComponent<RectTransform>(), x, y, w, h);
            var t = go.GetComponent<Text>();
            t.font = DefaultFont; t.fontSize = size; t.fontStyle = style; t.color = color; t.alignment = align;
            t.horizontalOverflow = HorizontalWrapMode.Wrap; t.verticalOverflow = VerticalWrapMode.Truncate;
            t.supportRichText = false; t.raycastTarget = false; SetText(t, text);
            return t;
        }

        /// <summary>A uGUI button (Image + Text + Button). The canvas-level ISDK poke surface turns a fingertip press into onClick.</summary>
        public static Button PokeButton(Transform parent, string name, string label, int size, float x, float y, float w, float h, Color bg, Color fg)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            Place(go.GetComponent<RectTransform>(), x, y, w, h);
            var img = go.GetComponent<Image>(); img.color = bg;
            var btn = go.GetComponent<Button>(); btn.targetGraphic = img;
            var colors = btn.colors;
            colors.normalColor = Color.white; colors.highlightedColor = new Color(1.15f, 1.15f, 1.15f, 1f);
            colors.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f); colors.selectedColor = Color.white; colors.disabledColor = new Color(0.5f, 0.5f, 0.5f, 0.6f);
            btn.colors = colors;
            var t = Label(go.transform, "Label", label, size, fg, 0, 0, w, h, TextAnchor.MiddleCenter, FontStyle.Bold);
            t.verticalOverflow = VerticalWrapMode.Overflow;
            return btn;
        }

        public static Image Ring(Transform parent, string name, float x, float y, float size, Color color, float fill)
        {
            var img = Box(parent, name, x, y, size, size, color);
            img.sprite = RingSprite();
            img.type = Image.Type.Filled; img.fillMethod = Image.FillMethod.Radial360; img.fillOrigin = 2; img.fillClockwise = true;
            img.fillAmount = fill;
            return img;
        }

        public static Sprite RingSprite()
        {
            if (_ring != null) return _ring;
            const int n = 128;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color32[n * n];
            float c = (n - 1) / 2f, ro = n / 2f - 1f, ri = ro * 0.78f;
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c));
                    float a = Mathf.Clamp01(Mathf.Min(ro - d, d - ri) + 0.5f);
                    px[y * n + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            tex.SetPixels32(px); tex.Apply(false, true);
            _ring = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f);
            _ring.name = "ph_ring";
            return _ring;
        }

        // ---- canvas ------------------------------------------------------------------------------------------------

        /// <summary>Prepares an existing (inactive) panel canvas from the scene: size in mm, scale, raycaster, camera; wipes its placeholder children.</summary>
        public static RectTransform PreparePanel(Transform panel, float wMm, float hMm, Camera worldCamera, bool attachPoke)
        {
            var rt = (RectTransform)panel;
            ClearChildren(panel);
            var canvas = panel.GetComponent<Canvas>() ?? panel.gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            if (worldCamera != null) canvas.worldCamera = worldCamera;
            var scaler = panel.GetComponent<CanvasScaler>() ?? panel.gameObject.AddComponent<CanvasScaler>();
            scaler.dynamicPixelsPerUnit = 4f;
            if (panel.GetComponent<GraphicRaycaster>() == null) panel.gameObject.AddComponent<GraphicRaycaster>();
            rt.sizeDelta = new Vector2(wMm, hMm); rt.localScale = Vector3.one * 0.001f;
            if (attachPoke) AttachPoke(panel.gameObject, canvas, wMm, hMm);
            return rt;
        }

        /// <summary>
        /// ISDK wiring for a world-space uGUI canvas (PointableCanvas + clipped plane surface + PokeInteractable): a fingertip
        /// poke through the panel becomes a normal uGUI click, picked up by the PointableCanvasModule on the scene's EventSystem.
        /// The panel GameObject must still be INACTIVE so the injected references exist before Awake/Start run.
        /// </summary>
        public static void AttachPoke(GameObject panel, Canvas canvas, float wMm, float hMm)
        {
            if (panel.activeSelf) Debug.LogWarning("[PhantomHand] AttachPoke on an active panel: references are injected after Awake.");
            if (panel.GetComponent<PokeInteractable>() != null) return;
            var plane = panel.AddComponent<PlaneSurface>();
            plane.InjectAllPlaneSurface(PlaneSurface.NormalFacing.Backward, false);
            var clipper = panel.AddComponent<BoundsClipper>();
            clipper.Position = Vector3.zero;
            clipper.Size = new Vector3(wMm, hMm, 100f);   // canvas units = mm; z covers the plane
            var clipped = panel.AddComponent<ClippedPlaneSurface>();
            clipped.InjectAllClippedPlaneSurface(plane, new List<IBoundsClipper> { clipper });
            var poke = panel.AddComponent<PokeInteractable>();
            poke.InjectAllPokeInteractable(clipped);
            var pc = panel.AddComponent<PointableCanvas>();
            pc.InjectAllPointableCanvas(canvas);
        }

        /// <summary>Real size of a rect in centimetres (width, height), accounting for the canvas scale.</summary>
        public static Vector2 SizeCm(RectTransform rt)
        {
            var s = rt.lossyScale;
            return new Vector2(rt.rect.width * Mathf.Abs(s.x) * 100f, rt.rect.height * Mathf.Abs(s.y) * 100f);
        }
    }
}
