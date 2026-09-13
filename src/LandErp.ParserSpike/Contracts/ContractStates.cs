namespace LandErp.ParserSpike.Contracts;

/// <summary>Observed page state; default is deliberately unknown.</summary>
public enum PageClassification
{
    Unknown, SearchResults, ListingDetails, AuthenticationRequired, Captcha,
    RateLimited, ListingUnavailable, SourceError
}

/// <summary>Inspection state, independent of raw text and typed value.</summary>
public enum Presence
{
    NotInspected, Absent, Empty, Present, ParseFailed
}

/// <summary>Overall result; default cannot imply success.</summary>
public enum Outcome
{
    Failure, Attention, Success
}
