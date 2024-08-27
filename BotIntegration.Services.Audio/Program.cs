using Hangfire;
using Hangfire.MemoryStorage;
using Serilog;

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.WebHost.ConfigureKestrel(serverOptions =>
    {
        serverOptions.Limits.MaxRequestBodySize = 167_772_160;
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
    
    builder.Services.AddHangfire(config =>
    {
        config.UseMemoryStorage();
    });

    builder.Services.AddHangfireServer();
    
    builder.Services.AddControllers();

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