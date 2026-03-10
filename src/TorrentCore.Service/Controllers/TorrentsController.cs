using Microsoft.AspNetCore.Mvc;
using TorrentCore.Contracts;
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
    [ProducesResponseType(StatusCodes.Status404NotFound, Type = typeof(ServiceErrorDto))]
    public async Task<ActionResult<TorrentDetailDto>> GetById(Guid torrentId, CancellationToken cancellationToken)
    {
        var torrent = await torrentApplicationService.GetTorrentAsync(torrentId, cancellationToken);
        return torrent is null
            ? NotFound(CreateError("torrent_not_found", $"Torrent '{torrentId}' was not found.", nameof(torrentId)))
            : Ok(torrent);
    }

    [HttpPost]
    [ProducesResponseType(StatusCodes.Status201Created, Type = typeof(TorrentDetailDto))]
    [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(ServiceErrorDto))]
    [ProducesResponseType(StatusCodes.Status409Conflict, Type = typeof(ServiceErrorDto))]
    public async Task<ActionResult<TorrentDetailDto>> Add([FromBody] AddMagnetRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var torrent = await torrentApplicationService.AddMagnetAsync(request, cancellationToken);
            return CreatedAtAction(nameof(GetById), new { torrentId = torrent.TorrentId }, torrent);
        }
        catch (ServiceOperationException exception)
        {
            return CreateActionResult(exception);
        }
    }

    [HttpPost("{torrentId:guid}/pause")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(TorrentActionResultDto))]
    [ProducesResponseType(StatusCodes.Status404NotFound, Type = typeof(ServiceErrorDto))]
    [ProducesResponseType(StatusCodes.Status409Conflict, Type = typeof(ServiceErrorDto))]
    public async Task<ActionResult<TorrentActionResultDto>> Pause(Guid torrentId, CancellationToken cancellationToken)
    {
        try
        {
            var result = await torrentApplicationService.PauseAsync(torrentId, cancellationToken);
            return Ok(result);
        }
        catch (ServiceOperationException exception)
        {
            return CreateActionResult(exception);
        }
    }

    [HttpPost("{torrentId:guid}/resume")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(TorrentActionResultDto))]
    [ProducesResponseType(StatusCodes.Status404NotFound, Type = typeof(ServiceErrorDto))]
    [ProducesResponseType(StatusCodes.Status409Conflict, Type = typeof(ServiceErrorDto))]
    public async Task<ActionResult<TorrentActionResultDto>> Resume(Guid torrentId, CancellationToken cancellationToken)
    {
        try
        {
            var result = await torrentApplicationService.ResumeAsync(torrentId, cancellationToken);
            return Ok(result);
        }
        catch (ServiceOperationException exception)
        {
            return CreateActionResult(exception);
        }
    }

    [HttpPost("{torrentId:guid}/remove")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(TorrentActionResultDto))]
    [ProducesResponseType(StatusCodes.Status404NotFound, Type = typeof(ServiceErrorDto))]
    public async Task<ActionResult<TorrentActionResultDto>> Remove(
        Guid torrentId,
        [FromBody] RemoveTorrentRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await torrentApplicationService.RemoveAsync(torrentId, request, cancellationToken);
            return Ok(result);
        }
        catch (ServiceOperationException exception)
        {
            return CreateActionResult(exception);
        }
    }

    private ActionResult CreateActionResult(ServiceOperationException exception)
    {
        var error = CreateError(exception.Code, exception.Message, exception.Target);
        return StatusCode(exception.StatusCode, error);
    }

    private ServiceErrorDto CreateError(string code, string message, string? target)
    {
        return new ServiceErrorDto
        {
            Code    = code,
            Message = message,
            Target  = target,
            TraceId = HttpContext.TraceIdentifier,
        };
    }
}
