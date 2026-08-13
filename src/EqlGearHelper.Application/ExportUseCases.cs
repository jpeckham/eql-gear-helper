using EqlGearHelper.Domain;

namespace EqlGearHelper.Application;

public sealed record DisposalExportRequest(IReadOnlyList<Assessment> Assessments, IReadOnlyDictionary<Guid, InventoryLocation> Locations);
public sealed record DisposalExportRow(Guid AssetInstanceId, InventoryLocation Location, string Explanation);
public sealed record DisposalExportResult(IReadOnlyList<DisposalExportRow> Rows);

public sealed class DisposalExportUseCase
{
    public Task<DisposalExportResult> ExecuteAsync(DisposalExportRequest request, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(request);
        token.ThrowIfCancellationRequested();
        var rows = request.Assessments
            .Where(assessment => assessment.FinalAction == FinalAction.DisposeCandidate && assessment.Confidence == RecommendationConfidence.Complete)
            .Where(assessment => request.Locations.ContainsKey(assessment.AssetInstanceId))
            .Select(assessment => new DisposalExportRow(assessment.AssetInstanceId, request.Locations[assessment.AssetInstanceId], assessment.Explanation))
            .OrderBy(row => row.Location.Container, StringComparer.OrdinalIgnoreCase)
            .ThenBy(row => row.Location.SubLocation, StringComparer.OrdinalIgnoreCase)
            .ThenBy(row => row.AssetInstanceId)
            .ToArray();
        return Task.FromResult(new DisposalExportResult(rows));
    }
}

public sealed class DisposalExportPresentationUseCase
{
    public IReadOnlyList<string> Present(DisposalExportResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return result.Rows.Select(row => $"{row.Location.Container}\t{row.Location.SubLocation}\t{row.AssetInstanceId}\t{row.Explanation}").ToArray();
    }
}

public sealed record CollectionBackupManifest(string CatalogVersion, string RulesetVersion, Guid SnapshotId, string DatabaseHash);

public interface ICollectionBackupService
{
    Task<CollectionBackupManifest> CreateAsync(string destinationPath, CatalogPackage catalog, InventorySnapshot snapshot, CancellationToken token);
    Task RecoverAsync(string backupPath, CollectionBackupManifest expected, CancellationToken token);
}
