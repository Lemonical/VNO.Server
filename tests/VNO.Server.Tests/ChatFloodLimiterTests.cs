using System;
using VNO.Server.Services;
using Xunit;

namespace VNO.Server.Tests;

/// <summary>
/// Tests for the per player chat flood cap
/// </summary>
public sealed class ChatFloodLimiterTests
{
    // a hand cranked clock so the refill can be tested without waiting on wall time
    private sealed class ManualClock : TimeProvider
    {
        private long _ticks = 1;

        public override long GetTimestamp() => _ticks;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public void Advance(TimeSpan delta) => _ticks += delta.Ticks;
    }

    [Fact]
    public void Allows_the_burst_then_caps_at_the_refill_rate()
    {
        var clock = new ManualClock();
        var limiter = new ChatFloodLimiter(capacity: 3, refillPerSecond: 1, clock);

        for (var i = 0; i < 3; i++)
        {
            Assert.True(limiter.TryAcquire(userId: 1));
        }

        // the burst is spent
        Assert.False(limiter.TryAcquire(userId: 1));

        // one second buys exactly one more line
        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.True(limiter.TryAcquire(userId: 1));
        Assert.False(limiter.TryAcquire(userId: 1));
    }

    [Fact]
    public void Players_do_not_share_a_bucket()
    {
        var clock = new ManualClock();
        var limiter = new ChatFloodLimiter(capacity: 1, refillPerSecond: 0.1, clock);

        Assert.True(limiter.TryAcquire(userId: 1));
        Assert.False(limiter.TryAcquire(userId: 1));

        // a second player has its own allowance
        Assert.True(limiter.TryAcquire(userId: 2));
    }

    [Fact]
    public void Zero_capacity_disables_the_cap()
    {
        var clock = new ManualClock();
        var limiter = new ChatFloodLimiter(capacity: 0, refillPerSecond: 0, clock);

        for (var i = 0; i < 100; i++)
        {
            Assert.True(limiter.TryAcquire(userId: 1));
        }
    }

    [Fact]
    public void Forgetting_a_player_resets_their_bucket()
    {
        var clock = new ManualClock();
        var limiter = new ChatFloodLimiter(capacity: 1, refillPerSecond: 0.001, clock);

        Assert.True(limiter.TryAcquire(userId: 1));
        Assert.False(limiter.TryAcquire(userId: 1));

        // a player who left and rejoined starts fresh rather than inheriting an empty bucket
        limiter.Forget(userId: 1);
        Assert.True(limiter.TryAcquire(userId: 1));
    }
}
