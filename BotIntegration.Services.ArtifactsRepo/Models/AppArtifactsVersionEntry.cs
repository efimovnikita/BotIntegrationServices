using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace BotIntegration.Services.ArtifactsRepo.Models;

public class AppArtifactsVersionEntry
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string EntryId { get; set; }

    // Name of the app
    public string AppName { get; set; }

    // Major version number
    public int MajorVersion { get; set; }

    // Minor version number
    public int MinorVersion { get; set; }

    // Patch version number
    public int PatchVersion { get; set; }

    // Date when the version was created
    public DateTime Date { get; set; }

    // Notes about the version (changes, fixes, etc.)
    public string Notes { get; set; }
    
    // Reference to the artifacts zip archive in the artifacts GridFS collection
    [BsonRepresentation(BsonType.ObjectId)]
    public string ArtifactsArchiveId { get; set; }
}