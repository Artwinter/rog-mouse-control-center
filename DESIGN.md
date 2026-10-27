---
version: alpha
name: "Peripheral Control Center"
description: "A local hardware control deck with tactical instrumentation, precise telemetry, and restrained gaming accents."
colors:
  void: "#080A0B"
  panel: "#101415"
  panel-raised: "#14191A"
  line: "#293032"
  line-soft: "#1B2223"
  text: "#F2F0EA"
  muted: "#89938F"
  dim: "#5B6561"
  signal-red: "#FF4655"
  signal-green: "#79FF5A"
  signal-amber: "#FFBD62"
  primary: "#79FF5A"
typography:
  display:
    fontFamily: "Bahnschrift, Microsoft YaHei UI, Segoe UI"
    fontSize: "28px"
  numeric:
    fontFamily: "Bahnschrift, Microsoft YaHei UI, Segoe UI"
    fontSize: "76px"
  body:
    fontFamily: "Microsoft YaHei UI, Segoe UI"
    fontSize: "12px"
  utility:
    fontFamily: "Bahnschrift, Microsoft YaHei UI, Segoe UI"
    fontSize: "9px"
rounded:
  DEFAULT: "0px"
  control: "2px"
  panel: "2px"
spacing:
  page-padding: "32px"
  rail-width: "230px"
  panel-gap: "18px"
components:
  device-rail:
    backgroundColor: "{colors.panel-raised}"
    textColor: "{colors.text}"
    typography: "{typography.utility}"
    rounded: "{rounded.control}"
  instrument-panel:
    backgroundColor: "{colors.panel}"
    textColor: "{colors.muted}"
    rounded: "{rounded.panel}"
  panel-border:
    backgroundColor: "{colors.line}"
    textColor: "{colors.text}"
  status-badge:
    backgroundColor: "{colors.void}"
    textColor: "{colors.signal-green}"
  status-warning:
    backgroundColor: "{colors.panel}"
    textColor: "{colors.signal-amber}"
  status-danger:
    backgroundColor: "{colors.panel}"
    textColor: "{colors.signal-red}"
  segmented-chip:
    backgroundColor: "{colors.line-soft}"
    textColor: "{colors.text}"
  muted-surface:
    backgroundColor: "{colors.dim}"
    textColor: "{colors.text}"
  primary-action:
    backgroundColor: "{colors.panel}"
    textColor: "{colors.primary}"
---

# Peripheral Control Center Design System

## Overview

### Creative North Star

A compact hardware test bench: the interface should feel like a purpose-built instrument for
reading and adjusting two physical devices, not like a generic dashboard. The visual language
comes from measurement tools, signal paths, equipment labels, and status lamps.

### Product context and register

- **Audience and primary job:** A Windows desktop user adjusting a ROG mouse and checking a Razer
  headset connected over 2.4 GHz.
- **Target market and evidence:** Chinese Windows 10 user; UI copy is Simplified Chinese with
  technical English utility labels.
- **Locale and language policy:** `zh-CN` primary. English is reserved for protocol, mode, and
  short technical labels.
- **Usage scene:** Desktop, frequent short sessions, occasional game use, and background tray use.
- **Register:** Product utility with a restrained gaming identity.
- **Memorable signature:** The left device rail plus instrument readouts and telemetry trace.
- **Restraint:** No decorative gradients, glassmorphism, oversized empty cards, or neon everywhere.
- **Anti-references:** Generic SaaS dashboards, nested rounded cards, soft glass panels, purple
  gradients, and RGB-everywhere gaming skins.
- **Token ownership:** This file owns the accepted visual tokens; `ui/MainWindow.xaml` mirrors them
  as WPF resources. Future visual changes update both together.

## Colors

`void` is the window ground. `panel` and `panel-raised` separate instrument surfaces without
shadows. `line` and `line-soft` encode structure. `text`, `muted`, and `dim` form a three-level
reading hierarchy. `signal-red` identifies the ROG mouse, `signal-green` identifies the Razer
headset and healthy telemetry, and `signal-amber` is reserved for caution or unknown state.

## Typography

Bahnschrift carries headings, labels, protocol strings, and numeric telemetry. Microsoft YaHei UI
carries Chinese prose and controls. The DPI hero uses Segoe UI because WPF's Bahnschrift Light
combination produced a zero-width glyph run in the measured layout; the headset hero and other
numeric readouts retain Bahnschrift. Utility labels are uppercase, spaced, and deliberately small.
Body text never drops below 10.5 px in the normal desktop layout.

## Layout

The window is designed at 1200x780: 62 px title bar, 698 px content, 52 px footer. A persistent
230 px device rail owns navigation; the remaining area is the active device workspace. Workspaces
use a two-column instrument grid and keep actions anchored to the bottom. Window fitting reduces
the shell to the current work area without reflowing controls into a different order.

## Elevation & Depth

Depth comes from tonal layers and one-pixel rules. Static panels do not use shadows. A small glow is
allowed only on live status dots and the active device signal bar. The window itself has one
restrained outer border.

## Shapes

Panels and controls are square or use a maximum 2 px radius. Device rail items, segmented chips, and
instrument panels share the same edge language. Circular geometry is reserved for status dots,
the mouse battery ring, and the physical device illustration.

## Components

### Foundational visual states

Default controls are muted; hover lifts the surface one tonal step; focus uses a 1 px signal-green
outline; selected uses a signal color rail or fill. Disabled controls keep geometry and reduce
contrast. Busy states preserve button size and change copy, never widen the button.

### Device rail

The rail is the only navigation owner. Each device item includes a small device glyph, index,
Chinese name, and connection state. The active item gets a 2 px signal bar and a raised surface.
The rail footer carries the read-only/service-loop explanation.

### Instrument panels

Panels use `panel`, `line`, and `panel-raised`. Titles are utility labels. Values are the visual
anchor; supporting prose stays muted. The right column is for telemetry and protocol facts, not
duplicated actions.

### Buttons and actions

Primary actions are rectangular signal-green outlines on dark surfaces, with uppercase Latin
utility text and Chinese action text. Destructive intent is not introduced. Re-read actions keep
the same geometry while busy.

### Iconography and motion

Icons are thin-stroke line glyphs, not filled emoji or multicolor art. Motion is limited to a short
opacity/slide reveal when a device page appears and a soft status-dot pulse. Reduced motion is
respected by keeping the final state visible.

## Do's and Don'ts

- **Do:** Let the active device rail carry navigation and status.
- **Do:** Keep red for ROG/mouse and green for Razer/headset signal states.
- **Do:** Use telemetry hierarchy: value, unit, state, then supporting data.
- **Don't:** Put every control inside a rounded card.
- **Don't:** Use large empty areas as decoration.
- **Don't:** Use the previous pink/orange gradient as the primary accent.
