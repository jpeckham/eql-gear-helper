using EqlGearHelper.Application;
using EqlGearHelper.Domain;

namespace EqlGearHelper.Application.Tests;

public sealed class BuildPlannerUseCaseTests
{
    [Fact]
    public async Task ExecuteAsync_RetainsLastCompleteResultWhenCancelled()
    {
        var useCase = new BuildPlannerUseCase(new LoadoutAssignmentService(), new LoadoutEvaluator());
        var item = new CatalogItem("helm", "Helm", ClassSet.Of("Bard", "Ranger", "Warrior"), [new EquipmentPosition(SlotType.Head)]);
        var collection = new Collection([item], [new OwnedItemInstance(Guid.NewGuid(), "helm", 0, InventoryLocation.Carried, [])]);
        var rules = new Ruleset(requiredPositions: [new EquipmentPosition(SlotType.Head)]);

        var completed = await useCase.ExecuteAsync(new ClassTrio("Bard", "Ranger", "Warrior"), collection, rules, CancellationToken.None);
        await Assert.ThrowsAsync<OperationCanceledException>(() => useCase.ExecuteAsync(new ClassTrio("Bard", "Ranger", "Warrior"), collection, rules, new CancellationToken(true)));

        Assert.True(completed.IsComplete);
        Assert.Same(completed, useCase.LastCompleteResult);
    }
}
