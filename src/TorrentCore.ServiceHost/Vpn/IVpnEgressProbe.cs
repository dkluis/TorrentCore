using TorrentCore.Service.Configuration;

namespace TorrentCore.Service.Vpn;

internal interface IVpnEgressProbe
{
    Task<VpnEgressValidationResult> ValidateAsync(
        RuntimeSettingsSnapshot settings,
        CancellationToken cancellationToken);
}
