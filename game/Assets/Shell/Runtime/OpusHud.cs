using Opus.Games.OrchardReach;
using Opus.Sdk;
using UnityEngine;
using UnityEngine.UI;

namespace Opus.Shell
{
    /// <summary>
    /// World-space status panel above the far edge of the table: what to do next, the trial count, whether the
    /// clinician app is connected, and a one-line hand readout (tracked / pinch strength / distance to the apple)
    /// so a tester can see WHY a grab did or didn't register.
    ///
    /// A uGUI world-space Canvas on purpose: the first version used legacy TextMesh, whose "GUI/Text Shader"
    /// doesn't support single-pass-instanced stereo, so the panel simply never appeared in the headset over Link.
    /// UI/Default does.
    /// </summary>
    public sealed class OpusHud : MonoBehaviour
    {
        private Text _main, _link, _debug, _rule;
        private Image _progressFill;
        private string _flash;
        private float _flashUntil;
        private float _nextRender;

        public static readonly Vector3 DefaultPosition = new Vector3(0f, 1.36f, 1.0f);

        // Run17 (task 2): brand palette (docs/design/OPUS_DESIGN_V2.md, `app/lib/core/theme/opus_tokens.dart`
        // `dark`), the same hex values the Flutter app uses -- so the in-headset HUD and the clinician app read
        // as one product instead of two different color systems. No cyan anywhere (that was a leftover from an
        // earlier, since-rejected direction).
        private static readonly Color PanelBg = new Color(0.071f, 0.027f, 0.031f, 0.93f);   // v2 panel #120708
        private static readonly Color PanelLine = new Color(0.200f, 0.086f, 0.102f, 0.55f); // v2 line #33161A
        private static readonly Color Oxblood = new Color(0.478f, 0.102f, 0.125f, 1f);      // v2 oxblood #7A1A20 (primary/dark-red accent)
        private static readonly Color Crimson = new Color(0.659f, 0.141f, 0.173f, 1f);      // v2 crimson #A8242C (accent text / red status)
        private static readonly Color InkWhite = new Color(0.925f, 0.894f, 0.890f, 1f);     // v2 text #ECE4E3
        private static readonly Color SlateDim = new Color(0.561f, 0.514f, 0.518f, 1f);     // v2 textDim #8F8384
        // Status colors: exactly one green / amber / red family for connection state, never a cyan/blue family.
        private static readonly Color StatusGreen = new Color(0.42f, 0.72f, 0.40f, 1f);
        private static readonly Color StatusAmber = new Color(0.85f, 0.62f, 0.25f, 1f);
        private static readonly Color StatusRed = Crimson;

        public static OpusHud Create(OrchardReachSceneController controller)
        {
            var root = new GameObject("OpusHud", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = controller.HeadReference != null ? controller.HeadReference.GetComponent<Camera>() : Camera.main;
            var rt = (RectTransform)root.transform;
            rt.sizeDelta = new Vector2(900, 380);                 // px
            rt.localScale = Vector3.one * 0.001f;                  // 1 px = 1 mm -> 0.90 m x 0.38 m panel
            rt.position = DefaultPosition;
            rt.rotation = Quaternion.Euler(-10f, 0f, 0f);          // tilt toward a seated eye below it
            root.GetComponent<CanvasScaler>().dynamicPixelsPerUnit = 4f;
            var hud = root.AddComponent<OpusHud>();

            // Run17 (task 2): re-styled to the brand palette (v2, see the Color constants above) -- dark panel,
            // a dark-red top accent strip, white body text, and a single green/amber/red status color for the
            // clinician-app link. No cyan anywhere.
            var bg = new GameObject("Backdrop", typeof(RectTransform), typeof(Image));
            bg.transform.SetParent(root.transform, false);
            Stretch((RectTransform)bg.transform, 0, 1);
            bg.GetComponent<Image>().color = PanelBg;

            var accent = new GameObject("AccentBar", typeof(RectTransform), typeof(Image));
            accent.transform.SetParent(root.transform, false);
            var accentRt = (RectTransform)accent.transform;
            accentRt.anchorMin = new Vector2(0f, 0.975f); accentRt.anchorMax = new Vector2(1f, 1f);
            accentRt.offsetMin = Vector2.zero; accentRt.offsetMax = Vector2.zero;
            accent.GetComponent<Image>().color = Oxblood;

            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            // Stage/rule banner above the main line -- dark-red brand accent (v2 crimson), never a cyan/neon
            // color. Empty string (no visible line) outside stage D or when no rule is active.
            hud._rule = MakeText(root.transform, font, "Rule", 0.86f, 1.00f, 28, Crimson, FontStyle.Bold);
            hud._main = MakeText(root.transform, font, "Main", 0.50f, 0.86f, 38, InkWhite, FontStyle.Bold);
            // Simple fill-bar progress meter (trials completed / total) — a thin backdrop strip with a
            // left-anchored fill Image whose fillAmount drives the visible width.
            var barBg = new GameObject("ProgressBg", typeof(RectTransform), typeof(Image));
            barBg.transform.SetParent(root.transform, false);
            var barBgRt = (RectTransform)barBg.transform;
            barBgRt.anchorMin = new Vector2(0.05f, 0.42f); barBgRt.anchorMax = new Vector2(0.95f, 0.48f);
            barBgRt.offsetMin = Vector2.zero; barBgRt.offsetMax = Vector2.zero;
            barBg.GetComponent<Image>().color = PanelLine;

            var barFill = new GameObject("ProgressFill", typeof(RectTransform), typeof(Image));
            barFill.transform.SetParent(barBg.transform, false);
            var barFillRt = (RectTransform)barFill.transform;
            barFillRt.anchorMin = Vector2.zero; barFillRt.anchorMax = Vector2.one;
            barFillRt.offsetMin = Vector2.zero; barFillRt.offsetMax = Vector2.zero;
            var fillImg = barFill.GetComponent<Image>();
            fillImg.color = Oxblood;
            fillImg.type = Image.Type.Filled;
            fillImg.fillMethod = Image.FillMethod.Horizontal;
            fillImg.fillOrigin = 0; // left
            fillImg.fillAmount = 0f;
            hud._progressFill = fillImg;

            // Link-status text starts amber ("searching…"); Render() switches it to green/amber/red by state.
            hud._link = MakeText(root.transform, font, "Link", 0.20f, 0.36f, 24, StatusAmber, FontStyle.Normal);
            hud._debug = MakeText(root.transform, font, "Debug", 0.00f, 0.17f, 20, SlateDim, FontStyle.Normal);
            return hud;
        }

        private static void Stretch(RectTransform r, float yMin, float yMax)
        {
            r.anchorMin = new Vector2(0, yMin);
            r.anchorMax = new Vector2(1, yMax);
            r.offsetMin = new Vector2(24, 6);
            r.offsetMax = new Vector2(-24, -6);
        }

        private static Text MakeText(Transform parent, Font font, string name, float yMin, float yMax, int size, Color color, FontStyle style)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            Stretch((RectTransform)go.transform, yMin, yMax);
            var t = go.GetComponent<Text>();
            t.font = font;
            t.fontSize = size;
            t.fontStyle = style;
            t.color = color;
            t.alignment = TextAnchor.MiddleCenter;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            return t;
        }

        public void Flash(string message)
        {
            _flash = message;
            _flashUntil = Time.time + 4f;
        }

        public void Render(OpusSessionRunner runner, OrchardReachSceneController c, LiveClient.ConnectionState link, float pinchHold)
        {
            if (Time.time < _nextRender) return;
            _nextRender = Time.time + 0.1f;

            var m = c.Module;
            string hand = m?.CurrentSide == "left" ? "LEFT" : "RIGHT";

            // Run15 (task 4): stage/rule banner, patient-facing wording only (never raw param values like
            // "D_sorting"/"red_only" -- same copy-rules concern APP_DESIGN raised for the Flutter live monitor).
            _rule.text = RuleBannerText(m?.Params);

            int trialN = (m?.TrialIndex ?? 0) + 1, trialTotal = m?.TrialCount ?? 0;
            _progressFill.fillAmount = trialTotal > 0 ? Mathf.Clamp01((float)(m?.TrialsCompleted ?? 0) / trialTotal) : 0f;

            string main;
            switch (runner.CurrentPhase)
            {
                case OpusSessionRunner.Phase.Running:
                    int n = (m?.TrialIndex ?? 0) + 1, total = m?.TrialCount ?? 0;
                    string step = m?.CurrentTrialState switch
                    {
                        TrialState.Grasped or TrialState.Holding => "Carry it to the basket and open your fingers",
                        _ => $"Reach the apple with your {hand} hand and pinch it",
                    };
                    main = $"Apple {Mathf.Min(n, total)} of {total}\n{step}";
                    if (runner.LastOutcome != null) main += $"\nLast apple: {Pretty(runner.LastOutcome)}";
                    break;
                case OpusSessionRunner.Phase.Paused:
                    main = "Paused by your clinician\nRest your arm";
                    break;
                case OpusSessionRunner.Phase.Finished:
                    string rt = runner.MeanReactionMs.HasValue ? $" · reaction {runner.MeanReactionMs.Value:F0} ms" : "";
                    main = $"Well done! {runner.TrialsSucceeded} of {runner.TrialsEnded} apples{rt}\n" +
                           (string.IsNullOrEmpty(runner.LastUploadMessage) ? "" : runner.LastUploadMessage + "\n") +
                           "Pinch BOTH hands and hold to play again";
                    break;
                default:
                    main = c.UsingRealHands
                        ? "Sit comfortably and look at the table\nStarting in a moment…"
                        : "Demo mode (no headset): starting…";
                    break;
            }
            if (pinchHold > 0.02f && runner.CurrentPhase != OpusSessionRunner.Phase.Running)
                main += $"\nStarting… {Mathf.RoundToInt(Mathf.Clamp01(pinchHold) * 100)}%";
            if (!string.IsNullOrEmpty(_flash) && Time.time < _flashUntil) main = _flash + "\n" + main;
            _main.text = main;

            var client = runner.Client;
            _link.text = link switch
            {
                LiveClient.ConnectionState.Connected =>
                    $"Clinician app connected{(client != null && client.LastRttMs > 0 ? $" · {client.LastRttMs:F0} ms" : "")}",
                LiveClient.ConnectionState.Connecting => "Clinician app: connecting…",
                LiveClient.ConnectionState.Reconnecting => "Clinician app: reconnecting… (still recording)",
                _ => "Clinician app: searching… (still recording on the headset)",
            };
            // Run17 (task 2): single green/amber/red status family, no cyan -- green only once actually
            // connected, red only when fully disconnected, amber for the two "in progress" states.
            _link.color = link switch
            {
                LiveClient.ConnectionState.Connected => StatusGreen,
                LiveClient.ConnectionState.Connecting or LiveClient.ConnectionState.Reconnecting
                    or LiveClient.ConnectionState.Discovering => StatusAmber,
                _ => StatusRed, // Disconnected only
            };

            var h = c.Hands;
            if (h == null || !c.UsingRealHands) { _debug.text = c.UsingRealHands ? "" : "demo hand driver"; return; }
            var side = m?.CurrentSide == "left" ? HandSide.Left : HandSide.Right;
            if (!h.IsTracked(side)) { _debug.text = $"{hand} hand not seen — hold it in front of you"; return; }
            float d = c.DistanceToFruit(side);
            _debug.text = $"{hand} hand · pinch {h.GetPinchStrength(side):F2}" +
                          (d >= 0 && runner.CurrentPhase == OpusSessionRunner.Phase.Running ? $" · {d * 100f:F0} cm to apple" : "");
        }

        private static string Pretty(string outcome) => outcome switch
        {
            "success" => "in the basket!",
            "timeout" => "time ran out",
            "dropped" => "dropped",
            // Run15 (task 2/4): gentle corrective wording, never a "wrong!"/"fail" framing -- the brief is
            // explicit that a wrong-container placement must never punish, only withhold reward.
            "wrong_target" => "not quite the right basket — try the next one",
            _ => outcome,
        };

        /// <summary>Run15 (task 4): the stage D sorting rule, in the patient-facing wording the brief's example
        /// uses ("Only RED apples in the red basket"), never the raw manifest enum value. Empty outside stage D
        /// or when no rule is chosen yet -- an empty banner line is the "nothing to show" state, not a bug.</summary>
        private static string RuleBannerText(OrchardReachParams p)
        {
            if (p == null || p.Stage != "D_sorting") return "";
            return p.SortRule switch
            {
                "red_only" => "Only RED apples in the red basket",
                "green_only" => "Only GREEN apples in the green basket",
                "ripe_only" => "Only ripe apples — unripe ones go in the compost",
                "unripe_only" => "Only unripe apples — ripe ones go in the compost",
                "color_match" => "Match each apple's color to its basket",
                _ => "",
            };
        }
    }
}
