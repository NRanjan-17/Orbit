# Orbit

A Windows companion app for Apple wireless headphones, designed around the **AirPods Max (USB-C)**:
battery and on-head status from Bluetooth LE advertisements, auto-pause, one-click switching between this PC
and your Mac, a tray icon and flyout, and Windows spatial sound for the headphones' output.

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
The binding, and all settings, are stored in `%LOCALAPPDATA%\Orbit\settings.json`; nothing about your device
is stored in this repository. Closing the window keeps Orbit in the tray; use the tray menu's **Exit** to quit.

> **Smart App Control.** If Windows' Smart App Control is on, it may block the locally built, unsigned
> `Orbit.exe` ("An Application Control policy has blocked this file"). This happened on the development PC.
> See [Known limits](#known-limits). The `orbit-cli` tool is a separate executable and may still run.

### Developer tool: `orbit-cli`
`tools\Orbit.Cli\bin\Debug\net10.0-windows10.0.19041.0\orbit-cli.exe`

| Command | What it does |
|---|---|
| `capture [--seconds n]` | Logs Apple BLE advertisements to `captures\*.csv`; keys tag the current state |
| `analyze <csv>` | Groups frames per device and lists bytes/bits that differ between tags |
| `devices` | Paired devices with their Device ID (vendor/product), no addresses |
| `diagnose` | Read-only check of the connect/disconnect plumbing |
| `connect` / `disconnect [--method ks\|service]` | Switch the headphones to/from this PC (changes real state) |
| `spatial` | Read-only Windows spatial sound state of the headphones' outputs |

The app's **Debug: Capture** page does the same capture with buttons.

## What is verified vs unverified
"Verified" means confirmed on the owner's AirPods Max (USB-C) by tagged captures or by Windows itself
([docs/protocol-findings.md](docs/protocol-findings.md)). Community sources are cited in
[docs/sources.md](docs/sources.md). The UI follows the same policy: verified values are shown plainly,
multi-source community values are marked *(unverified)*, single-source values are hidden unless
**Settings → Show unverified readings** is on, and only verified readings drive auto-pause and low-battery alerts.

| Item | Status |
|---|---|
| Frame structure (27 bytes, type 0x07, 16-byte encrypted tail) | ✅ Verified |
| Device recognition: Windows Device ID product ↔ advertised model bytes | ✅ Verified |
| On-head: `d[5]` = `2B` on / `01` off (exact values; `3F` in case is treated as unknown) | ✅ Verified (3 tagged transitions) |
| Battery: low nibble of `d[6]`, 10 % steps, shown as a range | ✅ Verified (two readings: Mac 37 % ↔ 3, 65 % ↔ 6) |
| Charging: `d[7] & 0x10`, held 30 s to bridge dropouts | ✅ Verified (set 28/37 frames plugged in, 1/432 unplugged) |
| Noise mode in advertisements | ❌ Not present in unencrypted bytes (verified) |
| "Connected to another device" bit | ❌ Not present in unencrypted bytes (verified) |
| Other AirPods models (LibrePods rules, cross-checked with AirPodsDesktop/OpenPods) | ⚠️ Unverified on real hardware; decoder matches AirPodsDesktop's sanitised AirPods 5 captures |
| Auto-pause / resume logic | ✅ Unit-tested; ⏳ not yet tried with real playback |
| Switch to this PC (incl. while the Mac is playing) | ✅ Verified: 3–4 s |
| Release to other device (hybrid: KS + service toggle) | ✅ Service-toggle release verified; ⏳ hybrid release → Mac connect to confirm |
| Status page with live data (battery, charging, on head, signal) | ✅ Seen on screen |
| Chime in the headphones (Orbit's own sound) | ⏳ Built; listen test needed |
| Windows spatial sound selector | ⏳ Not yet tried |
| Tray, flyout, toasts, start with Windows, hotkey, auto-connect | ⏳ Built; not yet tried |

## Known limits
- **Noise control and Apple Spatial Audio: not available on Windows.** They need Apple's accessory protocol over
  L2CAP, which Windows has no public API for. Options and risks: [docs/noise-control.md](docs/noise-control.md).
- **No automatic Apple-style switching.** That is iCloud-coordinated. Orbit offers manual switch/release, a
  hotkey and optional auto-connect, and cannot know whether the Mac is holding the headphones:
  [docs/multi-device.md](docs/multi-device.md).
- **The Max stops advertising when idle, charging, or in the Smart Case.** Orbit then shows "asleep or out of
  range" with the last known values and time.
- **Battery resolution is 10 %**, because that is all the advertisement carries.
- **Media apps without a Windows media session** (some browser players, older apps) cannot be auto-paused.
- **Smart App Control** can block the unsigned build. Choices: sign the build with a certificate Windows
  trusts (e.g. a code-signing service), or turn Smart App Control off (Windows Security → App & browser
  control). On older Windows 11 builds turning it off was one-way; current builds let you switch it back on.
- **The headphones' own connection chime can't be triggered** (it needs Apple's accessory protocol). Orbit can
  play its own short chime through the headphones instead (Settings → Chime).

## Test checklist (your hardware)
1. **Status:** Max out of the case, near the PC → Status shows battery range and *On head / Off head* within a
   few seconds of putting it on / taking it off.
2. **Battery spot checks:** now and then, check that the Mac's battery % falls inside Orbit's range.
3. **Auto-pause:** play music on the PC through the Max, take it off → pauses within ~3 s; put it on → resumes.
   Pause manually, take off, put on → must *not* resume.
4. **Charging:** plug in USB-C with the Max awake (out of case, picked up) → Charging shows "Yes" and stays
   "Yes"; unplug → "No" within about 30 s.
5. **Switching** (CLI or app): the four steps in [docs/multi-device.md](docs/multi-device.md#test-procedure-cli-works-even-while-the-gui-is-blocked);
   most important is *connect from the PC while the Mac is playing*.
6. **Spatial sound:** with the Max connected to the PC, pick Windows Sonic on the Status page; check Windows
   Settings → Sound shows the same.
7. **Tray:** hover → tooltip with battery; click → flyout; right-click → menu; Exit quits.
7a. **Chime:** Settings → *Test chime* plays in the headphones (not the laptop speakers); with *chime on put-on*
   enabled, putting the headphones on plays it a few seconds later.
8. **Low battery:** only testable when the battery reaches the threshold (default 20 %); expect one toast.
9. **Start with Windows:** enable in Settings, sign out and in → Orbit starts in the tray.

## Project layout
```
src/Orbit.Core      Parsing, model registry, capture/analysis, tracking, auto-pause, switching logic, settings (pure .NET, tested)
src/Orbit.Platform  BLE scanner, paired devices, media sessions, KS/BluetoothSetServiceState connectors, hotkey, spatial sound
src/Orbit           WinUI 3 app: Status, Settings, Debug Capture, tray + flyout
tests/Orbit.Tests   xUnit tests incl. anonymised capture fixtures
tools/Orbit.Cli     orbit-cli developer tool;  tools/make-icon.ps1 regenerates the icon
docs/               sources, protocol findings, multi-device, noise control
```

Raw captures in `captures/` are git-ignored because they contain the addresses of nearby devices. Test
fixtures are anonymised (address replaced, encrypted tail zeroed).

## License
MIT, see [LICENSE](LICENSE).
