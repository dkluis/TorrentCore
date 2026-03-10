using Microsoft.AspNetCore.Mvc;
using TorrentCore.Contracts.Torrents;
using TorrentCore.Service.Application;

namespace TorrentCore.Service.Controllers;

[ApiController]
[Route("api/torrents")]
[Produces("application/json")]
public sealed class TorrentsController(ITorrentApplicationService torrentApplicationService) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(IReadOnlyList<TorrentSummaryDto>))]
    public async Task<ActionResult<IReadOnlyList<TorrentSummaryDto>>> GetAll(CancellationToken cancellationToken)
    {
        var torrents = await torrentApplicationService.GetTorrentsAsync(cancellationToken);
        return Ok(torrents);
    }

    [HttpGet("{torrentId:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(TorrentDetailDto))]
    [ProducesResponseType(StatusCodes.Status404NotFound, Type = typeof(ProblemDetails))]
    public async Task<ActionResult<TorrentDetailDto>> GetById(Guid torrentId, CancellationToken cancellationToken)
    {
        var torrent = await torrentApplicationService.GetTorrentAsync(torrentId, cancellationToken);
        return Ok(torrent);
    }

    [HttpPost]
    [ProducesResponseType(StatusCodes.Status201Created, Type = typeof(TorrentDetailDto))]
    [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(ProblemDetails))]
    [ProducesResponseType(StatusCodes.Status409Conflict, Type = typeof(ProblemDetails))]
    public async Task<ActionResult<TorrentDetailDto>> Add([FromBody] AddMagnetRequest request, CancellationToken cancellationToken)
    {
        var torrent = await torrentApplicationService.AddMagnetAsync(request, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { torrentId = torrent.TorrentId }, torrent);
    }

    [HttpPost("{torrentId:guid}/pause")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(TorrentActionResultDto))]
    [ProducesResponseType(StatusCodes.Status404NotFound, Type = typeof(ProblemDetails))]
    [ProducesResponseType(StatusCodes.Status409Conflict, Type = typeof(ProblemDetails))]
    public async Task<ActionResult<TorrentActionResultDto>> Pause(Guid torrentId, CancellationToken cancellationToken)
    {
        var result = await torrentApplicationService.PauseAsync(torrentId, cancellationToken);
        return Ok(result);
    }

    [HttpPost("{torrentId:guid}/resume")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(TorrentActionResultDto))]
    [ProducesResponseType(StatusCodes.Status404NotFound, Type = typeof(ProblemDetails))]
    [ProducesResponseType(StatusCodes.Status409Conflict, Type = typeof(ProblemDetails))]
    public async Task<ActionResult<TorrentActionResultDto>> Resume(Guid torrentId, CancellationToken cancellationToken)
    {
        var result = await torrentApplicationService.ResumeAsync(torrentId, cancellationToken);
        return Ok(result);
    }

    [HttpPost("{torrentId:guid}/remove")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(TorrentActionResultDto))]
    [ProducesResponseType(StatusCodes.Status404NotFound, Type = typeof(ProblemDetails))]
    public async Task<ActionResult<TorrentActionResultDto>> Remove(
        Guid torrentId,
        [FromBody] RemoveTorrentRequest? request,
        CancellationToken cancellationToken)
    {
        var result = await torrentApplicationService.RemoveAsync(
            torrentId,
            request ?? new RemoveTorrentRequest(),
            cancellationToken);
        return Ok(result);
    }
}
