using EqlGearHelper.Application;
using EqlGearHelper.Domain;

namespace EqlGearHelper.Application.Tests;

public sealed class TargetPlannerTests
{
    private static readonly ClassTrio Trio = new("Bard", "Ranger", "Warrior");

    [Fact]
    public async Task ExecuteAsync_ExcludesQuestOnlyTargetsAndReportsMissingEffects()
    {
        var owned = Item("owned", "Owned Helm", SlotType.Head, 3);
        var quest = Item("quest", "Quest Helm", SlotType.Head, 20, [new GearEffect("Spell Focus")]);
        var obtainable = Item("obtainable", "Market Helm", SlotType.Head, 10, [new GearEffect("Spell Focus")]);
        var collection = new Collection([owned, quest, obtainable], [Owned(owned)]);
        var sources = new Dictionary<string, IReadOnlyList<AcquisitionSource>>
        {
            ["quest"] = [new AcquisitionSource("Quest giver", true)],
            ["obtainable"] = [new AcquisitionSource("Bazaar")]
        };

        var plan = await new BuildTargetPlanUseCase().ExecuteAsync(
            new BuildTargetRequest(Trio, collection, Rules(SlotType.Head), sources), CancellationToken.None);

        Assert.Equal("obtainable", Assert.Single(plan.Targets).Target.CatalogItemId);
        Assert.Contains(plan.Gaps, gap => gap.MissingEffects.Contains("Spell Focus"));
        Assert.DoesNotContain(plan.Targets.SelectMany(target => target.Alternatives), item => item.CatalogItemId == "quest");
    }

    [Fact]
    public async Task ExecuteAsync_ExcludesTargetsWithoutAConfirmedAcquisitionSource()
    {
        var owned = Item("owned", "Owned Helm", SlotType.Head, 3);
        var unknown = Item("unknown", "Unknown Helm", SlotType.Head, 30);
        var rumor = Item("rumor", "Rumor Helm", SlotType.Head, 20);
        var confirmed = Item("confirmed", "Confirmed Helm", SlotType.Head, 10);
        var collection = new Collection([owned, unknown, rumor, confirmed], [Owned(owned)]);
        var sources = new Dictionary<string, IReadOnlyList<AcquisitionSource>>
        {
            ["rumor"] = [new AcquisitionSource("Unverified rumor", isConfirmed: false)],
            ["confirmed"] = [new AcquisitionSource("Bazaar")]
        };

        var plan = await new BuildTargetPlanUseCase().ExecuteAsync(
            new BuildTargetRequest(Trio, collection, Rules(SlotType.Head), sources), CancellationToken.None);

        Assert.Equal("confirmed", Assert.Single(plan.Targets).Target.CatalogItemId);
    }

    private static Ruleset Rules(params SlotType[] positions) => new(requiredPositions: positions.Select((type, index) => new EquipmentPosition(type, index)).ToArray());

    private static CatalogItem Item(string id, string name, SlotType position, int strength, IReadOnlyList<GearEffect>? effects = null) =>
        new(id, name, ClassSet.Of("Bard", "Ranger", "Warrior"), [new EquipmentPosition(position)], new Dictionary<string, int> { ["STR"] = strength }, effects: effects);

    private static OwnedItemInstance Owned(CatalogItem item) => new(Guid.NewGuid(), item.CatalogItemId, 0, InventoryLocation.Carried, []);
}
