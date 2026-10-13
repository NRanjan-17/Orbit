using System.Text.Json;
using System.Text.Json.Serialization;

namespace Orbit.Core.Settings;

/// <summary>User settings, stored as JSON in %LOCALAPPDATA%\Orbit\settings.json (never in the repo).</summary>
public sealed class OrbitSettings
{
    /// <summary>Windows device ID of the user's bound headphones. Local only.</summary>
    public string? BoundDeviceId { get; set; }

    /// <summary>Ignore advertisements weaker than this (dBm).</summary>
    public int RssiThreshold { get; set; } = -70;

    public bool AutoPause { get; set; } = true;

    public bool AutoResume { get; set; } = true;

    /// <summary>
    /// "fast" = act on the first verified on-head/off-head reading (default; the exact-value rule showed no
    /// spurious flips in captures). "steady" = require 2 readings at least 1.5 s apart (about 2-3 s slower).
    /// </summary>
    public string AutoPauseResponse { get; set; } = AutoPauseResponses.Fast;

    /// <summary>Show single-source / unverified readings with a badge instead of "unknown".</summary>
    public bool ShowUnverified { get; set; }

    public bool StartWithWindows { get; set; }

    /// <summary>Short SHA-256 fingerprint of the exact-battery key that was confirmed (not the key itself).</summary>
    public string? ExactKeyFingerprint { get; set; }

    /// <summary>Byte order the confirmed key works in (true = reversed), so later launches skip re-checking.</summary>
    public bool? ExactKeyReversed { get; set; }

    /// <summary>Fingerprint of the key whose exact battery the owner confirmed matches their Mac.</summary>
    public string? ExactBatteryVerifiedFingerprint { get; set; }

    public bool LowBatteryToast { get; set; } = true;

    /// <summary>Warn when the advertised battery drops to or below this percent.</summary>
    public int LowBatteryPercent { get; set; } = 20;

    /// <summary>Play Orbit's chime in the headphones after "Switch to this PC" connects them.</summary>
    public bool ChimeOnConnect { get; set; } = true;

    /// <summary>Play Orbit's chime when the headphones are put on (and connected to this PC).</summary>
    public bool ChimeOnPutOn { get; set; }

    // Phase 4 (multi-device)
    public bool AutoConnect { get; set; }

    /// <summary>Global hotkey for "switch to this PC", e.g. "Ctrl+Alt+O". Empty = no hotkey.</summary>
    public string Hotkey { get; set; } = "Ctrl+Alt+O";

    /// <summary>"hybrid" (default), "ks" = audio-driver one-shot only, "service" = BluetoothSetServiceState only.</summary>
    public string SwitchMethod { get; set; } = SwitchMethods.Hybrid;
}

public static class AutoPauseResponses
{
    public const string Fast = "fast";
    public const string Steady = "steady";
}

public static class SwitchMethods
{
    public const string Hybrid = "hybrid";
    public const string Ks = "ks";
    public const string Service = "service";
}

/// <summary>Loads and saves <see cref="OrbitSettings"/>. Corrupt files fall back to defaults.</summary>
public sealed class SettingsStore(string path)
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public string Path { get; } = path;

    public OrbitSettings Load()
    {
        try
        {
            return File.Exists(Path)
                ? JsonSerializer.Deserialize<OrbitSettings>(File.ReadAllText(Path), Options) ?? new OrbitSettings()
                : new OrbitSettings();
        }
        catch (JsonException)
        {
            return new OrbitSettings();
        }
    }

    public void Save(OrbitSettings settings)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        var temp = Path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(settings, Options));
        File.Move(temp, Path, overwrite: true);
    }
}
