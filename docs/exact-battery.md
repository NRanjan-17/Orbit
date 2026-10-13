# Exact battery percentage (optional)

The unencrypted part of the headphones' Bluetooth advertisement carries battery in 10 % steps
(verified: Mac 37 % → `3`, Mac 65 % → `6`). The last 16 bytes are encrypted and contain the exact level.
They can be decrypted with the headphones' **encryption key** (`MagicAccEncKey`), which your Mac already
stores in its keychain. No driver, no L2CAP, no test mode.

## How it works
- AES-128 decryption of the last 16 bytes of the type-0x07 advertisement (one block, no padding).
  Sources: LibrePods `linux/ble/bleutils.cpp` `decryptLastBytes`, Android `BLEManager.kt` L229-243
  (`AES/ECB/NoPadding`); CAPod `ProximityMessage.kt`.
- Plaintext bytes 1, 2, 3 are battery components: level = `b & 0x7F` (percent), charging = `b & 0x80`,
  `0x7F` = not present. For headphones, LibrePods uses the first present component (`linux/battery.hpp`
  L157-217); CAPod uses byte 1 (`AirPodsMaxUsbc.kt`).
- No source has a checksum, so Orbit validates: every component ≤ 100 or `0x7F`, and the level must fall in
  the verified coarse range. The key is confirmed after 3 consistent advertisements and rejected after 12
  without one. Both byte orders of the key are tried.
- Status: **community-documented, not yet confirmed on the owner's Max** — shown with "(unverified)" until
  the owner compares a few readings with the Mac.

## Getting the key from the Mac (macOS 12 Monterey or later)
Documented in the CAPod wiki "AirPod Keys" (tested there on an AirPods Max).

1. Connect the AirPods Max to the Mac.
2. Find its Bluetooth address in Terminal:
   ```sh
   system_profiler SPBluetoothDataType | grep -B2 -A6 "AirPods Max"
   ```
   Note the `Address:` line (e.g. `AA:BB:CC:DD:EE:FF`).
3. Read the keychain item (enter your Mac password when asked):
   ```sh
   security find-generic-password -a "AA:BB:CC:DD:EE:FF" -s MobileBluetooth -g /Library/Keychains/System.keychain
   ```
   In the `password:` output, find `<key>MagicAccEncKey</key>` and the `<string>…</string>` after it.
4. If that finds nothing: open `/System/Library/CoreServices/Applications/Keychain Access.app`, search
   `MobileBluetooth`, open the item whose Account is the address (often under the iCloud tab), tick
   **Show password** (you are asked for your password a few times) and copy the text.
5. In Orbit → Status → **Exact battery**, paste either the `MagicAccEncKey` value (`3A-9C-81-…`) or the whole
   copied text, and press **Save key**.

## Privacy
- The key lets anyone who has it read your headphones' encrypted advertisements. Treat it like a password.
- Orbit stores it with Windows DPAPI (this Windows account on this PC only) in
  `%LOCALAPPDATA%\Orbit\device-key.bin`. It is never written to settings, logs or the repository.
- **Remove** in the same card deletes it.

## Caveats
- The key is described as stable across re-pairing (CAPod wiki); whether it survives a factory reset is not
  documented. If Orbit later says the key is rejected, copy it again.
