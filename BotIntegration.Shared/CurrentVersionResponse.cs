namespace BotIntegration.Shared;

public class CurrentVersionResponse
{
    public string AppName { get; set; }
    public int MajorVersion { get; set; }
    public int MinorVersion { get; set; }
    public int PatchVersion { get; set; }
    public DateTime Date { get; set; }
    public string Notes { get; set; }
}