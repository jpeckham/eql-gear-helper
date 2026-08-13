using EqlGearHelper.Domain;

namespace EqlGearHelper.Domain.Tests;

public sealed class LoadoutAssignmentTests
{
    private static readonly ClassTrio Trio = new("Bard", "Ranger", "Warrior");

    [Fact]
    public void FindBestOwned_UsesSecondPhysicalCopyForDuplicateRingPosition()
    {
        var first = Item("high-ring", "High Ring", SlotType.Ring, 20);
        var second = Item("other-ring", "Other Ring", SlotType.Ring, 5);
        var collection = CollectionOf(
            first,
            second,
            Owned(first),
            Owned(first),
            Owned(second));
        var plan = new LoadoutAssignmentService().FindBestOwned(Trio, collection, Rules(SlotType.Ring, SlotType.Ring));

        Assert.True(plan.IsComplete);
        Assert.Equal(2, plan.Loadout!.Assignments.Count);
        Assert.All(plan.Loadout.Assignments, assignment => Assert.Equal("high-ring", assignment.Item.CatalogItemId));
        Assert.Equal(2, plan.Loadout.Assignments.Select(assignment => assignment.Item.InstanceId).Distinct().Count());
    }

    [Fact]
    public void FindBestOwned_AllowsOtherwiseEquippableItemsInAnyPositions()
    {
        var helm = Item("helm", "Helm", SlotType.Head, 8);
        var collection = CollectionOf(helm, Owned(helm), Owned(helm));

        var plan = new LoadoutAssignmentService().FindBestOwned(Trio, collection, Rules(SlotType.Any, SlotType.Any));

        Assert.True(plan.IsComplete);
        Assert.Equal(2, plan.Loadout!.Assignments.Count);
    }

    [Fact]
    public void FindBestOwned_DoesNotAssignOnePhysicalCopyTwice()
    {
        var ring = Item("ring", "Ring", SlotType.Ring, 12);
        var plan = new LoadoutAssignmentService().FindBestOwned(Trio, CollectionOf(ring, Owned(ring)), Rules(SlotType.Ring, SlotType.Ring));

        Assert.False(plan.IsComplete);
        Assert.Null(plan.Loadout);
    }

    [Fact]
    public void FindBestOwned_EnforcesLoreAndTwoHandedConflicts()
    {
        var lore = Item("lore-ring", "Lore Ring", SlotType.Ring, 20, isLore: true);
        var ordinary = Item("ordinary-ring", "Ordinary Ring", SlotType.Ring, 1);
        var twoHanded = Item("greatsword", "Greatsword", SlotType.Primary, 20, twoHanded: true);
        var oneHanded = Item("sword", "Sword", SlotType.Primary, 1);
        var shield = Item("shield", "Shield", SlotType.Secondary, 20);
        var collection = CollectionOf(lore, ordinary, twoHanded, oneHanded, shield, Owned(lore), Owned(lore), Owned(ordinary), Owned(twoHanded), Owned(oneHanded), Owned(shield));

        var plan = new LoadoutAssignmentService().FindBestOwned(Trio, collection, Rules(SlotType.Ring, SlotType.Ring, SlotType.Primary, SlotType.Secondary));

        Assert.True(plan.IsComplete);
        Assert.Single(plan.Loadout!.Assignments, assignment => assignment.Item.CatalogItemId == "lore-ring");
        Assert.False(plan.Loadout.Assignments.Any(assignment => assignment.Item.CatalogItemId == "greatsword") &&
            plan.Loadout.Assignments.Any(assignment => assignment.Position.Type == SlotType.Secondary));
    }

    [Fact]
    public void Evaluate_UsesExaltationClassIntersectionAndSuppressesNonStackingEffects()
    {
        var first = Item("first", "First", SlotType.Head, 0, effects: [new GearEffect("Critical Chance", 1, false)]);
        var second = Item("second", "Second", SlotType.Face, 0, effects: [new GearEffect("Critical Chance", 3, false)]);
        var firstOwned = Owned(first, exaltations: [new InstalledExaltation("ex", ClassSet.Of("Bard", "Mage"), ClassSet.Of("Mage"), Guid.NewGuid())]);
        var secondOwned = Owned(second);
        var loadout = new Loadout(Trio, [new LoadoutAssignment(new EquipmentPosition(SlotType.Head), firstOwned, first), new LoadoutAssignment(new EquipmentPosition(SlotType.Face), secondOwned, second)]);

        var evaluation = new LoadoutEvaluator().Evaluate(loadout, Trio, Rules(SlotType.Head, SlotType.Face));

        Assert.Equal(3, evaluation.EffectCoverage.Single(coverage => coverage.EffectId == "Critical Chance").HighestTier);
        Assert.Equal(1, evaluation.EffectCoverage.Single(coverage => coverage.EffectId == "Critical Chance").ActiveSources);
        Assert.DoesNotContain(evaluation.ExplanationEvidence, evidence => evidence.Contains("ex", StringComparison.Ordinal));
    }

    [Fact]
    public void Evaluate_ZeroesDexCapsCharismaAndReportsNamedRequirementCoverage()
    {
        var item = new CatalogItem("stats", "Stats", ClassSet.Of("Bard"), [new EquipmentPosition(SlotType.Head)], new Dictionary<string, int> { ["DEX"] = 99, ["CHA"] = 100, ["STR"] = 4 });
        var owned = Owned(item);
        var rules = new Ruleset(
            charismaTargets: new Dictionary<string, int> { ["Bard"] = 75 },
            requiredPositions: [new EquipmentPosition(SlotType.Head)],
            requirements: [new NamedRequirement("Strength", "STR", 5)]);

        var evaluation = new LoadoutEvaluator().Evaluate(new Loadout(Trio, [new LoadoutAssignment(new EquipmentPosition(SlotType.Head), owned, item)]), Trio, rules);

        Assert.Equal(0, evaluation.UtilityTotals["DEX"]);
        Assert.Equal(75, evaluation.UtilityTotals["CHA"]);
        Assert.False(evaluation.Requirements.Single(requirement => requirement.Name == "Strength").IsMet);
    }

    [Fact]
    public void FindBestOwned_ReportsTwoHandedSecondaryConflictAsIncomplete()
    {
        var greatsword = Item("greatsword", "Greatsword", SlotType.Primary, 20, twoHanded: true);
        var shield = Item("shield", "Shield", SlotType.Secondary, 20);

        var plan = new LoadoutAssignmentService().FindBestOwned(Trio, CollectionOf(greatsword, shield, Owned(greatsword), Owned(shield)), Rules(SlotType.Primary, SlotType.Secondary));

        Assert.False(plan.IsComplete);
        Assert.Null(plan.Loadout);
        Assert.Contains(plan.UnfilledPositions, position => position.Type == SlotType.Secondary);
        Assert.Contains(plan.ExplanationEvidence, evidence => evidence.Contains("two-handed", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void FindBestOwned_PrefersCharismaThatContributesBelowTheCap()
    {
        var charisma = new CatalogItem("cha", "Charisma Helm", ClassSet.Of("Bard"), [new EquipmentPosition(SlotType.Head)], new Dictionary<string, int> { ["CHA"] = 25 });
        var plain = new CatalogItem("plain", "Plain Helm", ClassSet.Of("Bard"), [new EquipmentPosition(SlotType.Head)]);
        var rules = new Ruleset(charismaTargets: new Dictionary<string, int> { ["Bard"] = 75 }, requiredPositions: [new EquipmentPosition(SlotType.Head)]);

        var plan = new LoadoutAssignmentService().FindBestOwned(Trio, CollectionOf(charisma, plain, Owned(charisma), Owned(plain)), rules);

        Assert.Equal("cha", Assert.Single(plan.Loadout!.Assignments).Item.CatalogItemId);
        Assert.Equal(25, plan.Evaluation!.UtilityTotals["CHA"]);
    }

    [Fact]
    public void FindBestOwned_HonorsCancellationDuringLargeSearch()
    {
        var item = Item("candidate", "Candidate", SlotType.Head, 0);
        var positions = Enumerable.Range(0, 8).Select(index => new EquipmentPosition(SlotType.Any, index)).ToArray();
        var collection = CollectionOf(item, Enumerable.Range(0, 10).Select(_ => (object)Owned(item)).ToArray());
        using var cancellation = new CancellationTokenSource();
        cancellation.CancelAfter(TimeSpan.FromMilliseconds(10));

        Assert.Throws<OperationCanceledException>(() => new LoadoutAssignmentService().FindBestOwned(Trio, collection, new Ruleset(requiredPositions: positions), cancellation.Token));
    }

    private static Ruleset Rules(params SlotType[] positions) => new(requiredPositions: positions.Select((type, index) => new EquipmentPosition(type, index)).ToArray());

    private static Collection CollectionOf(CatalogItem first, params object[] rest)
    {
        var catalog = new List<CatalogItem> { first };
        var owned = new List<OwnedItemInstance>();
        foreach (var value in rest)
        {
            if (value is CatalogItem item) catalog.Add(item);
            if (value is OwnedItemInstance instance) owned.Add(instance);
        }

        return new Collection(catalog, owned);
    }

    private static CatalogItem Item(string id, string name, SlotType position, int strength, bool isLore = false, bool twoHanded = false, IReadOnlyList<GearEffect>? effects = null) =>
        new(id, name, ClassSet.Of("Bard", "Ranger", "Warrior"), [new EquipmentPosition(position)], new Dictionary<string, int> { ["STR"] = strength }, isLore, twoHanded, effects);

    private static OwnedItemInstance Owned(CatalogItem item, IReadOnlyList<InstalledExaltation>? exaltations = null) =>
        new(Guid.NewGuid(), item.CatalogItemId, 0, InventoryLocation.Carried, exaltations ?? []);
}
