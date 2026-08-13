using EqlGearHelper.Domain;

namespace EqlGearHelper.Domain.Tests;

public sealed class DomainModelTests
{
    [Fact]
    public void InstalledExaltation_NarrowsHostClassesByIntersection()
    {
        var effective = ClassSet.Of("Ranger", "Bard", "Warrior")
            .Intersect(ClassSet.Of("Ranger"));

        Assert.Equal(ClassSet.Of("Ranger"), effective);
    }

    [Fact]
    public void OwnedItemInstances_PreserveDistinctPhysicalCopyIdentity()
    {
        var first = new OwnedItemInstance(Guid.NewGuid(), "item-1", 4, InventoryLocation.Carried, []);
        var second = new OwnedItemInstance(Guid.NewGuid(), "item-1", 4, InventoryLocation.Carried, []);

        Assert.NotEqual(first.InstanceId, second.InstanceId);
        Assert.Equal(first.CatalogItemId, second.CatalogItemId);
    }

    [Fact]
    public void ClassTrio_RejectsDuplicateClasses()
    {
        Assert.Throws<ArgumentException>(() => new ClassTrio("Ranger", "Ranger", "Bard"));
    }

    [Fact]
    public void OwnedItemInstance_RejectsNegativeUpgradeLevel()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new OwnedItemInstance(Guid.NewGuid(), "item-1", -1, InventoryLocation.Carried, []));
    }

    [Fact]
    public void InstalledExaltation_RejectsEmptyEffectiveClassSet()
    {
        Assert.Throws<ArgumentException>(() =>
            new InstalledExaltation("exaltation-1", ClassSet.Of("Ranger"), ClassSet.Of("Bard"), Guid.NewGuid()));
    }

    [Fact]
    public void InstalledExaltation_RequiresAPhysicalSourceInstanceIdentity()
    {
        Assert.Throws<ArgumentException>(() =>
            new InstalledExaltation("exaltation-1", ClassSet.Of("Ranger"), ClassSet.Of("Ranger"), Guid.Empty));
    }

    [Fact]
    public void Ruleset_DexHasNoUtility()
    {
        var trio = new ClassTrio("Ranger", "Bard", "Warrior");
        var ruleset = Ruleset.Default;

        Assert.Equal(0, ruleset.UtilityFor("DEX", 500, trio));
    }

    [Fact]
    public void Ruleset_CharismaStopsAtConfiguredTargetForApplicableBuilds()
    {
        var chaUsingTrio = new ClassTrio("Bard", "Ranger", "Warrior");
        var ruleset = Ruleset.Default;

        Assert.Equal(75, ruleset.UtilityFor("CHA", 90, chaUsingTrio));
        Assert.Equal(50, ruleset.UtilityFor("CHA", 50, chaUsingTrio));
    }

    [Fact]
    public void Ruleset_CharismaHasNoUtilityWithoutAnApplicableClass()
    {
        var trio = new ClassTrio("Ranger", "Warrior", "Cleric");

        Assert.Equal(0, Ruleset.Default.UtilityFor("CHA", -100, trio));
    }

    [Fact]
    public void Ruleset_EnforcesConfiguredEquipmentPositions()
    {
        var trio = new ClassTrio("Ranger", "Bard", "Warrior");
        var position = new EquipmentPosition(SlotType.Ring, 1);
        var ruleset = new Ruleset(
            allowedPositions: new Dictionary<string, IReadOnlySet<SlotType>>(StringComparer.OrdinalIgnoreCase)
            {
                ["Ranger"] = new HashSet<SlotType> { SlotType.Ring }
            });

        Assert.True(ruleset.IsPositionAllowed(position, trio));
    }

    [Fact]
    public void Assessment_KeepsFinalActionSeparateFromConfidence()
    {
        var assessment = new Assessment(
            Guid.NewGuid(),
            FinalAction.Keep,
            RecommendationConfidence.Blocked,
            "Unknown catalog data");

        Assert.Equal(FinalAction.Keep, assessment.FinalAction);
        Assert.Equal(RecommendationConfidence.Blocked, assessment.Confidence);
    }

    [Fact]
    public void DomainCollectionProperties_AreNotMutableCollectionImplementations()
    {
        var item = new CatalogItem(
            "item-1",
            "Test Item",
            ClassSet.Of("Ranger"),
            [new EquipmentPosition(SlotType.Ring)],
            new Dictionary<string, int> { ["AC"] = 10 });
        var ownedItem = new OwnedItemInstance(Guid.NewGuid(), "item-1", 0, InventoryLocation.Carried, []);
        var ruleset = new Ruleset(
            charismaTargets: new Dictionary<string, int> { ["Bard"] = 75 },
            allowedPositions: new Dictionary<string, IReadOnlySet<SlotType>>
            {
                ["Bard"] = new HashSet<SlotType> { SlotType.Ring }
            });

        Assert.False(item.Statistics is Dictionary<string, int>);
        Assert.False(ownedItem.InstalledExaltations is List<InstalledExaltation>);
        Assert.False(ruleset.CharismaTargets is Dictionary<string, int>);
        Assert.False(ruleset.AllowedPositions is Dictionary<string, IReadOnlySet<SlotType>>);
        Assert.All(ruleset.AllowedPositions.Values, positions => Assert.False(positions is HashSet<SlotType>));
    }
}
