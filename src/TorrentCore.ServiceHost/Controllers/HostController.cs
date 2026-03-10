using Microsoft.AspNetCore.Mvc;
using TorrentCore.Contracts.Host;
using TorrentCore.Service.Application;

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
}
