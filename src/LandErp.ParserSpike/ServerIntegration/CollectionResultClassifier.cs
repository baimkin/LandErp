using LandErp.Collector.Contracts.V1;
using LandErp.ParserSpike.LocalCollection;

namespace LandErp.ParserSpike.ServerIntegration;

public static class CollectionResultClassifier
{
    public static CollectionResult[] Build(Guid jobId, Guid leaseId, CollectionJob job,
        ObservationEnvelope[] observations, PageJournal[] journal, MapScope? map,
        CollectionCompletionFacts? facts, string overrideReasonCode = "")
    {
        int unique = observations.Select(item => $"{item.Data.Source}:{item.Data.ExternalId}")
            .Distinct(StringComparer.Ordinal).Count();
        CollectionCompletionFacts effective = facts ?? LegacyFacts(job, unique);
        CollectionOutcome outcome = Map(effective.Kind);
        string reason = string.IsNullOrEmpty(overrideReasonCode) ? effective.ReasonCode : overrideReasonCode;
        if (!string.IsNullOrEmpty(overrideReasonCode)) outcome = CollectionOutcome.Interrupted;
        if (outcome == CollectionOutcome.Success && (!effective.EndReached || !effective.LoadingCompleted))
        {
            outcome = unique > 0 ? CollectionOutcome.Partial : CollectionOutcome.SourceError;
            reason = effective.LoadingCompleted ? CollectionResultReasonCodes.EndNotConfirmed : CollectionResultReasonCodes.LoadingInterrupted;
        }
        HashSet<string> warnings = new(effective.Warnings.Where(IsMachineCode), StringComparer.Ordinal);
        if (map?.ExpectedCount is int hint && hint != unique)
        {
            warnings.Add(CollectionResultReasonCodes.CountHintMismatch);
            if (outcome == CollectionOutcome.Success && string.IsNullOrEmpty(reason)) reason = CollectionResultReasonCodes.CountHintMismatch;
        }
        CollectionCoverage coverage = new(unique, map?.ExpectedCount, effective.EndReached,
            effective.LoadingCompleted, effective.StableRounds,
            journal.Where(item => item.Completed).Select(item => item.Page).Distinct().Count(),
            job.Limit, map?.ResponseBatches);
        List<CollectionResult> results = [];
        foreach (ObservationEnvelope[] chunk in observations.Chunk(25))
            results.Add(new(Guid.CreateVersion7(), jobId, leaseId, CollectionOutcome.Success, chunk, false));
        results.Add(new(Guid.CreateVersion7(), jobId, leaseId, outcome, [], true, reason,
            warnings.Take(20).ToArray(), coverage));
        return results.ToArray();
    }

    private static CollectionCompletionFacts LegacyFacts(CollectionJob job, int unique)
    {
        CollectionCompletionKind kind = job.State switch
        {
            JobState.Completed => CollectionCompletionKind.Success,
            JobState.LimitReached => CollectionCompletionKind.LimitReached,
            JobState.StoppedInterrupted => CollectionCompletionKind.Interrupted,
            _ when job.Reason.StartsWith("Captcha", StringComparison.Ordinal) => CollectionCompletionKind.Captcha,
            _ when job.Reason.StartsWith("AuthenticationRequired", StringComparison.Ordinal) => CollectionCompletionKind.AuthenticationRequired,
            _ when job.Reason.StartsWith("RateLimited", StringComparison.Ordinal) => CollectionCompletionKind.RateLimited,
            _ when unique > 0 => CollectionCompletionKind.Partial,
            _ => CollectionCompletionKind.SourceError
        };
        string reason = kind switch
        {
            CollectionCompletionKind.LimitReached => CollectionResultReasonCodes.PageLimitReached,
            CollectionCompletionKind.Interrupted => CollectionResultReasonCodes.AgentInterrupted,
            CollectionCompletionKind.Partial => CollectionResultReasonCodes.EndNotConfirmed,
            CollectionCompletionKind.SourceError => CollectionResultReasonCodes.InvalidSourceResponse,
            _ => ""
        };
        bool success = kind == CollectionCompletionKind.Success;
        return new(kind, success, success || kind == CollectionCompletionKind.LimitReached, 0, reason, []);
    }

    private static CollectionOutcome Map(CollectionCompletionKind kind) => kind switch
    {
        CollectionCompletionKind.Success => CollectionOutcome.Success,
        CollectionCompletionKind.LimitReached => CollectionOutcome.LimitReached,
        CollectionCompletionKind.Partial => CollectionOutcome.Partial,
        CollectionCompletionKind.RateLimited => CollectionOutcome.RateLimited,
        CollectionCompletionKind.SourceError => CollectionOutcome.SourceError,
        CollectionCompletionKind.Interrupted => CollectionOutcome.Interrupted,
        CollectionCompletionKind.Captcha => CollectionOutcome.Captcha,
        CollectionCompletionKind.AuthenticationRequired => CollectionOutcome.AuthenticationRequired,
        _ => CollectionOutcome.SourceError
    };
    private static bool IsMachineCode(string value) => value.Length is > 0 and <= 64
        && value.All(character => char.IsAsciiLetterUpper(character) || char.IsDigit(character) || character == '_');
}
