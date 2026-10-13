namespace Orbit.Core.Switching;

/// <summary>Modifier flags with the same values as Win32 RegisterHotKey's fsModifiers (MOD_*).</summary>
[Flags]
public enum HotkeyModifiers : uint
{
    None = 0,
    Alt = 0x1,
    Control = 0x2,
    Shift = 0x4,
    Win = 0x8,
}

/// <summary>A parsed hotkey such as "Ctrl+Alt+O". <see cref="VirtualKey"/> is a Win32 virtual-key code.</summary>
public sealed record HotkeyGesture(HotkeyModifiers Modifiers, uint VirtualKey)
{
    /// <summary>Parses "Ctrl+Alt+O", "Win+Shift+F9" etc. Requires at least one modifier and a letter, digit or F1-F24.</summary>
    public static bool TryParse(string? text, out HotkeyGesture? gesture)
    {
        gesture = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var mods = HotkeyModifiers.None;
        uint? key = null;
        foreach (var raw in text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var part = raw.ToUpperInvariant();
            switch (part)
            {
                case "CTRL" or "CONTROL": mods |= HotkeyModifiers.Control; continue;
                case "ALT": mods |= HotkeyModifiers.Alt; continue;
                case "SHIFT": mods |= HotkeyModifiers.Shift; continue;
                case "WIN" or "WINDOWS": mods |= HotkeyModifiers.Win; continue;
            }

            if (key is not null)
            {
                return false;
            }

            if (part.Length == 1 && char.IsAsciiLetterOrDigit(part[0]))
            {
                key = part[0]; // VK_A..VK_Z = 'A'..'Z', VK_0..VK_9 = '0'..'9'
            }
            else if (part.StartsWith('F') && int.TryParse(part[1..], out var f) && f is >= 1 and <= 24)
            {
                key = (uint)(0x70 + f - 1); // VK_F1 = 0x70
            }
            else
            {
                return false;
            }
        }

        if (mods == HotkeyModifiers.None || key is null)
        {
            return false;
        }

        gesture = new HotkeyGesture(mods, key.Value);
        return true;
    }
}
