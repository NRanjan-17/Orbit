# Noise control (Off / Noise Cancellation / Transparency) on Windows

**Status in Orbit: not available on Windows.** The Status page shows the control disabled with this reason.
No `IAapTransport` is implemented, because none of the options below is clearly feasible for a
self-built app on this PC. This document records why, with sources (keys refer to `docs/sources.md`).

## How Apple devices do it
Listening mode is set over Apple's accessory protocol (AACP, called "AAP" in LibrePods) on an **L2CAP channel,
PSM 0x1001**, on top of the existing bonded and encrypted Bluetooth Classic link [S2].

Community-documented packets (LibrePods docs + Linux/Android code, apple-wireshark dissector [S2]).
These are **not verified on AirPods Max (USB-C)**; no project's source code demonstrates them on the Max:

| Purpose | Bytes |
|---|---|
| Handshake (required before anything else) | `00 00 04 00 01 00 02 00 00 00 00 00 00 00 00 00` |
| Listening mode Off | `04 00 04 00 09 00 0D 01 00 00 00` |
| Noise Cancellation | `04 00 04 00 09 00 0D 02 00 00 00` |
| Transparency | `04 00 04 00 09 00 0D 03 00 00 00` |
| Adaptive (not on Max) | `04 00 04 00 09 00 0D 04 00 00 00` |

The set-features and notification-request packets *disagree* between LibrePods' own doc and code, which is
another reason not to ship them unverified.

## Can the mode be read without AACP?
**No.** Tagged captures (protocol-findings.md F4): switching ANC / Transparency / Off changed none of the
unencrypted advertisement bytes. If the mode is broadcast at all, it is inside the 16-byte encrypted tail,
which needs a per-device key that is itself only obtainable over AACP [S2, LibrePods `proximity_keys.py`].

## Windows options

| Option | What it takes | Effort | Risk | Verdict |
|---|---|---|---|---|
| **1. User-mode Windows API** | An L2CAP socket to PSM 0x1001 | – | – | **Impossible.** Winsock Bluetooth supports only RFCOMM (`BTHPROTO_RFCOMM`, `SOCK_STREAM`); WinRT exposes RFCOMM and GATT, no L2CAP [S3]. |
| **2. Kernel-mode Bluetooth profile driver** | KMDF driver on the headphones' devnode sending `BRB_L2CA_OPEN_CHANNEL` via `IOCTL_INTERNAL_BTH_SUBMIT_BRB`, plus an IOCTL bridge to Orbit [S3] | High (weeks): driver, INF, installer, user-mode bridge, AACP state machine | **High**: needs EV certificate + Microsoft attestation signing to load normally, or Test Mode (which weakens system security); **Smart App Control on this PC also blocks unsigned code**; a driver bug can blue-screen; co-existence with Microsoft's A2DP/HFP stack on the same devnode is unproven | Feasible in principle (MagicPods ships a closed-source "MagicAAP Driver" that requires Test Mode or a community signature [S3]); **not clearly feasible here** |
| **3. User-mode stack on a dedicated USB dongle** | Second Bluetooth adapter bound to WinUSB (Zadig), a user-mode stack (Google Bumble in Python, or airlow in Rust) that pairs with the Max itself [S3] | Medium-high | **UX**: the dongle is invisible to Windows audio, so either audio must also go through the user-mode stack (airlow implements A2DP for this) or the Max must hold two connections at once (untested); separate pairing; Python/Rust runtime; airlow is experimental and tested on one controller | Possible as an experiment; **not a fit for a tray companion app** |
| **4. Ask a Mac/iPhone to do it** | Phone/Mac shortcut triggered remotely | – | Requires the Apple device to be connected to the Max, which defeats the purpose on Windows | Out of scope |

## Recommendation
Keep noise control disabled (as shipped). If you want to pursue it later, the most realistic path is
**option 3 as a separate experiment** (spare USB dongle + Bumble), first only to confirm that the
listening-mode packet above works on the Max (USB-C), before any Orbit integration. Option 2 is only sensible
if you are prepared to buy code-signing for a kernel driver.

## Spatial Audio
Apple's head-tracked Spatial Audio is rendered by the Apple source device from the Max's motion data, which also
travels over AACP. Same verdict: not available on Windows. Orbit instead exposes **Windows** spatial sound
(Windows Sonic / Dolby Atmos for Headphones) for the Max's output, via the public
`SpatialAudioDeviceConfiguration` API.
