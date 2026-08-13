using EqlGearHelper.Domain;

namespace EqlGearHelper.Application;

public sealed record ExaltationResolutionRequest(InventorySnapshot Snapshot, IReadOnlyDictionary<string, ExaltationDefinition> Definitions);
public sealed record ExaltationResolutionRow(string Name, string HostPath, MappingStatus MappingStatus, bool IsTransferred, bool IsValuable);
public sealed record ExaltationResolutionResult(IReadOnlyList<ExaltationResolutionRow> Rows);

public sealed class ResolveExaltationsUseCase
{
    public Task<ExaltationResolutionResult> ExecuteAsync(ExaltationResolutionRequest request, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(request); token.ThrowIfCancellationRequested();
        var rows = request.Snapshot.Sockets.Where(socket => socket.IsExaltation).Select(socket =>
        {
            var known = request.Definitions.TryGetValue(socket.SocketItemId, out var definition);
            return new ExaltationResolutionRow(known ? definition!.Name : socket.Name, socket.HostPath, known ? socket.MappingStatus : MappingStatus.Unknown, socket.IsTransferred, known && definition!.IsValuable);
        }).ToArray();
        return Task.FromResult(new ExaltationResolutionResult(rows));
    }
}

public sealed record CreateCollectionBackupRequest(string DestinationPath, CatalogPackage Catalog, InventorySnapshot Snapshot);
public sealed class CreateCollectionBackupUseCase(ICollectionBackupService backupService)
{
    public Task<CollectionBackupManifest> ExecuteAsync(CreateCollectionBackupRequest request, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(request);
        return backupService.CreateAsync(request.DestinationPath, request.Catalog, request.Snapshot, token);
    }
}

public sealed record RecoverCollectionBackupRequest(string BackupPath, CollectionBackupManifest Expected);
public sealed class RecoverCollectionBackupUseCase(ICollectionBackupService backupService)
{
    public Task ExecuteAsync(RecoverCollectionBackupRequest request, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(request);
        return backupService.RecoverAsync(request.BackupPath, request.Expected, token);
    }
}

public sealed class WorkflowSessionUseCase(ICatalogRepository catalogRepository, IInventorySnapshotRepository snapshotRepository)
{
    public async Task<IReadOnlyList<string>> GetClassChoicesAsync(CancellationToken token)
    {
        var catalog = await catalogRepository.GetCurrentAsync(token);
        return catalog?.Items.SelectMany(item => item.Classes).Concat(Ruleset.Default.CharismaTargets.Keys).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray() ?? [];
    }

    public async Task<AnalyzeCollectionRequest> CreateCleanupRequestAsync(CancellationToken token)
    {
        var state = await GetStateAsync(token);
        return new AnalyzeCollectionRequest(state.Collection, state.Ruleset, state.Catalog.Exaltations.ToDictionary(item => item.ExaltationId, StringComparer.OrdinalIgnoreCase), state.UnknownAssetIds);
    }

    public async Task<BuildTargetRequest> CreateBuildRequestAsync(string first, string second, string third, CancellationToken token)
    {
        var state = await GetStateAsync(token);
        return new BuildTargetRequest(new ClassTrio(first, second, third), state.Collection, state.Ruleset);
    }

    public async Task<ExaltationResolutionRequest> CreateExaltationRequestAsync(CancellationToken token)
    {
        var state = await GetStateAsync(token);
        return new ExaltationResolutionRequest(state.Snapshot, state.Catalog.Exaltations.ToDictionary(item => item.ExaltationId, StringComparer.OrdinalIgnoreCase));
    }

    public async Task<CreateCollectionBackupRequest> CreateBackupRequestAsync(string destinationPath, CancellationToken token)
    {
        var state = await GetStateAsync(token);
        return new CreateCollectionBackupRequest(destinationPath, state.Catalog, state.Snapshot);
    }

    public RecoverCollectionBackupRequest CreateRecoveryRequest(string backupPath, CollectionBackupManifest manifest) => new(backupPath, manifest);

    public DisposalExportRequest CreateDisposalExportRequest(AnalyzeCollectionRequest analysisRequest, CollectionAnalysisResult analysisResult) =>
        new(analysisResult.Assessments.Select(item => item.Assessment).ToArray(), analysisRequest.Collection.OwnedItems.ToDictionary(item => item.InstanceId, item => item.Location));

    private async Task<WorkflowState> GetStateAsync(CancellationToken token)
    {
        var catalog = await catalogRepository.GetCurrentAsync(token) ?? throw new InvalidOperationException("Import a catalog package before this operation.");
        var snapshot = await snapshotRepository.GetCurrentAsync(token) ?? throw new InvalidOperationException("Import inventory before this operation.");
        var definitions = catalog.Items.ToDictionary(item => item.CatalogItemId, StringComparer.OrdinalIgnoreCase);
        var owned = snapshot.Items.Where(item => definitions.ContainsKey(item.ItemId)).Select(item => new OwnedItemInstance(item.InstanceId, item.ItemId, item.UpgradeLevel, new InventoryLocation(item.Path), [])).ToArray();
        var unknown = snapshot.Items.Where(item => item.MappingStatus == MappingStatus.Unknown || !definitions.ContainsKey(item.ItemId)).Select(item => item.InstanceId).ToHashSet();
        return new WorkflowState(catalog, snapshot, new Collection(catalog.Items, owned), Ruleset.Default, unknown);
    }

    private sealed record WorkflowState(CatalogPackage Catalog, InventorySnapshot Snapshot, Collection Collection, Ruleset Ruleset, IReadOnlySet<Guid> UnknownAssetIds);
}
