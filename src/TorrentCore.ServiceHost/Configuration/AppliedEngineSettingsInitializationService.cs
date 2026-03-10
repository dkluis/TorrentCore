namespace TorrentCore.Service.Configuration;

public sealed class AppliedEngineSettingsInitializationService(
    IRuntimeSettingsService runtimeSettingsService,
    AppliedEngineSettingsState appliedEngineSettingsState) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var settings = await runtimeSettingsService.GetEffectiveSettingsAsync(cancellationToken);
        appliedEngineSettingsState.Set(
            settings.EngineMaximumConnections,
            settings.EngineMaximumHalfOpenConnections,
            settings.EngineMaximumDownloadRateBytesPerSecond,
            settings.EngineMaximumUploadRateBytesPerSecond);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
