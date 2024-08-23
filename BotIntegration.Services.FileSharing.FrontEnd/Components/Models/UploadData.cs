using System.Text.Json.Serialization;

namespace BotIntegration.Services.FileSharing.FrontEnd.Components.Models;

public class UploadData
{
    [JsonPropertyName("fileUrl")]
    public string FileUrl { get; set; }
}