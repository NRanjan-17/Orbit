# Community sources and citations

Every byte position, bit mask and model ID in Orbit must trace back to either **this file** or
**our own captures** (see `docs/protocol-findings.md`). Code comments reference sections here as `[S1.3]` etc.

Sources were read directly from the repositories at these commits (2026-10-13):

| Key | Repository | Commit |
|---|---|---|
| LP | kavishdevar/librepods | `efccc4499b2c1fd2ac243e975ae1f42d8e828ffd` |
| OP | adolfintel/OpenPods | `9cd800875f594fa6eb7325dd33d9216c476a5782` |
| APD | SpriteOvO/AirPodsDesktop | `56b04001f75044dbd15507303a9438c71e93c6e4` |
| FM | furiousMAC/continuity | `d9fa98bddafbfa3baafc84e37a417d78a0598546` |
| TT | m2jean/ToothTray | `0d1a7eef82b4c9e69ed6b931ec07e6c760703434` |
| AW | pabloaul/apple-wireshark | `2e9822247195fad2f79c729010308c33a29546ea` |

Byte indices below are into the manufacturer data **after** the 2-byte company ID (what Windows'
`BluetoothLEManufacturerData.Data` returns — confirmed by our probe capture, whose first byte is `07`).

## S1. Proximity Pairing (type 0x07)

### S1.1 Framing
- `d[0]=0x07`, `d[1]=0x19` (25), total 27 bytes. OP `PodsStatusScanCallback.java` L29-42; LP `BLEManager.kt` L159-163;
  APD `AppleCP.h` L160 `static_assert(sizeof(AirPods) == 27)`, `AppleCP.cpp` L25-34.
- 16-byte encrypted tail `d[11..26]`. FM `proximity_pairing.md` L47-79, dissector `packet-bthci_cmd.c` L10292-10317.
- `d[2]`: FM "Undefined, 0x01"; LP "paired (0x01) / pairing mode (0x00)" (`linux/ble/blemanager.cpp` L139-142).

### S1.2 Model ID `d[3..4]`
On-air bytes; LP writes them as `0x1F20` (d[3]<<8|d[4]), APD as little-endian `0x201F`.

| Model | On-air bytes | Sources |
|---|---|---|
| AirPods 1 | `02 20` | FM, OP, LP, APD |
| AirPods 2 | `0F 20` | FM, OP, LP, APD |
| AirPods 3 | `13 20` | OP, LP, APD |
| AirPods 4 | `19 20` | LP, APD |
| AirPods 4 (ANC) | `1B 20` | LP, APD |
| AirPods Pro | `0E 20` | FM, OP, LP, APD |
| AirPods Pro 2 (Lightning) | `14 20` | OP, LP, APD |
| AirPods Pro 2 (USB-C) | `24 20` | OP, LP, APD |
| AirPods Pro 3 | `27 20` | OP, APD |
| AirPods Max (Lightning) | `0A 20` | LP (`BLEManager.kt` L88-99), APD (`AppleCP.cpp` L44-77), OP (partial match) |
| **AirPods Max (USB-C)** | **`1F 20`** | LP, APD (`AppleCP.cpp`, test `AirPodsDomainTests.cpp` L276). Not in OP. |

### S1.3 Status `d[5]`
| Bit | Meaning per source |
|---|---|
| 0x02 | APD `currInEar` (broadcasting pod in ear); OP/LP in-ear (side depends on flip) |
| 0x04 | APD `bothInCase`; LP Linux `areBothPodsInCase` |
| 0x08 | APD `anotInEar` (other pod in ear) |
| 0x10 | LP Linux `isOnePodInCase` |
| 0x20 | APD `broadcastFrom` 1=Left; LP `primaryLeft`; OP flipped when 0 |
| 0x40 | LP `isThisPodInTheCase` |

**No source documents an on-head bit for AirPods Max.** Sources disagree on the in-ear xor (LP Android vs LP Linux/OP/APD).

### S1.4 Battery `d[6]`, charging/case `d[7]`
- Two nibbles; nibble value 0-10 = ×10 %, `0xF` = unknown/disconnected (OP `Pod.java` L8, LP, APD `AppleCP.cpp` L105-106).
  Values 0xB-0xE: LP Android clamps to 100, APD/OP treat as invalid, FM "seen >100%". Orbit treats them as unknown.
- Earbuds: which nibble is left depends on status bit 0x20 (APD, LP, OP). FM instead fixes high=right/low=left.
- `d[7]` low nibble = case battery; high nibble flags: 0x1 broadcasting pod charging, 0x2 other pod, 0x4 case (APD, LP, OP, FM masks 0x10/0x20/0x40).
- **AirPods Max / single-battery devices (single source, OP `PodsStatus.java` L53, L60):** battery = low nibble of `d[6]`;
  charging = `d[7]` bit 0x10. No other project has a Max-specific branch.

### S1.5 Lid `d[8]`, colour `d[9]`, `d[10]`
- Lid: APD/LP bits 0-2 counter, bit 3 = closed. FM: whole-byte open counter.
- Colour table (FM L1447-1461, APD, LP agree): `00` White … `0C` Yellow. Values outside are unknown.
- `d[10]`: FM "Undefined 0x00"; LP connection-state enum `00 Disconnected, 04 Idle, 05 Music, 06 Call, 07 Ringing, 09 Hanging up`.

### S1.6 "Connected to another device"
**Not documented** in any source for type 0x07 (closest: LP's `d[10]` enum, which does not say *which* host).

## S2. AACP / "AAP" (L2CAP PSM 0x1001)
- PSM 0x1001: LP `docs/AAP Definitions.md` L3, `AirPodsService.kt` L2645; AW `aacp.lua` L69-70.
- Handshake `00 00 04 00 01 00 02 00 00 00 00 00 00 00 00 00` (LP doc L8-10, Linux `airpods_packets.h` L151, Android `AACPManager.kt` L640-658).
- Listening mode: `04 00 04 00 09 00 0D <mode> 00 00 00`, mode `01` Off, `02` ANC, `03` Transparency, `04` Adaptive
  (LP doc L196-204, Linux `BasicControlCommand.hpp` L6 + `airpods_packets.h` L19-22, AW `aacp_enums.lua` L305-310).
- Set-features / notification request bytes **disagree** between LP doc and LP code (doc `4D 00 FF…`, code `4D 00 D7…`).
- Needs a bonded, encrypted BR/EDR link (LP `extras/proximity_keys.py` L136-159). No key material needed for ANC.
- **No source code proves these bytes on AirPods Max (USB-C).**

## S3. Windows platform
- Winsock Bluetooth supports only RFCOMM (`SOCK_STREAM`, `BTHPROTO_RFCOMM`) — Microsoft Learn, "Bluetooth and socket".
  WinRT exposes RFCOMM, GATT, no L2CAP.
- Kernel L2CAP: `BRB_L2CA_OPEN_CHANNEL` via `IOCTL_INTERNAL_BTH_SUBMIT_BRB` in a profile driver — Microsoft Learn,
  "Creating a L2CAP Client Connection to a Remote Device".
- `KSPROPSETID_BtAudio` = `{7FA06C40-B8F6-4C7E-8556-E8C33A12E54D}`, `KSPROPERTY_ONESHOT_RECONNECT` = 0,
  `KSPROPERTY_ONESHOT_DISCONNECT` = 1, Get on the filter (SDK `ksmedia.h`; used by TT `BluetoothAudioDevices.cpp`
  L39-144 and APD `QuickConnect_win.cpp` L351-449). Filter found via `IMMDevice` → `IDeviceTopology` → connector →
  `GetConnectedTo` → `IPart::GetTopologyObject` → `GetDeviceId` → `IMMDevice.Activate(IKsControl)`.
- `BluetoothSetServiceState` (bthprops.cpl): "Enabling a service installs the corresponding device driver and disabling
  a service removes the corresponding device driver" (Microsoft Learn). TT README calls it slow (inquiry + driver churn).
  Service UUIDs (SDK `bthdef.h`): AudioSink `0x110B`, Handsfree `0x111E`, AVRCP `0x110E`/`0x110C`, base `-0000-1000-8000-00805F9B34FB`.
- Existing Windows ANC: MagicPods "MagicAAP Driver" (closed, unsigned/Test Mode or community-signed); airlow (user-mode
  stack over WinUSB, takes over the radio). AirPodsDesktop has no ANC.
