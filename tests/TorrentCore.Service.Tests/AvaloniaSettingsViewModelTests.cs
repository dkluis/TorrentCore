using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using TorrentCore.Avalonia.ViewModels;
using TorrentCore.Client;
using TorrentCore.Contracts.Categories;
using TorrentCore.Contracts.Host;

namespace TorrentCore.Service.Tests;

public sealed class AvaloniaSettingsViewModelTests
{
    [Fact]
    public async Task LoadAsync_PopulatesCallbackSettings_AndCategories()
    {
        var settings = CreateRuntimeSettings(
            completionCallbackEnabled: true,
            completionCallbackCommandPath: "/Users/dick/TVMaze/Scripts/torrentcore-callback.zsh",
            completionCallbackArguments: "--launch",
            completionCallbackWorkingDirectory: "/Users/dick/TVMaze",
            completionCallbackTimeoutSeconds: 45,
            completionCallbackFinalizationTimeoutSeconds: 150,
            completionCallbackApiBaseUrlOverride: "http://tvmaze:5078/",
            completionCallbackApiKeyOverride: "secret");
        var categories = CreateCategories();

        var torrentCoreClient = CreateClient((request, _) => Task.FromResult(CreateJsonResponse(
            request.RequestUri!.AbsolutePath switch
            {
                "/api/host/runtime-settings" when request.Method == HttpMethod.Get => settings,
                "/api/categories" when request.Method == HttpMethod.Get => categories,
                _ => throw new InvalidOperationException($"Unexpected request: {request.Method} {request.RequestUri}"),
            })));
        var viewModel = new SettingsViewModel(torrentCoreClient);

        await viewModel.LoadAsync();

        Assert.False(viewModel.HasError);
        Assert.Equal("MonoTorrent", viewModel.EngineRuntime);
        Assert.True(viewModel.CompletionCallbackEnabled);
        Assert.Equal("/Users/dick/TVMaze/Scripts/torrentcore-callback.zsh", viewModel.CompletionCallbackCommandPath);
        Assert.Equal("--launch", viewModel.CompletionCallbackArguments);
        Assert.Equal("/Users/dick/TVMaze", viewModel.CompletionCallbackWorkingDirectory);
        Assert.Equal(45, viewModel.CompletionCallbackTimeoutSeconds);
        Assert.Equal(150, viewModel.CompletionCallbackFinalizationTimeoutSeconds);
        Assert.Equal("http://tvmaze:5078/", viewModel.CompletionCallbackApiBaseUrlOverride);
        Assert.Equal("secret", viewModel.CompletionCallbackApiKeyOverride);
        Assert.False(viewModel.DeleteLogsForCompletedTorrents);
        Assert.Equal(90, viewModel.MetadataRefreshStaleSeconds);
        Assert.Equal(30, viewModel.MetadataRefreshRestartDelaySeconds);
        Assert.True(viewModel.HasCategories);
        Assert.Equal(2, viewModel.Categories.Count);

        var tvCategory = Assert.Single(viewModel.Categories, item => item.Key == "TV");
        Assert.Equal("TV", tvCategory.DisplayName);
        Assert.True(tvCategory.Enabled);
        Assert.True(tvCategory.InvokeCompletionCallback);
    }

    [Fact]
    public async Task SaveAsync_SendsCallbackSettings_AndCategoryUpdates()
    {
        var capturedRequests = new ConcurrentQueue<CapturedRequest>();
        var initialSettings = CreateRuntimeSettings(
            completionCallbackEnabled: false,
            completionCallbackCommandPath: string.Empty,
            completionCallbackArguments: string.Empty,
            completionCallbackWorkingDirectory: string.Empty,
            completionCallbackTimeoutSeconds: 30,
            completionCallbackFinalizationTimeoutSeconds: 120,
            completionCallbackApiBaseUrlOverride: string.Empty,
            completionCallbackApiKeyOverride: string.Empty);
        var initialCategories = CreateCategories();
        var updatedSettings = CreateRuntimeSettings(
            completionCallbackEnabled: true,
            completionCallbackCommandPath: "/Users/dick/TVMaze/Scripts/torrentcore-callback.zsh",
            completionCallbackArguments: "--launch",
            completionCallbackWorkingDirectory: "/Users/dick/TVMaze",
            completionCallbackTimeoutSeconds: 55,
            completionCallbackFinalizationTimeoutSeconds: 180,
            completionCallbackApiBaseUrlOverride: "http://tvmaze:5078/",
            completionCallbackApiKeyOverride: "secret",
            engineSettingsRequireRestart: true);

        var torrentCoreClient = CreateClient(async (request, body) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            capturedRequests.Enqueue(new CapturedRequest(request.Method.Method, path, body));

            return (path, request.Method.Method) switch
            {
                ("/api/host/runtime-settings", "GET") => CreateJsonResponse(initialSettings),
                ("/api/categories", "GET") => CreateJsonResponse(initialCategories),
                ("/api/host/runtime-settings", "PUT") => CreateJsonResponse(updatedSettings),
                ("/api/categories/TV", "PUT") => CreateJsonResponse(new TorrentCategoryDto
                {
                    Key = "TV",
                    DisplayName = "Television",
                    CallbackLabel = "tv",
                    DownloadRootPath = "/Volumes/Media/Incoming/TV",
                    Enabled = true,
                    InvokeCompletionCallback = true,
                    SortOrder = 5,
                }),
                ("/api/categories/Movies", "PUT") => CreateJsonResponse(new TorrentCategoryDto
                {
                    Key = "Movies",
                    DisplayName = "Movies",
                    CallbackLabel = "movies",
                    DownloadRootPath = "/Volumes/Media/Incoming/Movies",
                    Enabled = false,
                    InvokeCompletionCallback = false,
                    SortOrder = 20,
                }),
                _ => throw new InvalidOperationException($"Unexpected request: {request.Method} {request.RequestUri}"),
            };
        });
        var viewModel = new SettingsViewModel(torrentCoreClient);
        await viewModel.LoadAsync();

        viewModel.CompletionCallbackEnabled = true;
        viewModel.CompletionCallbackCommandPath = "/Users/dick/TVMaze/Scripts/torrentcore-callback.zsh";
        viewModel.CompletionCallbackArguments = "--launch";
        viewModel.CompletionCallbackWorkingDirectory = "/Users/dick/TVMaze";
        viewModel.CompletionCallbackTimeoutSeconds = 55;
        viewModel.CompletionCallbackFinalizationTimeoutSeconds = 180;
        viewModel.CompletionCallbackApiBaseUrlOverride = "http://tvmaze:5078/";
        viewModel.CompletionCallbackApiKeyOverride = "secret";
        viewModel.DeleteLogsForCompletedTorrents = true;
        viewModel.MetadataRefreshStaleSeconds = 120;
        viewModel.MetadataRefreshRestartDelaySeconds = 45;

        var tvCategory = Assert.Single(viewModel.Categories, item => item.Key == "TV");
        tvCategory.DisplayName = "Television";
        tvCategory.CallbackLabel = "tv";
        tvCategory.DownloadRootPath = "/Volumes/Media/Incoming/TV";
        tvCategory.Enabled = true;
        tvCategory.InvokeCompletionCallback = true;
        tvCategory.SortOrder = 5;

        var movieCategory = Assert.Single(viewModel.Categories, item => item.Key == "Movies");
        movieCategory.Enabled = false;

        await viewModel.SaveAsync();

        Assert.False(viewModel.HasError);
        Assert.Contains("Runtime settings and categories saved", viewModel.Message);
        Assert.True(viewModel.EngineSettingsRequireRestart);

        var runtimePut = capturedRequests.Single(item => item.Method == "PUT" && item.Path == "/api/host/runtime-settings");
        using var runtimeJson = JsonDocument.Parse(runtimePut.Body!);
        Assert.True(runtimeJson.RootElement.GetProperty("completionCallbackEnabled").GetBoolean());
        Assert.Equal("/Users/dick/TVMaze/Scripts/torrentcore-callback.zsh", runtimeJson.RootElement.GetProperty("completionCallbackCommandPath").GetString());
        Assert.Equal("--launch", runtimeJson.RootElement.GetProperty("completionCallbackArguments").GetString());
        Assert.Equal("/Users/dick/TVMaze", runtimeJson.RootElement.GetProperty("completionCallbackWorkingDirectory").GetString());
        Assert.Equal(55, runtimeJson.RootElement.GetProperty("completionCallbackTimeoutSeconds").GetInt32());
        Assert.Equal(180, runtimeJson.RootElement.GetProperty("completionCallbackFinalizationTimeoutSeconds").GetInt32());
        Assert.Equal("http://tvmaze:5078/", runtimeJson.RootElement.GetProperty("completionCallbackApiBaseUrlOverride").GetString());
        Assert.Equal("secret", runtimeJson.RootElement.GetProperty("completionCallbackApiKeyOverride").GetString());
        Assert.True(runtimeJson.RootElement.GetProperty("deleteLogsForCompletedTorrents").GetBoolean());
        Assert.Equal(120, runtimeJson.RootElement.GetProperty("metadataRefreshStaleSeconds").GetInt32());
        Assert.Equal(45, runtimeJson.RootElement.GetProperty("metadataRefreshRestartDelaySeconds").GetInt32());

        var tvPut = capturedRequests.Single(item => item.Method == "PUT" && item.Path == "/api/categories/TV");
        using var tvJson = JsonDocument.Parse(tvPut.Body!);
        Assert.Equal("Television", tvJson.RootElement.GetProperty("displayName").GetString());
        Assert.Equal("tv", tvJson.RootElement.GetProperty("callbackLabel").GetString());
        Assert.Equal("/Volumes/Media/Incoming/TV", tvJson.RootElement.GetProperty("downloadRootPath").GetString());
        Assert.True(tvJson.RootElement.GetProperty("enabled").GetBoolean());
        Assert.True(tvJson.RootElement.GetProperty("invokeCompletionCallback").GetBoolean());
        Assert.Equal(5, tvJson.RootElement.GetProperty("sortOrder").GetInt32());
    }

    private static RuntimeSettingsDto CreateRuntimeSettings(
        bool completionCallbackEnabled,
        string completionCallbackCommandPath,
        string completionCallbackArguments,
        string completionCallbackWorkingDirectory,
        int completionCallbackTimeoutSeconds,
        int completionCallbackFinalizationTimeoutSeconds,
        string completionCallbackApiBaseUrlOverride,
        string completionCallbackApiKeyOverride,
        bool engineSettingsRequireRestart = false)
    {
        return new RuntimeSettingsDto
        {
            EngineRuntime = "MonoTorrent",
            SupportsLiveUpdates = true,
            UsesPersistedOverrides = true,
            PartialFilesEnabled = true,
            PartialFileSuffix = ".!mt",
            SeedingStopMode = "Unlimited",
            SeedingStopRatio = 1.5,
            SeedingStopMinutes = 90,
            CompletedTorrentCleanupMode = "Never",
            CompletedTorrentCleanupMinutes = 120,
            DeleteLogsForCompletedTorrents = false,
            EngineConnectionFailureLogBurstLimit = 5,
            EngineConnectionFailureLogWindowSeconds = 60,
            EngineMaximumConnections = 150,
            EngineMaximumHalfOpenConnections = 8,
            EngineMaximumDownloadRateBytesPerSecond = 0,
            EngineMaximumUploadRateBytesPerSecond = 0,
            MaxActiveMetadataResolutions = 4,
            MaxActiveDownloads = 4,
            MetadataRefreshStaleSeconds = 90,
            MetadataRefreshRestartDelaySeconds = 30,
            CompletionCallbackEnabled = completionCallbackEnabled,
            CompletionCallbackCommandPath = completionCallbackCommandPath,
            CompletionCallbackArguments = completionCallbackArguments,
            CompletionCallbackWorkingDirectory = completionCallbackWorkingDirectory,
            CompletionCallbackTimeoutSeconds = completionCallbackTimeoutSeconds,
            CompletionCallbackFinalizationTimeoutSeconds = completionCallbackFinalizationTimeoutSeconds,
            CompletionCallbackApiBaseUrlOverride = completionCallbackApiBaseUrlOverride,
            CompletionCallbackApiKeyOverride = completionCallbackApiKeyOverride,
            AppliedEngineMaximumConnections = 150,
            AppliedEngineMaximumHalfOpenConnections = 8,
            AppliedEngineMaximumDownloadRateBytesPerSecond = 0,
            AppliedEngineMaximumUploadRateBytesPerSecond = 0,
            EngineSettingsRequireRestart = engineSettingsRequireRestart,
            UpdatedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-5),
            RetrievedAtUtc = DateTimeOffset.UtcNow,
        };
    }

    private static IReadOnlyList<TorrentCategoryDto> CreateCategories()
    {
        return
        [
            new TorrentCategoryDto
            {
                Key = "TV",
                DisplayName = "TV",
                CallbackLabel = "television",
                DownloadRootPath = "/Volumes/Media/TV",
                Enabled = true,
                InvokeCompletionCallback = true,
                SortOrder = 10,
            },
            new TorrentCategoryDto
            {
                Key = "Movies",
                DisplayName = "Movies",
                CallbackLabel = "movies",
                DownloadRootPath = "/Volumes/Media/Movies",
                Enabled = true,
                InvokeCompletionCallback = false,
                SortOrder = 20,
            },
        ];
    }

    private static TorrentCoreClient CreateClient(Func<HttpRequestMessage, string?, Task<HttpResponseMessage>> responseFactory)
    {
        var endpointProvider = new MutableTorrentCoreEndpointProvider("http://localhost:7033/");
        var httpClient = new HttpClient(new TestHttpMessageHandler(responseFactory));
        return new TorrentCoreClient(httpClient, endpointProvider);
    }

    private static HttpResponseMessage CreateJsonResponse(object payload)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"),
        };
    }

    private sealed record CapturedRequest(string Method, string Path, string? Body);

    private sealed class TestHttpMessageHandler(Func<HttpRequestMessage, string?, Task<HttpResponseMessage>> responseFactory) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            var response = await responseFactory(request, body);
            response.RequestMessage = request;
            response.Content.Headers.ContentType ??= new MediaTypeHeaderValue("application/json");
            return response;
        }
    }
}
