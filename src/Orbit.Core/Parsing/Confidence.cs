namespace Orbit.Core.Parsing;

/// <summary>How much we trust a decoded field. Ordered from least to most trusted.</summary>
public enum Confidence
{
    /// <summary>No source documents this field for this model. Never shown.</summary>
    Unknown = 0,

    /// <summary>One community project documents it (see docs/sources.md). Hidden unless "show unverified" is on.</summary>
    SingleSource = 1,

    /// <summary>Two or more independent projects agree in source code, but not yet checked against our captures. Shown with an "unverified" badge.</summary>
    Corroborated = 2,

    /// <summary>Confirmed against tagged captures of the real device (docs/protocol-findings.md).</summary>
    VerifiedOnDevice = 3,
}

/// <summary>A decoded value plus where it came from.</summary>
public readonly record struct Reading<T>(T? Value, Confidence Confidence, string Source)
{
    public static Reading<T> Unknown(string why = "") => new(default, Confidence.Unknown, why);

    public bool HasValue => Value is not null && Confidence > Confidence.Unknown;

    /// <summary>True when the value meets <paramref name="minimum"/> and may be shown/acted on.</summary>
    public bool IsUsable(Confidence minimum) => HasValue && Confidence >= minimum;

    public override string ToString() => HasValue ? $"{Value} ({Confidence})" : "unknown";
}
