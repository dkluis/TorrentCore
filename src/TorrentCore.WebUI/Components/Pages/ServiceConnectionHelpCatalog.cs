namespace TorrentCore.WebUI.Components.Pages;

public static class ServiceConnectionHelpCatalog
{
    public static readonly SettingHelpContent CurrentEndpoint = new(
        "Current Endpoint",
        "Shows the currently saved service endpoint for this WebUI host and the last reachability result.",
        "This is the host-global endpoint that TorrentCore.WebUI uses when it calls TorrentCore.Service. Reachability reflects the most recent probe result, not a permanent health guarantee, so it can change if the service stops, the host address changes, or the network path becomes unavailable."
    );

    public static readonly SettingHelpContent ServiceBaseUrl = new(
        "Service Base URL",
        "Sets the HTTP base URL the WebUI host should use for TorrentCore.Service.",
        "Enter the full HTTP address for the machine running TorrentCore.Service, including the port and trailing slash when appropriate. This is not the browser address and not the WebUI address. Use the service host name or LAN IP that this WebUI host can actually reach."
    );

    public static readonly SettingHelpContent Test = new(
        "Test",
        "Probes the typed endpoint without saving it.",
        "Use Test when you want to confirm that the entered service URL is reachable before replacing the currently saved endpoint. It does not change the stored configuration file. It only runs a connectivity check and shows the result."
    );

    public static readonly SettingHelpContent Save = new(
        "Save",
        "Persists the typed endpoint for this WebUI host and applies it immediately if reachable.",
        "Save writes the endpoint to `Config/service-connection.json` for this TorrentCore.WebUI host. If the saved endpoint is reachable, the WebUI immediately starts using it for later API calls. If the endpoint cannot be reached, TorrentCore.WebUI reports the failure instead of silently switching to a broken target."
    );

    public static readonly SettingHelpContent Recheck = new(
        "Recheck",
        "Retests the currently saved endpoint without changing the stored value.",
        "Use Recheck when the service may have restarted, the network path may have recovered, or you want a fresh availability result. It does not save a new URL. It only probes the endpoint that is already stored for this WebUI host."
    );
}
