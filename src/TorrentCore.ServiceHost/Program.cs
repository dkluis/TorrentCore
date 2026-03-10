using TorrentCore.Core.Diagnostics;
using TorrentCore.Core.Torrents;
using TorrentCore.Persistence.Sqlite.Configuration;
using TorrentCore.Persistence.Sqlite.Logging;
using TorrentCore.Persistence.Sqlite.Schema;
using TorrentCore.Persistence.Sqlite.Torrents;
using TorrentCore.Service.Application;
using TorrentCore.Service.Configuration;
using TorrentCore.Service.Engine;
using TorrentCore.Service.Infrastructure;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ServiceOperationExceptionHandler>();
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new()
    {
        Title = "TorrentCore Service API",
        Version = "v1",
        Description = "Management API for the TorrentCore service host.",
    });
});
builder.Services.AddSingleton<IValidateOptions<TorrentCoreServiceOptions>, TorrentCoreServiceOptionsValidator>();
builder.Services.AddOptions<TorrentCoreServiceOptions>()
    .Bind(builder.Configuration.GetSection(TorrentCoreServiceOptions.SectionName))
    .ValidateOnStart();
builder.Services.AddSingleton(serviceProvider =>
{
    var hostEnvironment = serviceProvider.GetRequiredService<IHostEnvironment>();
    var serviceOptions = serviceProvider.GetRequiredService<IOptions<TorrentCoreServiceOptions>>().Value;
    return TorrentCoreServicePathResolver.Resolve(hostEnvironment.ContentRootPath, serviceOptions);
});
builder.Services.AddSingleton<ServiceInstanceContext>();
builder.Services.AddSingleton<StartupRecoveryState>();
builder.Services.AddHostedService<TorrentCoreStorageInitializer>();
builder.Services.AddSingleton<IActivityLogService>(serviceProvider =>
{
    var servicePaths = serviceProvider.GetRequiredService<ResolvedTorrentCoreServicePaths>();
    var serviceOptions = serviceProvider.GetRequiredService<IOptions<TorrentCoreServiceOptions>>().Value;
    return new SqliteActivityLogService(servicePaths.DatabaseFilePath, serviceOptions.MaxActivityLogEntries);
});
builder.Services.AddSingleton(serviceProvider =>
{
    var servicePaths = serviceProvider.GetRequiredService<ResolvedTorrentCoreServicePaths>();
    return new SqliteSchemaMigrator(servicePaths.DatabaseFilePath);
});
builder.Services.AddSingleton(serviceProvider =>
{
    var servicePaths = serviceProvider.GetRequiredService<ResolvedTorrentCoreServicePaths>();
    return new SqliteRuntimeSettingsStore(servicePaths.DatabaseFilePath);
});
builder.Services.AddSingleton<ITorrentStateStore>(serviceProvider =>
{
    var servicePaths = serviceProvider.GetRequiredService<ResolvedTorrentCoreServicePaths>();
    return new SqliteTorrentStateStore(servicePaths.DatabaseFilePath);
});
builder.Services.AddSingleton<IRuntimeSettingsService, RuntimeSettingsService>();
builder.Services.AddSingleton<PersistedTorrentEngineAdapter>();
builder.Services.AddSingleton<MonoTorrentEngineAdapter>();
builder.Services.AddSingleton<ITorrentEngineAdapter>(serviceProvider =>
    serviceProvider.GetRequiredService<IOptions<TorrentCoreServiceOptions>>().Value.EngineMode == TorrentEngineMode.MonoTorrent
        ? serviceProvider.GetRequiredService<MonoTorrentEngineAdapter>()
        : serviceProvider.GetRequiredService<PersistedTorrentEngineAdapter>());
builder.Services.AddHostedService<SqlitePersistenceInitializer>();
builder.Services.AddHostedService<TorrentStartupRecoveryService>();
builder.Services.AddHostedService<FakeTorrentRuntimeService>();
builder.Services.AddHostedService(serviceProvider => serviceProvider.GetRequiredService<MonoTorrentEngineAdapter>());
builder.Services.AddHostedService<TorrentEngineSynchronizationService>();
builder.Services.AddSingleton<ITorrentApplicationService, TorrentApplicationService>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "TorrentCore Service API v1");
        options.RoutePrefix = "swagger";
    });
}

app.UseExceptionHandler();
app.UseHttpsRedirection();
app.MapControllers();

app.Run();

public partial class Program
{
}
