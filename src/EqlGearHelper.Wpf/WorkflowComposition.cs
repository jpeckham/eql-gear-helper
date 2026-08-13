using System.IO;
using EqlGearHelper.Wpf.Controllers;
using EqlGearHelper.Wpf.ViewModels;

namespace EqlGearHelper.Wpf;

public sealed class WorkflowOperations
{
    public Func<CancellationToken, Task<CleanupViewModel>> Reanalyze { get; init; } = Unconfigured<CleanupViewModel>;
    public Func<CancellationToken, Task<InventoryViewModel>> ImportInventory { get; init; } = Unconfigured<InventoryViewModel>;
    public Func<CancellationToken, Task<InventoryViewModel>> LoadInventory { get; init; } = Unconfigured<InventoryViewModel>;
    public Func<CancellationToken, Task> ImportCleanup { get; init; } = Unconfigured;
    public Func<string, string, string, CancellationToken, Task<BuildPlannerViewModel>> Build { get; init; } = UnconfiguredBuild;
    public Func<CancellationToken, Task<ExaltationsViewModel>> ResolveExaltations { get; init; } = Unconfigured<ExaltationsViewModel>;
    public Func<CancellationToken, Task<DataViewModel>> ImportCatalog { get; init; } = Unconfigured<DataViewModel>;
    public Func<CancellationToken, Task> Export { get; init; } = Unconfigured;
    public Func<CancellationToken, Task> Backup { get; init; } = Unconfigured;
    public Func<CancellationToken, Task> Recover { get; init; } = Unconfigured;
    public Func<CancellationToken, Task<IReadOnlyList<string>>> LoadClassChoices { get; init; } = _ => Task.FromResult<IReadOnlyList<string>>([]);
    private static Task Unconfigured(CancellationToken _) => Task.FromException(new InvalidOperationException("The local application services are not configured."));
    private static Task<T> Unconfigured<T>(CancellationToken _) => Task.FromException<T>(new InvalidOperationException("The local application services are not configured."));
    private static Task<BuildPlannerViewModel> UnconfiguredBuild(string _, string __, string ___, CancellationToken token) => Unconfigured<BuildPlannerViewModel>(token);
}

public static class WorkflowComposition
{
    public static MainViewModel CreateMainViewModel(WorkflowOperations? operations = null)
    {
        operations ??= new WorkflowOperations();
        var model = new MainViewModel();
        model.Cleanup.Initialize("Import inventory and reanalyze to produce a conservative cleanup recommendation.");
        model.Inventory.Initialize("Import an inventory output file to establish collection coverage.");
        model.BuildPlanner.Initialize("Select three distinct classes before building a loadout.");
        model.Exaltations.Initialize("Import inventory to inspect installed Exaltations and socket resolution.");
        model.Data.Initialize("Import a catalog package and inventory snapshot before creating a backup.");
        model.Cleanup.ConfigureImport(async token => { await operations.ImportCleanup(token); model.BuildPlanner.SetClassChoices(await operations.LoadClassChoices(token)); });
        model.Cleanup.ConfigureReanalysis(async token => model.Cleanup.Apply(await operations.Reanalyze(token)));
        model.Cleanup.ConfigureExport(operations.Export);
        model.Inventory.ConfigureImport(async token => { model.Inventory.Apply(await operations.ImportInventory(token)); model.BuildPlanner.SetClassChoices(await operations.LoadClassChoices(token)); });
        model.BuildPlanner.ConfigureBuild(async token => model.BuildPlanner.Apply(await operations.Build(model.BuildPlanner.ClassOne!, model.BuildPlanner.ClassTwo!, model.BuildPlanner.ClassThree!, token)));
        model.Exaltations.ConfigureResolution(async token => model.Exaltations.Apply(await operations.ResolveExaltations(token)));
        model.Data.ConfigureCatalog(async token => { model.Data.Apply(await operations.ImportCatalog(token)); model.BuildPlanner.SetClassChoices(await operations.LoadClassChoices(token)); });
        model.Data.ConfigureBackup(operations.Backup);
        model.Data.ConfigureRecovery(operations.Recover);
        return model;
    }

    public static WorkflowOperations FromControllers(
        CleanupController cleanup, Func<object> cleanupRequest,
        InventoryController inventory, Func<Stream> inventoryStream,
        BuildPlannerController buildPlanner, Func<string, string, string, object> buildTargetRequest,
        ExaltationsController exaltations, Func<object> exaltationRequest,
        DataController data, Func<Stream> catalogStream, Func<object?> snapshot, Func<object> backupRequest, Func<object> recoveryRequest,
        Func<CancellationToken, Task> export)
    {
        var cleanupWorkflow = new CleanupWorkflowController(cleanup);
        return new WorkflowOperations
        {
            Reanalyze = token => cleanupWorkflow.ReanalyzeAsync(async _ => (object)cleanupRequest(), token),
            ImportInventory = token => inventory.ImportAsync(inventoryStream(), token),
            ImportCleanup = async token => { await inventory.ImportAsync(inventoryStream(), token); },
            Build = (first, second, third, token) => buildPlanner.PlanAsync(buildTargetRequest(first, second, third), token),
            ResolveExaltations = token => exaltations.ResolveAsync(exaltationRequest(), token),
            ImportCatalog = token => data.ImportCatalogAsync(catalogStream(), snapshot(), token),
            Export = export,
            Backup = token => data.BackupAsync(backupRequest(), token),
            Recover = token => data.RecoverAsync(recoveryRequest(), token)
        };
    }
}
