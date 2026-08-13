using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using EqlGearHelper.Application;
using EqlGearHelper.Domain;
using EqlGearHelper.Infrastructure.Backup;
using EqlGearHelper.Infrastructure.Sqlite;
using Microsoft.Data.Sqlite;

namespace EqlGearHelper.Infrastructure.Tests;

public sealed class CollectionBackupServiceTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "EqlGearHelper", Guid.NewGuid().ToString("N"));
    private readonly string _databasePath;
    private readonly string _connectionString;

    public CollectionBackupServiceTests()
    {
        Directory.CreateDirectory(_directory);
        _databasePath = Path.Combine(_directory, "collection.sqlite");
        _connectionString = $"Data Source={_databasePath}";
    }

    [Fact]
    public async Task RecoverAsync_ValidatesEmbeddedCatalogRulesetSnapshotAndHashBeforeAtomicReplacement()
    {
        var initializer = new DatabaseInitializer(_connectionString);
        await initializer.InitializeAsync();
        var catalogRepository = new CatalogRepository(_connectionString);
        var snapshotRepository = new CollectionRepository(_connectionString);
        var catalog = Catalog("catalog-1", "rules-1");
        await catalogRepository.ReplaceAsync(catalog, CancellationToken.None);
        await snapshotRepository.ReplaceWithAsync(Snapshot(), CancellationToken.None);
        var snapshot = (await snapshotRepository.GetCurrentAsync(CancellationToken.None))!;
        var backup = new CollectionBackupService(_databasePath);
        var expected = await backup.CreateAsync(Path.Combine(_directory, "valid.zip"), catalog, snapshot, CancellationToken.None);

        await catalogRepository.ReplaceAsync(Catalog("catalog-2", "rules-1"), CancellationToken.None);
        var tampered = Path.Combine(_directory, "tampered.zip");
        await WriteArchiveAsync(tampered, expected, _databasePath);

        await Assert.ThrowsAsync<InvalidDataException>(() => backup.RecoverAsync(tampered, expected, CancellationToken.None));
        Assert.Equal("catalog-2", await ReadCatalogVersionAsync());
    }

    [Fact]
    public async Task RecoverAsync_RestoresValidatedBackupOnlyAfterItsIdentityMatches()
    {
        var initializer = new DatabaseInitializer(_connectionString);
        await initializer.InitializeAsync();
        var catalogRepository = new CatalogRepository(_connectionString);
        var snapshotRepository = new CollectionRepository(_connectionString);
        var catalog = Catalog("catalog-1", "rules-1");
        await catalogRepository.ReplaceAsync(catalog, CancellationToken.None);
        await snapshotRepository.ReplaceWithAsync(Snapshot(), CancellationToken.None);
        var snapshot = (await snapshotRepository.GetCurrentAsync(CancellationToken.None))!;
        var backup = new CollectionBackupService(_databasePath);
        var path = Path.Combine(_directory, "valid.zip");
        var expected = await backup.CreateAsync(path, catalog, snapshot, CancellationToken.None);

        await catalogRepository.ReplaceAsync(Catalog("catalog-2", "rules-1"), CancellationToken.None);
        await backup.RecoverAsync(path, expected, CancellationToken.None);

        Assert.Equal("catalog-1", await ReadCatalogVersionAsync());
        Assert.Equal(snapshot.SnapshotId, await ReadSnapshotIdAsync());
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }

    private static CatalogPackage Catalog(string catalogVersion, string rulesetVersion) =>
        new(catalogVersion, rulesetVersion, [new CatalogItem("helm", "Helm", ClassSet.Of("Bard"), [new EquipmentPosition(SlotType.Head)])], []);

    private static InventorySnapshotDraft Snapshot() => new(
        [new RawInventoryRow(1, "Carried/Helm", "Helm", "helm", 1, 0, (MappingStatus)0, "Helm")], [], [], []);

    private static async Task WriteArchiveAsync(string path, CollectionBackupManifest manifest, string databasePath)
    {
        await using var hashStream = new FileStream(databasePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 4096, FileOptions.Asynchronous);
        var databaseHash = Convert.ToHexString(await SHA256.HashDataAsync(hashStream));
        var tamperedManifest = manifest with { DatabaseHash = databaseHash };
        await using var file = File.Create(path);
        using var archive = new ZipArchive(file, ZipArchiveMode.Create, leaveOpen: false);
        var databaseEntry = archive.CreateEntry("collection.sqlite");
        await using (var source = new FileStream(databasePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 4096, FileOptions.Asynchronous))
        await using (var destination = databaseEntry.Open())
            await source.CopyToAsync(destination);
        var manifestEntry = archive.CreateEntry("manifest.json");
        await using var manifestStream = manifestEntry.Open();
        await JsonSerializer.SerializeAsync(manifestStream, tamperedManifest);
    }

    private async Task<string> ReadCatalogVersionAsync()
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT json_extract(package_json, '$.CatalogVersion') FROM catalog_packages WHERE id = 1;";
        return (string)(await command.ExecuteScalarAsync())!;
    }

    private async Task<Guid> ReadSnapshotIdAsync()
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT snapshot_id FROM inventory_snapshots;";
        return Guid.Parse((string)(await command.ExecuteScalarAsync())!);
    }
}
