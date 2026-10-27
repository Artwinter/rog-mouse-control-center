# Razer BlackShark V2 Pro 2.4G Battery Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a read-only, Razer-inspired battery page for a Razer BlackShark V2 Pro connected through its 2.4 GHz receiver, without changing mouse behavior or adding Bluetooth support.

**Architecture:** A small Razer HID transport opens the matching `1532:0555` vendor collection only for a query, speaks the 64-byte "PA" remote-wake and read sequence, parses only ACKed `PI` frames, and releases the handle. A separate headset controller owns polling and UI state; the existing mouse controller keeps its DPI and tray responsibilities.

**Tech Stack:** C# 5, .NET Framework 4.x, WPF, Windows SetupAPI/HID/kernel32, Windows-bundled `csc.exe`, a dependency-free console test harness.

**Spec:** `docs/superpowers/specs/2026-10-27-razer-blackshark-v2-pro-battery-design.md`

## Global Constraints

- Windows 10/11, .NET Framework 4.x, x86 single executable.
- C# 5 syntax only: no `out var`, string interpolation, null-conditional operators, expression-bodied members.
- No NuGet, SDK, Razer binaries, Synapse installation, or runtime dependency.
- No Bluetooth code path.
- No headset write commands other than the remote session on/off frames required by the read sequence.
- Headset I/O runs off the WPF UI thread.
- HID collections are shared-opened and released after each transaction.
- Only Razer VID `1532`, PID `0555`, product containing `BlackShark V2 Pro`, and usage page `0xFF00` are accepted.
- A battery value outside 0-100 is rejected; no fallback or clamping is displayed.
- A charging byte of non-zero is presented as `已连接充电线`, never as `正在充电`.
- Design window is 1120x760, fitted down to the current work area with a 940x650 minimum.
- Mouse polling, tray, autostart, and single-instance behavior remain intact.

## Review Focus

- A Razer mouse or another Razer peripheral is also connected: the collector must not open, poll, or display its reply as the headset.
- The headset is powered off while the receiver remains inserted: no stale percentage may be presented as current.
- Synapse or the audio driver holds the HID collection: shared access must succeed, and failure must not crash or flood retries.
- A malformed, unrelated, truncated, or out-of-range `PI` frame arrives: it must be ignored or rejected without replacing a good visible value.
- The app is minimized, hidden, or a fullscreen game is active: no headset transaction may run until the app resumes.

---

### Task 1: Protocol Parser and Frame Builder

**Files:**
- Create: `src/RazerBlackSharkProtocol.cs`
- Create: `tests/HeadsetProtocolTests.cs`
- Create: `tests/HeadsetTests.csproj`
- Create: `build/test.ps1`

**Interfaces:**
- Consumes: plain byte arrays only.
- Produces:
  - `RazerBlackSharkProtocol.PacketSize = 64`
  - `RazerBlackSharkProtocol.BuildRemote(bool on) -> byte[]`
  - `RazerBlackSharkProtocol.BuildQuery(byte command) -> byte[]`
  - `RazerBlackSharkProtocol.TryReadPayload(byte[] report, byte command, out byte[] payload) -> bool`
  - `RazerBlackSharkProtocol.TryReadBattery(byte[] report, out int percent) -> bool`
  - `RazerBlackSharkProtocol.TryReadExternalPower(byte[] report, out bool connected) -> bool`

- [ ] **Step 1: Create the failing test harness**

`tests/HeadsetProtocolTests.cs` defines a tiny assertion helper and these exact tests:

```csharp
static void BatteryAckParsesPercent()
// report 0x02, PI at 2..3, chargers, command 0x21 at 12, ACK 0x01 at 13,
// length 1 at 14, value 87 at 15 -> TryReadBattery returns true, percent 87

static void ChargingAckParsesExternalPower()
// command 0x2A, value 2 -> TryReadExternalPower returns true, connected true

static void UnrelatedOrMalformedFramesAreRejected()
// wrong report id, command echo, ACK, payload length, short report -> false

static void OutOfRangeBatteryIsRejected()
// values 101 and 255 -> false

static void QueryFramesMatchProtocol()
// remote-on/off and 0x21/0x2A frames match the exact leading byte arrays in the spec

static void PacketIsZeroPaddedAndReportIdFirst()
// every built frame is 64 bytes, starts 0x02, and has required bytes
```

The first run must fail to compile because `RazerBlackSharkProtocol` does not exist.

- [ ] **Step 2: Add the dependency-free test runner and verify RED**

`build/test.ps1` invokes the Windows .NET Framework `csc.exe` on
`src/RazerBlackSharkProtocol.cs` and `tests/HeadsetProtocolTests.cs`, emits a temporary exe in
`_test/`, and exits non-zero on assertion failure. Run:

```powershell
.\build\test.ps1
```

Expected: compilation fails because the protocol type is missing.

- [ ] **Step 3: Implement the protocol builder and parser**

Define the four frames from the spec, the ACK offsets, exact `PI` header, a payload length check of
at least 1 and at most 16, and battery bounds 0-100. Use a shift when reading the command echo so
the same parser accepts a report with or without the leading report ID only if the spec's captured
form requires it; for this implementation, require report byte 0 == `0x02`.

- [ ] **Step 4: Run tests and verify GREEN**

Run: `.\build\test.ps1`

Expected: all six tests print PASS and the process exits 0.

- [ ] **Step 5: Commit**

```powershell
git add src/RazerBlackSharkProtocol.cs tests/HeadsetProtocolTests.cs tests/HeadsetTests.csproj build/test.ps1
git commit -m "feat: add BlackShark PA protocol parser"
```

---

### Task 2: Razer HID Discovery and Transport

**Files:**
- Create: `src/RazerHidDevice.cs`
- Modify: `src/NativeMethods.cs`
- Modify: `tests/HeadsetProtocolTests.cs`
- Modify: `build/test.ps1`

**Interfaces:**

- Consumes: `RazerBlackSharkProtocol` frames and parser.
- Produces:
  - `RazerHidDevice.Enumerate() -> List<RazerHidInterface>`
  - `RazerHidDevice.OpenFirst(ushort vendorId, ushort productId, string productNeedle, out string error) -> RazerHidDevice`
  - `RazerHidDevice.Query(byte command, int timeoutMs, out string error) -> byte[]`
  - `RazerHidDevice.DescribeCollection() -> string`
  - `RazerHidDevice.Dispose()`
- `RazerHidInterface` exposes `Path`, `ProductName`, `UsagePage`, `Usage`, `InputLength`, `OutputLength`, `FeatureLength`.

- [ ] **Step 1: Add failing selection tests**

Add pure tests for a `Select` helper accepting an array of interface descriptors and returning the
first interface whose vendor id/product id/product name/usage page all match, or null otherwise.
Cover: exact device; another Razer mouse; wrong product string; collection without `FF00`; no
collection. Run `.\build\test.ps1`; expect compile failure because the selection helper is absent.

- [ ] **Step 2: Implement HID enumeration and selection**

Use the SetupAPI/HID pattern already in `AsusHidDevice`, but preserve all interfaces for selection
instead of filtering by ASUS. Read attributes, product string, and `HidP_GetCaps`. Add any missing
P/Invoke declarations to `NativeMethods.cs` rather than duplicating them.

- [ ] **Step 3: Implement synchronous shared I/O**

Open with `GENERIC_READ | GENERIC_WRITE` and `FILE_SHARE_READ | FILE_SHARE_WRITE`, no overlapped
flag. `WriteFile` exactly 64 bytes including report ID. Drain pending reads, then send the sequence:
remote-on, sleep 35 ms, query, read until timeout, retry up to 3 times, remote-off in `finally`.
Use a copied `CancelSynchronousIo` watchdog and report the Win32 error. Ignore unrelated frames.

- [ ] **Step 4: Add deterministic selection tests to GREEN**

Run: `.\build\test.ps1`. Expected: all selection tests pass, no hardware access in this harness.

- [ ] **Step 5: Commit**

```powershell
git add src/RazerHidDevice.cs src/NativeMethods.cs tests/HeadsetProtocolTests.cs build/test.ps1
git commit -m "feat: add Razer headset HID transport"
```

---

### Task 3: Headset Read Service and Live Read-Only Probe

**Files:**
- Create: `src/HeadsetService.cs`
- Create: `tests/HeadsetServiceTests.cs`
- Modify: `build/test.ps1`
- Create: `tools/probe-headset.ps1` (temporary diagnostic, not shipped)

**Interfaces:**

- Consumes: `RazerHidDevice` and `RazerBlackSharkProtocol`.
- Produces:
  - `HeadsetReading` with `BatteryPercent`, `ExternalPower`, `ChargingKnown`, `ReadAt`, `Status`, `Error`
  - `HeadsetService.ReadOnce() -> HeadsetReading`
  - `HeadsetService.LastError`

- [ ] **Step 1: Write failing service-state tests**

Use a fake transport implementing the same tiny query interface to assert:

- receiver absent -> `Status = ReceiverMissing`, battery `-1`;
- receiver present and battery ACK absent -> `Status = NoResponse`, battery `-1`;
- battery good + charging good -> battery value and external power known;
- battery good + charging absent -> battery value, `ChargingKnown == false`;
- malformed battery -> `Status = InvalidReply`, no value.

Run `.\build\test.ps1`; expect compile failure for the service type.

- [ ] **Step 2: Implement the service**

Open a fresh `RazerHidDevice`, perform the two queries, close it in `finally` on every path, map
states explicitly, and expose only the latest successful reading. Do not catch and hide all
exceptions: map known device errors to user text and log the exception type.

- [ ] **Step 3: Run deterministic tests GREEN**

Run: `.\build\test.ps1`. Expected: all protocol, selection, and service tests PASS.

- [ ] **Step 4: Probe the real receiver read-only**

Run the debug exe with:

```powershell
.\_test\HeadsetProbe.exe --once
```

Expected on this machine: `RECEIVER=ASUS? no` and a headset battery integer 0-100, or a precise
`NoResponse` result with the logged endpoint. No write to settings. If Synapse owns the collection,
repeat once with Synapse closed and once open, recording both results in the ledger. If the headset
is physically unavailable, do not invent a percentage; mark this check blocked and continue.

- [ ] **Step 5: Commit**

```powershell
git add src/HeadsetService.cs tests/HeadsetServiceTests.cs build/test.ps1
git commit -m "feat: add headset read service"
```

---

### Task 4: WPF Headset Page and Device Selector

**Files:**
- Modify: `ui/MainWindow.xaml`
- Create: `src/HeadsetController.cs`
- Modify: `src/MainController.cs`
- Modify: `src/App.cs:220-236`
- Modify: `build/build.ps1`

**Interfaces:**

- Consumes: `HeadsetService.ReadOnce()`, `HeadsetReading`.
- Produces:
  - `HeadsetController.Window` host and `HeadsetController.Initialize()`
  - `HeadsetController.ShowPage()`, `HeadsetController.HidePage()`, `HeadsetController.Refresh()`
  - status and controls named `HeadsetBattery`, `HeadsetBar`, `HeadsetState`, `HeadsetConn`, `HeadsetLastRead`, `HeadsetStatus`, `BtnHeadsetRefresh`

- [ ] **Step 1: Add the selector and headset page without wiring**

Keep the existing mouse left card and mouse controls. Increase window design size to 1120x760.
Add a `鼠标` / `耳机` selector above the right workspace and a hidden `HeadsetPage` grid. The
headset page shows the level, bar, status badge, connection row, last-read row, protocol row, and
refresh action. Use existing colors and card style, but a distinct Razer-green accent for the
headset charge treatment and no generic purple.

- [ ] **Step 2: Verify the static page renders**

Build with `.\build\build.ps1` and render with:

```powershell
.\dist\ROGMouseControlCenter.exe --shot _test\headset-static.png
```

Expected: exit 0 and a PNG that shows the selector, the unchanged mouse page, and no clipped
bottom controls.

- [ ] **Step 3: Add failing selector/state tests**

Add tests for a `HeadsetPageState` helper that decides whether polling is allowed from
`PageSelected`, `WindowVisible`, `WindowMinimized`, and `Fullscreen`, and text mapping for
receiver missing/no-response/external-power/on-battery. Run `.\build\test.ps1`; expect failure.

- [ ] **Step 4: Implement controller wiring**

Wire selector clicks, page visibility, refresh, 60-second timer, and the suspension gate. All
device work runs through `Task.Factory.StartNew`. On `HidePage` stop the timer; on `ShowPage`
refresh once. Use a busy guard so manual and timer reads cannot overlap.

- [ ] **Step 5: Run tests, build, and screenshot GREEN**

Run `.\build\test.ps1`, `.\build\build.ps1`, then `--shot _test\headset-page.png`.
Expected: tests pass; build exit 0; screenshot shows both pages reachable and no overlapping text.

- [ ] **Step 6: Commit**

```powershell
git add ui/MainWindow.xaml src/HeadsetController.cs src/MainController.cs src/App.cs build/build.ps1 tests/HeadsetServiceTests.cs build/test.ps1
git commit -m "feat: add Razer headset battery page"
```

---

### Task 5: Wire Live Headset Data, Tray-Safe Suspension, and Small-Screen Fit

**Files:**
- Modify: `src/HeadsetController.cs`
- Modify: `src/MainController.cs`
- Modify: `src/App.cs:220-236`
- Modify: `tests/HeadsetServiceTests.cs`

**Interfaces:**

- Consumes: existing `ShouldSuspend`, `Parked`, `FullScreenAppRunning`.
- Produces: `HeadsetController.SyncAccess(bool suspended)` and `App.FitWindowToWorkArea(Window)`.

- [ ] **Step 1: Add failing fit and expiration tests**

Add tests for:

- a reading older than 90 seconds is rendered as stale/cleared;
- external power false renders `电池供电`;
- charging unknown renders `充电状态未知`;
- a new successful read replaces an expired reading;
- fit calculation returns 1120x760 when the work area allows, and never exceeds the work area
  minus a 16 px margin on smaller screens.

Run `.\build\test.ps1`; expect failures for missing helpers.

- [ ] **Step 2: Implement fit, expiration, and suspension**

Call `FitWindowToWorkArea` after loading the window. Use the current `SystemParameters.WorkArea`.
Respect the 940x650 floor; if the work area is smaller, use the work area minus margin and allow
compact vertical spacing. Stop headset timer and in-flight acquisition when suspended. Resume only
when the headset page is selected. Do not start a second mouse poll.

- [ ] **Step 3: Run all deterministic tests and build**

Run: `.\build\test.ps1`

Expected: every test PASS, exit 0.

Run: `.\build\build.ps1`

Expected: exit 0 and one x86 exe in `dist`.

- [ ] **Step 4: Verify live read and screenshots**

Run:

```powershell
.\dist\ROGMouseControlCenter.exe --shot-live _test\headset-live.png
```

Expected: render succeeds; if the receiver/headset is available, the headset page contains a
current integer and exact status. Also run `--shot` to verify the no-device state. Record whether
the result came from hardware or the unavailable path.

- [ ] **Step 5: Commit**

```powershell
git add src/HeadsetController.cs src/MainController.cs src/App.cs tests/HeadsetServiceTests.cs
git commit -m "feat: finish headset polling and window fit"
```

---

### Task 6: Documentation and Release Artifact

**Files:**
- Modify: `README.md`
- Modify: `README.zh-CN.md`
- Modify: `build/build.ps1`
- Create: `tools/probe-headset.ps1` or remove if no longer useful

**Interfaces:**

- Consumes: completed implementation and verified behavior.
- Produces: accurate user instructions and protocol provenance; final exe under `dist`.

- [ ] **Step 1: Update docs**

Document the `鼠标` / `耳机` selector, receiver requirement, 60-second visible-page polling,
fullscreen/parked suspension, read-only scope, `0x21/0x2A` commands, and provenance. Explicitly
exclude Bluetooth and claim no more than the charging byte proves.

- [ ] **Step 2: Build and inspect final executable**

Run:

```powershell
.\build\build.ps1
Get-Item .\dist\ROGMouseControlCenter.exe | Select-Object FullName,Length,LastWriteTime
```

Expected: exit 0, non-zero exe, no missing-resource error.

- [ ] **Step 3: Final screenshot and live check**

Run both shots again and inspect the PNGs. Confirm the mouse page still renders its original
controls and the headset page has no clipped content at the fitted size.

- [ ] **Step 4: Commit**

```powershell
git add README.md README.zh-CN.md build/build.ps1
git commit -m "docs: document Razer headset battery support"
```

---

## Self-Review

Spec coverage checked: goals, non-goals, protocol frames, shared HID access, selected page
polling, stale-state handling, small-screen fit, fail-safe states, provenance, and verification all
have owning tasks. Signatures are consistent across tasks (`ReadOnce`, `HeadsetReading`,
`HeadsetController.ShowPage/HidePage/Refresh`). Review Focus items each have a deterministic test
in Tasks 1, 2, 3, or 5, except the fullscreen gate, whose existing `ShouldSuspend` path is tested at
the helper level and verified live by the existing behavior.
