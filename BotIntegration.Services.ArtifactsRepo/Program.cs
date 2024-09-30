using BotIntegration.Services.ArtifactsRepo.Models;
using BotIntegration.Services.ArtifactsRepo.Services;
using Serilog;

try
{
    var builder = WebApplication.CreateBuilder(args);
    
    builder.WebHost.ConfigureKestrel(serverOptions =>
    {
        serverOptions.Limits.MaxRequestBodySize = null;
    });
    
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

    builder.Services.AddControllers();
    
    builder.Services.Configure<DatabaseSettings>(
        builder.Configuration.GetSection("Database"));
    
    builder.Services.AddSingleton<DatabaseService>();

    var app = builder.Build();
    app.MapControllers();

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