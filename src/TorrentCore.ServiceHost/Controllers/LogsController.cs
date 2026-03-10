using Microsoft.AspNetCore.Mvc;
using TorrentCore.Contracts.Diagnostics;
using TorrentCore.Core.Diagnostics;

namespace TorrentCore.Service.Controllers;

[ApiController]
[Route("api/logs")]
[Produces("application/json")]
public sealed class LogsController(IActivityLogService activityLogService) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(IReadOnlyList<ActivityLogEntryDto>))]
    public async Task<ActionResult<IReadOnlyList<ActivityLogEntryDto>>> GetRecent(
        [FromQuery] int take = 100,
        [FromQuery] ActivityLogLevel? level = null,
        [FromQuery] string? category = null,
        [FromQuery] string? eventType = null,
        [FromQuery] Guid? torrentId = null,
        [FromQuery] Guid? serviceInstanceId = null,
        [FromQuery] DateTimeOffset? fromUtc = null,
        [FromQuery] DateTimeOffset? toUtc = null,
        CancellationToken cancellationToken = default)
    {
        var logs = await activityLogService.GetRecentAsync(new ActivityLogQuery
        {
            Take = take,
            Level = level,
            Category = category,
            EventType = eventType,
            TorrentId = torrentId,
            ServiceInstanceId = serviceInstanceId,
            FromUtc = fromUtc,
            ToUtc = toUtc,
        }, cancellationToken);

        return Ok(logs.Select(log => new ActivityLogEntryDto
        {
            LogEntryId    = log.LogEntryId,
            OccurredAtUtc = log.OccurredAtUtc,
            Level         = log.Level.ToString(),
            Category      = log.Category,
            EventType     = log.EventType,
            Message       = log.Message,
            TorrentId     = log.TorrentId,
            ServiceInstanceId = log.ServiceInstanceId,
            TraceId       = log.TraceId,
            DetailsJson   = log.DetailsJson,
        }).ToArray());
    }
}
