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

    /// <summary>Show single-source / unverified readings with a badge instead of "unknown".</summary>
    public bool ShowUnverified { get; set; }

    public bool StartWithWindows { get; set; }

    public bool LowBatteryToast { get; set; } = true;

    /// <summary>Warn when the advertised battery drops to or below this percent.</summary>
    public int LowBatteryPercent { get; set; } = 20;

    // Phase 4 (multi-device)
    public bool AutoConnect { get; set; }

    /// <summary>Global hotkey for "switch to this PC", e.g. "Ctrl+Alt+O".</summary>
    public string Hotkey { get; set; } = "Ctrl+Alt+O";
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
