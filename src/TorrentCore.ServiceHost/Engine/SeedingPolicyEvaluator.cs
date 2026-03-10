using TorrentCore.Service.Configuration;

namespace TorrentCore.Service.Engine;

public static class SeedingPolicyEvaluator
{
    public static SeedingPolicyDecision Evaluate(
        SeedingStopMode mode,
        double targetRatio,
        int targetMinutes,
        long uploadedBytes,
        long? totalBytes,
        DateTimeOffset? seedingStartedAtUtc,
        DateTimeOffset now)
    {
        var ratio = totalBytes is > 0
            ? uploadedBytes / (double)totalBytes.Value
            : 0d;
        var seedingMinutes = seedingStartedAtUtc is null
            ? 0d
            : Math.Max(0d, (now - seedingStartedAtUtc.Value).TotalMinutes);

        return mode switch
        {
            SeedingStopMode.Unlimited => new SeedingPolicyDecision(false, "unlimited", ratio, seedingMinutes),
            SeedingStopMode.StopImmediately => new SeedingPolicyDecision(true, "complete", ratio, seedingMinutes),
            SeedingStopMode.StopAfterRatio => new SeedingPolicyDecision(ratio >= targetRatio, "ratio", ratio, seedingMinutes),
            SeedingStopMode.StopAfterTime => new SeedingPolicyDecision(seedingMinutes >= targetMinutes, "time", ratio, seedingMinutes),
            SeedingStopMode.StopAfterRatioOrTime => new SeedingPolicyDecision(
                ratio >= targetRatio || seedingMinutes >= targetMinutes,
                ratio >= targetRatio ? "ratio" : "time",
                ratio,
                seedingMinutes),
            _ => new SeedingPolicyDecision(false, "unknown", ratio, seedingMinutes),
        };
    }
}

public sealed record SeedingPolicyDecision(
    bool ShouldStop,
    string Reason,
    double CurrentRatio,
    double CurrentSeedingMinutes);
