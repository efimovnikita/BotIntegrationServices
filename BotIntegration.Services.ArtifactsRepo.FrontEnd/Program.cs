using Blazored.LocalStorage;
using BotIntegration.Services.ArtifactsRepo.FrontEnd.Components;
using BotIntegration.Shared;
using Serilog;
using Refit;

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
    
    builder.Services.AddRazorComponents()
        .AddInteractiveServerComponents();

    builder.Services.AddBlazoredLocalStorage();

    builder.Services.AddRefitClient<IVersionEntryApi>()
        .ConfigureHttpClient(client =>
        {
            client.BaseAddress = new Uri(builder.Configuration["Urls:GatewayBaseAddress"] ?? "");
            client.Timeout = TimeSpan.FromMinutes(5);
        });

    builder.Services.AddRefitClient<IAuthApi>()
        .ConfigureHttpClient(client =>
            client.BaseAddress = new Uri(builder.Configuration["Urls:AuthGatewayBaseAddress"] ?? ""));
    
    var app = builder.Build();

    if (!app.Environment.IsDevelopment())
    {
        app.UseExceptionHandler("/Error", createScopeForErrors: true);
        app.UseHsts();
    }

    app.UseStaticFiles();
    app.UseAntiforgery();

    app.MapRazorComponents<App>()
        .AddInteractiveServerRenderMode();

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