using System.Collections.Concurrent;

namespace TorrentCore.Service.Tests.Fixtures;

internal sealed class RecordingEngineLifecycle
{
    private readonly ConcurrentQueue<EngineLifecycleObservation> _observations = new();

    public IReadOnlyList<EngineLifecycleObservation> Observations => _observations.ToArray();

    public Guid RecordCreated()
    {
        var instanceId = Guid.NewGuid();
        Record(instanceId, EngineLifecycleStage.Created);
        return instanceId;
    }

    public void Record(Guid instanceId, EngineLifecycleStage stage)
        => _observations.Enqueue(new EngineLifecycleObservation(instanceId, stage));
}

internal sealed record EngineLifecycleObservation(Guid InstanceId, EngineLifecycleStage Stage);

internal enum EngineLifecycleStage
{
    Created,
    Started,
    StopRequested,
    Stopped,
    Disposed,
}
