using System.Text.Json.Serialization;

namespace BotIntegration.Services.YouTube.Models;

public class UploadData
{
    [JsonPropertyName("fileUrl")]
    public string FileUrl { get; set; }
}