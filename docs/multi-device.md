# Multi-device switching (Windows PC ⇄ Mac)

## What Apple does, and why Windows can't
On Apple devices, automatic switching is coordinated through iCloud: devices signed in to the same Apple
Account negotiate which one owns the headphones and use Apple's accessory protocol (AACP, L2CAP PSM 0x1001)
to hand over. Windows is not part of that iCloud group and has no public L2CAP API (see
[noise-control.md](noise-control.md)), so a Windows app cannot take part in that handover. Orbit instead
offers manual and rule-based switching using standard Bluetooth connect/disconnect.

## What Orbit provides
| Feature | How | Where |
|---|---|---|
| **Switch to this PC** | Ask Windows to connect the headphones' audio profiles, verify, retry with backoff | Status page, tray menu, hotkey |
| **Release to other device** | Ask Windows to disconnect them so the Mac can connect | Status page, tray menu |
| **Global hotkey** | `RegisterHotKey` (default **Ctrl+Alt+O**) → Switch to this PC | Settings |
| **Auto-connect** (off by default) | When Windows playback *starts*, the headphones are heard nearby (BLE, above the RSSI threshold) and not connected → Switch to this PC; at most once per 2 minutes | Settings |

### Connection methods
0. **Hybrid (default)**, chosen from the tests below: *connect* = enable the A2DP/Hands-Free services (no-op
   if already enabled) + KS one-shot reconnect; *release* = KS one-shot disconnect + disable the services.
1. **Audio driver one-shot reconnect.** Sends `KSPROPERTY_ONESHOT_RECONNECT` / `_DISCONNECT`
   (`KSPROPSETID_BtAudio`, `{7FA06C40-B8F6-4C7E-8556-E8C33A12E54D}`) to the Bluetooth audio KS filter found
   via the device's render endpoints (Core Audio `IDeviceTopology`). Used by ToothTray and AirPodsDesktop.
   No driver churn. Microsoft documents that success means "an attempt was made", so Orbit verifies the
   connection itself.
2. **Toggle Bluetooth services (fallback).** `BluetoothSetServiceState` (bthprops.cpl) on A2DP Sink
   (`0x110B`) and Hands-Free (`0x111E`): disable then enable to connect, disable to release. Microsoft:
   enabling/disabling a service *installs/removes the device driver*. Slower, and it briefly removes the
   audio endpoints; use only if method 1 does not work on your PC.

### Retry, backoff and cooldown
`SwitchCoordinator`: attempt → wait up to 8 s for Windows to report the device connected → retry after
2 s, 4 s, 8 s (4 attempts in total). If none succeed: show *"Couldn't connect. The headphones are probably
in use by another device (such as your Mac), asleep, or out of range…"* and refuse further attempts for
30 s. Auto-connect has its own 2-minute cooldown.

### "In use by another device" detection
The task asked to use a captured "connected elsewhere" bit **only if verified**. Tagged captures
(protocol-findings.md F4) show **no** unencrypted advertisement byte that tracks *connected to Mac* vs
*connected to PC* vs *disconnected* (`d[10]` varies but does not follow the tags). So Orbit does **not**
claim to know where the headphones are connected; the failure message names the likely causes instead.

## Verified on the owner's AirPods Max (USB-C), 2026-10-13
| Check | Result |
|---|---|
| Struct layout: `BLUETOOTH_DEVICE_INFO` marshals to 560 bytes | ✅ |
| KS interop finds the Max's Bluetooth audio KS filter(s) via its container | ✅ |
| **KS connect, Max idle** | ✅ connected in 3.1 s, 1 attempt |
| **KS connect while the Mac is playing** | ✅ connected in 4.1 s, 1 attempt: Windows *can* take the Max from the Mac |
| KS release | ⚠️ `hr=0` and both audio outputs go *unplugged* within seconds, but the **Bluetooth link stays up (40 s+)** and **Hands-Free reconnects by itself**; the Mac could not take the Max (owner's test B) |
| Service-toggle release | ✅ audio outputs *not present* after 6.5 s, Bluetooth link dropped at 39.6 s |
| Service-toggle connect | ✅ connected in 6.6 s (owner's test D and a re-run) |
| `BluetoothSetServiceState` return codes | `0` = done; `1168` (ERROR_NOT_FOUND) when disabling an already-disabled service |

Consequences in Orbit:
- **"Connected" means an active audio output** (Headphones/Headset endpoint), not the Bluetooth link,
  because the link can outlive the audio by 40 s or more. `AudioConnectionState` implements this.
- **Hybrid is the default method** (fast KS connect, complete service-toggle release).

Still to test: hybrid release followed by connecting on the Mac (expected to work given the service-toggle
result); hotkey and auto-connect in the app.

## Test procedure (CLI, works even while the GUI is blocked)
From the Orbit folder in PowerShell (`orbit-cli` = `tools\Orbit.Cli\bin\Debug\net10.0-windows10.0.19041.0\orbit-cli.exe`):
1. **Idle connect:** Max out of the case and awake, not connected to the Mac → `orbit-cli connect`.
2. **Take from Mac:** Max connected to the Mac and playing → `orbit-cli connect`. Record whether it switches,
   how long it takes, and whether the Mac loses audio.
3. **Release:** with the Max on the PC → `orbit-cli disconnect`, then connect from the Mac's Bluetooth menu.
4. **Other methods:** repeat 1-3 with `--method ks` or `--method service`.
Add `--watch 60` to print the link and audio-output state for 60 s after the command, and
`2>&1 | Tee-Object -Append captures\switch-tests.txt` to keep a log. Output contains no addresses.
