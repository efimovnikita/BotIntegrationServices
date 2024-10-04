using Serilog;
using Microsoft.Extensions.FileProviders;

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

    var app = builder.Build();

    var uploadsPath = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads"));

    app.UseDirectoryBrowser(new DirectoryBrowserOptions
    {
        FileProvider = new PhysicalFileProvider(uploadsPath),
        RequestPath = "/uploads"
    });

    app.UseStaticFiles();
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