using System.Text;
using TorrentCore.Contracts.Host;

namespace TorrentCore.WebUI.Components.Shared;

internal static class VpnStatusDisplayText
{
    public const string DefaultPausedMessage =
        "VPN connection could not be confirmed. Torrent processing is paused.";

    public static string Validation(bool? enabled)
        => enabled switch
        {
            true => "Enabled",
            false => "Disabled",
            _ => "--",
        };

    public static string Processing(bool? available)
        => available switch
        {
            true => "Active",
            false => "Paused",
            _ => "--",
        };

    public static string Identifier(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "--";
        }

        var output = new StringBuilder(value.Length + 8);
        foreach (var character in value)
        {
            if (char.IsUpper(character) && output.Length > 0)
            {
                output.Append(' ');
            }

            output.Append(character);
        }

        return output.ToString();
    }

    public static string Interval(int? seconds)
        => seconds is null ? "--" : $"{seconds.Value:n0} seconds";

    public static string OperatorMessage(EngineHostStatusDto status)
    {
        if (!string.IsNullOrWhiteSpace(status.TorrentProcessingMessage))
        {
            return status.TorrentProcessingMessage;
        }

        if (status.VpnValidationEnabled == true)
        {
            return "VPN connection is available. Torrent processing is active.";
        }

        if (status.VpnValidationEnabled is null)
        {
            return "VPN connection status is unavailable from this Service version.";
        }

        return "VPN validation is disabled. Torrent processing is active.";
    }

    public static string? PausedReason(string? reason)
        => reason switch
        {
            "DirectIsp" => "The service is using the configured direct ISP connection.",
            "InvalidResponse" => "The VPN check returned an invalid public address.",
            "TimedOut" => "The VPN check timed out.",
            "EndpointFailure" => "The VPN check service could not be reached.",
            "UnexpectedFailure" => "The VPN check failed unexpectedly.",
            "EngineActivationFailed" => "The VPN is available, but torrent processing could not restart.",
            "EngineSuspensionFailed" => "Torrent processing could not be paused cleanly.",
            _ => null,
        };
}

internal sealed class TorrentProcessingAvailabilityState
{
    public EngineHostStatusDto? LastExplicitStatus { get; private set; }

    public bool IsUnavailable => LastExplicitStatus?.TorrentProcessingAvailable == false;

    public event Action? Changed;

    public void Accept(EngineHostStatusDto? status)
    {
        if (status?.TorrentProcessingAvailable is not null)
        {
            LastExplicitStatus = status;
            Changed?.Invoke();
        }
    }
}
