namespace TorrentCore.Service.Configuration;

public sealed class ServiceInstanceContext
{
    public Guid ServiceInstanceId { get; init; } = Guid.NewGuid();
}
