namespace Orbit.Core.Media;

/// <summary>Windows audio roles a default output is set for (mmdeviceapi.h ERole).</summary>
public enum AudioRole
{
    Console = 0,
    Multimedia = 1,
    Communications = 2,
}

/// <summary>Form factor of an output endpoint, reduced to what auto-output needs.</summary>
public enum AudioOutputKind
{
    Speakers,
    Headphones,
    Other,
}

/// <summary>An active audio output (render endpoint).</summary>
/// <param name="ContainerId">Groups the endpoint with its device; the PC's own devices share <see cref="AudioOutputs.ThisPcContainer"/>.</param>
public sealed record AudioOutput(string Id, string Name, AudioOutputKind Kind, Guid? ContainerId);

/// <summary>Reads and changes the Windows default audio output.</summary>
public interface IAudioOutputs
{
    IReadOnlyList<AudioOutput> GetOutputs();

    string? GetDefault(AudioRole role);

    bool SetDefault(string endpointId, AudioRole role);
}

public static class AudioOutputs
{
    /// <summary>Container ID Windows gives devices built into the PC (e.g. laptop speakers).</summary>
    public static readonly Guid ThisPcContainer = new("00000000-0000-0000-FFFF-FFFFFFFFFFFF");

    public static readonly AudioRole[] AllRoles = [AudioRole.Console, AudioRole.Multimedia, AudioRole.Communications];
}

/// <summary>What the auto-output controller did.</summary>
public enum AutoOutputAction
{
    None,
    SwitchedToSpeakers,
    SwitchedToHeadphones,
}

/// <summary>
/// Moves Windows' default audio output to the PC's speakers when the headphones are taken off, and back to the
/// headphones when they are put on again — but only for roles where the headphones were the default, and only
/// back from the speakers Orbit chose (a device the user picked in the meantime is left alone).
/// Fed with the same debounced, verified on-head transitions as auto-pause.
/// </summary>
public sealed class AutoOutputController(IAudioOutputs outputs)
{
    private Switched? _switched;

    private sealed record Switched(string SpeakerId, IReadOnlyList<(AudioRole Role, string HeadphonesId)> Roles);

    public bool Enabled { get; set; } = true;

    /// <summary>ContainerId of the bound headphones (their audio endpoints share it). Null = nothing to do.</summary>
    public Guid? HeadphonesContainer { get; set; }

    /// <summary>True while Orbit has moved output to the speakers and will move it back.</summary>
    public bool HoldingSwitch => _switched is not null;

    public AutoOutputAction OnWornChanged(bool worn) => worn ? OnPutOn() : OnTakenOff();

    /// <summary>Forget a pending switch-back (device changed, settings changed).</summary>
    public void Reset() => _switched = null;

    /// <summary>The PC's own speakers: a Speakers endpoint, preferring one built into this PC.</summary>
    public static AudioOutput? PickSpeakers(IEnumerable<AudioOutput> candidates, Guid? headphonesContainer) =>
        candidates
            .Where(o => o.Kind == AudioOutputKind.Speakers && o.ContainerId != headphonesContainer)
            .OrderByDescending(o => o.ContainerId == AudioOutputs.ThisPcContainer)
            .FirstOrDefault();

    private AutoOutputAction OnTakenOff()
    {
        if (!Enabled || HeadphonesContainer is not { } container)
        {
            return AutoOutputAction.None;
        }

        var all = outputs.GetOutputs();
        var headphoneIds = all.Where(o => o.ContainerId == container).Select(o => o.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var roles = AudioOutputs.AllRoles
            .Select(r => (Role: r, Current: outputs.GetDefault(r)))
            .Where(x => x.Current is not null && headphoneIds.Contains(x.Current))
            .ToList();
        if (roles.Count == 0 || PickSpeakers(all, container) is not { } speakers)
        {
            // The headphones aren't this PC's output (connected elsewhere, or the user picked another device),
            // or there are no speakers to fall back to.
            return AutoOutputAction.None;
        }

        var switched = roles.Where(x => outputs.SetDefault(speakers.Id, x.Role)).Select(x => (x.Role, x.Current!)).ToList();
        if (switched.Count == 0)
        {
            return AutoOutputAction.None;
        }

        _switched = new Switched(speakers.Id, switched);
        return AutoOutputAction.SwitchedToSpeakers;
    }

    private AutoOutputAction OnPutOn()
    {
        var switched = _switched;
        _switched = null;
        if (!Enabled || switched is null)
        {
            return AutoOutputAction.None;
        }

        var present = outputs.GetOutputs().Select(o => o.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var restored = false;
        foreach (var (role, headphonesId) in switched.Roles)
        {
            // Only undo our own change, and only if the headphones are still an output of this PC.
            if (string.Equals(outputs.GetDefault(role), switched.SpeakerId, StringComparison.OrdinalIgnoreCase) && present.Contains(headphonesId))
            {
                restored |= outputs.SetDefault(headphonesId, role);
            }
        }

        return restored ? AutoOutputAction.SwitchedToHeadphones : AutoOutputAction.None;
    }
}
