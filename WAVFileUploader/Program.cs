using BotIntegration.Shared;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Refit;

namespace WAVFileUploader;

static class Program
{
    /// <summary>
    ///  The main entry point for the application.
    /// </summary>
    [STAThread]
    static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        var host = Host.CreateDefaultBuilder()
            .ConfigureAppConfiguration((_, config) =>
            {
                config.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
            })
            .ConfigureServices((context, services) =>
            {
                services.AddSingleton<Form1>();

                services.AddRefitClient<IFileSharingApi>()
                    .ConfigureHttpClient(client =>
                    {
                        client.BaseAddress = new Uri(context.Configuration["Urls:GatewayBaseAddress"] ?? "");
                        client.Timeout = TimeSpan.FromMinutes(5);
                    });
                
                services.AddRefitClient<IVersionEntryApi>()
                    .ConfigureHttpClient(client =>
                    {
                        client.BaseAddress = new Uri(context.Configuration["Urls:GatewayBaseAddress"] ?? "");
                        client.Timeout = TimeSpan.FromMinutes(5);
                    });

                services.AddRefitClient<IAuthApi>()
                    .ConfigureHttpClient(client =>
                        client.BaseAddress = new Uri(context.Configuration["Urls:AuthGatewayBaseAddress"] ?? ""));
            })
            .Build();

        var form = host.Services.GetRequiredService<Form1>();
        form.WindowState = FormWindowState.Minimized;
        form.ShowInTaskbar = false;

        Application.Run();
    }
}