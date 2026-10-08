---
name: Kinetic Clinical VR
colors:
  surface: '#10131a'
  surface-dim: '#10131a'
  surface-bright: '#363940'
  surface-container-lowest: '#0b0e14'
  surface-container-low: '#191c22'
  surface-container: '#1d2026'
  surface-container-high: '#272a31'
  surface-container-highest: '#32353c'
  on-surface: '#e0e2eb'
  on-surface-variant: '#e5bdb7'
  inverse-surface: '#e0e2eb'
  inverse-on-surface: '#2d3037'
  outline: '#ac8883'
  outline-variant: '#5c403b'
  surface-tint: '#ffb4a8'
  primary: '#ffb4a8'
  on-primary: '#690001'
  primary-container: '#d92d20'
  on-primary-container: '#fff6f5'
  inverse-primary: '#bc140d'
  secondary: '#7bd0ff'
  on-secondary: '#00354a'
  secondary-container: '#00a6e0'
  on-secondary-container: '#00374d'
  tertiary: '#ffb3b0'
  on-tertiary: '#680010'
  tertiary-container: '#d4333e'
  on-tertiary-container: '#fff6f5'
  error: '#ffb4ab'
  on-error: '#690005'
  error-container: '#93000a'
  on-error-container: '#ffdad6'
  primary-fixed: '#ffdad5'
  primary-fixed-dim: '#ffb4a8'
  on-primary-fixed: '#410000'
  on-primary-fixed-variant: '#930002'
  secondary-fixed: '#c4e7ff'
  secondary-fixed-dim: '#7bd0ff'
  on-secondary-fixed: '#001e2c'
  on-secondary-fixed-variant: '#004c69'
  tertiary-fixed: '#ffdad8'
  tertiary-fixed-dim: '#ffb3b0'
  on-tertiary-fixed: '#410006'
  on-tertiary-fixed-variant: '#93001b'
  background: '#10131a'
  on-background: '#e0e2eb'
  surface-variant: '#32353c'
typography:
  display-lg:
    fontFamily: Hanken Grotesk
    fontSize: 40px
    fontWeight: '700'
    lineHeight: 48px
    letterSpacing: -0.02em
  display-lg-mobile:
    fontFamily: Hanken Grotesk
    fontSize: 32px
    fontWeight: '700'
    lineHeight: 40px
    letterSpacing: -0.01em
  headline-lg:
    fontFamily: Hanken Grotesk
    fontSize: 28px
    fontWeight: '600'
    lineHeight: 36px
    letterSpacing: -0.01em
  headline-md:
    fontFamily: Hanken Grotesk
    fontSize: 22px
    fontWeight: '600'
    lineHeight: 28px
  headline-sm:
    fontFamily: Hanken Grotesk
    fontSize: 18px
    fontWeight: '600'
    lineHeight: 24px
  body-lg:
    fontFamily: Hanken Grotesk
    fontSize: 16px
    fontWeight: '400'
    lineHeight: 24px
  body-md:
    fontFamily: Hanken Grotesk
    fontSize: 14px
    fontWeight: '400'
    lineHeight: 20px
  body-sm:
    fontFamily: Hanken Grotesk
    fontSize: 12px
    fontWeight: '400'
    lineHeight: 16px
  label-lg:
    fontFamily: JetBrains Mono
    fontSize: 14px
    fontWeight: '600'
    lineHeight: 18px
    letterSpacing: 0.04em
  label-md:
    fontFamily: JetBrains Mono
    fontSize: 12px
    fontWeight: '500'
    lineHeight: 16px
    letterSpacing: 0.05em
  label-sm:
    fontFamily: JetBrains Mono
    fontSize: 10px
    fontWeight: '500'
    lineHeight: 14px
    letterSpacing: 0.08em
rounded:
  sm: 0.125rem
  DEFAULT: 0.25rem
  md: 0.375rem
  lg: 0.5rem
  xl: 0.75rem
  full: 9999px
spacing:
  gutter: 1rem
  gutter-tablet: 1.25rem
  gutter-desktop: 1.5rem
  margin: 1rem
  margin-tablet: 1.5rem
  margin-desktop: 2rem
  space-xs: 0.25rem
  space-sm: 0.5rem
  space-md: 0.75rem
  space-lg: 1.25rem
  space-xl: 2rem
---

## Brand & Style

This design system is engineered for next-generation medical virtual reality rehabilitation and surgical telemetric diagnostics. The aesthetic combines surgical hardware rigor with real-time biometric telemetry. The brand personality is uncompromising, clinical, vigilant, and authoritative—built specifically for high-acuity physical therapists, neuro-rehabilitation clinicians, and orthopedic surgeons operating in both 2D companion console dashboards and head-mounted spatial heads-up displays (HUDs).

The design style fuses **Minimalism** with **High-Contrast Precision Hardware Instruments**. Every visual element must project absolute diagnostic fidelity and zero latency. Warm, muddy, pastel, or earthy tones (such as beige, peach, or muted olive) are strictly prohibited; the visual environment relies strictly on pure obsidian depths, razor-sharp geometric separation, vibrant clinical crimson, and luminous kinematic ice blue. Interactions evoke medical-grade physical actuators—solid, tactile, and definitive.

## Colors

The palette enforces extreme signal-to-noise separation against an ultra-dark spatial void.

- **Base Void (Canvas):** `#080B11` serves as the primary canvas, tuned to optimize OLED power draw, prevent optic flare inside VR headsets, and maintain absolute contrast.
- **Container Surfaces:** Surface layering relies on cold obsidian tones:
  - Base Surface Level 1: `#0F1622` (primary cards, telemetry panels, structural sidebars).
  - Raised Surface Level 2: `#161F2E` (hovered nodes, interactive controls, tool overlays, floating HUD modules).
- **Structural Boundaries:** `#242E3D` defines crisp, 1px structural boundaries across all surfaces to guarantee panel delineation in pitch environments.
- **Primary Clinical Crimson:** `#D92D20` and its highlight `#E02828` are reserved strictly for high-priority operational interactions, session start/stops, active recording states, and safety threshold alerts. `#B51A2B` acts as the pressed/deep-shade anchor.
- **Kinematic Ice Blue:** `#38BDF8` (with `#60A5FA` for soft vector trails) represents spatial joint coordinates, degrees-of-freedom telemetry, limb path traces, and calibration indicators.
- **Typography & Metric Tones:**
  - Primary Readouts: `#F8FAFC` (pure, razor-sharp contrast for live metric values, angles, and critical patient state).
  - Secondary / Supporting: `#94A3B8` (metadata, units, anatomical coordinate labels, inactive timestamps).
  - Ambient Demarcation / Inactive Borders: `#1E293B`.

## Typography

The typographical hierarchy pairs a clean, hyper-legible neo-grotesque sans-serif (`Hanken Grotesk`) with a specialized, fixed-pitch monospaced typeface (`JetBrains Mono`) for all live physiological metrics, coordinate axes (X, Y, Z), joint angles, and range-of-motion percentage displays.

All numeric metric readouts must use tabular figure formatting (`tnum`) to eliminate spatial jitter during continuous high-frequency updates (e.g., 90Hz to 120Hz limb tracking). Section headings maintain tight negative tracking for clinical authority, while telemetry labels are rendered in all-caps monospaced type with expanded letter spacing to ensure rapid identification under reduced cognitive bandwidth or low-resolution VR viewport conditions.

## Layout & Spacing

The design system employs a rigid modular layout based on a base 4px/8px incremental grid.

- **Grid Architecture:** Desktop companion monitors operate on a 12-column fluid grid with `1.5rem` (24px) gutters. VR flat-plane HUD overlays use a strict 6-column or quadrant multi-panel layout to anchor within human peripheral comfort cones (30°–45° visual field). Mobile and tablet telemetry tablets use an 8-column and 4-column structure respectively.
- **Spatial Densities:** Spacing tokens prioritize operational density over expansive leisure space. Critical biometrics and spatial telemetry cards pack tightly using `space-xs` (4px) and `space-sm` (8px) gaps to minimize physical eye travel during live therapeutic tracking.
- **Section Margins:** Exterior canvas margins default to `2rem` (32px) on desktop monitoring setups, scaling down to `1rem` (16px) on touch companion devices.

## Elevation & Depth

Spatial hierarchy does not rely on soft, naturalistic drop shadows; such blurs bleed light and muddy dark environments. Instead, depth is structured through **Tonal Stacking**, **Luminous Optical Edges**, and **Direct Backing Traces**:

- **Ground Level (Elevation 0):** Pure spatial void `#080B11`.
- **Structural Panels (Elevation 1):** Solid `#0F1622` bounded by a 1px continuous stroke of `#242E3D`.
- **Floating Controls & Diagnostics (Elevation 2):** `#161F2E` bounded by `#242E3D`, layered with an ultra-concentrated rim glow on interaction: `0 0 0 1px #38BDF8` for tracking tools, or `0 0 0 1px #D92D20` for critical alert and emergency stop surfaces.
- **Overlay Modals & Spatial Heads-Up Displays (Elevation 3):** Solid `#161F2E` backed by an ambient occlusion border `0 8px 32px rgba(0, 0, 0, 0.85)` with a 1px `#242E3D` perimeter.

All transparent overlays are strictly restricted to 95% opacity dark obsidian; pure frosted glass or excessive blur filters are avoided to preserve frame rate and optical clarity.

## Shapes

The design system adheres to a precision-engineered **Soft Architectural Geometry (0.25rem / 4px base)**. 

Medical instruments require defined structural boundaries rather than playful organic curves. Buttons, data badges, input fields, and kinematic meters share a sharp, surgical 4px corner radius. Data cards and primary HUD containers step up to 8px (`rounded-lg`), retaining structural rectilinear discipline. Fully circular pill shapes are forbidden for interactive targets, reserved solely for live status radar beacons and joint tracking vector anchors.

## Components

### Buttons
- **Primary Clinical Action (Emergency / Record / Commit):** Solid `#D92D20` background, pure `#F8FAFC` label, crisp 1px `#B51A2B` border. Hover transitions instantly to `#E02828` with an active inner state of `#B51A2B`.
- **Secondary Hardware Control:** Deep obsidian `#161F2E` with a 1px perimeter of `#242E3D`. Label in `#F8FAFC`. On hover, border activates to `#38BDF8` (Kinematic Ice Blue) with zero background shift.
- **Ghost/Tertiary Actions:** Transparent background, `#94A3B8` typography, shifting to `#F8FAFC` and `#242E3D` background highlight upon hover.

### Chips & Badges
- Display telemetry status, active VR sensors, and bio-feedback state.
- Formed with a 4px corner radius, a background of `#0F1622`, a 1px border of `#242E3D`, and uppercase `label-sm` monospaced font.
- Kinematic tracking states feature an active 6px circular dot pulsing in `#38BDF8`. Critical patient strain chips use an active dot in `#D92D20`.

### Lists & Telemetry Feeds
- Compact horizontal rows partitioned by 1px borders (`#242E3D`).
- Alternating or highlighted row states use background `#161F2E`.
- Numeric labels and kinematic delta metrics are right-aligned using tabular figures in `label-md`.

### Checkboxes & Radios
- Square 16px geometric check targets with 2px corner radius.
- Inactive state: `#0F1622` filled, bordered with `#242E3D`.
- Selected state: `#D92D20` solid fill with a centered `#F8FAFC` medical checkmark. Focus states introduce a 2px offset ring in `#38BDF8`.

### Input Fields & Calibrators
- Dark background (`#080B11`) inset into `#0F1622` card surfaces to communicate depth.
- Border: 1px `#242E3D`. Text: `#F8FAFC`. Placeholder: `#94A3B8`.
- Focus state: border immediately shifts to `#38BDF8` with zero outer shadow blur.

### Cards & Telemetric Containers
- Built on `#0F1622` background with `#242E3D` 1px border.
- Header bands incorporate an integrated monospaced tracking label (`label-sm`) in `#94A3B8`, separating analytical parameters from graphical joint traces.

### Specialized Component: Kinematic Angle Gauge
- A circular or planar degree meter dedicated to VR rehabilitation tracking. Background path in `#161F2E`, active movement path in dynamic `#38BDF8`, exceeding range-of-motion threshold switches the active path immediately to `#D92D20`. Real-time degree numbers display centered in `#F8FAFC` via `JetBrains Mono`.