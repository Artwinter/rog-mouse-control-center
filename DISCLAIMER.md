# Disclaimer

This is an **unofficial** project. It is not affiliated with, endorsed by, or supported by
ASUSTeK Computer Inc. "ROG", "Armoury Crate" and related marks are trademarks of ASUSTeK and are
used here only to describe what this tool is compatible with.

This software talks to your mouse over its **private, undocumented HID protocol** and writes
settings into its **onboard firmware**. That carries risk:

- Commands were verified against one specific model (ROG Keris Wireless AimPoint 36K, USB PID
  `0x1A68`). Other ROG models may share the protocol, but they are untested — a wrong write could
  leave settings in an unexpected state.
- The software is provided "as is", without warranty of any kind. The authors are not liable for
  any damage to hardware, data loss, or voided warranty.
- Keep Armoury Crate (or a backup of your settings) around if you want a way back to vendor defaults.

If you are unsure, use `--no-device` first to check the UI without any device access.
