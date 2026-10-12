using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace FinSightIQ.Gateway.Services;

/// <summary>
/// Distributed sliding-window quota store powered by Redis sorted sets.
/// Guarantees rolling 24-hour limit consistency across multiple gateway replicas.
/// </summary>
public sealed class RedisPredictionQuotaStore : IPredictionQuotaStore
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<RedisPredictionQuotaStore> _logger;
    private readonly InMemoryPredictionQuotaStore _fallbackStore;
    private readonly TimeProvider _timeProvider;

    private const string LuaScript = @"
        local key = KEYS[1]
        local now = tonumber(ARGV[1])
        local windowMs = tonumber(ARGV[2])
        local limit = tonumber(ARGV[3])
        local cutoff = now - windowMs

        -- Purge expired entries
        redis.call('ZREMRANGEBYSCORE', key, 0, cutoff)

        -- Count remaining in window
        local count = redis.call('ZCARD', key)

        if count >= limit then
            local oldest = redis.call('ZRANGE', key, 0, 0, 'WITHSCORES')
            local oldestScore = tonumber(oldest[2])
            return { 0, count, oldestScore }
        else
            redis.call('ZADD', key, now, tostring(now))
            redis.call('PEXPIRE', key, windowMs)
            return { 1, count + 1, now }
        end
    ";

    public RedisPredictionQuotaStore(
        IConnectionMultiplexer redis,
        ILogger<RedisPredictionQuotaStore> logger,
        TimeProvider? timeProvider = null)
    {
        _redis = redis;
        _logger = logger;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _fallbackStore = new InMemoryPredictionQuotaStore(_timeProvider);
    }

    public async Task<QuotaResult> CheckAndConsumeQuotaAsync(
        string userId,
        int limit,
        TimeSpan window,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var db = _redis.GetDatabase();
            var now = _timeProvider.GetUtcNow();
            var nowMs = now.ToUnixTimeMilliseconds();
            var windowMs = (long)window.TotalMilliseconds;
            var key = $"quota:prediction:{userId}";

            var result = (RedisResult[]?)await db.ScriptEvaluateAsync(
                LuaScript,
                keys: [new RedisKey(key)],
                values: [new RedisValue(nowMs.ToString()), new RedisValue(windowMs.ToString()), new RedisValue(limit.ToString())]);

            if (result != null && result.Length >= 3)
            {
                var isAllowed = (int)result[0] == 1;
                var count = (int)result[1];
                var oldestScoreMs = (long)result[2];

                if (!isAllowed)
                {
                    var oldestTime = DateTimeOffset.FromUnixTimeMilliseconds(oldestScoreMs);
                    var resetTime = oldestTime.Add(window);
                    var retryAfter = resetTime > now ? resetTime - now : TimeSpan.FromSeconds(1);

                    return new QuotaResult(
                        IsAllowed: false,
                        Limit: limit,
                        Remaining: 0,
                        ResetTimestampSeconds: resetTime.ToUnixTimeSeconds(),
                        RetryAfter: retryAfter);
                }

                var remaining = Math.Max(0, limit - count);
                var resetTimestamp = now.Add(window).ToUnixTimeSeconds();

                return new QuotaResult(
                    IsAllowed: true,
                    Limit: limit,
                    Remaining: remaining,
                    ResetTimestampSeconds: resetTimestamp,
                    RetryAfter: TimeSpan.Zero);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis quota check failed for user {UserId}. Falling back to in-memory store.", userId);
        }

        return await _fallbackStore.CheckAndConsumeQuotaAsync(userId, limit, window, cancellationToken);
    }
}
