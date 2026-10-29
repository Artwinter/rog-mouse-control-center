# Changelog

## v1.6.0 - 2026-10-29

### Added

- New porcelain-cobalt application icon with multi-size ICO assets.
- Branded tray menu with concise battery status and vertical-centered text.
- Hover-triggered tooltip refresh for more reliable battery tooltips.

### Changed

- Renamed the application to "外设仪表台" / "Peripheral Instrument".
- Renamed the executable to `PeripheralInstrument.exe`.
- Tooltip and tray status text now show battery percentages only.
- Tray device rows open the corresponding mouse or headset page.
- Existing startup entries migrate automatically to the new application name.

### Fixed

- Windows launch approval could leave autostart enabled in the UI while disabled by the OS.
- Tooltip and menu text no longer include transport or charging details.
- Tray menu text is vertically centered within its selected row.
