# Contributing

Thanks for looking! This is a small, focused tool — patches that keep it that way are welcome.

## Build and test

```powershell
.\build\build.ps1
```

Test with a real device. The diagnostic switches are there so you can bisect without touching the
mouse's HID link:

| Switch | Use it to |
| --- | --- |
| `--no-device` | Check UI/tray behaviour with no device access at all |
| `--no-poll` | Keep the handle but stop periodic traffic |
| `--shot out.png` | Render the window offscreen (no device needed) — good for UI diffs |

## Ground rules

1. **Never add device traffic while the window is hidden or a fullscreen app is running.** That
   property is the whole point of this project; a patch that polls in the background will be
   rejected. The only exception is the 5-minute tray keep-alive, which must keep its fullscreen guard.
2. **C# 5 only.** `csc.exe` from .NET Framework does not understand `out var`, string
   interpolation or `?.`. Keep the source compilable with the stock compiler.
3. **No new dependencies.** No NuGet, no SDK, no third-party DLLs.
4. **Explain protocol changes with evidence.** If you add or change a HID command, say how you
   verified it on hardware.

## Reporting a bug

Include:
- Windows version, mouse model and connection (2.4 GHz / Bluetooth / wired)
- The relevant tail of `%LOCALAPPDATA%\ROGMouse\tray.log`
- What the footer / "托盘诊断" button reported
