using TorrentCore.Client;

namespace TorrentCore.Service.Tests;

public sealed class TorrentCoreClientOptionsTests
{
    [Fact]
    public void ToUri_ReturnsUri_ForValidHttpsBaseUrl()
    {
        var options = new TorrentCoreClientOptions {BaseUrl = "https://localhost:7033/"};

        var result = options.ToUri();

        Assert.Equal("https://localhost:7033/", result.ToString());
    }

    [Fact]
    public void ToUri_Throws_ForBlankBaseUrl()
    {
        var options = new TorrentCoreClientOptions();

        var action = () => options.ToUri();

        Assert.Throws<InvalidOperationException>(action);
    }
}
