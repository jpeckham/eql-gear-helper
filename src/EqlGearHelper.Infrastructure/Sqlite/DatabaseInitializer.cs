using Microsoft.Data.Sqlite;

namespace EqlGearHelper.Infrastructure.Sqlite;

public sealed class DatabaseInitializer(string connectionString)
{
    public string ConnectionString { get; } = string.IsNullOrWhiteSpace(connectionString)
        ? throw new ArgumentException("A SQLite connection string is required.", nameof(connectionString))
        : connectionString;

    public async Task InitializeAsync(CancellationToken token = default)
    {
        await using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync(token);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA foreign_keys = ON;
            CREATE TABLE IF NOT EXISTS catalog_packages (id INTEGER PRIMARY KEY CHECK (id = 1), package_json TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS inventory_snapshots (snapshot_id TEXT PRIMARY KEY, imported_at_utc TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS inventory_rows (snapshot_id TEXT NOT NULL, row_number INTEGER NOT NULL, path TEXT NOT NULL, mapping_status INTEGER NOT NULL, row_json TEXT NOT NULL, PRIMARY KEY (snapshot_id, row_number), FOREIGN KEY (snapshot_id) REFERENCES inventory_snapshots(snapshot_id) ON DELETE CASCADE);
            CREATE TABLE IF NOT EXISTS inventory_items (snapshot_id TEXT NOT NULL, path TEXT NOT NULL, item_json TEXT NOT NULL, PRIMARY KEY (snapshot_id, path), FOREIGN KEY (snapshot_id) REFERENCES inventory_snapshots(snapshot_id) ON DELETE CASCADE);
            CREATE TABLE IF NOT EXISTS inventory_sockets (snapshot_id TEXT NOT NULL, path TEXT NOT NULL, socket_json TEXT NOT NULL, PRIMARY KEY (snapshot_id, path), FOREIGN KEY (snapshot_id) REFERENCES inventory_snapshots(snapshot_id) ON DELETE CASCADE);
            CREATE TABLE IF NOT EXISTS inventory_storage (snapshot_id TEXT NOT NULL, name TEXT NOT NULL, availability INTEGER NOT NULL, storage_json TEXT NOT NULL, PRIMARY KEY (snapshot_id, name), FOREIGN KEY (snapshot_id) REFERENCES inventory_snapshots(snapshot_id) ON DELETE CASCADE);
            """;
        await command.ExecuteNonQueryAsync(token);
    }
}
