using Microsoft.AspNetCore.Mvc;
using TorrentCore.Contracts.Health;

namespace TorrentCore.Service.Controllers;

[ApiController]
[Route("api/health")]
[Produces("application/json")]
public sealed class HealthController : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(ServiceHealthDto))]
    public ActionResult<ServiceHealthDto> Get()
    {
        return Ok(new ServiceHealthDto
        {
            ServiceName     = "TorrentCore.Service",
            Status          = "ok",
            EnvironmentName = HttpContext.RequestServices.GetRequiredService<IHostEnvironment>().EnvironmentName,
            CheckedAtUtc    = DateTimeOffset.UtcNow,
        });
    }
}
