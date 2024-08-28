using CliWrap;
using Microsoft.AspNetCore.Mvc;

namespace BotIntegration.Services.Audio.Controllers;

public static class Shared
{
    public const string JobResultParameterName = "TranslationResult";
    public const string JobExceptionParameterName = "ExceptionMessage";
    public const int LengthLimit = 167_772_160;
    public const int MaxSizeInMbs = 160;
    public const double MaxEncodeLimit = 24.5;
    public const string ModelName = "whisper-1";
    public static TimeSpan Timeout = TimeSpan.FromMinutes(10);
    public const string? AllowedExtension = ".mp3";
    
    public static async Task<string> CompressHugeFile(string filePath, ILogger<ControllerBase> log)
    {
        var uniqueFileNameForOutput = Guid.NewGuid() + ".ogg";

        var result = await Cli.Wrap("ffmpeg")
            .WithArguments([
                "-i", $"{filePath}", "-vn", "-map_metadata", "-1", "-ac", "1", "-c:a", "libopus", "-b:a", "12k",
                "-application", "voip", $"{uniqueFileNameForOutput}"
            ])
            .WithWorkingDirectory(Path.GetTempPath())
            .ExecuteAsync();

        log.LogInformation("Encoding result: {Result}", result.ExitCode);

        if (result.ExitCode != 0)
        {
            log.LogWarning("File encoding result was unsuccessful");
            return "";
        }

        var encodedFilePath = Path.Combine(Path.GetTempPath(), uniqueFileNameForOutput);
        if (File.Exists(encodedFilePath) == false)
        {
            log.LogWarning("Error getting the encoded file");
            return "";
        }

        var encodedFileInfo = new FileInfo(encodedFilePath);
        var encodedFileLengthInMbs = (double)encodedFileInfo.Length / (1024 * 1024);
        if (encodedFileLengthInMbs >= 24.5)
        {
            log.LogWarning("Encoded file is too big");
            return "";
        }

        return encodedFilePath;
    }
}