#region

using Microsoft.AspNetCore.Mvc;
using TorrentCore.Contracts.Host;
using TorrentCore.Service.Application;

#endregion

namespace TorrentCore.Service.Controllers;

[ApiController]
[Route("api/host")]
[Produces("application/json")]
public sealed class HostController(ITorrentApplicationService torrentApplicationService) : ControllerBase
{
    [HttpGet("status")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(EngineHostStatusDto))]
    public async Task<ActionResult<EngineHostStatusDto>> GetStatus(CancellationToken cancellationToken)
    {
        var status = await torrentApplicationService.GetHostStatusAsync(cancellationToken);
        return Ok(status);
    }

    [HttpGet("runtime-settings")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(RuntimeSettingsDto))]
    public async Task<ActionResult<RuntimeSettingsDto>> GetRuntimeSettings(CancellationToken cancellationToken)
    {
        var settings = await torrentApplicationService.GetRuntimeSettingsAsync(cancellationToken);
        return Ok(settings);
    }

    [HttpPut("runtime-settings")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(RuntimeSettingsDto))]
    public async Task<ActionResult<RuntimeSettingsDto>> UpdateRuntimeSettings(
        [FromBody] UpdateRuntimeSettingsRequest request, CancellationToken cancellationToken)
    {
        var settings = await torrentApplicationService.UpdateRuntimeSettingsAsync(request, cancellationToken);
        return Ok(settings);
    }
}
