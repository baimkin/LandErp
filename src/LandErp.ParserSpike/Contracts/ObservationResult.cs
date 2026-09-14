namespace LandErp.ParserSpike.Contracts;

/// <summary>Versioned result metadata without session data or diagnostic filesystem paths.</summary>
public sealed class ObservationResult
{
    /// <summary>Validates metadata and normalizes the observation instant to UTC.</summary>
    public ObservationResult(string schemaVersion, string sourceCode, string adapterVersion,
        Uri? requestedUrl, Uri? finalUrl, DateTimeOffset observedAtUtc,
        PageClassification classification, Outcome outcome, IReadOnlyList<string> warnings,
        IReadOnlyList<string> errors, Guid correlationId, IReadOnlyList<Guid> diagnosticReferences)
    {
        SchemaVersion = ContractValidation.Required(schemaVersion, nameof(schemaVersion));
        SourceCode = ContractValidation.Codes([sourceCode])[0];
        AdapterVersion = ContractValidation.Required(adapterVersion, nameof(adapterVersion));
        if (!Enum.IsDefined(classification))
            throw new ArgumentOutOfRangeException(nameof(classification));
        if (!Enum.IsDefined(outcome))
            throw new ArgumentOutOfRangeException(nameof(outcome));
        if (outcome == Outcome.Success && classification is not
            (PageClassification.SearchResults or PageClassification.ListingDetails))
            throw new ArgumentException("Only recognized content pages may succeed.", nameof(outcome));
        if (correlationId == Guid.Empty)
            throw new ArgumentException("Correlation ID is required.", nameof(correlationId));
        if (observedAtUtc == default)
            throw new ArgumentException("Observation time is required.", nameof(observedAtUtc));
        ArgumentNullException.ThrowIfNull(diagnosticReferences);
        if (diagnosticReferences.Contains(Guid.Empty))
            throw new ArgumentException("Diagnostic references must be opaque nonempty IDs.", nameof(diagnosticReferences));
        RequestedUrl = ContractValidation.PublicUrl(requestedUrl);
        FinalUrl = ContractValidation.PublicUrl(finalUrl);
        ObservedAtUtc = observedAtUtc.ToUniversalTime();
        Classification = classification;
        Outcome = outcome;
        Warnings = ContractValidation.Codes(warnings);
        Errors = ContractValidation.Codes(errors);
        CorrelationId = correlationId;
        DiagnosticReferences = Array.AsReadOnly(diagnosticReferences.ToArray());
    }

    /// <summary>Required version of the exported schema.</summary>
    public string SchemaVersion { get; }
    /// <summary>Stable source identifier.</summary>
    public string SourceCode { get; }
    /// <summary>Required adapter version.</summary>
    public string AdapterVersion { get; }
    /// <summary>Optional public URL requested, without session parameters.</summary>
    public Uri? RequestedUrl { get; }
    /// <summary>Optional public final URL, without session parameters.</summary>
    public Uri? FinalUrl { get; }
    /// <summary>Observation instant with zero UTC offset.</summary>
    public DateTimeOffset ObservedAtUtc { get; }
    /// <summary>Observed page classification.</summary>
    public PageClassification Classification { get; }
    /// <summary>Validated overall outcome.</summary>
    public Outcome Outcome { get; }
    /// <summary>Stable warning codes, copied from input.</summary>
    public IReadOnlyList<string> Warnings { get; }
    /// <summary>Stable error codes, copied from input.</summary>
    public IReadOnlyList<string> Errors { get; }
    /// <summary>Nonempty ID joining diagnostics to this observation.</summary>
    public Guid CorrelationId { get; }
    /// <summary>Opaque diagnostic IDs; never paths or URLs.</summary>
    public IReadOnlyList<Guid> DiagnosticReferences { get; }
}
