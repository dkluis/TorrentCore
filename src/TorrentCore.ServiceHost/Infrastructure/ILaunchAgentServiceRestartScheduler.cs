namespace TorrentCore.Service.Infrastructure;

public interface ILaunchAgentServiceRestartScheduler
{
    Task<ServiceRestartScheduleResult> ScheduleRestartAsync(CancellationToken cancellationToken);
}
