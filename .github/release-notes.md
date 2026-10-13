## Orbit — a Windows companion for your Apple headphones

Orbit is a small Windows app for Apple wireless headphones, built and tested with the **AirPods Max (USB-C)**.
It lives in the system tray and shows what Windows can't: battery, whether the headphones are on your head, and
which device they're connected to. It also pauses your music when you take them off.

### Features
- **Battery** from the headphones' Bluetooth broadcast, shown in the app, the tray menu and (optionally) as the tray icon itself
- **Exact battery %** (optional) using the headphones' key copied once from your Mac — otherwise in 10 % steps, or as a single number
- **On-head detection** with an animated indicator, and **auto-pause / resume** when you take the headphones off and put them back on
- **Switch to this PC / Release to other device**, a global hotkey, and optional **auto-connect** when Windows starts playing
- **"In use by another device"** — shows when your headphones are connected to your Mac (needs the exact-battery key)
- **USB-C cable detection** and notifications when the headphones connect
- **Windows spatial sound** (Windows Sonic, Dolby Atmos, DTS) for the headphones, including turning it off
- A **chime** in the headphones when Orbit connects them or you put them on
- Tray icon with a menu and quick flyout, start with Windows, low-battery notification

### Not possible on Windows
Noise control (ANC / Transparency) and Apple's head-tracked Spatial Audio use Apple's private control channel,
which Windows only exposes to kernel drivers. Use the button on the headphones. Details in the repository's
`docs/noise-control.md`.

### Install
1. Pair your headphones in **Windows Settings → Bluetooth** first.
2. Download **`Orbit-<version>-win-x64-setup.exe`** (or **`-win-arm64-setup.exe`** for ARM PCs) below and run it.
   You can pick the install folder, install just for you (no admin) or for all users, and add a desktop
   shortcut or start-with-Windows. Uninstall from **Settings → Apps**; it removes Orbit and all its settings.
3. **Portable instead?** Download `Orbit-<version>-win-x64.zip`, extract it anywhere and run `Orbit.exe`.

Nothing else needs to be installed; .NET and the Windows App SDK are included.

Requirements: Windows 10 2004 or later / Windows 11, and a Bluetooth adapter.

> **The build is not code-signed.** Windows SmartScreen may warn on first run ("More info" → "Run anyway"), and
> **Smart App Control**, if it's on, can block it entirely. Check the downloaded zip against `SHA256SUMS.txt`.

`orbit-cli-<version>-win-x64.zip` contains the developer tool for capturing and analysing Bluetooth broadcasts; most
people don't need it.

Orbit is an independent project, not affiliated with or endorsed by Apple.
