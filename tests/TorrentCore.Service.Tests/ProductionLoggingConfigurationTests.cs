using System.Text.Json;
using TorrentCore.Service.Infrastructure;

namespace TorrentCore.Service.Tests;

public sealed class ProductionLoggingConfigurationTests
{
    [Theory]
    [InlineData("src/TorrentCore.ServiceHost/appsettings.json")]
    [InlineData("src/TorrentCore.WebUI/appsettings.json")]
    public void ProductionHost_UsesWarningConsoleBaselineWithUtcTimestamps(string relativePath)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(GetRepositoryPath(relativePath)));
        var logging = document.RootElement.GetProperty("Logging");

        Assert.Equal("Warning", logging.GetProperty("LogLevel").GetProperty("Default").GetString());

        var console = logging.GetProperty("Console");
        Assert.Equal("simple", console.GetProperty("FormatterName").GetString());

        var formatterOptions = console.GetProperty("FormatterOptions");
        Assert.False(formatterOptions.GetProperty("SingleLine").GetBoolean());
        Assert.Equal("yyyy-MM-ddTHH:mm:ss.fffK ", formatterOptions.GetProperty("TimestampFormat").GetString());
        Assert.True(formatterOptions.GetProperty("UseUtcTimestamp").GetBoolean());
    }

    [Theory]
    [InlineData("src/TorrentCore.ServiceHost/appsettings.Development.json")]
    [InlineData("src/TorrentCore.WebUI/appsettings.Development.json")]
    public void DevelopmentHost_RetainsInformationConsoleBaseline(string relativePath)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(GetRepositoryPath(relativePath)));

        Assert.Equal(
            "Information",
            document.RootElement
                    .GetProperty("Logging")
                    .GetProperty("LogLevel")
                    .GetProperty("Default")
                    .GetString()
        );
    }

    [Fact]
    public void FatalProcessMarker_IncludesUtcTimestampAndExceptionContext()
    {
        var writer = new StringWriter();
        var occurredAtUtc = new DateTimeOffset(2026, 7, 27, 8, 30, 57, TimeSpan.Zero);

        ProcessFatalExceptionDiagnostics.WriteMarker(
            writer,
            occurredAtUtc,
            1234,
            isTerminating: true,
            new InvalidOperationException("peer exchange failed")
        );

        var marker = writer.ToString();
        Assert.Contains("[2026-07-27T08:30:57.0000000+00:00]", marker, StringComparison.Ordinal);
        Assert.Contains("ProcessId=1234", marker, StringComparison.Ordinal);
        Assert.Contains("IsTerminating=True", marker, StringComparison.Ordinal);
        Assert.Contains("ExceptionType=System.InvalidOperationException", marker, StringComparison.Ordinal);
        Assert.Contains("Message=peer exchange failed", marker, StringComparison.Ordinal);
    }

    private static string GetRepositoryPath(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TorrentCore.sln")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return Path.Combine(directory.FullName, relativePath);
    }
}
