using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace Orbit.Core.Crypto;

/// <summary>
/// The headphones' "MagicAccEncKey" (16 bytes), which decrypts the last 16 bytes of their Proximity Pairing
/// advertisement. Copied once from the Mac keychain item "MobileBluetooth" (CAPod wiki "AirPod Keys";
/// LibrePods calls the same pair magicAccIRK / magicAccEncKey, linux/airpods_packets.h L192-235).
/// </summary>
public static partial class ProximityKey
{
    /// <summary>
    /// Accepts the bare key ("3A-9C-81-...", "3A9C81...", with spaces or colons) or the whole keychain plist,
    /// from which &lt;key&gt;MagicAccEncKey&lt;/key&gt;&lt;string&gt;…&lt;/string&gt; is extracted.
    /// </summary>
    public static bool TryParse(string? text, out byte[] key)
    {
        key = [];
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var match = PlistEncKey().Match(text);
        var hex = match.Success ? match.Groups[1].Value : text;
        var clean = new string(hex.Where(Uri.IsHexDigit).ToArray());
        if (clean.Length != 32)
        {
            return false;
        }

        key = Convert.FromHexString(clean);
        return true;
    }

    /// <summary>
    /// AES-128 decryption of one 16-byte block, no padding. ECB and zero-IV CBC are identical for one block
    /// (LibrePods: Android BLEManager.kt L229-243 "AES/ECB/NoPadding"; Linux bleutils.cpp decryptLastBytes).
    /// </summary>
    public static byte[] DecryptBlock(ReadOnlySpan<byte> block, byte[] key)
    {
        using var aes = Aes.Create();
        aes.Key = key;
        return aes.DecryptEcb(block, PaddingMode.None);
    }

    [GeneratedRegex(@"<key>\s*MagicAccEncKey\s*</key>\s*<string>\s*([0-9A-Fa-f\-\s:]+?)\s*</string>", RegexOptions.IgnoreCase)]
    private static partial Regex PlistEncKey();
}
