using System.Diagnostics;
using BotIntegration.Shared;
using Microsoft.Extensions.Configuration;
using Refit;
using WAVFileUploader.Properties;

namespace WAVFileUploader;

public partial class Form1 : Form
{
    private readonly IConfiguration _configuration;
    private readonly IFileSharingApi _fileSharingApi;
    private readonly IAuthApi _authApi;
    private readonly IVersionEntryApi _versionEntryApi;
    private NotifyIcon? _trayIcon;
    private ContextMenuStrip? _trayMenu;
    private FileSystemWatcher? _fileWatcher;
    private const string ClientId = "Configuration:ClientId";
    private const string ClientSecret = "Configuration:ClientSecret";
    private const string? MultipartFormData = "multipart/form-data";
    
    public Form1(IConfiguration configuration, IFileSharingApi fileSharingApi,
        IAuthApi authApi, IVersionEntryApi versionEntryApi)
    {
        _configuration = configuration;
        _fileSharingApi = fileSharingApi;
        _authApi = authApi;
        _versionEntryApi = versionEntryApi;
        InitializeComponent();
        InitializeSystemTrayIcon();
        InitializeFileWatcher();
    }

    private async void OnCheckNewVersion(object? sender, EventArgs e)
    {
        try
        {
            var data = new Dictionary<string, string>
            {
                { IAuthApi.GrantType, IAuthApi.ClientCredentials },
                { IAuthApi.ClientId, _configuration[ClientId] ?? "" },
                { IAuthApi.ClientSecret, _configuration[ClientSecret] ?? "" }
            };

            var authData = await _authApi.GetAuthData(data);
            var versionResponse = await _versionEntryApi.GetCurrentVersion($"Bearer {authData.AccessToken}", Resources.AppName);
            
            // Get the current assembly version
            var currentVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);

            // Create a Version object for the remote version
            var remoteVersion = new Version(versionResponse.MajorVersion, versionResponse.MinorVersion, versionResponse.PatchVersion);

            if (!IsNewVersionAvailable(currentVersion, remoteVersion))
            {
                MessageBox.Show($"You are using the latest version ({currentVersion}).", $"{Resources.AppName} - No Update Available", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var result = MessageBox.Show(
                $"A new version ({remoteVersion}) is available. Do you want to update now?", 
                $"{Resources.AppName} - Update Available", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

            if (result != DialogResult.Yes)
            {
                return;
            }

            var updaterPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "WAVFileUploaderUpdater.exe");
            var gatewayBaseAddress = _configuration["Urls:GatewayBaseAddress"] ?? "";
            var authGatewayBaseAddress = _configuration["Urls:AuthGatewayBaseAddress"] ?? "";
            var mainAppPath = Application.ExecutablePath;

            var startInfo = new ProcessStartInfo
            {
                FileName = updaterPath,
                Arguments =
                    $"\"{_configuration[ClientId]}\" \"{_configuration[ClientSecret]}\" \"{mainAppPath}\" \"{gatewayBaseAddress}\" \"{authGatewayBaseAddress}\"",
                UseShellExecute = true,
            };

            try
            {
                Process.Start(startInfo);
                Application.Exit();
            }
            catch (Exception ex)
            {
                ShowNotification("Update Error", $"Error starting the updater: {ex.Message}", ToolTipIcon.Error);
            }
        }
        catch
        {
            // ignored
        }
    }
    
    private static bool IsNewVersionAvailable(Version currentVersion, Version remoteVersion)
    {
        return remoteVersion > currentVersion;
    }

    private void InitializeSystemTrayIcon()
    {
        _trayMenu = new ContextMenuStrip();
        _trayMenu.Items.Add("Check for updates", null, OnCheckNewVersion);
        _trayMenu.Items.Add("Close", Resources.CloseIcon, OnClose);
        
        _trayIcon = new NotifyIcon
        {
            Text = Resources.AppName,
            Icon = IconFromImage(Resources.TrayIcon),
            ContextMenuStrip = _trayMenu,
            Visible = true
        };
    }

    private static Icon IconFromImage(Bitmap image)
    {
        using var resizedImage = new Bitmap(image, new Size(32, 32));
        return Icon.FromHandle(resizedImage.GetHicon());
    }

    private void InitializeFileWatcher()
    {
        var watcherPath = _configuration.GetSection("WatcherSettings:Path").Value ?? "";
        var fileExtension = _configuration.GetSection("WatcherSettings:FileExtension").Value ?? "*.wav";
        _fileWatcher = new FileSystemWatcher(watcherPath)
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
            Filter = fileExtension
        };
        _fileWatcher.Renamed += OnWavFileRenamed;
        _fileWatcher.EnableRaisingEvents = true;
    }

    private void OnWavFileRenamed(object sender, RenamedEventArgs e)
    {
        HandleWavFile(e.FullPath, "renamed").GetAwaiter().GetResult();
    }

    private async Task HandleWavFile(string filePath,
        string _)
    {
        try
        {
            // Show a notification that the upload process has started
            ShowNotification("Upload Started", "File upload process has begun.", ToolTipIcon.Info, 1500);
            
            var data = new Dictionary<string, string>
            {
                { IAuthApi.GrantType, IAuthApi.ClientCredentials },
                { IAuthApi.ClientId, _configuration[ClientId] ?? "" },
                { IAuthApi.ClientSecret, _configuration[ClientSecret] ?? "" }
            };

            var authData = await _authApi.GetAuthData(data);

            var fileStream = File.OpenRead(filePath);
            var streamPart = new StreamPart(fileStream, Path.GetFileName(filePath), MultipartFormData);

            var uploadData = await _fileSharingApi.UploadFile($"Bearer {authData.AccessToken}", streamPart);
            var url = uploadData.FileUrl;

            // Use a thread-safe method to set the clipboard text
            Thread thread = new(() => Clipboard.SetText(url));
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();

            // Show a tooltip notification for successful upload
            ShowNotification("Upload Complete", "File uploaded successfully. URL copied to clipboard.", ToolTipIcon.Info);
        }
        catch (Exception ex)
        {
            // Show error notification using balloon tip
            ShowNotification("Error", $"An error occurred: {ex.Message}", ToolTipIcon.Error);
        }
    }
    
    private void ShowNotification(string title, string message, ToolTipIcon icon, int timeout = 3000)
    {
        _trayIcon?.ShowBalloonTip(timeout, title, message, icon);
    }

    private static void OnClose(object? sender, EventArgs e)
    {
        Application.Exit();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        base.OnFormClosing(e);
        _trayIcon?.Dispose();
    }
}