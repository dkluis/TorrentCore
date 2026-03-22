#region

using Microsoft.Extensions.Configuration;
using TorrentCore.Avalonia.Models;

#endregion

namespace TorrentCore.Avalonia.Infrastructure;

public static class AppConfigLoader
{
    public static AppConfiguration Load()
    {
        var configRoot = new ConfigurationBuilder().SetBasePath(AppContext.BaseDirectory)
                                                   .AddJsonFile("Config/appsettings.json", false, true)
                                                   .AddEnvironmentVariables()
                                                   .Build();

        return configRoot.Get<AppConfiguration>() ?? new AppConfiguration();
    }
}
