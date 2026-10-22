# orbit-cli

A command-line companion to Orbit for **investigating and testing**: capturing the headphones' Bluetooth
broadcasts, analysing them, and exercising switching and spatial sound from a terminal. It shares Orbit's code
(`Orbit.Core`, `Orbit.Platform`) but has no window. Everyday use doesn't need it; everything useful is in the app.

## Get it
- **Release:** download `orbit-cli-<version>-win-x64.zip` from the Releases page; it contains a single
  `orbit-cli.exe` (self-contained, nothing to install).
- **From source:** `dotnet build Orbit.slnx`, then
  `tools\Orbit.Cli\bin\Debug\net10.0-windows10.0.19041.0\orbit-cli.exe`.

Run it from PowerShell or Windows Terminal. `orbit-cli` with no arguments prints the command list.
Requirements are the same as Orbit's (Windows 10 2004+, Bluetooth, headphones paired in Windows).

## Privacy
- Commands never print Bluetooth addresses, except `capture`, which records the address of every Apple
  broadcast it hears into its CSV (needed to tell devices apart). Keep capture files private; the repository
  ignores the `captures/` folder for this reason.
- Nothing is uploaded anywhere.

## Commands

### `capture` — record Apple Bluetooth broadcasts
```powershell
orbit-cli capture [--out <folder>] [--seconds <n>] [--all-apple]
```
Logs every Apple (company ID `0x004C`) BLE advertisement to `<folder>\orbit-capture-<date>-<time>.csv`
(default folder: `.\captures` in the current directory) until you press **q**.

| Option | Meaning |
|---|---|
| `--out <folder>` | Where to write the CSV |
| `--seconds <n>` | Stop automatically after *n* seconds (also the only way to stop when input is redirected) |
| `--all-apple` | Print every Apple message type on screen, not just Proximity Pairing (`0x07`). All types are always saved |

While it runs, press a key the moment you change what the headphones are doing; that label applies to every
following broadcast until the next key:

| Key | Tag | Key | Tag |
|---|---|---|---|
| `1` | on-head | `6` | transparency |
| `2` | off-head | `7` | noise-off |
| `3` | charging | `8` | connected-mac |
| `4` | not-charging | `9` | connected-pc |
| `5` | anc-on | `0` | disconnected |
| `c` | in-case | `n` | type a free-text note (e.g. the battery % your Mac shows) |
| `q` | quit | | |

Keys only register while the terminal window has focus. The app's **Debug** page does the same with buttons, and
it can additionally record the **decrypted** part of each broadcast when an exact-battery key is set; the CLI
records the raw broadcast only.

CSV columns: `kind, timestamp_utc, address, rssi, adv_type, company_id, msg_type, payload_len, payload_hex, tag, note`
(`kind` is `ADV` for a broadcast or `MARKER` for a tag/note; `payload_hex` is the manufacturer data after the
company ID).

**Tip:** wait 30–60 s after each change. The headphones broadcast about once a second but some values update
more slowly, and the encrypted part only changes every few seconds to minutes.

### `analyze` — find what changes between tagged states
```powershell
orbit-cli analyze <file.csv> [<file2.csv> ...] [--group <prefixHex>]
```
Groups Proximity Pairing frames by device (first five payload bytes, strongest signal first), then for the
selected group prints, per byte offset, the most common values under each tag, and lists every bit that is set
in one tag's frames but not another's (≥ 80 % difference). Offsets that look random (the encrypted tail) are
marked `*`. Use `--group` to pick a different device from the group list. This is how the on-head and battery
bytes were found (`docs/protocol-findings.md`).

### `devices` — paired Bluetooth devices
```powershell
orbit-cli devices
```
Name, connection state and the Device ID profile values (vendor, product, source) of each paired device; no
addresses. Apple devices show vendor `0x004C` (the AirPods Max USB-C shows product `0x201F`).

### `diagnose` — check the switching plumbing (read-only)
```powershell
orbit-cli diagnose
```
Checks that the Bluetooth device-info structure has the expected size and how many Bluetooth audio driver
endpoints Orbit can reach for each paired Apple device. Sends nothing to the headphones.

### `connect` / `disconnect` — switch to or from this PC
```powershell
orbit-cli connect    [--method hybrid|ks|service] [--watch <seconds>]
orbit-cli disconnect [--method hybrid|ks|service] [--watch <seconds>]
```
**Changes real state** (takes the headphones from another device, or releases them). Acts on the **first** paired
Apple audio device. Retries with backoff and prints the result, attempts and time taken.

| `--method` | What it does |
|---|---|
| `hybrid` (default) | Connect: re-enable the audio services if a release removed them, then the audio-driver reconnect. Release: driver disconnect + disable the services (fully frees the headphones for your Mac) |
| `ks` | Audio-driver one-shot connect/disconnect only (fast; on its own a release may not free the headphones) |
| `service` | Toggle the Bluetooth A2DP/Hands-Free services only (slower) |

`--watch <n>` keeps printing the Bluetooth link and audio-output state for *n* seconds whenever it changes —
useful to see how long a release takes to fully drop the link. Details: `docs/multi-device.md`.

### `rfcomm` — Bluetooth services the headphones offer (read-only)
```powershell
orbit-cli rfcomm
```
Fresh (uncached) query of each paired Apple device's RFCOMM services, printed as service IDs. On the AirPods Max
this shows only Hands-Free (`0000111e-…`), which is why Apple's control protocol can't be reached through RFCOMM.

### `spatial` — Windows spatial sound for the headphones
```powershell
orbit-cli spatial                      # show state of each audio output of the paired Apple devices
orbit-cli spatial --set sonic|dolby|off
```
`--set` changes the format of the headphones' stereo "Headphones" output and reads it back. `dolby` needs Dolby's
Dolby Access app installed; `off` turns spatial sound off.

### `outputs` — audio outputs and auto-output's choice (read-only)
```powershell
orbit-cli outputs
```
Lists the active audio outputs with their kind (Speakers / Headphones / Other), which one is the Windows default
for each role (console, multimedia, communications), which belong to the paired headphones, and which speakers
auto-output switches to when the headphones are taken off (built-in speakers preferred). Changes nothing.

## Exit codes
`0` success · `1` error (message on stderr), or a switch that ended in failure · `2` unknown command (help printed).
