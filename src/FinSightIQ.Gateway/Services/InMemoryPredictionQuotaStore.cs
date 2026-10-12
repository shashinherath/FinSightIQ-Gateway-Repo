using System.Collections.Concurrent;

namespace FinSightIQ.Gateway.Services;

/// <summary>
/// Thread-safe in-memory sliding-window quota store. Suitable for single-instance
/// development, automated testing, or environments without external Redis clusters.
/// </summary>
public sealed class InMemoryPredictionQuotaStore : IPredictionQuotaStore
{
    private readonly ConcurrentDictionary<string, List<DateTimeOffset>> _userTimestamps = new();
    private readonly TimeProvider _timeProvider;

    public InMemoryPredictionQuotaStore(TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public Task<QuotaResult> CheckAndConsumeQuotaAsync(
        string userId,
        int limit,
        TimeSpan window,
        CancellationToken cancellationToken = default)
    {
        var now = _timeProvider.GetUtcNow();
        var cutoff = now.Subtract(window);

        var list = _userTimestamps.GetOrAdd(userId, _ => new List<DateTimeOffset>());

        lock (list)
        {
            // Remove timestamps outside the rolling window
            list.RemoveAll(ts => ts < cutoff);

            if (list.Count >= limit)
            {
                // Find oldest timestamp in window to compute retry-after
                var oldestInWindow = list[0];
                var resetTime = oldestInWindow.Add(window);
                var retryAfter = resetTime > now ? resetTime - now : TimeSpan.FromSeconds(1);

                return Task.FromResult(new QuotaResult(
                    IsAllowed: false,
                    Limit: limit,
                    Remaining: 0,
                    ResetTimestampSeconds: resetTime.ToUnixTimeSeconds(),
                    RetryAfter: retryAfter));
            }

            // Consume quota
            list.Add(now);
            var remaining = Math.Max(0, limit - list.Count);
            var resetTimestamp = now.Add(window).ToUnixTimeSeconds();

            return Task.FromResult(new QuotaResult(
                IsAllowed: true,
                Limit: limit,
                Remaining: remaining,
                ResetTimestampSeconds: resetTimestamp,
                RetryAfter: TimeSpan.Zero));
        }
    }
}
