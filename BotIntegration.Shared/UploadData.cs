using System.Text.Json.Serialization;

namespace BotIntegration.Shared;

public class UploadData
{
    [JsonPropertyName("fileUrl")]
    public string FileUrl { get; set; }
}