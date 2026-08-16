using System.Diagnostics;
using System.Text;

namespace TorrentCore.Service.Vpn;

internal sealed class ExternalProcessRunner(TimeProvider timeProvider) : IExternalProcessRunner
{
    private const int MaximumCapturedCharacters = 16 * 1024;

    public async Task<ExternalProcessResult> RunAsync(
        ExternalProcessRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.FileName);
        ArgumentNullException.ThrowIfNull(request.Arguments);
        if (request.Timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "The process timeout must be positive.");
        }
        cancellationToken.ThrowIfCancellationRequested();

        using var process = new Process
        {
            StartInfo = BuildStartInfo(request),
        };

        try
        {
            if (!process.Start())
            {
                return StartFailure("The process did not start.");
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return StartFailure(Sanitize(exception.Message));
        }

        var standardOutputTask = ReadBoundedAsync(process.StandardOutput);
        var standardErrorTask = ReadBoundedAsync(process.StandardError);
        var exitTask = process.WaitForExitAsync(cancellationToken);
        var timeoutTask = Task.Delay(request.Timeout, timeProvider, cancellationToken);

        try
        {
            var completed = await Task.WhenAny(exitTask, timeoutTask);
            if (completed == timeoutTask)
            {
                await timeoutTask;
                TryKill(process);
                await process.WaitForExitAsync(CancellationToken.None);
                return new ExternalProcessResult
                {
                    Started = true,
                    TimedOut = true,
                    ExitCode = process.HasExited ? process.ExitCode : null,
                    StandardOutput = await standardOutputTask,
                    StandardError = await standardErrorTask,
                    FailureSummary = "The process timed out.",
                };
            }

            await exitTask;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            TryKill(process);
            await process.WaitForExitAsync(CancellationToken.None);
            await Task.WhenAll(standardOutputTask, standardErrorTask);
            throw;
        }

        var standardOutput = await standardOutputTask;
        var standardError = await standardErrorTask;
        return new ExternalProcessResult
        {
            Started = true,
            TimedOut = false,
            ExitCode = process.ExitCode,
            StandardOutput = standardOutput,
            StandardError = standardError,
            FailureSummary = process.ExitCode == 0
                ? null
                : Sanitize(string.IsNullOrWhiteSpace(standardError) ? standardOutput : standardError),
        };
    }

    private static ProcessStartInfo BuildStartInfo(ExternalProcessRequest request)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = request.FileName,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = "/",
        };
        foreach (var argument in request.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return startInfo;
    }

    private static async Task<string> ReadBoundedAsync(StreamReader reader)
    {
        var captured = new StringBuilder(MaximumCapturedCharacters);
        var buffer = new char[4096];
        int read;
        while ((read = await reader.ReadAsync(buffer)) > 0)
        {
            if (captured.Length >= MaximumCapturedCharacters)
            {
                continue;
            }

            var toAppend = Math.Min(read, MaximumCapturedCharacters - captured.Length);
            captured.Append(buffer, 0, toAppend);
        }

        return captured.ToString();
    }

    private static ExternalProcessResult StartFailure(string failureSummary) => new()
    {
        Started = false,
        TimedOut = false,
        FailureSummary = failureSummary,
    };

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or
                                          System.ComponentModel.Win32Exception or
                                          NotSupportedException)
        {
            // The process exited concurrently or the platform could not service the kill request.
        }
    }

    internal static string Sanitize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "The process failed without diagnostic output.";
        }

        var singleLine = string.Join(
            ' ',
            value.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        );
        var printable = new string(singleLine.Where(character => !char.IsControl(character)).ToArray());
        return printable.Length <= 512 ? printable : printable[..512];
    }
}
