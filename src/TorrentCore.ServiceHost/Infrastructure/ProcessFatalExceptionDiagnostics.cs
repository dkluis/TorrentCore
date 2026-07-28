namespace TorrentCore.Service.Infrastructure;

internal static class ProcessFatalExceptionDiagnostics
{
    public static void Register()
    {
        AppDomain.CurrentDomain.UnhandledException += static (_, eventArgs) =>
        {
            try
            {
                WriteMarker(
                    Console.Error,
                    DateTimeOffset.UtcNow,
                    Environment.ProcessId,
                    eventArgs.IsTerminating,
                    eventArgs.ExceptionObject
                );
            }
            catch
            {
                // Process-fatal diagnostics must not mask the original unhandled exception.
            }
        };
    }

    internal static void WriteMarker(TextWriter writer, DateTimeOffset occurredAtUtc, int processId,
        bool isTerminating, object? exceptionObject)
    {
        ArgumentNullException.ThrowIfNull(writer);

        var exceptionType = exceptionObject?.GetType().FullName ?? "(unknown)";
        var message = exceptionObject is Exception exception
                ? exception.Message
                : exceptionObject?.ToString() ?? "(no exception details)";

        writer.WriteLine(
            "[{0:O}] TorrentCore.Service unhandled process exception. ProcessId={1} IsTerminating={2} ExceptionType={3} Message={4}",
            occurredAtUtc,
            processId,
            isTerminating,
            exceptionType,
            message
        );
    }
}
