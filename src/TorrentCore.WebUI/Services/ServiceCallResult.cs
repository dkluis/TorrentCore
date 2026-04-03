namespace TorrentCore.WebUI.Services;

public sealed class ServiceCallResult
{
    private ServiceCallResult(
        bool isSuccess,
        string? errorMessage,
        int? statusCode,
        string? errorCode,
        string? traceId
    )
    {
        IsSuccess = isSuccess;
        ErrorMessage = errorMessage;
        StatusCode = statusCode;
        ErrorCode = errorCode;
        TraceId = traceId;
    }

    public bool IsSuccess { get; }
    public string? ErrorMessage { get; }
    public int? StatusCode { get; }
    public string? ErrorCode { get; }
    public string? TraceId { get; }

    public static ServiceCallResult Success() => new(true, null, null, null, null);

    public static ServiceCallResult Failure(
        string errorMessage,
        int? statusCode = null,
        string? errorCode = null,
        string? traceId = null
    )
    {
        return new ServiceCallResult(false, errorMessage, statusCode, errorCode, traceId);
    }
}

public sealed class ServiceCallResult<T>
{
    private ServiceCallResult(
        bool isSuccess,
        T? value,
        string? errorMessage,
        int? statusCode,
        string? errorCode,
        string? traceId
    )
    {
        IsSuccess = isSuccess;
        Value = value;
        ErrorMessage = errorMessage;
        StatusCode = statusCode;
        ErrorCode = errorCode;
        TraceId = traceId;
    }

    public bool IsSuccess { get; }
    public T? Value { get; }
    public string? ErrorMessage { get; }
    public int? StatusCode { get; }
    public string? ErrorCode { get; }
    public string? TraceId { get; }

    public static ServiceCallResult<T> Success(T? value) => new(true, value, null, null, null, null);

    public static ServiceCallResult<T> Failure(
        string errorMessage,
        int? statusCode = null,
        string? errorCode = null,
        string? traceId = null
    )
    {
        return new ServiceCallResult<T>(false, default, errorMessage, statusCode, errorCode, traceId);
    }
}
