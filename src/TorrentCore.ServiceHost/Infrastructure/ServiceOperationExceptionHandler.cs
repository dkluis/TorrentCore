using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using TorrentCore.Service.Application;

namespace TorrentCore.Service.Infrastructure;

public sealed class ServiceOperationExceptionHandler(IProblemDetailsService problemDetailsService) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is not ServiceOperationException serviceOperationException)
        {
            return false;
        }

        httpContext.Response.StatusCode = serviceOperationException.StatusCode;

        var problemDetails = new ProblemDetails
        {
            Title  = "TorrentCore request failed.",
            Detail = serviceOperationException.Message,
            Status = serviceOperationException.StatusCode,
            Type   = $"urn:torrentcore:error:{serviceOperationException.Code}",
        };

        problemDetails.Extensions["code"] = serviceOperationException.Code;
        problemDetails.Extensions["traceId"] = httpContext.TraceIdentifier;

        if (!string.IsNullOrWhiteSpace(serviceOperationException.Target))
        {
            problemDetails.Extensions["target"] = serviceOperationException.Target;
        }

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = problemDetails,
        });
    }
}
