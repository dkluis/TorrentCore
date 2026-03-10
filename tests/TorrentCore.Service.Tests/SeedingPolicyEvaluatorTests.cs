using TorrentCore.Service.Configuration;
using TorrentCore.Service.Engine;

namespace TorrentCore.Service.Tests;

public sealed class SeedingPolicyEvaluatorTests
{
    [Fact]
    public void Evaluate_StopImmediately_ReturnsStop()
    {
        var decision = SeedingPolicyEvaluator.Evaluate(
            SeedingStopMode.StopImmediately,
            targetRatio: 1.0,
            targetMinutes: 60,
            uploadedBytes: 0,
            totalBytes: 1_000,
            seedingStartedAtUtc: DateTimeOffset.UtcNow,
            now: DateTimeOffset.UtcNow);

        Assert.True(decision.ShouldStop);
        Assert.Equal("complete", decision.Reason);
    }

    [Fact]
    public void Evaluate_StopAfterRatio_ReturnsStopWhenRatioReached()
    {
        var decision = SeedingPolicyEvaluator.Evaluate(
            SeedingStopMode.StopAfterRatio,
            targetRatio: 1.5,
            targetMinutes: 60,
            uploadedBytes: 1_500,
            totalBytes: 1_000,
            seedingStartedAtUtc: DateTimeOffset.UtcNow,
            now: DateTimeOffset.UtcNow);

        Assert.True(decision.ShouldStop);
        Assert.Equal("ratio", decision.Reason);
    }

    [Fact]
    public void Evaluate_StopAfterTime_ReturnsStopWhenTimeReached()
    {
        var startedAt = DateTimeOffset.UtcNow.AddMinutes(-90);
        var decision = SeedingPolicyEvaluator.Evaluate(
            SeedingStopMode.StopAfterTime,
            targetRatio: 1.0,
            targetMinutes: 60,
            uploadedBytes: 0,
            totalBytes: 1_000,
            seedingStartedAtUtc: startedAt,
            now: DateTimeOffset.UtcNow);

        Assert.True(decision.ShouldStop);
        Assert.Equal("time", decision.Reason);
    }
}
