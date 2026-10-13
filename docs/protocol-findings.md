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

## F3. Untagged session (2026-10-13 22:59, 279 frames)
Tags did not register. `d[5]` went `01 → 2B → 01`; `d[8]` cycled `02, 83..87, 80, 81, 01, 82, 02` in the
first minute (unknown cause). Used only as a lead for F4.

## F4. Tagged session (2026-10-13 23:04, 3.5 min, 23 markers) — fixture `tests/Orbit.Tests/Fixtures/tagged-session-max-usbc.csv`
Tag windows were short (5–15 s). The Max reports changes ~2–4 s *before* the tag click.

| Field | Community claim | What the capture shows | Status |
|---|---|---|---|
| On-head | none documented | `d[5]` = `2B` on head, `01` off head; all 3 tagged transitions match (lead 2–4 s) | **VERIFIED** (exact values only) |
| In Smart Case | – | `d[5]` = `3F` while in case + charging; `0x3F` ⊇ bits of `0x2B`, so bit masks are unsafe | OBSERVED → treated as unknown |
| Noise mode | – | ANC / Transparency / Off tags: `d[0..9]` unchanged | **VERIFIED: not in unencrypted bytes** |
| Battery | low nibble `d[6]` (OpenPods only) | `d[6]` constant `03` all session; Mac reported **37 %** | **VERIFIED** (one reading; 10 % steps, shown as "30–39%") |
| Charging | `d[7] & 0x10` (OpenPods only) | `80 → 90` 1 s before the first `charging` tag, back to `80` after 7 s; not set on the second (in-case) charge | OBSERVED, inconsistent → unverified |
| `d[8]` | lid counter / closed bit (earbuds) | `02` normally; `03`, `84`, `04` around the Smart Case | OBSERVED |
| `d[10]` | LibrePods: connection state | `C0`/`C4`/`C8`, not aligned with Mac/PC/disconnected tags | OBSERVED, not usable |
| Connected elsewhere | none documented | no byte tracks Mac vs PC vs disconnected | **NOT AVAILABLE** in unencrypted bytes |
| Colour `d[9]` | table `00..0C` | constant `12` (not in any table) | OBSERVED |

Consequences in Orbit:
- On-head (bound headphones): `ByteValueRule(d[5], true: 2B, false: 01)`, VerifiedOnDevice. Any other value → unknown.
  Auto-pause may act on it.
- Noise mode cannot be read from BLE; multi-device switching cannot rely on a "connected elsewhere" bit.
- Battery (bound headphones): low nibble of `d[6]`, VerifiedOnDevice, displayed as a 10 % range.
  A second reading at a different level would strengthen this (README test checklist).
- Charging stays unverified (shown only with "Show unverified readings").

Until a field is VERIFIED, Orbit shows it as "unknown" (or with an "unverified" badge when the user
turns on *Show unverified readings*), and never acts on it automatically (e.g. auto-pause stays off).
