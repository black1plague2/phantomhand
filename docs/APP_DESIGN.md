# Chetna App Design Direction (v1, 2026-09-14, Opus)

Binding for Track A. Produced with the frontend-design skill. Implement as a Flutter `ThemeExtension` + widgets, not per-screen styling.

## Subject, audience, job
- **Subject:** VR upper-limb neurorehabilitation. What we sell is *movement quality*: reach paths, smoothness, trunk lean, the affected side.
- **Audience:** physiotherapists and rehab doctors on clinic tablets and PCs, often mid-session with a patient beside them. Secondary: patients and caregivers at home (older users, possibly low vision or one working hand).
- **Primary job:** see at a glance whether a patient is *really* improving, and supervise a live session without looking away from the patient for long.

## The one memorable thing: the reach trace
Every session is represented by **the patient's actual hand paths**, drawn top-down over their workspace, instead of a generic icon, avatar, or score tile.
Left-hand paths are lake blue, right-hand paths are ochre, and unsuccessful reaches are drawn dashed.
It appears as a small glyph in session lists and the patient timeline, large on the live monitor (drawing in as each reach finishes), and full-size in the session report.
Everything else stays quiet and disciplined so the traces carry the personality.

## Tokens

### Color (light)
| Token | Hex | Role |
|---|---|---|
| `mist` | `#EDF1F4` | app ground (cool, not cream) |
| `paper` | `#FFFFFF` | working surfaces: forms, report body, charts |
| `ink` | `#1C2833` | text, primary filled buttons, chart baselines |
| `slate` | `#5E6B77` | secondary text, inactive icons, axis labels |
| `rule` | `#D3DBE2` | 1 px separators and input outlines (the only border color) |
| `lake` | `#2F6FA8` | **left side** (paths, L/R charts, side chips) |
| `ochre` | `#B8741F` | **right side** |
| `leaf` | `#2E7D5B` | only for "improved beyond MDC", never decorative |
| `alert` | `#B3261E` | errors, Stop session, invalid data |

Dark: `mist #10171D`, `paper #18212A`, `ink #E4EAEF`, `slate #9AA7B3`, `rule #2C3843`, `lake #7FB1E0`, `ochre #E0A55A`, `leaf #6CC39B`, `alert #F2837A`.
Lake/ochre were chosen to stay distinguishable in deuteranopia/protanopia. Side is **also** encoded by line style in the report (left solid, right dotted) and by an "L"/"R" letter on chips, so color is never the only carrier. All text pairs meet WCAG AA; verify with a contrast test.

### Type
- **One family: Atkinson Hyperlegible Next** (OFL; designed by the Braille Institute for low-vision legibility). The choice is justified by the audience, not taste. Weights 400 / 600 / 700. Use **tabular figures** (`FontFeature.tabularFigures()`) for every number in tables, live readouts and charts. No monospace face.
- Bundle the font files in `app/assets/fonts/` (from github.com/google/fonts, OFL) so clinics work offline. Don't fetch fonts at runtime.
- Scale (ratio 1.25, 16 px body): 12.8 caption · 16 body · 20 title-s · 25 title · 31 headline · 39 display (patient name on profile, live trial counter only). Line height: 1.45 body, 1.2 headings. Line length ≤ 72 ch in notes/reports.
- Patient mode multiplies the whole scale by 1.25 and must still pass at a 200 % system text scale.
- Headings are plain sentence case. No all-caps labels, no eyebrow labels above headings, and no single emphasized word inside a heading.

### Shape, depth, spacing
- Radius follows hierarchy: panes 16, inputs/buttons 10, chips fully round, charts 0.
- **No drop shadows.** Depth comes from `mist` ground vs `paper` surface, plus `rule` lines only where content needs separating.
- Spacing on a 4 pt grid: 8 / 12 / 16 / 24 / 32 / 48. Touch targets ≥ 48 dp; live-session controls 64 dp.
- Content is not chopped into identical cards. Lists are lists (rows with rules); only the live monitor and the report use large panes.

## Layout

### Tablet / desktop (≥ 900 dp): clinician workstation, left-aligned
```
┌──────┬──────────────────────────────────────────────┬───────────────────┐
│ rail │ Asha Verma                                    │ Live now          │
│      │ Right side affected · Ischemic stroke, 9 wks  │ Quest 3 · paired  │
│ Pts  │                                              │ ┌───────────────┐ │
│ Prog │ Recovery line (SPARC, reaction time, reach)   │ │  live trace   │ │
│ Live │ ───────────────╱▔▔▔▔ MDC band shaded ───────  │ └───────────────┘ │
│ Devs │                                              │ Trial 7 of 20     │
│      │ Sessions  (time runs left → right)           │ [Pause] [End]     │
│      │ ◌ ◌ ◌ ◌ ◌ ◌ ◌   ← reach-trace glyphs          │                   │
│      │ Outcome measures · Notes                     │                   │
└──────┴──────────────────────────────────────────────┴───────────────────┘
```
The right "Live now" pane appears only when a headset is connected. Otherwise the centre column takes the width.

### Phone (< 600 dp): single column, bottom navigation, the live monitor full-screen with controls in thumb reach.

### Live monitor (the screen used mid-session)
```
┌────────────────────────────────────────────────────────────┐
│ Connected to Quest 3, 18 ms delay        Hands: L good R low│
│                                                            │
│              top-down workspace, patient at bottom         │
│         targets appear as rings; finished reaches          │
│         draw in as lake/ochre strokes (400 ms)             │
│                                                            │
│ Trial 7 of 20      Reaction 412 ms   Smoothness −1.9  Trunk lean 3 cm │
├────────────────────────────────────────────────────────────┤
│ [ Pause ]  [ Adjust difficulty ]  [ Send message ]   [ End session ] │
└────────────────────────────────────────────────────────────┘
```
"End session" is `alert`-outlined and asks for confirmation. Pause is an immediate `ink` filled button. Every command shows its state inline: "Pausing…" then "Paused". If an ack fails, the button explains: "Headset didn't confirm. Try again."

### Program builder (a real sequence, so numbering is meaningful here)
Left: game library (from manifests). Middle: numbered, reorderable blocks with duration. Right: the dynamic form for the selected block, grouped by `x-ui.group`, with presets as a segmented control at the top and a live preview of the target workspace (the same top-down view as the reach trace) that updates as ranges change.

## Motion
One orchestrated motion only: **the reach stroke drawing in** on the live monitor when a trial ends (400 ms ease-out). Everything else is instant, apart from responses to user actions (expand/collapse, reorder). Honour `MediaQuery.disableAnimations`: the stroke then appears fully drawn.

## Language
- Plain, clinical, sentence case, active verbs: "Start session", "Pause", "End session", "Send program to headset", "Run analysis".
- The same action keeps the same name through the flow: "Send program to headset" → toast "Program sent to headset".
- Metrics use a human name first with the method in brackets: "Smoothness (SPARC)", "Reaction time", "Trunk lean", "Reach area".
- Data quality in words plus a glyph, never color alone: **Reliable** · **Partial tracking** (hatched fill) · **Not usable** (struck through, excluded from trends).
- Change is stated honestly: "Improved beyond normal variation" (leaf) · "Within normal variation" (slate) · "Worse beyond normal variation" (alert). This is the MDC band made readable.
- Errors say what happened and what to do, with no apologies: "Headset disconnected. The session keeps running on the headset and will sync when it reconnects."
- Empty states invite action: "No sessions yet. Send a program to a headset to start the first one."
- No middle-dot meta strings in the UI chrome (the wireframe dots are notation only). Use commas or separate lines.

## Review against generic defaults (what I changed and why)
1. First draft used a deep green brand primary. That is the default look for health apps, so primary buttons are now **ink**, and green is reserved for real improvement.
2. First draft had a KPI tile row (big number + small label) on the patient page. That is the generic dashboard hero, so it was replaced with the **recovery line with MDC band**, which answers the actual clinical question.
3. Session history as a grid of cards was replaced by a **time-ordered strip of reach-trace glyphs**, which carry real information.
4. A monospace face for numbers was dropped in favor of tabular figures in the same accessible family.

## Implementation notes for Flutter
- `OpusTokens extends ThemeExtension` (colors, radii, spacing, side colors, quality styles); `ColorScheme` mapped from the tokens; `TextTheme` from the scale with `fontFamily: 'AtkinsonHyperlegibleNext'`.
- `ReachTraceGlyph` (`CustomPainter`): input = decimated wrist paths per trial (~40 points/trial, x = lateral, y = forward, in calibration space) + outcome + side. Decimate once in the repository from `kin_*.json` using events for trial windows; cache in Drift. Sizes: 48 dp (list), 160 dp (timeline hover/detail), full (report/live). Draw the workspace arc (max reach) faintly in `rule`.
- Live monitor uses the same painter fed by the live stream: each `trial_event` target → ring, and on `trial_end` it draws the path. The live channel carries no kinematics, so draw a straight home→target stroke live, then replace it with the real path once the kin chunk for that trial is uploaded.
- Charts: `fl_chart` with no grid clutter (baseline + MDC band only), direct end-of-line labels instead of legends, tabular figures.
- **Visual QA required:** golden tests for patient profile, live monitor, program builder and session report at phone (390×844), tablet (1280×800) and desktop (1600×1000), light and dark, with text scale 1.0 and 2.0. Also screenshot the Windows build of those screens to `logs/sessions/screens/app/` and review them for overflow, contrast, and alignment.
