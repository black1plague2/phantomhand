# ⟶ v3 AMENDMENT (2026-09-19 17:20, user feedback on v2 — BINDING, wins over anything below)

User verdict on the v2 build: **"very clustered and very dull"**. New direction: *more colours, readable for a
doctor, fewer subtitles, dropdowns and charts wherever they help, less content per page, easy/layman.*

**What stays from v2:** true-black base, dark-red brand (oxblood primary, crimson for live/Stop), sectioned
containers, titles only, one primary action per screen, Atkinson Hyperlegible Next, the reduced 360 dp type scale.

**What changes:**
1. **Colour is now used for meaning, generously but not neon.** Add a readable semantic palette (mid saturation,
   tested ≥ 4.5:1 on `panel`):
   | Token | Hex | Meaning |
   |---|---|---|
   | `good` | `#4FBF8B` | success, improving, in the basket, connected |
   | `warn` | `#E0A43B` | timeout, needs attention, trunk lean |
   | `info` | `#5B9BE6` | reaction time series, neutral info, links |
   | `accent2` | `#A98BE0` | smoothness series, second data series |
   | `bad` | `#E0605A` | dropped / wrong basket / decline / disconnected |
   Outcome colours (everywhere): success `good`, timeout `warn`, dropped `#C98B5B`, wrong basket `bad`.
   Metric colours (fixed per metric so the doctor learns them): Reaction time `info`, Movement time `#56C2C9`,
   Peak speed `accent2`, Smoothness `#D07BB5`, Trunk lean `warn`, Success `good`.
   KPI tiles get a 3 px left colour bar in their metric colour + the value in that colour. Improvement arrows
   ↑/↓ coloured `good`/`bad` by *clinical* direction (lower RT = good).
   Still forbidden: gradients, glows, neon (saturation > ~75 %), shadows.
2. **Less per page.** Max 4 sections visible per screen. Anything else goes behind a **dropdown selector**
   (e.g. Patient screen: a `Metric` dropdown switches one big trend chart between Reaction time / Movement time /
   Peak speed / Smoothness / Trunk lean, instead of 5 stacked charts; Session report: `Trial` dropdown; Reports:
   `Patient` + `Period` dropdowns as filters).
3. **Layman words.** "Reaction time" → keep; "SPARC" → "Smoothness"; "LDLJ", "MDC", "endpoint error" never
   shown by those names ("Accuracy (cm)" instead); outcomes "In basket / Too slow / Dropped / Wrong basket".
   Numbers rounded (RT to 10 ms, speeds 1 dp, %) — no raw decimals.
4. **Charts wherever a number has history:** donut for outcomes, one trend chart with the metric dropdown,
   bars per trial, weekly dose bars. Big, few, labelled axes, legend only when > 1 series.
5. **Zero subtitles.** A title, then content. No helper paragraphs anywhere (empty states: one line).

---

# OPUS app — Design v2 (binding, 2026-09-19)

Supersedes the Stitch "Kinetic Clinical VR" direction. The user rejected it: too neon, too much text, too many
buttons, weak analytics. Direction from the user, verbatim in spirit: **dark red and black, darker colours, no
neon, titles only (no subtitles or helper text), proper structured containers, real graphs and pie charts for
analytics, remove extra buttons and text.** The Stitch screens remain useful only for *which information* each
screen shows, not for how it looks.

## 1. Palette (the only colours allowed)

| Token | Hex | Use |
|---|---|---|
| `black` | `#000000` | App background. True black, not tinted. |
| `panel` | `#120708` | Section containers. |
| `panelRaised` | `#1C0A0C` | Tiles inside a section, table header, selected row. |
| `line` | `#33161A` | 1 px borders and dividers. The only way containers are separated — no shadows, no glows. |
| `oxblood` | `#7A1A20` | Primary: filled primary button, selected nav item, the main data series. |
| `crimson` | `#A8242C` | Emphasis only: live indicator dot, the current value on a chart, destructive "Stop". Never large fills. |
| `text` | `#ECE4E3` | Titles and values. |
| `textDim` | `#8F8384` | Axis labels, units, table secondary columns. |

Chart series (in this order, all muted — no saturated or glowing colours): `#A8242C` crimson, `#D8CCC4` bone,
`#7E706C` stone, `#5A1419` maroon, `#B9837E` clay. Outcome colours are fixed everywhere:
success = bone `#D8CCC4`, timeout = stone `#7E706C`, dropped = clay `#B9837E`, wrong basket = crimson `#A8242C`.
Improvement vs. decline is shown with an arrow glyph + sign, not green/red.

Forbidden: gradients, glows, blurs, neon/cyan/blue accents, drop shadows, coloured chips everywhere, pill badges on
every row, emoji.

## 2. Type

One family: **Atkinson Hyperlegible Next** (already bundled; chosen for low-vision legibility — stroke patients
and older clinicians). Tabular figures for every number (`FontFeature.tabularFigures()`).

| Role | Size / weight |
|---|---|
| Screen title | 22 / 700 |
| Section title | 14 / 600 |
| Big value | 26 / 700, unit 12 / 400 `textDim` beside it |
| Body / row | 13 / 400 |
| Axis / table meta | 11 / 400 `textDim` |

Sizes reduced 2026-09-19 (was 28 / 17 / 34 / 15 / 12). The original scale was authored against the 390 dp
golden width, but the pilot phone (CPH2381, 1080x2412 at density 480) renders **360 dp** wide at system
`font_scale` 1.0, which pushed list rows to two lines and wrapped section titles. Goldens must include a
360x800 phone breakpoint, not only 390x844.

Sentence case everywhere. No all-caps labels, no eyebrows above titles, no "A · B · C" meta strings.

## 3. Copy rules (the big one)

- **Titles only.** A section has a title and its content. No subtitle, no description, no helper paragraph, no
  "Learn more". If a value needs a unit, the unit sits next to the number.
- Empty states: one short line saying what to do ("No sessions yet"), plus at most one button.
- Errors: one line, what happened and the fix ("Headset disconnected. Reconnecting.").
- Remove: mock-auth explanation text, "pairing code" paragraphs, "open a session folder" card on phone, MDC
  explainer text (MDC shown as a dashed band on the chart instead), long clinical descriptions in list rows.

## 4. Structure

- Screen = left-aligned title + a vertical stack of **sections**. Section = `panel` background, 1 px `line`
  border, radius 12, 16 px padding, section title top-left, optional single icon action top-right.
- Inside a section: tiles (`panelRaised`, radius 8, no border) in a 2- or 3-column grid; or a chart; or a list
  with 1 px dividers. Two radii only (12 outer, 8 inner). 16 px gaps between sections, 8 between tiles.
- **One primary action per screen** (filled `oxblood`, full width at the bottom of its section). Everything
  else: icon buttons in the app bar or an overflow menu. Delete duplicate/decorative buttons.
- Bottom nav: Patients · Monitor · Programs · Reports. Icons + label, selected = `oxblood` indicator, no
  animation beyond the default. Devices/Settings behind the app-bar avatar.

## 5. Screens

**Patients** — title "Patients", search field, list rows: name (15/600), condition short form ("Stroke · right"),
right side: last session date + a 40 px success bar. No descriptions.

**Patient** — title = patient name.
1. Tiles: Sessions · Success % · Dose this week (x / target).
2. Section "Reaction time" — line chart over sessions, MDC as a dashed band, latest point crimson.
3. Section "Smoothness" — line chart (SPARC).
4. Section "Outcomes" — donut (success / timeout / dropped / wrong basket) with the success % in the centre and a
   4-row legend with counts.
5. Section "Weekly dose" — bar chart, sessions per week vs target line.
6. Section "Sessions" — list (date, success %, trials) → session report. Primary action: "New program".

**Monitor** — title "Monitor"; live dot (crimson) + latency in the app bar.
1. Big "Trial 3 of 20" + stage name.
2. Tiles: Reaction time · Peak speed · Trunk lean · Success % (live values from metrics_tick/status).
3. Section "Outcomes" — small live donut.
4. Section "Reaction time" — sparkline of per-trial values this session.
5. Section "Events" — last 8 events, one line each ("Apple 3 in the basket").
Actions: Pause (outlined) and Stop (filled crimson) side by side at the bottom. Empty state: "No headset
connected" + "Start hub" + the Wi-Fi IP.

**Reports** — title "Reports", list of sessions (patient, date, success %), newest first.

**Session report** — title = date; app-bar subtitle-free.
1. Tiles: Trials · Success % · Mean RT · Mean peak speed.
2. Section "Outcomes" — donut.
3. Section "Reaction time per trial" — bar chart, one bar per trial coloured by outcome.
4. Section "Speed profile" — line chart (tooltip: 2 dp + unit).
5. Section "Haptic cues" — pie by cue type (trunk lean / hand out of view / success) + delivered %.
6. Section "Trials" — table: #, outcome, RT, MT, peak, SPARC, error cm.

**Programs** — title "Programs"; builder form grouped into sections by param group (Task, Difficulty, Feedback,
Safety), each field a plain label + control; primary action "Send to headset".

**Devices** — title "Devices". Sections: "Hub" (address row, on/off switch), "Headsets" (row per headset with
state + RTT; actions in an overflow menu: Start, Pause, Stop, Recenter), "Haptic sleeve" (connected, battery,
cues sent).

## 6. Motion
None by default. Chart values animate once on first load (300 ms). The live dot pulses. Nothing else moves.

## 7. Quality floor
Contrast ≥ 4.5:1 for text (check `textDim` on `panel`), touch targets ≥ 48 px, text scales to 2.0 without
clipping (goldens at 1.0 and 2.0), works at 390 px wide.
