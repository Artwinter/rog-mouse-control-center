# Tactical UI Redesign

## Intent

Replace the current stacked-card interface with the selected A direction: a tactical hardware
instrument deck. Keep all mouse and headset behavior; change the visual shell, navigation, type,
spacing, state language, and motion.

## Goals

- Make the device rail the persistent navigation owner.
- Preserve mouse DPI, polling, debounce, snapping, autostart, tray, and status behavior.
- Preserve headset battery, charging-path, receiver, read-only, and suspension behavior.
- Establish a reusable WPF visual system with tokens documented in `DESIGN.md`.
- Keep the window usable on a 1366x768 Windows desktop.
- Make the active page and live status legible at a glance.

## Non-goals

- New device protocols or settings.
- Bluetooth headset support.
- A new settings store or profile system.
- A second tray icon.
- Pixel-for-pixel reproduction of Razer Synapse.

## Chosen Direction

The user selected A from two rendered previews: `design-previews/a-tactical.png`.

The shell uses a graphite ground, instrument panels, a 230 px device rail, signal red for the ROG
mouse, signal green for the Razer headset, Bahnschrift utility labels and numerics, and restrained
line icons. The signature is the persistent device rail plus live telemetry readouts.

## Interaction

- Rail items switch `MousePage` and `HeadsetPage`; the rail never disappears.
- Mouse page prioritizes DPI, stages, and response settings.
- Headset page prioritizes battery level, charging-path state, receiver status, and last read.
- Actions stay at the bottom of the active workspace.
- Existing footer diagnostics remain visible.

## Constraints

- C# 5 and .NET Framework 4.x WPF.
- No new dependencies.
- All existing control names used by controllers remain available or are updated in the same change.
- The app remains a single x86 exe.

## Verification

- Build and tests pass.
- Mouse and headset screenshots at the fitted design size.
- Live headset reading remains 76% or another honest real value.
- Page switching, refresh, minimize/park, fullscreen gate, and `--no-device` paths remain valid.
