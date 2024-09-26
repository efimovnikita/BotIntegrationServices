using Serilog;

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

    builder.Services.AddControllers();

    builder.Services.AddHealthChecks();

    var app = builder.Build();

    app.MapControllers();

    app.MapHealthChecks("/api/health");
    
    var exitCode = Microsoft.Playwright.Program.Main(["install"]);
    if (exitCode != 0)
    {
        throw new Exception($"Playwright exited with code {exitCode}");
    }

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