using System.Text.Json;
using EqlGearHelper.Application;
using EqlGearHelper.Domain;
using Microsoft.Data.Sqlite;

namespace EqlGearHelper.Infrastructure.Sqlite;

public sealed class AnalysisRepository(string connectionString) : IAnalysisRepository
{
    public async Task SaveAsync(Guid snapshotId, IReadOnlyList<Assessment> assessments, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(assessments);
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(token);
        await using var transaction = await connection.BeginTransactionAsync(token);
        await using var create = connection.CreateCommand();
        create.Transaction = (SqliteTransaction)transaction;
        create.CommandText = "CREATE TABLE IF NOT EXISTS analysis_results (snapshot_id TEXT PRIMARY KEY, assessments_json TEXT NOT NULL);";
        await create.ExecuteNonQueryAsync(token);
        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText = "INSERT INTO analysis_results (snapshot_id, assessments_json) VALUES ($id, $json) ON CONFLICT(snapshot_id) DO UPDATE SET assessments_json = excluded.assessments_json;";
        command.Parameters.AddWithValue("$id", snapshotId.ToString("D"));
        command.Parameters.AddWithValue("$json", JsonSerializer.Serialize(assessments));
        await command.ExecuteNonQueryAsync(token);
        await transaction.CommitAsync(token);
    }

    public async Task<IReadOnlyList<Assessment>?> GetAsync(Guid snapshotId, CancellationToken token)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(token);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT assessments_json FROM analysis_results WHERE snapshot_id = $id;";
        command.Parameters.AddWithValue("$id", snapshotId.ToString("D"));
        var json = await command.ExecuteScalarAsync(token) as string;
        return json is null ? null : JsonSerializer.Deserialize<List<Assessment>>(json) ?? throw new InvalidOperationException("Stored analysis is invalid.");
    }
}
