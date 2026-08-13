using System.Text.Json;
using EqlGearHelper.Application;
using Microsoft.Data.Sqlite;

namespace EqlGearHelper.Infrastructure.Sqlite;

public sealed class CollectionRepository(string connectionString, Action? afterExistingSnapshotDeleted = null) : IInventorySnapshotRepository
{
    public async Task ReplaceWithAsync(InventorySnapshotDraft snapshot, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        snapshot.Validate();
        var snapshotId = Guid.NewGuid();
        var importedAt = DateTimeOffset.UtcNow;
        await using var connection = await OpenConnectionAsync(connectionString, token);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(token);
        try
        {
            await ExecuteAsync(connection, transaction, "DELETE FROM inventory_snapshots;", token);
            afterExistingSnapshotDeleted?.Invoke();
            await ExecuteAsync(connection, transaction, "INSERT INTO inventory_snapshots (snapshot_id, imported_at_utc) VALUES ($id, $at);", token,
                ("$id", snapshotId.ToString("D")), ("$at", importedAt.ToString("O")));
            foreach (var row in snapshot.Rows)
            {
                await ExecuteAsync(connection, transaction, "INSERT INTO inventory_rows (snapshot_id, row_number, path, mapping_status, row_json) VALUES ($id, $row, $path, $status, $json);", token,
                    ("$id", snapshotId.ToString("D")), ("$row", row.RowNumber), ("$path", row.Path), ("$status", (int)row.MappingStatus), ("$json", JsonSerializer.Serialize(row)));
            }
            foreach (var item in snapshot.Items)
            {
                await ExecuteAsync(connection, transaction, "INSERT INTO inventory_items (snapshot_id, path, item_json) VALUES ($id, $path, $json);", token,
                    ("$id", snapshotId.ToString("D")), ("$path", item.Path), ("$json", JsonSerializer.Serialize(item)));
            }
            foreach (var socket in snapshot.Sockets)
            {
                await ExecuteAsync(connection, transaction, "INSERT INTO inventory_sockets (snapshot_id, path, socket_json) VALUES ($id, $path, $json);", token,
                    ("$id", snapshotId.ToString("D")), ("$path", socket.Path), ("$json", JsonSerializer.Serialize(socket)));
            }
            foreach (var storage in snapshot.Storage)
            {
                await ExecuteAsync(connection, transaction, "INSERT INTO inventory_storage (snapshot_id, name, availability, storage_json) VALUES ($id, $name, $availability, $json);", token,
                    ("$id", snapshotId.ToString("D")), ("$name", storage.Name), ("$availability", (int)storage.Availability), ("$json", JsonSerializer.Serialize(storage)));
            }
            await transaction.CommitAsync(token);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    public async Task<InventorySnapshot?> GetCurrentAsync(CancellationToken token)
    {
        await using var connection = await OpenConnectionAsync(connectionString, token);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT snapshot_id, imported_at_utc FROM inventory_snapshots LIMIT 1;";
        await using var reader = await command.ExecuteReaderAsync(token);
        if (!await reader.ReadAsync(token)) return null;
        var snapshotId = Guid.Parse(reader.GetString(0));
        var importedAt = DateTimeOffset.Parse(reader.GetString(1), null, System.Globalization.DateTimeStyles.RoundtripKind);
        return new InventorySnapshot(snapshotId, importedAt,
            await ReadAsync<RawInventoryRow>(connection, "inventory_rows", "row_json", snapshotId, token),
            await ReadAsync<InventoryItemDraft>(connection, "inventory_items", "item_json", snapshotId, token),
            await ReadAsync<InventorySocketDraft>(connection, "inventory_sockets", "socket_json", snapshotId, token),
            await ReadAsync<InventoryStorage>(connection, "inventory_storage", "storage_json", snapshotId, token));
    }

    private static async Task<IReadOnlyList<T>> ReadAsync<T>(SqliteConnection connection, string table, string column, Guid snapshotId, CancellationToken token)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {column} FROM {table} WHERE snapshot_id = $id ORDER BY rowid;";
        command.Parameters.AddWithValue("$id", snapshotId.ToString("D"));
        await using var reader = await command.ExecuteReaderAsync(token);
        var values = new List<T>();
        while (await reader.ReadAsync(token))
        {
            values.Add(JsonSerializer.Deserialize<T>(reader.GetString(0)) ?? throw new InvalidOperationException($"Stored {typeof(T).Name} is invalid."));
        }
        return values;
    }

    private static async Task<SqliteConnection> OpenConnectionAsync(string connectionString, CancellationToken token)
    {
        var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(token);
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA foreign_keys = ON;";
            await command.ExecuteNonQueryAsync(token);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    private static async Task ExecuteAsync(SqliteConnection connection, SqliteTransaction transaction, string sql, CancellationToken token, params (string Name, object Value)[] parameters)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach (var parameter in parameters) command.Parameters.AddWithValue(parameter.Name, parameter.Value);
        await command.ExecuteNonQueryAsync(token);
    }
}
