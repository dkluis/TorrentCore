using TorrentCore.Service.Vpn;

namespace TorrentCore.Service.Tests;

// Builds process configuration only; no processes, configuration helpers, databases or services are used.
public sealed class ExternalProcessRequestTests
{
    [Fact]
    public void ExistingArgumentListInvocationKeepsDefaults()
    {
        var start = ExternalProcessRunner.BuildStartInfo(new("/unit/controller", ["status", "two words"], TimeSpan.FromSeconds(5)));
        Assert.Equal(new[] { "status", "two words" }, start.ArgumentList);
        Assert.Equal("/", start.WorkingDirectory);
        Assert.False(start.RedirectStandardInput);
        Assert.False(start.UseShellExecute);
    }

    [Fact]
    public void CallbackPreservesConfiguredArgumentsAndRoutesPayloadThroughStandardInput()
    {
        var start = ExternalProcessRunner.BuildStartInfo(new("/unit/callback", [], TimeSpan.FromSeconds(30))
        {
            ArgumentString = "--api-base-url \"http://unit.invalid\" --payload-rejection",
            StandardInput = "payload", WorkingDirectory = "/unit/apps",
            Environment = new Dictionary<string, string> { ["TVMAZE_API_COMPLETE_API_KEY"] = "unit-key" },
        });
        Assert.Empty(start.ArgumentList);
        Assert.Equal("--api-base-url \"http://unit.invalid\" --payload-rejection", start.Arguments);
        Assert.Equal("/unit/apps", start.WorkingDirectory);
        Assert.Equal("unit-key", start.Environment["TVMAZE_API_COMPLETE_API_KEY"]);
        Assert.True(start.RedirectStandardInput);
        Assert.True(start.RedirectStandardOutput);
        Assert.False(start.UseShellExecute);
    }
}
