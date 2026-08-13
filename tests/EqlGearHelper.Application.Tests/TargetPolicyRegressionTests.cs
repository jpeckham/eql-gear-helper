using EqlGearHelper.Application;
using EqlGearHelper.Domain;

namespace EqlGearHelper.Application.Tests;

public sealed class TargetPolicyRegressionTests
{
    [Fact]
    public async Task ExecuteAsync_UsesCatalogTargetWhenSourceIsUnknownButExcludesConfirmedQuestOnlyTarget()
    {
        var position = new EquipmentPosition(SlotType.Head);
        var item = new CatalogItem("helm", "Catalog Helm", ClassSet.Of("Bard", "Ranger", "Warrior"), [position]);
        var request = new BuildTargetRequest(new ClassTrio("Bard", "Ranger", "Warrior"), new Collection([item], []), new Ruleset(requiredPositions: [position]));
        var useCase = new BuildTargetPlanUseCase();

        var unknownSourceResult = await useCase.ExecuteAsync(request, CancellationToken.None);
        var questOnlyResult = await useCase.ExecuteAsync(request with { Sources = new Dictionary<string, IReadOnlyList<AcquisitionSource>> { [item.CatalogItemId] = [new AcquisitionSource("Quest", isQuestReward: true)] } }, CancellationToken.None);

        Assert.Single(unknownSourceResult.Targets);
        Assert.Empty(questOnlyResult.Targets);
    }
}
