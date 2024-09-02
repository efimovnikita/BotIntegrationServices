using BotIntegration.Services.YouTube.Models;
using Hangfire;
using Hangfire.MemoryStorage;
using Serilog;
using Refit;

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Configuration.AddEnvironmentVariables();
    
    Log.Logger = new LoggerConfiguration()
        .WriteTo.Console()
        .WriteTo.Seq(builder.Configuration["Urls:LogServer"] ?? string.Empty)
        .CreateLogger();
    
    builder.Host.UseSerilog((_, configuration) =>
    {
        configuration.WriteTo.Console();
        configuration.WriteTo.Seq(builder.Configuration["Urls:LogServer"] ?? string.Empty);
    });
    
    builder.Services.AddHangfire(config =>
    {
        config.UseMemoryStorage();
    });

    builder.Services.AddHangfireServer();
    
    builder.Services.AddRefitClient<IFileSharingApi>()
        .ConfigureHttpClient(client => client.BaseAddress = new Uri(builder.Configuration["Urls:GatewayBaseAddress"] ?? ""));
    
    builder.Services.AddRefitClient<IAuthApi>()
        .ConfigureHttpClient(client => client.BaseAddress = new Uri(builder.Configuration["Urls:AuthGatewayBaseAddress"] ?? ""));

    builder.Services.AddControllers();

    builder.Services.AddHealthChecks();

    var app = builder.Build();

    app.MapControllers();

    app.MapHealthChecks("/api/health");

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Host terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}