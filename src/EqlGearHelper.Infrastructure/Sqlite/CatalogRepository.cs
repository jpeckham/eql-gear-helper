using System.Text.Json;
using EqlGearHelper.Application;
using Microsoft.Data.Sqlite;

namespace EqlGearHelper.Infrastructure.Sqlite;

public sealed class CatalogRepository(string connectionString) : ICatalogRepository
{
    public async Task ReplaceAsync(CatalogPackage value, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(value);
        value.Validate();
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(token);
        await using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO catalog_packages (id, package_json) VALUES (1, $package) ON CONFLICT(id) DO UPDATE SET package_json = excluded.package_json;";
        command.Parameters.AddWithValue("$package", JsonSerializer.Serialize(value));
        await command.ExecuteNonQueryAsync(token);
    }

    public async Task<CatalogPackage?> GetCurrentAsync(CancellationToken token)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(token);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT package_json FROM catalog_packages WHERE id = 1;";
        var value = await command.ExecuteScalarAsync(token) as string;
        return value is null ? null : JsonSerializer.Deserialize<CatalogPackage>(value);
    }
}
