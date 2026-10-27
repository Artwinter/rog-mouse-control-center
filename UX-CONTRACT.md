# Peripheral Control Center UX Contract

## Navigation

- The persistent left device rail owns navigation between `鼠标` and `耳机`.
- The active device is visually marked by a signal bar, raised surface, and `Tag=on`.
- The rail remains visible on both device pages; a page can never cover navigation.
- Global app settings live in the rail footer. `开机自启动` is owned there, not inside either
  device page.
- Clicking the active device re-reads only when the page is already selected and the action is
  explicitly requested; selecting a different device switches the visible workspace immediately.

## Device page states

- Mouse page: waiting for device, connected, partial read, write pending, write success, and write
  failure.
- Headset page: reading, receiver missing, receiver online/headset no response, invalid reply,
  stale reading, battery reading, and charging-path known/unknown.
- A failed headset read clears the large percentage and retains the last successful timestamp as
  expired context.
- A missing receiver must not look like a zero-percent battery.

## Refresh and suspension

- Headset polling only runs while the headset page is selected, the window is visible, not
  minimized, and no fullscreen app is foregrounded.
- Switching to the mouse page stops headset polling and invalidates in-flight results.
- Returning from suspension triggers one immediate read when the headset page is selected.
- Mouse device access keeps its existing suspension policy and must not be coupled to headset
  failures.

## Feedback

- Page selection, pending read, success, and failure are expressed in text plus color; color alone
  is never the only signal.
- Refresh buttons retain their geometry during busy work and become enabled again on success or
  failure.
- Footer status remains the canonical low-priority diagnostic channel.

## Accessibility and input

- All navigation and actions use native WPF buttons with visible focus and pointer cursor.
- Controls keep keyboard behavior from the existing WPF primitives.
- Status text is not truncated when it changes; long error text wraps or remains available in the
  footer/log.
- The window fits the current work area and does not introduce horizontal scrolling.
