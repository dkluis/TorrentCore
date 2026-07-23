using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using TorrentCore.Contracts;
using TorrentCore.Contracts.Diagnostics;
using TorrentCore.Contracts.Host;
using TorrentCore.Service.Configuration;

namespace TorrentCore.Service.Tests;

public sealed class OpenApiContractTests
{
    private const string UpdateEnvironmentVariable = "TORRENTCORE_UPDATE_OPENAPI";

    [Fact]
    public async Task OpenApiDocument_MatchesCommittedContract()
    {
        var rootPath = Path.Combine(Path.GetTempPath(), $"torrentcore-openapi-{Guid.NewGuid():N}");

        try
        {
            await using var factory = CreateFactory(rootPath);
            using var httpClient = factory.CreateClient();

            var health = await httpClient.GetFromJsonAsync<ServiceHealthDto>("api/health");
            var hostStatus = await httpClient.GetFromJsonAsync<EngineHostStatusDto>("api/host/status");
            var openApiJson = await httpClient.GetStringAsync("swagger/v1/swagger.json");

            Assert.NotNull(health);
            Assert.NotNull(hostStatus);
            Assert.Equal(ServiceApiContract.CurrentVersion, health.ApiVersion);
            Assert.Equal(ServiceApiContract.CurrentVersion, hostStatus.ApiVersion);

            var normalizedDocument = Normalize(openApiJson);
            var contractPath = GetContractPath();

            if (string.Equals(
                    Environment.GetEnvironmentVariable(UpdateEnvironmentVariable),
                    "1",
                    StringComparison.Ordinal))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(contractPath)!);
                await File.WriteAllTextAsync(contractPath, normalizedDocument);
            }

            Assert.True(
                File.Exists(contractPath),
                $"The committed OpenAPI contract is missing. Set {UpdateEnvironmentVariable}=1 and rerun this test."
            );

            var committedDocument = await File.ReadAllTextAsync(contractPath);
            Assert.Equal(normalizedDocument, committedDocument);

            AssertContractShape(JsonNode.Parse(normalizedDocument));
        }
        finally
        {
            if (Directory.Exists(rootPath))
            {
                Directory.Delete(rootPath, recursive: true);
            }
        }
    }

    private static WebApplicationFactory<Program> CreateFactory(string rootPath)
    {
        var downloadPath = Path.Combine(rootPath, "downloads");
        var storagePath  = Path.Combine(rootPath, "storage");

        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
                {
                    builder.UseEnvironment("Development");
                    builder.ConfigureAppConfiguration((_, configurationBuilder) =>
                        {
                            configurationBuilder.AddInMemoryCollection(
                                new Dictionary<string, string?>
                                {
                                    [$"{TorrentCoreServiceOptions.SectionName}:EngineMode"] =
                                        TorrentEngineMode.Fake.ToString(),
                                    [$"{TorrentCoreServiceOptions.SectionName}:DownloadRootPath"] = downloadPath,
                                    [$"{TorrentCoreServiceOptions.SectionName}:StorageRootPath"] = storagePath,
                                }
                            );
                        }
                    );
                }
            );
    }

    private static string GetContractPath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TorrentCore.sln")))
        {
            directory = directory.Parent;
        }

        if (directory is null)
        {
            throw new DirectoryNotFoundException("Unable to locate the TorrentCore repository root.");
        }

        return Path.Combine(
            directory.FullName,
            "clients",
            "apple",
            "Packages",
            "TorrentCoreKit",
            "Sources",
            "TorrentCoreAPI",
            "openapi.json"
        );
    }

    private static string Normalize(string json)
    {
        var document = JsonNode.Parse(json) ??
                throw new InvalidOperationException("The generated OpenAPI document was empty.");
        var sortedDocument = SortNode(document);

        return sortedDocument.ToJsonString(
            new JsonSerializerOptions
            {
                WriteIndented = true,
            }
        ) + Environment.NewLine;
    }

    private static JsonNode SortNode(JsonNode node)
    {
        return node switch
        {
            JsonObject jsonObject => new JsonObject(
                jsonObject
                    .OrderBy(property => property.Key, StringComparer.Ordinal)
                    .Select(property => KeyValuePair.Create(
                            property.Key,
                            property.Value is null ? null : SortNode(property.Value)
                        )
                    )
            ),
            JsonArray jsonArray => new JsonArray(
                jsonArray
                    .Select(item => item is null ? null : SortNode(item))
                    .ToArray()
            ),
            _ => node.DeepClone(),
        };
    }

    private static void AssertContractShape(JsonNode? document)
    {
        Assert.NotNull(document);

        var operationIds = document["paths"]?
            .AsObject()
            .SelectMany(path => path.Value?.AsObject() ?? [])
            .Select(operation => operation.Value?["operationId"]?.GetValue<string>())
            .Where(operationId => !string.IsNullOrWhiteSpace(operationId))
            .ToArray();

        Assert.NotNull(operationIds);
        Assert.NotEmpty(operationIds);
        Assert.Equal(operationIds.Length, operationIds.Distinct(StringComparer.Ordinal).Count());

        var problemProperties = document["components"]?["schemas"]?[nameof(ServiceProblemDetailsDto)]?["properties"];
        Assert.NotNull(problemProperties);
        Assert.NotNull(problemProperties["code"]);
        Assert.NotNull(problemProperties["target"]);
        Assert.NotNull(problemProperties["traceId"]);
    }
}
