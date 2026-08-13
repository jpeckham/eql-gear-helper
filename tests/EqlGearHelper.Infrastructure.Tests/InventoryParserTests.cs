using System.Text;
using EqlGearHelper.Application;
using EqlGearHelper.Infrastructure.Import;
using EqlGearHelper.Infrastructure.Sqlite;
using Microsoft.Data.Sqlite;

namespace EqlGearHelper.Infrastructure.Tests;

public sealed class InventoryParserTests
{
    [Fact]
    public void Parse_PreservesEveryRowNestedPathsAndDistinctDuplicateCopies()
    {
        var snapshot = new InventoryParser().Parse(FixtureStream());

        Assert.Equal(13, snapshot.Rows.Count);
        var transferredSocket = Assert.Single(snapshot.Sockets, socket => socket.Path == "Bank12-Slot2-Slot10");
        Assert.Equal("Bank12-Slot2", transferredSocket.HostPath);

        var swords = snapshot.Items.Where(item => item.Name == "Short Sword of the Ykesha +4").ToArray();
        Assert.Equal(2, swords.Length);
        Assert.NotEqual(swords[0].InstanceId, swords[1].InstanceId);
        Assert.Equal("Bank12-Slot1", swords[0].Path);
        Assert.Equal("Bank12-Slot2", swords[1].Path);

        var emptySwordSocket = Assert.Single(snapshot.Sockets, socket => socket.Path == "Bank12-Slot1-Slot10");
        Assert.Equal("5500", emptySwordSocket.HostItemId);
        Assert.Equal("0", emptySwordSocket.SocketItemId);
        Assert.False(emptySwordSocket.IsExaltation);
        Assert.Equal(MappingStatus.Empty, emptySwordSocket.MappingStatus);

        Assert.Equal(MappingStatus.Empty, Assert.Single(snapshot.Rows, row => row.Path == "Bank12-Slot1-Slot10").MappingStatus);
        Assert.Equal(MappingStatus.ExaltationCandidate, Assert.Single(snapshot.Rows, row => row.Path == "Bank12-Slot2-Slot10").MappingStatus);
    }

    [Fact]
    public void Parse_RecordsNativeAndTransferredExaltationsAndUnavailableStorage()
    {
        var snapshot = new InventoryParser().Parse(FixtureStream());

        var native = Assert.Single(snapshot.Sockets, socket => socket.Path == "Face-Slot7");
        Assert.True(native.IsExaltation);
        Assert.Equal("4505", native.HostItemId);
        Assert.Equal("4505", native.SocketItemId);
        Assert.False(native.IsTransferred);

        var transferred = Assert.Single(snapshot.Sockets, socket => socket.Path == "Bank9-Slot5-Slot8");
        Assert.True(transferred.IsExaltation);
        Assert.Equal("1883", transferred.HostItemId);
        Assert.Equal("177708", transferred.SocketItemId);
        Assert.True(transferred.IsTransferred);

        Assert.Equal(["Dragon Hoard", "Exaltation Storage", "Item Storage"], snapshot.Storage.Select(storage => storage.Name).Order());
        Assert.All(snapshot.Storage, storage => Assert.Equal(StorageAvailability.Unavailable, storage.Availability));
    }

    [Fact]
    public async Task Import_WhenParsingFails_PreservesExistingCompleteSnapshot()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"eql-gear-helper-{Guid.NewGuid():N}.db");
        try
        {
            var initializer = new DatabaseInitializer($"Data Source={databasePath};Pooling=False");
            await initializer.InitializeAsync();
            var repository = new CollectionRepository(initializer.ConnectionString);
            var useCase = new ImportInventorySnapshotUseCase(new InventoryParser(), repository);

            await useCase.ExecuteAsync(FixtureStream(), CancellationToken.None);
            var original = await repository.GetCurrentAsync(CancellationToken.None);

            await Assert.ThrowsAsync<FormatException>(() => useCase.ExecuteAsync(BadFixtureStream(), CancellationToken.None));

            var current = await repository.GetCurrentAsync(CancellationToken.None);
            Assert.NotNull(original);
            Assert.NotNull(current);
            Assert.Equal(original!.SnapshotId, current!.SnapshotId);
            Assert.Equal(original.Rows.Count, current.Rows.Count);
        }
        finally
        {
            if (File.Exists(databasePath)) File.Delete(databasePath);
        }
    }

    [Fact]
    public async Task ReplaceWithAsync_WhenReplacementFailsAfterDelete_RollsBackAndLeavesNoOrphanedRows()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"eql-gear-helper-{Guid.NewGuid():N}.db");
        var connectionString = $"Data Source={databasePath};Pooling=False";
        try
        {
            var initializer = new DatabaseInitializer(connectionString);
            await initializer.InitializeAsync();
            var stableRepository = new CollectionRepository(connectionString);
            await stableRepository.ReplaceWithAsync(new InventoryParser().Parse(FixtureStream()), CancellationToken.None);
            var original = await stableRepository.GetCurrentAsync(CancellationToken.None);

            var failingRepository = new CollectionRepository(connectionString, () => throw new InvalidOperationException("Injected replacement failure."));
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                failingRepository.ReplaceWithAsync(new InventoryParser().Parse(ReplacementFixtureStream()), CancellationToken.None));

            var current = await stableRepository.GetCurrentAsync(CancellationToken.None);
            Assert.NotNull(original);
            Assert.NotNull(current);
            Assert.Equal(original!.SnapshotId, current!.SnapshotId);

            await using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM inventory_rows WHERE snapshot_id NOT IN (SELECT snapshot_id FROM inventory_snapshots);";
            Assert.Equal(0L, (long)(await command.ExecuteScalarAsync())!);
        }
        finally
        {
            if (File.Exists(databasePath)) File.Delete(databasePath);
        }
    }

    [Fact]
    public async Task ReplaceWithAsync_ReplacesPriorSnapshotWithoutOrphanedSubordinateRows()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"eql-gear-helper-{Guid.NewGuid():N}.db");
        var connectionString = $"Data Source={databasePath};Pooling=False";
        try
        {
            var initializer = new DatabaseInitializer(connectionString);
            await initializer.InitializeAsync();
            var repository = new CollectionRepository(connectionString);
            await repository.ReplaceWithAsync(new InventoryParser().Parse(FixtureStream()), CancellationToken.None);
            await repository.ReplaceWithAsync(new InventoryParser().Parse(ReplacementFixtureStream()), CancellationToken.None);

            await using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM inventory_rows WHERE snapshot_id NOT IN (SELECT snapshot_id FROM inventory_snapshots);";
            Assert.Equal(0L, (long)(await command.ExecuteScalarAsync())!);
            command.CommandText = "SELECT COUNT(*) FROM inventory_rows;";
            Assert.Equal(1L, (long)(await command.ExecuteScalarAsync())!);
        }
        finally
        {
            if (File.Exists(databasePath)) File.Delete(databasePath);
        }
    }

    private static Stream FixtureStream() => new MemoryStream(Encoding.UTF8.GetBytes("""
        Location\tName\tID\tCount\tSlots
        Face\tPolished Mithril Mask +4\t4505\t1\t10
        Face-Slot7\tPolished Mithril Mask (Exaltation)\t4505\t1\t10
        General 1\tBackpack\t17005\t1\t8
        General 1-Slot7\tWand of Conflagration +4\t12500\t1\t10
        General 1-Slot7-Slot9\tEmpty\t0\t0\t0
        Bank12\tBackpack\t17005\t1\t8
        Bank12-Slot1\tShort Sword of the Ykesha +4\t5500\t1\t10
        Bank12-Slot1-Slot10\tEmpty\t0\t0\t0
        Bank12-Slot2\tShort Sword of the Ykesha +4\t5500\t1\t10
        Bank12-Slot2-Slot10\tShimmering Ruby Stiletto (Exaltation)\t5820\t1\t10
        Bank9\tBackpack\t17005\t1\t8
        Bank9-Slot5\tPristine Studded Leather Boots +4\t1883\t1\t10
        Bank9-Slot5-Slot8\tBoots of the Long Road (Exaltation)\t177708\t1\t10
        """.Replace("\\t", "\t")));

    private static Stream BadFixtureStream() => new MemoryStream(Encoding.UTF8.GetBytes("""
        Location\tName\tID\tCount\tSlots
        Face\tPolished Mithril Mask +4\t4505\tnot-a-number\t10
        """.Replace("\\t", "\t")));

    private static Stream ReplacementFixtureStream() => new MemoryStream(Encoding.UTF8.GetBytes("""
        Location\tName\tID\tCount\tSlots
        Face\tReplacement Mask +1\t9999\t1\t10
        """.Replace("\\t", "\t")));
}
