using LandErp.Collector.Contracts.V1;

namespace LandErp.Infrastructure.Modules.Collection;

internal sealed record CollectionRetryDecision(TimeSpan? Delay, bool AttentionRequired);

internal static class CollectionRetryPolicy
{
    private static readonly TimeSpan[] TransientDelays = [TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(15), TimeSpan.FromMinutes(45)];
    private static readonly TimeSpan[] RateLimitDelays = [TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(90), TimeSpan.FromHours(4)];
    private static readonly TimeSpan[] InterruptedDelays = [TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(30)];

    public static CollectionRetryDecision Decide(CollectionOutcome outcome, string reasonCode, int retryAttempt)
    {
        if (outcome is CollectionOutcome.Success or CollectionOutcome.LimitReached)
            return new(null, false);
        if (outcome is CollectionOutcome.Captcha or CollectionOutcome.AuthenticationRequired)
            return new(null, true);
        if (reasonCode is CollectionResultReasonCodes.InvalidSearchUrl
            or CollectionResultReasonCodes.LayoutChanged
            or CollectionResultReasonCodes.InvalidSourceResponse)
            return new(null, true);

        TimeSpan[] delays = outcome switch
        {
            CollectionOutcome.RateLimited => RateLimitDelays,
            CollectionOutcome.Interrupted => InterruptedDelays,
            CollectionOutcome.Partial or CollectionOutcome.SourceError => TransientDelays,
            _ => []
        };
        return retryAttempt < delays.Length
            ? new(delays[retryAttempt], false)
            : new(null, true);
    }
}
