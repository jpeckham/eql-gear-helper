using System.Reflection;
using System.IO;
using EqlGearHelper.Application;
using EqlGearHelper.Wpf.Controllers;
using EqlGearHelper.Wpf.Presenters;
using EqlGearHelper.Wpf.ViewModels;
using Microsoft.Win32;

namespace EqlGearHelper.Wpf;

public static class LocalWorkflowComposition
{
    public static async Task<WorkflowOperations> CreateAsync()
    {
        var infrastructure = Assembly.LoadFrom(Path.Combine(AppContext.BaseDirectory, "EqlGearHelper.Infrastructure.dll"));
        var databasePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EqlGearHelper", "collection.sqlite");
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
        var connection = $"Data Source={databasePath}";
        var initializer = Create(infrastructure, "EqlGearHelper.Infrastructure.Sqlite.DatabaseInitializer", connection);
        await InvokeTaskAsync(initializer, "InitializeAsync", CancellationToken.None);
        var catalogRepository = (ICatalogRepository)Create(infrastructure, "EqlGearHelper.Infrastructure.Sqlite.CatalogRepository", connection);
        var snapshotRepository = (IInventorySnapshotRepository)Create(infrastructure, "EqlGearHelper.Infrastructure.Sqlite.CollectionRepository", connection, null!);
        var parser = (IInventoryParser)Create(infrastructure, "EqlGearHelper.Infrastructure.Import.InventoryParser");
        var backup = (ICollectionBackupService)Create(infrastructure, "EqlGearHelper.Infrastructure.Backup.CollectionBackupService", databasePath);
        var session = new WorkflowSessionUseCase(catalogRepository, snapshotRepository);
        var importInventory = new ImportInventorySnapshotUseCase(parser, snapshotRepository);
        await SeedIncludedInventoryAsync(importInventory, snapshotRepository);
        var inventory = new InventoryController(importInventory, new InventoryPresenter());
        var data = new DataController(new CatalogPackageImportUseCase((ICatalogPackageImporter)Create(infrastructure, "EqlGearHelper.Infrastructure.Import.CatalogPackageImporter"), catalogRepository), new CreateCollectionBackupUseCase(backup), new RecoverCollectionBackupUseCase(backup), new DataPresenter());
        var cleanup = new CleanupController(CreateCleanupUseCase(), new CleanupPresenter());
        var cleanupWorkflow = new CleanupWorkflowController(cleanup);
        var disposalExport = new DisposalExportUseCase();
        var disposalPresentation = new DisposalExportPresentationUseCase();
        var build = new BuildPlannerController(new BuildTargetPlanUseCase(), new BuildPlannerPresenter());
        var exaltations = new ExaltationsController(new ResolveExaltationsUseCase(), new ExaltationsPresenter());
        CollectionBackupManifest? lastBackup = null;
        return new WorkflowOperations
        {
            ImportInventory = token => inventory.ImportAsync(OpenRead("Inventory output (*.txt)|*.txt"), token),
            LoadInventory = async token => new InventoryPresenter().Present(await snapshotRepository.GetCurrentAsync(token) ?? throw new InvalidOperationException("No inventory snapshot is available.")),
            ImportCleanup = async token => { await inventory.ImportAsync(OpenRead("Inventory output (*.txt)|*.txt"), token); },
            Reanalyze = token => cleanupWorkflow.ReanalyzeAsync(async cancellationToken => (object)await session.CreateCleanupRequestAsync(cancellationToken), token),
            Build = async (first, second, third, token) => await build.PlanAsync(await session.CreateBuildRequestAsync(first, second, third, token), token),
            ResolveExaltations = async token => await exaltations.ResolveAsync(await session.CreateExaltationRequestAsync(token), token),
            Export = async token =>
            {
                var request = session.CreateDisposalExportRequest(cleanup.LastRequest ?? throw new InvalidOperationException("Run cleanup analysis before exporting."), cleanup.LastResult ?? throw new InvalidOperationException("Run cleanup analysis before exporting."));
                var result = await disposalExport.ExecuteAsync(request, token);
                await File.WriteAllLinesAsync(SavePath("Disposal export (*.txt)|*.txt"), disposalPresentation.Present(result), token);
            },
            ImportCatalog = async token => await data.ImportCatalogAsync(OpenRead("Catalog package (*.json)|*.json"), await snapshotRepository.GetCurrentAsync(token), token),
            Backup = async token => lastBackup = (CollectionBackupManifest)await data.BackupAsync(await session.CreateBackupRequestAsync(SavePath("Collection backup (*.zip)|*.zip"), token), token),
            Recover = token => data.RecoverAsync(session.CreateRecoveryRequest(OpenPath("Collection backup (*.zip)|*.zip"), lastBackup ?? throw new InvalidOperationException("Create a backup before recovery.")), token),
            LoadClassChoices = session.GetClassChoicesAsync
        };
    }

    private static AnalyzeCollectionUseCase CreateCleanupUseCase()
    {
        var domain = Assembly.Load("EqlGearHelper.Domain");
        var assignment = Create(domain, "EqlGearHelper.Domain.LoadoutAssignmentService");
        var evaluator = Create(domain, "EqlGearHelper.Domain.LoadoutEvaluator");
        return (AnalyzeCollectionUseCase)Activator.CreateInstance(typeof(AnalyzeCollectionUseCase), assignment, evaluator)!;
    }
    private static async Task SeedIncludedInventoryAsync(ImportInventorySnapshotUseCase importInventory, IInventorySnapshotRepository snapshotRepository)
    {
        if (await snapshotRepository.GetCurrentAsync(CancellationToken.None) is not null) return;
        await using var sample = typeof(LocalWorkflowComposition).Assembly.GetManifestResourceStream("EqlGearHelper.Wpf.Samples.Parnell_oggok-Inventory.txt")
            ?? throw new InvalidOperationException("The included inventory sample could not be loaded.");
        await importInventory.ExecuteAsync(sample, CancellationToken.None);
    }
    private static object Create(Assembly assembly, string typeName, params object[] arguments) => Activator.CreateInstance(assembly.GetType(typeName, throwOnError: true)!, arguments)!;
    private static async Task InvokeTaskAsync(object target, string method, CancellationToken token) => await (Task)target.GetType().GetMethod(method)!.Invoke(target, [token])!;
    private static FileStream OpenRead(string filter) => File.OpenRead(OpenPath(filter));
    private static string OpenPath(string filter) { var dialog = new OpenFileDialog { Filter = filter }; return dialog.ShowDialog() == true ? dialog.FileName : throw new OperationCanceledException(); }
    private static string SavePath(string filter) { var dialog = new SaveFileDialog { Filter = filter }; return dialog.ShowDialog() == true ? dialog.FileName : throw new OperationCanceledException(); }
}
