using System.Collections.Concurrent;
using System.Net;
using System.Text;

namespace TorrentCore.Service.Tests.Fixtures;

internal sealed class ScriptedHttpMessageHandler : HttpMessageHandler
{
    private readonly ConcurrentQueue<Func<CancellationToken, Task<HttpResponseMessage>>> _responses = new();
    private readonly ConcurrentQueue<ObservedHttpRequest> _requests = new();

    public IReadOnlyList<ObservedHttpRequest> Requests => _requests.ToArray();

    public void EnqueueJson(string json, HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        _responses.Enqueue(_ => Task.FromResult(new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        }));
    }

    public void EnqueueStatus(HttpStatusCode statusCode)
        => _responses.Enqueue(_ => Task.FromResult(new HttpResponseMessage(statusCode)));

    public void EnqueueException(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        _responses.Enqueue(_ => Task.FromException<HttpResponseMessage>(exception));
    }

    public void EnqueueCancellation()
        => _responses.Enqueue(cancellationToken => Task.FromCanceled<HttpResponseMessage>(
            cancellationToken.IsCancellationRequested
                ? cancellationToken
                : new CancellationToken(canceled: true)
        ));

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        _requests.Enqueue(new ObservedHttpRequest(request.Method, request.RequestUri));

        if (!_responses.TryDequeue(out var response))
        {
            throw new InvalidOperationException("No scripted HTTP response remains for this request.");
        }

        return response(cancellationToken);
    }
}

internal sealed record ObservedHttpRequest(HttpMethod Method, Uri? RequestUri);
