# Razer BlackShark V2 Pro 2.4G Battery Design

## Intent

Add a read-only battery panel for the Razer BlackShark V2 Pro (2023) to the existing ROG mouse
control center. The headset is connected through its 2.4 GHz receiver. Bluetooth is out of scope.

The panel should feel like a compact Razer Synapse device page while preserving the existing
mouse controls and the app's Windows 10, single-exe, no-runtime-install constraints.

## Goals

- Show the headset's battery percentage, charging state, connection state, and last-read time.
- Read and release the headset HID handle within one background operation.
- Keep the headset link silent while the app is parked or a fullscreen game is foregrounded.
- Preserve the current mouse behavior, polling, tray, autostart, and single-instance flow.
- Support the exact 2.4 GHz receiver identity observed on this machine:
  Razer `VID_1532`, `PID_0555`, interface `MI_03`, vendor HID usage page `0xFF00`.
- Refuse malformed or out-of-range replies instead of displaying a guessed percentage.

## Non-goals

- Bluetooth headset support or Windows Bluetooth battery discovery.
- EQ, sidetone, microphone, THX, lighting, firmware, or any write command.
- Running Synapse or bundling Razer binaries.
- Claiming a charging state that the headset does not report.
- Supporting other BlackShark revisions without an identified protocol and hardware validation.

## Constraints

- .NET Framework 4.x and C# 5 only, built with the Windows-bundled `csc.exe`.
- Output remains one x86 Windows executable.
- HID I/O must not run on the WPF UI thread.
- Opening a HID collection must request shared read/write access. An exclusive open fails with
  Windows error 32 while audio or Razer services hold the collection.
- The receiver enumerates two HID collections on this machine. Selection must use HID caps, not
  a hard-coded interface number or enumeration order.
- The protocol is the 64-byte Razer "PA" exchange, not the 90-byte Razer mouse report.

## Device Protocol

The observed receiver exposes the headset control collection on usage page `0xFF00`. Output and
input reports are 64 bytes including report ID `0x02`.

Wake and remote-mode frames:

```text
remote on : 02 80 07 00 00 50 41 0E 00 02 E1 01 ... (zero padded)
remote off: 02 80 07 00 00 50 41 0E 00 02 E1 00 ... (zero padded)
```

Read-only queries:

```text
battery : 02 80 08 00 00 50 41 08 00 03 21 00 ... (zero padded)
charging: 02 80 08 00 00 50 41 08 00 03 2A 00 ... (zero padded)
```

The dongle answers with `50 49` ("PI") frames. With the report ID retained, byte 12 echoes the
command, byte 13 is the ACK byte `0x01`, byte 14 is the payload length, and the payload starts at
byte 15. A battery payload is one byte in the range 0-100. A charging payload is one byte; zero
means on battery and non-zero means on the cable.

After an idle period the radio may consume the first frame while waking. The query sequence drains
stale input, sends a sacrificial remote-on frame, retries the query within a bounded window, and
always attempts remote-off before the handle is closed.

On Windows the 64-byte buffer passed to `WriteFile` includes report ID `0x02`. The collection is
opened synchronously with shared read/write access. Reads are synchronous with the same
`CancelSynchronousIo` watchdog pattern already used by the mouse transport. Unrelated input frames
are ignored until a `PI` frame echoes the requested command and carries the ACK byte.

Remote-on/off frames select the control session only; remote-off is the same shutdown sequence
OpenRazer uses after each query. No audio, microphone, EQ, or power setting is written.

## Approaches Considered

### A. Add a sibling headset card (selected)

Keep the mouse card and its battery ring in the left column. Turn the right side into a compact
workspace with a mouse/headset selector, then show a Razer-inspired headset page with a large
percentage, level bar, charging badge, and read-only status details.

Trade-off: a larger XAML and controller change, but the two devices remain visually and
operationally separate. The headset page can have its own refresh and status semantics without
pretending it is the mouse.

The window's design size grows from 1000x700 to 1120x760 so both device pages retain breathing room.
It remains non-resizable, but startup computes a fit size from the current work area (at most
1120x760, at least 940x650), so it stays inside a 1366x768 desktop with the taskbar visible.
Layout rows use star sizing and compact margins at the smaller size. The left card stays visible on
both pages, so the selected device is always obvious.

### B. Add a second ring to the existing left card

Place mouse and headset percentages side by side in the left card and leave the right control card
untouched.

Trade-off: the smallest code change, but the card becomes crowded, its identity is ambiguous, and
there is no room for Razer-style connection or charging detail.

### C. Let Windows Bluetooth expose the level

Read the standard Bluetooth battery property instead of the receiver protocol.

Rejected: the user connects through 2.4 GHz and explicitly excludes Bluetooth. Windows also reports
Bluetooth while the 2.4 GHz receiver is active on this machine, so reading Bluetooth could describe
the wrong link.

## Selected Interaction

- The title bar keeps the existing window chrome.
- A small device selector appears near the top of the right workspace: `鼠标` and `耳机`.
- The mouse page keeps the current DPI, polling, debounce, snapping, and autostart controls.
- The headset page contains:
  - product name and a live 2.4 GHz status line;
  - a large battery number and a segmented level bar;
  - a state badge for external power / on battery / unknown / unavailable. The API's non-zero
    value proves that the charging path is active, not the electrochemical state near 100%, so the
    UI says `已连接充电线` rather than claiming `正在充电`;
  - rows for connection, receiver state, last read, polling policy, and a read-only protocol label;
  - a prominent `重新读取` action scoped to the headset.
- When the receiver is absent, the card remains visible with an honest unavailable state and the
  mouse page continues to work.
- The tray continues to represent the mouse. The headset reading is visible in the app; adding a
  second tray icon would duplicate Windows' battery presentation and is not part of this change.
- Switching to the mouse page stops headset polling, but retains the last successfully read value
  with a visible timestamp for the remainder of the current visible session.
- The device is considered from this app if all three match: Razer VID, `PID_0555`, and a product
  name containing `BlackShark V2 Pro`; the candidate collection must also advertise vendor usage
  page `0xFF00`.

## Polling and Suspension

- The visible headset page reads once when shown and then every 60 seconds.
- Reads occur only while the headset page is selected, the window is visible and not minimized, and
  no fullscreen app is foregrounded.
- A manual refresh performs one complete transaction and releases the handle.
- The headset handle is opened only for a transaction, never kept across timer ticks.
- Headset reads are skipped when the window is parked or a fullscreen app is foregrounded.
- Mouse polling and suspension state remain independent; one device failing cannot block the other.
- Dev mode `--no-device` skips headset access as well.
- Returning from a parked or fullscreen state triggers one immediate read if the headset page is
  selected; the mouse continues to follow its existing resume path.

## Failure Handling

- Receiver absent: show `未检测到 2.4 GHz 接收器`.
- Receiver present but no ACK: show `接收器在线 · 耳机未响应`, clear the large percentage, and
  mark the last successful value as expired rather than presenting it as current.
- Reply length/format mismatch: reject it and log a short diagnostic line.
- Level outside 0-100: reject it and log the raw value.
- Charging query failure with a good battery read: show the level and the explicit text
  `充电状态未知`; never substitute `电池供电`.
- HID open failure: show a short reason in the panel and write the full Win32 error and collection
  path to the existing diagnostic log. Retry only on the next manual or scheduled read; no rapid
  retry loop while Synapse is running.

## Provenance

The frame facts are cross-checked against HaloBattery's MIT-licensed `providers/blackshark.py`
(`PA` framing) and OpenRazer PR #2862's driver notes for the same `1532:0555` hardware. The app
uses the protocol facts only, not copied driver code or Razer binaries. The protocol remains
unofficial; the README will say so next to the ASUS protocol note.

The initial implementation will be validated on this machine's receiver with a read-only query.
Situations this session cannot physically exercise, such as unplugging the headset or toggling the
charging cable, remain explicit manual release checks rather than being claimed as verified.

## Files

- `src/RazerHidDevice.cs`: enumerate and share-open Razer HID collections for VID 1532 / PID 0555.
- `src/RazerBlackSharkProtocol.cs`: build the four permitted frames, query, and parse replies.
- `src/HeadsetController.cs`: polling, suspension gate, UI state, and read-only diagnostics.
- `src/MainController.cs`: own the selector, expose the headset page, and share the fullscreen gate.
- `ui/MainWindow.xaml`: selector, headset page, status bar, and expanded window layout.
- `build/build.ps1`: compile the new sources.
- `README.md`, `README.zh-CN.md`: usage and protocol notes.

## Verification

- Compile with `build\build.ps1`.
- Use `--shot` to inspect the UI without touching hardware.
- Run a read-only live query against the real receiver, with Synapse closed and then open.
- Confirm the percentage against the headset's current state and the charging query with and
  without the USB cable.
- Verify receiver-absent, headset-off, malformed-frame, and timeout paths with protocol unit tests
  or a small deterministic harness.
- Confirm the mouse page still loads, refreshes, and applies a setting after the headset code is
  added.
