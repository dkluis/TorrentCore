namespace TorrentCore.Service.Engine;

internal static class TorrentDownloadAdmissionPolicy
{
    public static int CalculateMetadataResolutionLimit(
        int configuredMetadataResolutionLimit,
        int configuredDownloadLimit,
        int resolvedDownloadDemand)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(configuredMetadataResolutionLimit);
        ArgumentOutOfRangeException.ThrowIfNegative(configuredDownloadLimit);
        ArgumentOutOfRangeException.ThrowIfNegative(resolvedDownloadDemand);

        var unreservedDownloadSlots = Math.Max(0, configuredDownloadLimit - resolvedDownloadDemand);
        return Math.Min(configuredMetadataResolutionLimit, unreservedDownloadSlots);
    }

    public static int CalculateAvailableMetadataResolutionSlots(
        int configuredMetadataResolutionLimit,
        int configuredDownloadLimit,
        int resolvedDownloadDemand,
        int activeMetadataResolutions)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(activeMetadataResolutions);

        var effectiveLimit = CalculateMetadataResolutionLimit(
            configuredMetadataResolutionLimit,
            configuredDownloadLimit,
            resolvedDownloadDemand);
        return Math.Max(0, effectiveLimit - activeMetadataResolutions);
    }
}
