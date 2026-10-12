namespace FinSightIQ.Gateway.Services;

/// <summary>
/// Result of a quota check and increment attempt.
/// </summary>
public sealed record QuotaResult(
    bool IsAllowed,
    int Limit,
    int Remaining,
    long ResetTimestampSeconds,
    TimeSpan RetryAfter);

/// <summary>
/// Abstraction for tracking user prediction quota over a rolling window.
/// Implementations support distributed stores (Redis) and local in-memory fallback.
/// </summary>
public interface IPredictionQuotaStore
{
    /// <summary>
    /// Checks and atomically increments the prediction count for a user within the specified window.
    /// </summary>
    Task<QuotaResult> CheckAndConsumeQuotaAsync(
        string userId,
        int limit,
        TimeSpan window,
        CancellationToken cancellationToken = default);
}
