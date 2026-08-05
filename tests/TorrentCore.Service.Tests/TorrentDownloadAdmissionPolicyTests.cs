using TorrentCore.Service.Engine;

namespace TorrentCore.Service.Tests;

public sealed class TorrentDownloadAdmissionPolicyTests
{
    [Fact]
    public void RapidMetadataTransitions_DoNotAdmitBeyondDownloadLimit()
    {
        const int downloadLimit = 4;
        const int metadataLimit = 10;
        const int burstSize = 20;

        var unresolvedCount = burstSize;
        var activeMetadataCount = 0;
        var resolvedDownloadDemand = 0;

        ReconcileAdmission();
        Assert.Equal(downloadLimit, activeMetadataCount);

        for (var index = 0; index < downloadLimit; index++)
        {
            activeMetadataCount--;
            resolvedDownloadDemand++;

            ReconcileAdmission();

            Assert.True(
                activeMetadataCount + resolvedDownloadDemand <= downloadLimit,
                $"Transition {index + 1} exceeded the configured download limit.");
        }

        Assert.Equal(downloadLimit, resolvedDownloadDemand);
        Assert.Equal(0, activeMetadataCount);
        Assert.Equal(burstSize - downloadLimit, unresolvedCount);

        resolvedDownloadDemand--;
        ReconcileAdmission();

        Assert.Equal(1, activeMetadataCount);
        Assert.Equal(downloadLimit, activeMetadataCount + resolvedDownloadDemand);

        void ReconcileAdmission()
        {
            var effectiveMetadataLimit = TorrentDownloadAdmissionPolicy.CalculateMetadataResolutionLimit(
                metadataLimit,
                downloadLimit,
                resolvedDownloadDemand);
            var dispatchCount = Math.Min(unresolvedCount, Math.Max(0, effectiveMetadataLimit - activeMetadataCount));
            unresolvedCount -= dispatchCount;
            activeMetadataCount += dispatchCount;
        }
    }

    [Theory]
    [InlineData(10, 4, 0, 4)]
    [InlineData(10, 4, 1, 3)]
    [InlineData(10, 4, 4, 0)]
    [InlineData(10, 4, 12, 0)]
    [InlineData(2, 4, 0, 2)]
    public void CalculateMetadataResolutionLimit_ReservesDownloadCapacity(
        int metadataLimit,
        int downloadLimit,
        int resolvedDownloadDemand,
        int expectedLimit)
    {
        Assert.Equal(
            expectedLimit,
            TorrentDownloadAdmissionPolicy.CalculateMetadataResolutionLimit(
                metadataLimit,
                downloadLimit,
                resolvedDownloadDemand));
    }
}
