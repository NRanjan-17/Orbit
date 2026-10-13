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

## F5. Max stops advertising when idle / charging — OBSERVED (2026-10-13 23:19)
- Max plugged into the PC by USB-C for charging, not connected over Bluetooth: 45 s capture had **zero**
  Proximity Pairing frames from it, while 403 frames from other nearby Apple devices were received.
- Consequence: Orbit treats "not heard for 20 s" as *asleep or out of range* and shows the last known values
  with their time, instead of stale values presented as current. Auto-pause forgets any held pause.
- Answered by F6: picking the Max up while it stays plugged in restarts advertising.

## F7. Encrypted tail decrypts with the Mac's MagicAccEncKey (2026-10-14)
- Key copied from the Mac keychain item `MobileBluetooth` (`MagicAccEncKey`), used **as entered** (not reversed),
  AES-128 one block over `d[11..26]`. Two earlier pasted values (other fields) decrypted to random bytes and were
  rejected by the consistency check.
- Plaintext from two consecutive broadcasts:
  `04 41 00 00 xx xx xx 00 00 00 00 01 3A E4 C9 39` and `04 41 00 00 xx xx xx 00 00 00 00 02 ED CF 7E 3F`.
  Bytes 0-10 constant, byte 11 a counter, bytes 12-15 varying — impossible with a wrong key.
- **Byte 1 = `0x41` = 65 %, charging bit clear**, inside the coarse band (nibble 6 → 60-69 %); the Mac had shown
  65 % in the F6 session. Bytes 2 and 3 are `00` (LibrePods expects `7F` for absent components; the decoder takes
  byte 1, as CAPod does). Meaning of bytes 0, 4-6 unknown.
- Status: decoding works on the Max (USB-C); the exact value is **pending one side-by-side check with the Mac**
  before it is marked VerifiedOnDevice.

## F8. Noise mode is not in the broadcast, encrypted or not (2026-10-14 01:49, decrypted capture)
- Max on head, connected; tags anc-on (100 frames) → transparency (30 frames) → anc-on (18 frames), each frame
  decrypted with the confirmed key (Debug capture, `plain=` column).
- Decrypted bytes 0-10 were identical across ANC and Transparency (`05 3F 00 00 xx xx xx 00 00 00 00`); only the
  counter (byte 11) and bytes 12-15 changed. Together with F4 (unencrypted bytes unchanged): **the current
  listening mode cannot be read from BLE advertisements.** It is only available over AACP (L2CAP PSM 0x1001),
  as LibrePods does it (docs/noise-control.md).
- Battery byte `0x3F` = 63 %, matching the value shown at the time.
- OBSERVED: decrypted byte 0 alternated `04`/`05` exactly with unencrypted `d[10]` low nibble (`C4`/`C5`);
  LibrePods names d[10] a connection state (`04` idle, `05` music). Not verified; not used.

## F6. Charging session (2026-10-13 23:43, 60 s, Max plugged in by USB-C and awake) — fixture `charging-session-max-usbc.csv`
- **Battery — VERIFIED (2nd reading):** `d[6] = 06` all session; the Mac showed **65 %** → "60–69%". With F4
  (37 % ↔ `03`) the low-nibble rule holds at two different levels.
- **Charging — VERIFIED with smoothing:** `d[7]` was `90` (bit `0x10` set) in 28 of 37 frames, with one ~16 s
  stretch of `80` mid-charge. Across both unplugged sessions the bit was set in 1 of 432 frames, at the moment of
  plugging in. So: bit set ⇒ charging; bit clear ⇒ not charging *unless* it was set within the last 30 s
  (`ChargingLatch`).

Until a field is VERIFIED, Orbit shows it as "unknown" (or with an "unverified" badge when the user
turns on *Show unverified readings*), and never acts on it automatically (e.g. auto-pause stays off).
