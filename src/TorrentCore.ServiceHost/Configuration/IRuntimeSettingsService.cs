#region

using TorrentCore.Contracts.Host;

#endregion

namespace TorrentCore.Service.Configuration;

public interface IRuntimeSettingsService
{
    Task<RuntimeSettingsSnapshot> GetEffectiveSettingsAsync(CancellationToken cancellationToken);
    Task<RuntimeSettingsDto> GetRuntimeSettingsDtoAsync(CancellationToken cancellationToken);
    Task<RuntimeSettingsDto> UpdateAsync(UpdateRuntimeSettingsRequest request, CancellationToken cancellationToken);
}
