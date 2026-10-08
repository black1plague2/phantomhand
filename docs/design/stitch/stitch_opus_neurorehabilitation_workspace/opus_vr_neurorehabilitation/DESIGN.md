---
name: Opus VR Neurorehabilitation
colors:
  surface: '#0b1324'
  surface-dim: '#0b1324'
  surface-bright: '#31394b'
  surface-container-lowest: '#060e1e'
  surface-container-low: '#131b2c'
  surface-container: '#171f31'
  surface-container-high: '#222a3b'
  surface-container-highest: '#2d3547'
  on-surface: '#dae2fa'
  on-surface-variant: '#d9c1ba'
  inverse-surface: '#dae2fa'
  inverse-on-surface: '#283042'
  outline: '#a18c86'
  outline-variant: '#54433e'
  surface-tint: '#ffb59c'
  primary: '#ffccbb'
  on-primary: '#571f09'
  primary-container: '#ffa586'
  on-primary-container: '#793821'
  inverse-primary: '#914b32'
  secondary: '#bbc6e7'
  on-secondary: '#25304a'
  secondary-container: '#3b4662'
  on-secondary-container: '#aab5d5'
  tertiary: '#ffcbc8'
  on-tertiary: '#680010'
  tertiary-container: '#ffa3a0'
  on-tertiary-container: '#9b001d'
  error: '#ffb4ab'
  on-error: '#690005'
  error-container: '#93000a'
  on-error-container: '#ffdad6'
  primary-fixed: '#ffdbcf'
  primary-fixed-dim: '#ffb59c'
  on-primary-fixed: '#390c00'
  on-primary-fixed-variant: '#73341d'
  secondary-fixed: '#d9e2ff'
  secondary-fixed-dim: '#bbc6e7'
  on-secondary-fixed: '#0f1b34'
  on-secondary-fixed-variant: '#3b4662'
  tertiary-fixed: '#ffdad8'
  tertiary-fixed-dim: '#ffb3b0'
  on-tertiary-fixed: '#410006'
  on-tertiary-fixed-variant: '#93001b'
  background: '#0b1324'
  on-background: '#dae2fa'
  surface-variant: '#2d3547'
typography:
  headline-xl:
    fontFamily: Inter
    fontSize: 40px
    fontWeight: '700'
    lineHeight: 48px
    letterSpacing: -0.02em
  headline-xl-mobile:
    fontFamily: Inter
    fontSize: 32px
    fontWeight: '700'
    lineHeight: 40px
    letterSpacing: -0.015em
  headline-lg:
    fontFamily: Inter
    fontSize: 32px
    fontWeight: '600'
    lineHeight: 40px
    letterSpacing: -0.015em
  headline-lg-mobile:
    fontFamily: Inter
    fontSize: 26px
    fontWeight: '600'
    lineHeight: 34px
    letterSpacing: -0.01em
  headline-md:
    fontFamily: Inter
    fontSize: 24px
    fontWeight: '600'
    lineHeight: 32px
    letterSpacing: -0.01em
  headline-sm:
    fontFamily: Inter
    fontSize: 20px
    fontWeight: '600'
    lineHeight: 28px
    letterSpacing: -0.005em
  title-lg:
    fontFamily: Inter
    fontSize: 18px
    fontWeight: '600'
    lineHeight: 26px
  title-md:
    fontFamily: Inter
    fontSize: 16px
    fontWeight: '600'
    lineHeight: 24px
  body-lg:
    fontFamily: Inter
    fontSize: 16px
    fontWeight: '400'
    lineHeight: 24px
  body-md:
    fontFamily: Inter
    fontSize: 14px
    fontWeight: '400'
    lineHeight: 20px
  body-sm:
    fontFamily: Inter
    fontSize: 12px
    fontWeight: '400'
    lineHeight: 18px
  label-lg:
    fontFamily: Inter
    fontSize: 14px
    fontWeight: '500'
    lineHeight: 20px
    letterSpacing: 0.01em
  label-md:
    fontFamily: Inter
    fontSize: 12px
    fontWeight: '500'
    lineHeight: 16px
    letterSpacing: 0.02em
  label-sm:
    fontFamily: Inter
    fontSize: 10px
    fontWeight: '600'
    lineHeight: 14px
    letterSpacing: 0.04em
rounded:
  sm: 0.25rem
  DEFAULT: 0.5rem
  md: 0.75rem
  lg: 1rem
  xl: 1.5rem
  full: 9999px
spacing:
  gutter: 1.25rem
  gutter-mobile: 0.75rem
  gutter-desktop: 1.5rem
  margin: 1.5rem
  margin-mobile: 1rem
  margin-desktop: 2.5rem
  space-xs: 0.25rem
  space-sm: 0.5rem
  space-md: 1rem
  space-lg: 1.5rem
  space-xl: 2.25rem
---

## Brand & Style

This design system establishes a clinical yet human-centric visual language for immersive neurorehabilitation therapy. Balancing medical-grade precision with the comfort required for cognitive and motor recovery, the aesthetic fuses modern clinical precision with dark-mode spatial UI principles.

The target audience spans clinical neurologists, physical therapists, and patients undergoing cognitive-motor rehabilitation. The emotional baseline must foster calm focus, clinical authority, and encouraging warmth, preventing visual fatigue during extended VR-paired digital monitoring sessions.

The design movement combines **Corporate / Modern** spatial discipline with **Glassmorphism** and subtle depth layering:
- Dark slate-indigo foundations eliminate harsh glare and eye strain.
- Vibrant accents (soft peach coral and vital crimson) provide clear functional hierarchy and clinical alert signaling without provoking cognitive distress.
- Crisp structural geometry and subtle translucent containment define focused interaction areas.

## Colors

The color palette is derived from deep neurological darks and energizing metabolic accents, calibrated for high visual ergonomics in dark mode:

- **Primary (`#FFA586` - Soft Peach Coral):** The primary brand and action tint. Used for primary calls-to-action, key progress highlights, active session states, and restorative touchpoints. It softens traditional clinical blues with warm, human reassurance.
- **Secondary (`#242F49` - Rich Indigo Navy):** The structural workhorse for elevated surfaces, floating modules, segmented bars, and card containers resting above the background.
- **Tertiary (`#B51A2B` - Crimson Red):** High-priority clinical feedback, safety boundaries, emergency halt triggers, and key biometrics out-of-range alerts. Paired with `#541A2E` (Deep Wine Berry) for alert backdrops and threshold states.
- **Neutral (`#161E2F` - Deep Midnight Navy):** The foundational dark-mode canvas. Reduces photoreceptive strain and provides deep visual contrast for telemetry readouts.
- **Support Tone (`#384358` - Steel Blue-Gray):** Applied to subtle architectural outlines, inactive track bars, secondary text, and divider surfaces.

## Typography

The typographic hierarchy uses **Inter** across all display levels, delivering the objective clarity and neutral geometry evocative of San Francisco and modern clinical instrumentation.

- **Headlines:** Set with tight tracking to maintain solid, legible anchoring across patient monitoring panels, VR calibration stages, and clinical assessment overviews.
- **Body:** Open line heights ensure effortless scanning of clinical records, session logs, and motor performance summaries under dim clinical lighting.
- **Labels & Telemetry:** Medium to semi-bold weights with subtle tracking expansion for crisp rendering on low-resolution hardware displays, VR HUD overlays, and high-density patient telemetry graphs.

## Layout & Spacing

The layout is built on a responsive 12-column grid system tuned for dense clinical diagnostics and focused rehab sessions:

- **Desktop & VR Companion Dashboards:** 12-column fluid structure with `1.5rem` gutters and `2.5rem` outer margins. Accommodates multi-pane spatial graphs, realtime 3D kinematic avatars, and biometric stream rails.
- **Tablet (Therapist Field Unit):** 8-column layout with `1.25rem` gutters and `1.5rem` margins. Prioritizes single-hand quick selections and patient status side-sheets.
- **Mobile (Patient Tracker):** 4-column layout with `0.75rem` gutters and `1rem` margins, collapsing complex multi-axis motor tracking into stacked session summaries.

Rhythmic component spacing follows a base 4px/8px scale (`space-xs` through `space-xl`) to establish disciplined vertical cadence.

## Elevation & Depth

Visual hierarchy uses tonal surface stacking paired with low-contrast structural outlines and subtle ambient indigo halos:

- **Level 0 (Canvas Base):** Solid deep midnight navy (`#161E2F`). Immersive, unlit backdrop for all spatial telemetry.
- **Level 1 (Surface Standard):** Rich indigo navy (`#242F49`) with a 1px ghost border rendered in `#384358` at 35% opacity. Used for resting session tiles, persistent panels, and charting backgrounds.
- **Level 2 (Elevated & Interactive):** `#242F49` with 6% white overlay, elevated by a soft ambient shadow (`0px 10px 25px -5px rgba(10, 14, 24, 0.65)`).
- **Level 3 (Modals, HUD Overlays, Calibration Tools):** `#242F49` at 85% opacity with `backdrop-filter: blur(16px)` and an ambient primary glow (`0 0 32px -8px rgba(255, 165, 134, 0.18)`), bordered by `#FFA586` at 20% opacity.

## Shapes

The design system adopts a refined **Rounded (`2`)** shape scale, utilizing `0.5rem` (8px) base radii for controls, `1rem` (16px) for cards, and `1.5rem` (24px) for prominent modality modules. 

The curved language mirrors the organic contour pills in the palette specimen, eliminating sharp medical abrasiveness while avoiding excessive softness. Fully circular/pill geometry is strictly reserved for state chips, session badges, and round progress indicators.

## Components

### Buttons
- **Primary Action:** Solid soft peach coral (`#FFA586`) surface with deep midnight navy (`#161E2F`) typography in semi-bold. Height 44px (desktop) or 48px (touch/VR target). Hover initiates an ambient peach glow.
- **Secondary Action:** Rich indigo navy (`#242F49`) container with 1px border in `#384358`. Text in `#FFA586` or high-contrast white.
- **Clinical Emergency / Stop:** Crimson red (`#B51A2B`) fill, white bold label, reinforced with deep wine berry (`#541A2E`) shadow halo for instant somatic identification.

### Chips & Badges
- **Status & Metric Chips:** Pill-shaped capsules (border-radius: 9999px) with semi-transparent indigo fill (`#242F49` at 70%) and steel blue-gray (`#384358`) borders.
- **Therapy Stage Indicators:** Active stage uses soft peach coral text with an inner accent pip; critical range indicators use crimson red text against deep wine berry background fills.

### Lists & Data Rows
- Encased within Level 1 surface cards. Rows separated by 1px subtle rules in `#384358` at 20% opacity.
- Interactive list items feature subtle scale transitions (1.005x) and a background shift to `#384358` at 25% opacity on hover.

### Inputs & Selectors
- Dark recessed surfaces (`#161E2F`) with a continuous 1.5px border in `#384358`. 
- Focused state transforms the border to `#FFA586` with a faint 2px outer glow (`rgba(255, 165, 134, 0.25)`). Text is rendered in crisp off-white (`#F5F7FA`).

### Checkboxes & Radio Controls
- Radio rings and square-rounded checkboxes use `#384358` inactive outlines.
- Selected state fills with `#FFA586`, displaying a `#161E2F` checkmark or center core dot.

### Cards & Telemetry Containers
- Built on Level 1 or Level 2 surfaces using rounded-lg (`1rem`) corners.
- Header zones partition patient kinematic metrics, Range-of-Motion (ROM) angles, and cognitive scores with steel blue-gray dividers and peach coral metadata callouts.

### Domain-Specific Components
- **Biometric Range Gauge:** Segmented arc or bar transitioning from secondary indigo (`#242F49`) through peach coral (`#FFA586`) to deep wine/crimson (`#B51A2B`) for muscle spasticity or heart-rate threshold bounds.
- **VR Session Calibration Tile:** High-contrast focal reticle with soft peach alignment brackets over a frosted `#242F49` translucent card.