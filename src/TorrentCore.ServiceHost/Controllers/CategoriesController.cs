#region

using Microsoft.AspNetCore.Mvc;
using TorrentCore.Contracts.Categories;
using TorrentCore.Service.Application;

#endregion

namespace TorrentCore.Service.Controllers;

[ApiController]
[Route("api/categories")]
[Produces("application/json")]
public sealed class CategoriesController(ITorrentApplicationService torrentApplicationService) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(IReadOnlyList<TorrentCategoryDto>))]
    public async Task<ActionResult<IReadOnlyList<TorrentCategoryDto>>> GetAll(CancellationToken cancellationToken)
    {
        var categories = await torrentApplicationService.GetCategoriesAsync(cancellationToken);
        return Ok(categories);
    }

    [HttpPut("{key}")]
    [ProducesResponseType(StatusCodes.Status200OK,         Type = typeof(TorrentCategoryDto))]
    [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(ProblemDetails))]
    [ProducesResponseType(StatusCodes.Status404NotFound,   Type = typeof(ProblemDetails))]
    public async Task<ActionResult<TorrentCategoryDto>> Update(string key,
        [FromBody] UpdateTorrentCategoryRequest                       request, CancellationToken cancellationToken)
    {
        var category = await torrentApplicationService.UpdateCategoryAsync(key, request, cancellationToken);
        return Ok(category);
    }
}
