using Microsoft.Extensions.Configuration;
using TorrentCore.Avalonia.Models;

namespace TorrentCore.Avalonia.Infrastructure;

public static class AppConfigLoader
{
    public static AppConfiguration Load()
    {
        var configRoot = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("Config/appsettings.json", optional: false, reloadOnChange: true)
            .AddEnvironmentVariables()
            .Build();

        return configRoot.Get<AppConfiguration>() ?? new AppConfiguration();
    }
}
