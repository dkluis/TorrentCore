#region

using Microsoft.AspNetCore.Mvc;
using TorrentCore.Contracts;
using TorrentCore.Contracts.Maintenance;
using TorrentCore.Service.Application;

#endregion

namespace TorrentCore.Service.Controllers;

[ApiController]
[Route("api/maintenance")]
[Produces("application/json")]
public sealed class MaintenanceController(IMaintenanceCleanupService maintenanceCleanupService) : ControllerBase
{
    [HttpPost("logs/cleanup")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(CleanupByDateResultDto))]
    [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(ServiceProblemDetailsDto))]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable, Type = typeof(ServiceProblemDetailsDto))]
    public async Task<ActionResult<CleanupByDateResultDto>> CleanupLogs(
        [FromBody] CleanupByDateRequest request,
        CancellationToken cancellationToken)
    {
        return Ok(await maintenanceCleanupService.DeleteLogsAsync(request, cancellationToken));
    }

    [HttpPost("history/cleanup")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(CleanupByDateResultDto))]
    [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(ServiceProblemDetailsDto))]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable, Type = typeof(ServiceProblemDetailsDto))]
    public async Task<ActionResult<CleanupByDateResultDto>> CleanupHistory(
        [FromBody] CleanupByDateRequest request,
        CancellationToken cancellationToken)
    {
        return Ok(await maintenanceCleanupService.DeleteHistoryAsync(request, cancellationToken));
    }
}
