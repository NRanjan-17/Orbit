namespace Orbit.Core;

/// <summary>Hex helpers shared by capture files, fixtures and logs.</summary>
public static class Hex
{
    public static string Format(ReadOnlySpan<byte> bytes) => Convert.ToHexString(bytes);

    /// <summary>Parses hex, tolerating spaces, dashes, colons and a leading "0x".</summary>
    public static byte[] Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var trimmed = text.Trim();
        if (trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed[2..];
        }

        var clean = new string(trimmed.Where(c => !char.IsWhiteSpace(c) && c is not ('-' or ':')).ToArray());
        return Convert.FromHexString(clean);
    }
}
