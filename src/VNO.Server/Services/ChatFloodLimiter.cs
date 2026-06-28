using System;
using System.Collections.Concurrent;

namespace VNO.Server.Services;

/// <summary>
/// A per player token bucket that caps chat spam
/// </summary>
/// <remarks>
/// One bucket per session scoped user id. In character, out of character, and music lines
/// each spend a token, so a player may burst up to the capacity then send at the steady
/// refill rate. A flood past that is dropped rather than relayed to the whole area. Uses a
/// <see cref="TimeProvider"/> so tests drive the clock without waiting. A capacity of zero
/// disables the cap
/// </remarks>
public sealed class ChatFloodLimiter
{
    private readonly double _capacity;
    private readonly double _refillPerSecond;
    private readonly TimeProvider _time;
    private readonly ConcurrentDictionary<int, Bucket> _buckets = new();

    /// <summary>
    /// Creates the limiter from a burst capacity and a steady refill rate
    /// </summary>
    public ChatFloodLimiter(int capacity, double refillPerSecond, TimeProvider? timeProvider = null)
    {
        _capacity = capacity;
        _refillPerSecond = refillPerSecond;
        _time = timeProvider ?? TimeProvider.System;
    }

    /// <summary>
    /// Tries to spend one token for a player, returns true when the line may be relayed
    /// </summary>
    public bool TryAcquire(int userId)
    {
        if (_capacity <= 0)
        {
            return true;
        }

        var now = _time.GetTimestamp();
        var bucket = _buckets.GetOrAdd(userId, _ => new Bucket(_capacity, now));

        lock (bucket)
        {
            var elapsed = _time.GetElapsedTime(bucket.LastRefill, now).TotalSeconds;
            bucket.LastRefill = now;
            bucket.Tokens = Math.Min(_capacity, bucket.Tokens + (elapsed * _refillPerSecond));

            if (bucket.Tokens >= 1)
            {
                bucket.Tokens -= 1;
                return true;
            }

            return false;
        }
    }

    /// <summary>
    /// Forgets a player's bucket, called when the player leaves so state does not linger
    /// </summary>
    public void Forget(int userId) => _buckets.TryRemove(userId, out _);

    private sealed class Bucket
    {
        public Bucket(double tokens, long lastRefill)
        {
            Tokens = tokens;
            LastRefill = lastRefill;
        }

        public double Tokens { get; set; }

        public long LastRefill { get; set; }
    }
}
