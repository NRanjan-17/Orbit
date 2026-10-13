# Orbit

A Windows companion app for Apple wireless headphones, designed around the **AirPods Max (USB-C)**:
battery (optionally the exact %) and on-head status from Bluetooth LE advertisements, auto-pause and resume,
one-click switching between this PC and your Mac, USB-C cable detection, connection notifications, a tray icon
(optionally showing the battery) with a menu and flyout, an animated on-head indicator, a chime in the
headphones, and Windows spatial sound for the headphones' output.

Orbit is an independent project, not affiliated with or endorsed by Apple. Product names such as
"AirPods Max" appear only as detected device names.

## Setup
Requirements: Windows 10 2004+ / Windows 11, a Bluetooth adapter, the headphones **paired in Windows
Settings → Bluetooth**, and the [.NET 10 SDK](https://dotnet.microsoft.com/) (Windows App SDK 2.5 is pulled
from NuGet and bundled; nothing else to install).

```powershell
dotnet build Orbit.slnx
dotnet test --solution Orbit.slnx
.\src\Orbit\bin\Debug\net10.0-windows10.0.19041.0\win-x64\Orbit.exe
```

On first start Orbit binds to your paired Apple headphones automatically (or choose them on the Status page).
The binding and all settings are stored in `%LOCALAPPDATA%\Orbit\settings.json`; nothing about your device is
stored in this repository. Closing the window keeps Orbit in the tray; use the tray menu's **Quit** to exit.
`Orbit.exe --tray` starts without a window (used by *Start with Windows*); `--selftest` opens the tray menu,
flyout and pages on a timer and logs any error to `%LOCALAPPDATA%\Orbit\orbit.log`.

> **Smart App Control** may block the locally built, unsigned `Orbit.exe` ("An Application Control policy has
> blocked this file"). See [Known limits](#known-limits). The `orbit-cli` tool is a separate executable.

### Exact battery (optional)
Copy the headphones' `MagicAccEncKey` once from your Mac's keychain and paste it on the Status page; Orbit then
shows the exact percentage instead of a 10 % range. Steps: [docs/exact-battery.md](docs/exact-battery.md).
The key is stored with Windows DPAPI for your account only and is never written to settings, logs or the repo.
No Mac or iPhone? **Settings → Battery as a single number** shows the middle of the 10 % step (e.g. 65 %)
instead of a range; it is approximate, since that is all the readable broadcast carries.

### Developer tool: `orbit-cli`
`tools\Orbit.Cli\bin\Debug\net10.0-windows10.0.19041.0\orbit-cli.exe`

| Command | What it does |
|---|---|
| `capture [--seconds n]` | Logs Apple BLE advertisements to `captures\*.csv`; keys tag the current state |
| `analyze <csv>` | Groups frames per device and lists bytes/bits that differ between tags |
| `devices` | Paired devices with their Device ID (vendor/product), no addresses |
| `diagnose` | Read-only check of the connect/disconnect plumbing |
| `rfcomm` | Read-only list of the device's RFCOMM services |
| `connect` / `disconnect [--method hybrid\|ks\|service] [--watch n]` | Switch the headphones to/from this PC (changes real state) |
| `spatial` | Read-only Windows spatial sound state of the headphones' outputs |

The app's **Debug** page does the same capture with buttons and, with a confirmed exact-battery key, also
records the decrypted data.

## What is verified vs unverified
"Verified" means confirmed on the owner's AirPods Max (USB-C) by tagged captures, by Windows itself, or by the
owner trying it ([docs/protocol-findings.md](docs/protocol-findings.md)). Community sources are cited in
[docs/sources.md](docs/sources.md). In the app, values not yet verified carry an ⓘ icon with a tooltip next to the
battery; single-source values are hidden unless **Settings → Show unverified readings** is on; only verified
readings drive auto-pause and low-battery alerts.

| Item | Status |
|---|---|
| Frame structure (27 bytes, type 0x07, 16-byte encrypted tail) | ✅ Verified |
| Device recognition: Windows Device ID product ↔ advertised model bytes | ✅ Verified |
| On-head: `d[5]` = `2B` on / `01` off (exact values; `3F` in case → unknown) | ✅ Verified (tagged transitions) |
| Battery: low nibble of `d[6]`, 10 % steps | ✅ Verified (Mac 37 % ↔ 3, 65 % ↔ 6) |
| Exact battery % from the decrypted tail (key from your Mac) | ✅ Verified: decrypts on the Max (byte 1 = battery) and the owner confirmed it matches the Mac |
| Charging: `d[7] & 0x10`, held 30 s to bridge dropouts | ✅ Verified |
| Auto-pause / resume with real playback | ✅ Verified by the owner; background-tab resume fixed 2026-10-14 (⏳ re-check) |
| Switch to this PC (incl. while the Mac is playing) | ✅ Verified: 3–4 s |
| Release to other device (hybrid: KS + service toggle) | ✅ Service-toggle release verified; ⏳ hybrid release → Mac connect to confirm |
| USB-C cable detection | ✅ Detected (Apple USB audio device); on the cable the Max broadcasts only now and then, so on-head/charging update intermittently |
| Connection notifications (USB-C / Bluetooth) | ⏳ Built; not yet tried |
| Status page, animated on-head indicator, tray menu, flyout, battery tray icon | ✅ Seen on screen; self-test opens them without errors |
| Chime in the headphones (Orbit's own sound) | ✅ Verified by the owner; deeper "bass" version ⏳ needs a listen |
| Windows spatial sound selector, including **Off** | ✅ Verified (owner; Off set and read back via the API) |
| Dolby Atmos for Headphones | Not bundled (Dolby's paid Dolby Access app); appears in the spatial list once installed |
| Start with Windows, low-battery toast, hotkey, auto-connect | ⏳ Built; not yet tried |
| Noise mode in advertisements | ❌ Not broadcast at all, readable or encrypted (verified, F4/F8) |
| "Connected to another device" | ⏳ Likely in the decrypted tail (bytes 4-6 = connected host's address, F11); one capture with the Mac needed |
| Other AirPods models (LibrePods rules, cross-checked with AirPodsDesktop/OpenPods) | ⚠️ Unverified on real hardware |

## Known limits
- **Noise control and Apple Spatial Audio: not available on Windows.** They need Apple's accessory protocol over
  L2CAP, which Windows exposes only to kernel drivers; the Max has no Apple RFCOMM service (F10), and the mode is
  not in its broadcasts (F8). Driver options and risks, and why the community-signed MagicAAP install is not
  recommended: [docs/noise-control.md](docs/noise-control.md).
- **No automatic Apple-style switching.** That is iCloud-coordinated. Orbit offers manual switch/release, a hotkey
  and optional auto-connect: [docs/multi-device.md](docs/multi-device.md).
- **The Max stops broadcasting when idle or in the Smart Case, and broadcasts only intermittently on the USB-C
  cable.** Orbit then shows the last known values dimmed, with the time they were seen.
- **Battery resolution is 10 %** from the readable broadcast; the exact % needs the key from your Mac.
- **Media apps without a Windows media session** cannot be auto-paused.
- **Smart App Control** can block the unsigned build: sign it with a trusted certificate, or turn Smart App Control
  off (Windows Security → App & browser control; current Windows 11 builds let you turn it back on).
- **The headphones' own chime can't be triggered** from Windows; Orbit plays its own chime instead.

## Test checklist (your hardware)
1. **Status:** Max out of the case, near the PC → battery and *On head / Off head* update within a few seconds.
2. **Auto-pause:** play music (also in a background Chrome tab), take the Max off → pauses within ~1–2 s; put it on
   → the same player resumes. Pause manually, take off, put on → must *not* resume.
3. **Exact battery:** compare Orbit's number with the Mac's Bluetooth menu, press **It matches my Mac**.
4. **Charging:** plug in USB-C with the Max awake → Charging "Yes"; unplug → "No" within about 30 s.
5. **USB-C:** connect the cable → "USB-C cable" chip and a notification; disconnect → notification.
6. **Switching:** see [docs/multi-device.md](docs/multi-device.md); most important: hybrid release, then connect on the Mac.
7. **"Connected elsewhere" (F11):** with the Max connected to the **Mac**, run a Debug capture for a minute.
8. **Tray:** hover → tooltip; click → flyout; right-click → menu; Quit exits. Optional battery number as the icon.
9. **Chime:** Settings → *Test chime* plays in the headphones.
10. **Low battery / Start with Windows / hotkey / auto-connect:** when convenient.

## Project layout
```
src/Orbit.Core      Parsing, model registry, decryption, capture/analysis, tracking, auto-pause, switching logic, settings (pure .NET, tested)
src/Orbit.Platform  BLE scanner, paired devices, USB audio watcher, media sessions, KS/BluetoothSetServiceState connectors, hotkey, spatial sound, key store
src/Orbit           WinUI 3 app: Status, Settings, Debug, tray menu + flyout, tray battery icon
tests/Orbit.Tests   xUnit tests incl. anonymised capture fixtures
tools/Orbit.Cli     orbit-cli developer tool;  tools/make-icon.ps1 regenerates the app icon
docs/               sources, protocol findings, multi-device, noise control, exact battery
```

Raw captures in `captures/` are git-ignored because they contain the addresses of nearby devices. Test fixtures
are anonymised (addresses replaced, encrypted tails zeroed, address bytes in decrypted data replaced).

## License
MIT, see [LICENSE](LICENSE).
