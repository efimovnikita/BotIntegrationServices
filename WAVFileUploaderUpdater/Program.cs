using System.Diagnostics;
using BotIntegration.Shared;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Refit;

namespace WAVFileUploaderUpdater;

class Program
{
    static async Task Main(string[] args)
    {
        try
        {
            if (args.Length != 5)
            {
                Console.WriteLine("Error: Please provide exactly 5 arguments.");
                return;
            }

            var clientId = args[0];
            var clientSecret = args[1];
            var mainAppPath = args[2];
            var gatewayBaseAddress = args[3];
            var authGatewayBaseAddress = args[4];

            await Task.Delay(5000);
        
            var host = Host.CreateDefaultBuilder()
                .ConfigureServices((context, services) =>
                {
                    services.AddRefitClient<IVersionEntryApi>()
                        .ConfigureHttpClient(client =>
                        {
                            client.BaseAddress = new Uri(gatewayBaseAddress);
                            client.Timeout = TimeSpan.FromMinutes(5);
                        });

                    services.AddRefitClient<IAuthApi>()
                        .ConfigureHttpClient(client =>
                            client.BaseAddress = new Uri(authGatewayBaseAddress));
                })
                .Build();

            var updater = new Updater(clientId, clientSecret, mainAppPath, host.Services.GetRequiredService<IVersionEntryApi>(),
                host.Services.GetRequiredService<IAuthApi>());

            await updater.Update();
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
        }
    }
}

public class Updater(
    string clientId,
    string clientSecret,
    string mainAppPath,
    IVersionEntryApi versionEntryApi,
    IAuthApi authApi)
{
    public async Task Update()
    {
        var data = new Dictionary<string, string>
        {
            { IAuthApi.GrantType, IAuthApi.ClientCredentials },
            { IAuthApi.ClientId, clientId },
            { IAuthApi.ClientSecret, clientSecret }
        };

        Console.WriteLine("Getting auth data...");
        var authData = await authApi.GetAuthData(data);

        Console.WriteLine("Getting latest version archive...");
        var latestVersionArchive = await versionEntryApi.GetLatestVersionArchive($"Bearer {authData.AccessToken}",
            Path.GetFileNameWithoutExtension(mainAppPath));

        Console.WriteLine("Downloading latest version archive...");
        var tempFolderPath = Path.GetTempPath();
        var tempFileName = Path.ChangeExtension(Path.GetRandomFileName(), ".zip");
        var tempFilePath = Path.Combine(tempFolderPath, tempFileName);

        Console.WriteLine("Saving latest version archive...");
        await using (var fileStream = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            await latestVersionArchive.CopyToAsync(fileStream);
        }

        Console.WriteLine("Deleting old version...");
        if (File.Exists(mainAppPath))
        {
            File.Delete(mainAppPath);
        }

        var extractPath = Path.GetDirectoryName(mainAppPath);
        if (extractPath == null)
        {
            throw new InvalidOperationException("The main application path is invalid.");
        }

        Console.WriteLine("Extracting new version...");
        System.IO.Compression.ZipFile.ExtractToDirectory(tempFilePath, extractPath, true);

        Console.WriteLine("Cleaning up...");
        File.Delete(tempFilePath);

        Console.WriteLine("Starting new version...");
        var startInfo = new ProcessStartInfo
        {
            FileName = mainAppPath,
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(mainAppPath)
        };

        try
        {
            var process = Process.Start(startInfo);
            if (process == null)
            {
                Console.WriteLine("Error: Process.Start returned null. The application failed to start.");
            }
            else
            {
                Console.WriteLine($"Application started with process ID: {process.Id}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error starting the application: {ex.Message}");
            Console.WriteLine($"Stack trace: {ex.StackTrace}");
        }

        Console.WriteLine("Update process completed.");
    }
}