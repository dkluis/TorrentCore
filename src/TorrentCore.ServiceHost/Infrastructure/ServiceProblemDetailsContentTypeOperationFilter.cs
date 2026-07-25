using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;
using TorrentCore.Contracts;

namespace TorrentCore.Service.Infrastructure;

public sealed class ServiceProblemDetailsContentTypeOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        foreach (var responseType in context.ApiDescription.SupportedResponseTypes
                     .Where(responseType => responseType.Type == typeof(ServiceProblemDetailsDto)))
        {
            if (operation.Responses is null ||
                !operation.Responses.TryGetValue(responseType.StatusCode.ToString(), out var response) ||
                response?.Content is not { Count: > 0 } content)
            {
                continue;
            }

            var mediaType = content.Values.First();
            content.Clear();
            content["application/problem+json"] = mediaType;
        }
    }
}
