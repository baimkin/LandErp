namespace LandErp.ParserSpike.Contracts;

/// <summary>A field observation. Text may be raw-only; typed values are nullable value types.</summary>
public sealed class ObservedValue<T> where T : struct
{
    /// <summary>Creates a validated observation; missing fields never imply clearing earlier observations.</summary>
    public ObservedValue(Presence presence, string? raw, T? parsed, IReadOnlyList<string> warnings)
    {
        if (!Enum.IsDefined(presence))
            throw new ArgumentOutOfRangeException(nameof(presence));
        bool hasRaw = !string.IsNullOrWhiteSpace(raw);
        if (presence == Presence.Present && !hasRaw && !parsed.HasValue)
            throw new ArgumentException("Present requires raw or parsed value.", nameof(presence));
        if (presence == Presence.ParseFailed && (!hasRaw || parsed.HasValue))
            throw new ArgumentException("ParseFailed requires raw and forbids parsed value.", nameof(presence));
        if (presence is Presence.NotInspected or Presence.Absent && (raw is not null || parsed.HasValue))
            throw new ArgumentException("Uninspected or absent fields cannot carry values.", nameof(presence));
        if (presence == Presence.Empty && (hasRaw || parsed.HasValue))
            throw new ArgumentException("Empty cannot carry a meaningful value.", nameof(presence));
        Presence = presence;
        Raw = raw;
        Parsed = parsed;
        Warnings = ContractValidation.Codes(warnings);
    }

    /// <summary>Inspection state for this observation only.</summary>
    public Presence Presence { get; }
    /// <summary>Original non-sensitive field text, if inspected.</summary>
    public string? Raw { get; }
    /// <summary>Typed value; zero is a value, null means no typed value.</summary>
    public T? Parsed { get; }
    /// <summary>Stable warning codes, copied from input.</summary>
    public IReadOnlyList<string> Warnings { get; }
}
