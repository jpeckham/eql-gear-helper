using System.IO;
using EqlGearHelper.Application;
using EqlGearHelper.Wpf.Presenters;
using EqlGearHelper.Wpf.ViewModels;

namespace EqlGearHelper.Wpf.Controllers;

public sealed class CleanupController(AnalyzeCollectionUseCase useCase, CleanupPresenter presenter)
{
    public AnalyzeCollectionRequest? LastRequest { get; private set; }
    public CollectionAnalysisResult? LastResult { get; private set; }
    public void InvalidateExportState()
    {
        LastRequest = null;
        LastResult = null;
    }
    public async Task<CleanupViewModel> AnalyzeAsync(object request, CancellationToken token)
    {
        InvalidateExportState();
        var nextRequest = (AnalyzeCollectionRequest)request;
        var nextResult = await useCase.ExecuteAsync(nextRequest, token);
        var viewModel = presenter.Present(nextResult, nextRequest);
        LastRequest = nextRequest;
        LastResult = nextResult;
        return viewModel;
    }
}

public sealed class CleanupWorkflowController(CleanupController cleanup)
{
    public async Task<CleanupViewModel> ReanalyzeAsync(Func<CancellationToken, Task<object>> requestFactory, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(requestFactory);
        cleanup.InvalidateExportState();
        return await cleanup.AnalyzeAsync(await requestFactory(token), token);
    }
}
public sealed class InventoryController(ImportInventorySnapshotUseCase useCase, InventoryPresenter presenter)
{
    public async Task<InventoryViewModel> ImportAsync(Stream stream, CancellationToken token) => presenter.Present(await useCase.ExecuteAsync(stream, token));
}
public sealed class BuildPlannerController(BuildTargetPlanUseCase useCase, BuildPlannerPresenter presenter)
{
    public async Task<BuildPlannerViewModel> PlanAsync(object request, CancellationToken token) => presenter.Present(await useCase.ExecuteAsync((BuildTargetRequest)request, token));
}
public sealed class ExaltationsController(ResolveExaltationsUseCase useCase, ExaltationsPresenter presenter)
{
    public async Task<ExaltationsViewModel> ResolveAsync(object request, CancellationToken token) => presenter.Present(await useCase.ExecuteAsync((ExaltationResolutionRequest)request, token));
}
public sealed class DataController(CatalogPackageImportUseCase catalogUseCase, CreateCollectionBackupUseCase backupUseCase, RecoverCollectionBackupUseCase recoveryUseCase, DataPresenter presenter)
{
    public async Task<DataViewModel> ImportCatalogAsync(Stream stream, object? snapshot, CancellationToken token) => presenter.Present(await catalogUseCase.ExecuteAsync(stream, token), snapshot);
    public async Task<object> BackupAsync(object request, CancellationToken token) => await backupUseCase.ExecuteAsync((CreateCollectionBackupRequest)request, token);
    public Task RecoverAsync(object request, CancellationToken token) => recoveryUseCase.ExecuteAsync((RecoverCollectionBackupRequest)request, token);
}
