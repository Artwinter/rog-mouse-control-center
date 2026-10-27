# Tactical UI Redesign Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the stacked-card WPF interface with the selected tactical instrument-deck direction while preserving all mouse and headset behavior.

**Architecture:** Keep `MainController` and `HeadsetController` as behavior owners. Replace `ui/MainWindow.xaml` with a shared shell (header, persistent device rail, workspace, footer), shared WPF styles, and two device workspaces. Update only the controller lookups and visual state assignments needed by the new element tree.

**Tech Stack:** C# 5, .NET Framework 4.x, WPF, XAML, Windows-bundled `csc.exe`, Chrome-rendered design previews for visual comparison only.

**Spec:** `docs/superpowers/specs/2026-10-28-tactical-ui-redesign.md`

## Global Constraints

- Windows 10/11, .NET Framework 4.x, x86 single executable.
- No new packages or runtime dependencies.
- Keep all existing mouse and headset functions.
- Keep `--no-device`, `--no-poll`, tray, autostart, single-instance, and suspension behavior.
- The device rail must remain visible on both pages.
- Use the approved A palette and type hierarchy from `DESIGN.md`.
- Use native WPF buttons and existing control semantics.
- Verify with tests, build, `--shot`, and `--shot-headset`.

## Review Focus

- A headset page must never cover the device rail; switching must remain possible in both directions.
- Controller `FindName` casts must match the new element types.
- A read/refresh failure must not blank unrelated mouse controls.
- The fitted window must not clip the bottom actions at the minimum work area.
- The headset page must keep real device data and honest unavailable states.

---

### Task 1: Shared Tactical Shell and Tokens

**Files:**
- Modify: `ui/MainWindow.xaml`
- Modify: `src/App.cs`
- Modify: `src/WindowFit.cs`
- Modify: `tests/HeadsetServiceTests.cs`

**Interfaces:**

- Consumes: existing WPF window resources and controller names.
- Produces: `DeviceRail`, `TabMouse`, `TabHeadset`, `MousePage`, `HeadsetPage`, `FootStatus`, `BtnRefresh`, `BtnTrayDiag` in the new shell.

- [ ] **Step 1: Replace the root resources with the tactical token set**

Define `Void`, `Panel`, `PanelRaised`, `Line`, `LineSoft`, `TextPrimary`, `TextSecondary`,
`TextTertiary`, `SignalRed`, `SignalGreen`, `SignalAmber`, plus shared `InstrumentPanel`,
`UtilityLabel`, `InstrumentButton`, `DeviceItem`, `Chip`, `SegChip`, `TacticalSlider`, and
`TacticalToggle` styles.

- [ ] **Step 2: Rebuild the shell**

Create a 62 px title bar, a body grid with a 230 px device rail and active workspace, and a 52 px
footer. Keep the window chrome buttons and footer actions. The rail contains `TabMouse` and
`TabHeadset`; both remain visible on either page.

- [ ] **Step 3: Update window fitting**

Change the design target to 1200x780 and keep the fitted work-area clamp. Add tests for 1200x780,
1366x728, and a narrow 1024x728 work area.

- [ ] **Step 4: Verify the shell parses and the mouse page renders**

Run `.\build\build.ps1` and `.\build\build.ps1` followed by
`.\dist\ROGMouseControlCenter.exe --shot _test\tactical-shell.png`.

Expected: build succeeds and the screenshot shows the rail, header, mouse workspace, and footer
without a XAML parse error.

- [ ] **Step 5: Commit**

```powershell
git add ui/MainWindow.xaml src/App.cs src/WindowFit.cs tests/HeadsetServiceTests.cs DESIGN.md UX-CONTRACT.md
git commit -m "feat: add tactical WPF shell"
```

---

### Task 2: Mouse Instrument Workspace

**Files:**
- Modify: `ui/MainWindow.xaml`
- Modify: `src/MainController.cs`

**Interfaces:**

- Consumes: existing `MouseState`, `AsusProtocol`, and all existing named controls.
- Produces: the same control names in a new two-column instrument layout.

- [ ] **Step 1: Build the DPI instrument panel**

Place `HeroImage` in a compact identity strip, then a large `DpiValue` readout, `DpiSlider`,
`BtnMinus`, `BtnPlus`, `StagePanel`, and the quick-pick buttons. Keep `BatteryArc` and
`BatteryValue` in the rail power cell.

- [ ] **Step 2: Build the response panel**

Place `RatePanel`, `DebouncePanel`, `SnapToggle`, and `AutostartToggle` in a right instrument panel
with compact rows and the same keyboard behavior.

- [ ] **Step 3: Preserve status and firmware surfaces**

Keep `DeviceName`, `StatusDot`, `ConnText`, `SpecPreset`, `SpecRate`, `SpecDebounce`, `SpecConn`,
`SpecCharge`, `SaveHint`, and `InfoFirmware` in the new layout.

- [ ] **Step 4: Verify the mouse screenshot**

Run:

```powershell
.\build\build.ps1
.\dist\ROGMouseControlCenter.exe --shot _test\tactical-mouse.png
```

Expected: controls are readable, no overlap, no clipped actions.

- [ ] **Step 5: Commit**

```powershell
git add ui/MainWindow.xaml src/MainController.cs
git commit -m "feat: rebuild mouse workspace as instrument deck"
```

---

### Task 3: Headset Telemetry Workspace

**Files:**
- Modify: `ui/MainWindow.xaml`
- Modify: `src/HeadsetController.cs`

**Interfaces:**

- Consumes: `HeadsetReading`, `HeadsetService`, `HeadsetPageState`.
- Produces: `HeadsetBattery`, `HeadsetBar`, `HeadsetState`, `HeadsetConn`, `HeadsetPower`,
  `HeadsetReceiver`, `HeadsetLastRead`, `HeadsetStatus`, `HeadsetStateBadge`, `BtnHeadsetRefresh`.

- [ ] **Step 1: Build the telemetry panel**

Use the selected A composition: large percentage, segmented level track, telemetry trace, charging
path, receiver, protocol, and last-read details. Keep the action anchored at the bottom.

- [ ] **Step 2: Add state-specific visual treatment**

Map healthy headset state to signal green, no-response to amber, and receiver missing/open failure
to red. Keep text labels as the primary state signal.

- [ ] **Step 3: Verify the live headset screenshot**

Run:

```powershell
.\build\build.ps1
.\dist\ROGMouseControlCenter.exe --shot-headset _test\tactical-headset.png
```

Expected: real or honest unavailable data, no overlapping elements, visible rail.

- [ ] **Step 4: Commit**

```powershell
git add ui/MainWindow.xaml src/HeadsetController.cs
git commit -m "feat: rebuild headset workspace as telemetry panel"
```

---

### Task 4: Interaction, Fitting, and Final Verification

**Files:**
- Modify: `src/MainController.cs`
- Modify: `src/HeadsetController.cs`
- Modify: `src/App.cs`
- Modify: `tests/HeadsetServiceTests.cs`

- [ ] **Step 1: Recheck page-switch state**

Confirm `SwitchDevice` shows/hides the correct workspace, updates both rail `Tag`s, syncs the
headset suspension gate, and never starts a second read for an already-running request.

- [ ] **Step 2: Recheck keyboard and mouse interaction**

Verify mouse slider, wheel, quick picks, chips, toggles, headset refresh, window buttons, and
footer actions remain wired.

- [ ] **Step 3: Run deterministic tests and build**

Run:

```powershell
.\build\test.ps1
.\build\build.ps1
```

Expected: tests pass and the exe builds.

- [ ] **Step 4: Inspect both fitted screenshots and the live headset screenshot**

Open `_test\tactical-mouse.png` and `_test\tactical-headset.png`; confirm no overlap, no clipped
text, visible focus geometry, and consistent rail state.

- [ ] **Step 5: Commit**

```powershell
git add src/MainController.cs src/HeadsetController.cs src/App.cs tests/HeadsetServiceTests.cs
git commit -m "fix: finalize tactical UI interactions"
```

## Self-Review

The plan covers the shell, mouse workspace, headset workspace, navigation, state colors, window
fitting, and both visual verification paths. Existing device protocol behavior is intentionally
untouched. The main regression risk is controller element lookup after the XAML replacement; each
task verifies the page that owns the changed names before moving on.
