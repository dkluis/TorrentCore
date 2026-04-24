using System.Diagnostics;

namespace TorrentCore.Service.Infrastructure;

public sealed class LaunchAgentServiceRestartScheduler(
    ILogger<LaunchAgentServiceRestartScheduler> logger
) : ILaunchAgentServiceRestartScheduler
{
    private const int RestartDelaySeconds = 2;

    public Task<ServiceRestartScheduleResult> ScheduleRestartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var serviceLabel = Environment.GetEnvironmentVariable("XPC_SERVICE_NAME");
        if (string.IsNullOrWhiteSpace(serviceLabel))
        {
            throw new InvalidOperationException(
                "Service restart is only supported when TorrentCore.Service is running under launchd."
            );
        }

        var currentUserId = GetCurrentUserId();
        var launchDomain = $"gui/{currentUserId}";
        var command =
            $"nohup /bin/zsh -c 'sleep {RestartDelaySeconds}; launchctl kickstart -k {launchDomain}/{serviceLabel}' >/dev/null 2>&1 &";

        var startInfo = new ProcessStartInfo
        {
            FileName = "/bin/zsh",
            WorkingDirectory = AppContext.BaseDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add(command);

        using var process = Process.Start(startInfo)
                            ?? throw new InvalidOperationException("Unable to launch the restart scheduler process.");

        logger.LogInformation(
            "Scheduled launchctl restart for {ServiceLabel} in domain {LaunchDomain}.",
            serviceLabel,
            launchDomain
        );

        return Task.FromResult(
            new ServiceRestartScheduleResult
            {
                ServiceLabel = serviceLabel,
                Message =
                    $"Restart requested for {serviceLabel}. The service will restart shortly and may be unavailable briefly.",
            }
        );
    }

    private static string GetCurrentUserId()
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "/usr/bin/id",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("-u");

        using var process = Process.Start(startInfo)
                            ?? throw new InvalidOperationException("Unable to resolve the current user id.");

        var output = process.StandardOutput.ReadToEnd().Trim();
        process.WaitForExit();

        if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(output))
        {
            var error = process.StandardError.ReadToEnd().Trim();
            throw new InvalidOperationException(
                $"Unable to resolve the current user id for launchctl restart scheduling. {error}".Trim()
            );
        }

        return output;
    }
}
