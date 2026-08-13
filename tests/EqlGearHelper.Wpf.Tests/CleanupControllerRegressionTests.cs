using EqlGearHelper.Application;
using EqlGearHelper.Domain;
using EqlGearHelper.Wpf.Controllers;
using EqlGearHelper.Wpf.Presenters;
using EqlGearHelper.Wpf;
using System.IO;

namespace EqlGearHelper.Wpf.Tests;

public sealed class CleanupControllerRegressionTests
{
    [Fact]
    public async Task ReanalyzeAsync_RequestConstructionCancellationInvalidatesPriorExportState()
    {
        var controller = CreateControllerWithCompletedAnalysis(out var request);
        var coordinator = new CleanupWorkflowController(controller);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => coordinator.ReanalyzeAsync(_ => Task.FromCanceled<object>(new CancellationToken(canceled: true)), CancellationToken.None));

        Assert.Null(controller.LastRequest);
        Assert.Null(controller.LastResult);
    }

    [Fact]
    public async Task ReanalyzeAsync_RequestConstructionFailureInvalidatesPriorExportState()
    {
        var controller = CreateControllerWithCompletedAnalysis(out _);
        var coordinator = new CleanupWorkflowController(controller);

        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.ReanalyzeAsync(_ => Task.FromException<object>(new InvalidOperationException("Catalog unavailable.")), CancellationToken.None));

        Assert.Null(controller.LastRequest);
        Assert.Null(controller.LastResult);
    }

    [Fact]
    public async Task FromControllers_ReanalyzeFactoryFailureInvalidatesPriorExportState()
    {
        var controller = CreateControllerWithCompletedAnalysis(out _);
        var operations = WorkflowComposition.FromControllers(
            controller, () => throw new InvalidOperationException("Catalog unavailable."),
            null!, () => Stream.Null,
            null!, (_, _, _) => new object(),
            null!, () => new object(),
            null!, () => Stream.Null, () => null, () => new object(), () => new object(),
            _ => Task.CompletedTask);

        await Assert.ThrowsAsync<InvalidOperationException>(() => operations.Reanalyze(CancellationToken.None));

        Assert.Null(controller.LastRequest);
        Assert.Null(controller.LastResult);
    }

    [Fact]
    public async Task AnalyzeAsync_CancelledReanalysisInvalidatesPriorExportState()
    {
        var controller = new CleanupController(new AnalyzeCollectionUseCase(new LoadoutAssignmentService(), new LoadoutEvaluator()), new CleanupPresenter());
        var item = new CatalogItem("helm", "Helm", ClassSet.Of("Bard", "Ranger", "Warrior"), [new EquipmentPosition(SlotType.Head)]);
        var request = new AnalyzeCollectionRequest(new Collection([item], [new OwnedItemInstance(Guid.NewGuid(), item.CatalogItemId, 0, InventoryLocation.Carried, [])]), new Ruleset(requiredPositions: [new EquipmentPosition(SlotType.Head)]));

        await controller.AnalyzeAsync(request, CancellationToken.None);
        await Assert.ThrowsAsync<OperationCanceledException>(() => controller.AnalyzeAsync(request, new CancellationToken(canceled: true)));

        Assert.Null(controller.LastRequest);
        Assert.Null(controller.LastResult);
    }

    private static CleanupController CreateControllerWithCompletedAnalysis(out AnalyzeCollectionRequest request)
    {
        var controller = new CleanupController(new AnalyzeCollectionUseCase(new LoadoutAssignmentService(), new LoadoutEvaluator()), new CleanupPresenter());
        var item = new CatalogItem("helm", "Helm", ClassSet.Of("Bard", "Ranger", "Warrior"), [new EquipmentPosition(SlotType.Head)]);
        request = new AnalyzeCollectionRequest(new Collection([item], [new OwnedItemInstance(Guid.NewGuid(), item.CatalogItemId, 0, InventoryLocation.Carried, [])]), new Ruleset(requiredPositions: [new EquipmentPosition(SlotType.Head)]));
        controller.AnalyzeAsync(request, CancellationToken.None).GetAwaiter().GetResult();
        return controller;
    }
}
