# Protocol findings from our own captures

Ground truth from the owner's AirPods Max (USB-C). Community claims live in `docs/sources.md`.
No addresses, device names, or encrypted-tail bytes are recorded here or in fixtures.

Status legend: **VERIFIED** (confirmed by tagged capture or by Windows on the real device) ·
**OBSERVED** (seen, meaning not established) · **PENDING** (needs the tagged capture session).

## F1. Device ID ↔ advertised model bytes — VERIFIED (2026-10-13)
- Windows reports the paired Max's Device ID as vendor `0x004C`, product `0x201F`, vendor-ID source 1
  (`orbit-cli devices`, properties `System.DeviceInterface.Bluetooth.VendorId/ProductId`).
- The only Proximity Pairing device in range at the same time advertised `d[3..4] = 1F 20`.
- So advertised model bytes = product ID little-endian, as implied by AirPodsDesktop [S1.2].
  Orbit uses this to recognise the bound device at runtime (`BoundDeviceProfile`), instead of a hardcoded ID.
- Class of Device of the paired Max: Audio/Video major class, Headphones minor class (SIG assigned numbers).
  Orbit uses this to decide the device is single-battery headphones.

## F2. Frame structure on the Max — VERIFIED (probe, 14 frames, 20 s, untagged)
- Windows `ManufacturerData.Data` excludes the company ID: first byte is `07`.
- Length 27, `d[1] = 0x19`, `d[2] = 0x01`. WinRT advertisement type: `ScannableUndirected`.
- `d[11..26]` (16 bytes) changed every ~4-5 s while `d[0..9]` did not → consistent with the encrypted tail [S1.1].
- Observed head (Max on desk, connected to the PC, not charging; battery not recorded):
  `07 19 01 1F 20 | 01 | 03 | 80 | 00 | 12 | C4/C0`

## F3. Fields — PENDING tagged capture session
| Field | Community claim | Probe observation | Status |
|---|---|---|---|
| Battery | low nibble `d[6]` (OpenPods only) | `d[6]=0x03` | PENDING — compare with Mac-reported % |
| Charging | `d[7] & 0x10` (OpenPods only) | `d[7]=0x80` (bit 0x80 not in any table) | PENDING |
| On-head | none documented | `d[5]=0x01` | PENDING |
| Colour | table `00..0C` | `d[9]=0x12` (not in any table) | OBSERVED |
| `d[10]` | LibrePods: connection state (`04` Idle, `00` Disconnected) | `C4`, once `C0` | OBSERVED |
| Connected elsewhere | none documented | – | PENDING |

Until a field is VERIFIED, Orbit shows it as "unknown" (or with an "unverified" badge when the user
turns on *Show unverified readings*), and never acts on it automatically (e.g. auto-pause stays off).
