#region

using Microsoft.AspNetCore.Mvc;
using TorrentCore.Contracts;
using TorrentCore.Contracts.History;
using TorrentCore.Service.Application;

#endregion

namespace TorrentCore.Service.Controllers;

[ApiController]
[Route("api/history")]
[Produces("application/json")]
public sealed class HistoryController(ITorrentApplicationService torrentApplicationService) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(IReadOnlyList<TorrentHistorySummaryDto>))]
    public async Task<ActionResult<IReadOnlyList<TorrentHistorySummaryDto>>> GetAll(
        [FromQuery] TorrentHistoryQueryRequest request,
        CancellationToken cancellationToken)
    {
        var history = await torrentApplicationService.GetHistoryAsync(request, cancellationToken);
        return Ok(history);
    }

    [HttpGet("filter-options")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(TorrentHistoryFilterOptionsDto))]
    public async Task<ActionResult<TorrentHistoryFilterOptionsDto>> GetFilterOptions(
        CancellationToken cancellationToken, [FromQuery] bool includePayloadRejection = false)
    {
        var options = await torrentApplicationService.GetHistoryFilterOptionsAsync(cancellationToken, includePayloadRejection);
        return Ok(options);
    }

    [HttpGet("by-torrent/{torrentId:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(TorrentHistoryDetailDto))]
    [ProducesResponseType(StatusCodes.Status404NotFound, Type = typeof(ServiceProblemDetailsDto))]
    public async Task<ActionResult<TorrentHistoryDetailDto>> GetByTorrentId(Guid torrentId,
        CancellationToken cancellationToken, [FromQuery] bool includePayloadRejection = false)
    {
        var history = await torrentApplicationService.GetHistoryByTorrentIdAsync(torrentId, cancellationToken, includePayloadRejection);
        return Ok(history);
    }
}
