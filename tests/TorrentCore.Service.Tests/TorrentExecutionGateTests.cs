using TorrentCore.Service.Engine;

namespace TorrentCore.Service.Tests;

public sealed class TorrentExecutionGateTests
{
    [Fact]
    public void Gate_IsOpenByDefault()
    {
        var gate = new TorrentExecutionGate();

        using var lease = gate.TryAcquire();

        Assert.True(gate.IsOpen);
        Assert.NotNull(lease);
    }

    [Fact]
    public async Task CloseAsync_PreventsNewWorkAndWaitsForAdmittedWork()
    {
        var gate = new TorrentExecutionGate();
        var admittedLease = gate.TryAcquire();
        Assert.NotNull(admittedLease);

        var closeTask = gate.CloseAsync(CancellationToken.None);

        Assert.False(gate.IsOpen);
        Assert.Null(gate.TryAcquire());
        Assert.False(closeTask.IsCompleted);

        admittedLease.Dispose();
        await closeTask;

        gate.Open();
        using var reopenedLease = gate.TryAcquire();
        Assert.NotNull(reopenedLease);
    }

    [Fact]
    public async Task CancelledCloseWait_LeavesGateClosedUntilAdmittedWorkDrains()
    {
        var gate = new TorrentExecutionGate();
        using var admittedLease = gate.TryAcquire();
        Assert.NotNull(admittedLease);
        using var cancellationSource = new CancellationTokenSource();

        var closeTask = gate.CloseAsync(cancellationSource.Token);
        cancellationSource.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => closeTask);
        Assert.False(gate.IsOpen);
        Assert.Null(gate.TryAcquire());
        Assert.Throws<InvalidOperationException>(() => gate.Open());
    }

    [Fact]
    public async Task InitiallyClosedGate_CanOpenWithoutAClosingOperation()
    {
        var gate = new TorrentExecutionGate(initiallyOpen: false);

        Assert.Null(gate.TryAcquire());
        await gate.CloseAsync(CancellationToken.None);

        gate.Open();
        using var lease = gate.TryAcquire();
        Assert.NotNull(lease);
    }
}
