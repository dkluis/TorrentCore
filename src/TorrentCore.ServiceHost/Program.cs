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
builder.Services.AddHostedService<TorrentCoreStorageInitializer>();
builder.Services.AddSingleton<ITorrentEngineAdapter, InMemoryTorrentEngineAdapter>();
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
