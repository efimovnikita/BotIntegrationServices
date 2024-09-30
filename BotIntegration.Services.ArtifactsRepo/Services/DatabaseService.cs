using BotIntegration.Services.ArtifactsRepo.Models;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using MongoDB.Driver.GridFS;

namespace BotIntegration.Services.ArtifactsRepo.Services;

public class DatabaseService
{
    private readonly IMongoCollection<AppArtifactsVersionEntry> _versionEntries;
    private readonly IGridFSBucket _gridFsBucket;

    public DatabaseService(IOptions<DatabaseSettings> databaseSettings)
    {
        var mongoClient = new MongoClient(databaseSettings.Value.ConnectionString);
        var mongoDatabase = mongoClient.GetDatabase(databaseSettings.Value.DatabaseName);
        _versionEntries = mongoDatabase.GetCollection<AppArtifactsVersionEntry>("VersionEntries");
        _gridFsBucket = new GridFSBucket(mongoDatabase);
    }

    public async Task<AppArtifactsVersionEntry> CreateVersionEntryAsync(AppArtifactsVersionEntry versionEntry)
    {
        await _versionEntries.InsertOneAsync(versionEntry);
        return versionEntry;
    }

    public async Task<string> StoreZipArchiveAsync(string fileName, Stream zipStream)
    {
        var options = new GridFSUploadOptions
        {
            Metadata = new MongoDB.Bson.BsonDocument("contentType", "application/zip"),
            ChunkSizeBytes = 1048576 // 1 MB chunks
        };

        var fileId = await _gridFsBucket.UploadFromStreamAsync(fileName, zipStream, options);
        return fileId.ToString();
    }
    
    public async Task<AppArtifactsVersionEntry?> GetVersionEntryAsync(string appName, int majorVersion, int minorVersion, int patchVersion)
    {
        var filter = Builders<AppArtifactsVersionEntry>.Filter.And(
            Builders<AppArtifactsVersionEntry>.Filter.Eq(e => e.AppName, appName),
            Builders<AppArtifactsVersionEntry>.Filter.Eq(e => e.MajorVersion, majorVersion),
            Builders<AppArtifactsVersionEntry>.Filter.Eq(e => e.MinorVersion, minorVersion),
            Builders<AppArtifactsVersionEntry>.Filter.Eq(e => e.PatchVersion, patchVersion)
        );

        return await _versionEntries.Find(filter).FirstOrDefaultAsync();
    }
    
    public async Task<AppArtifactsVersionEntry?> GetVersionEntryByIdAsync(string id)
    {
        var filter = Builders<AppArtifactsVersionEntry>.Filter.Eq(e => e.EntryId, id);
        return await _versionEntries.Find(filter).FirstOrDefaultAsync();
    }

    public async Task<(string FileName, Stream ArchiveStream)?> GetZipArchiveAsync(string archiveId)
    {
        try
        {
            var fileInfo = await _gridFsBucket.FindAsync(new MongoDB.Bson.BsonDocument("_id", new MongoDB.Bson.ObjectId(archiveId)));
            var file = await fileInfo.FirstOrDefaultAsync();

            if (file == null)
            {
                return null;
            }

            var stream = await _gridFsBucket.OpenDownloadStreamAsync(new MongoDB.Bson.ObjectId(archiveId));
            return (file.Filename, stream);
        }
        catch (Exception)
        {
            return null;
        }
    }
    
    public async Task<AppArtifactsVersionEntry?> GetLatestVersionEntryAsync(string appName)
    {
        var filter = Builders<AppArtifactsVersionEntry>.Filter.Eq(e => e.AppName, appName);
        var sort = Builders<AppArtifactsVersionEntry>.Sort
            .Descending(e => e.MajorVersion)
            .Descending(e => e.MinorVersion)
            .Descending(e => e.PatchVersion);

        return await _versionEntries.Find(filter)
            .Sort(sort)
            .FirstOrDefaultAsync();
    }
    
    public async Task<List<AppArtifactsVersionEntry>> GetAllVersionEntriesAsync()
    {
        return await _versionEntries.Find(_ => true)
            .Sort(Builders<AppArtifactsVersionEntry>.Sort
                .Ascending(e => e.AppName)
                .Descending(e => e.MajorVersion)
                .Descending(e => e.MinorVersion)
                .Descending(e => e.PatchVersion))
            .ToListAsync();
    }
}