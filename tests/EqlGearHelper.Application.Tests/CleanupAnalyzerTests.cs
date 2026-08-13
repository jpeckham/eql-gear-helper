using EqlGearHelper.Application;
using EqlGearHelper.Domain;

namespace EqlGearHelper.Application.Tests;

public sealed class CleanupAnalyzerTests
{
    [Fact]
    public async Task ExecuteAsync_EvaluatesEveryLegalTrioAndRetainsRepresentativeUse()
    {
        var helmet = Item("helm", "Useful Helm", ClassSet.Of("Bard", "Ranger", "Warrior", "Mage"), SlotType.Head, 10);
        var item = Owned(helmet);
        var result = await Analyzer().ExecuteAsync(new AnalyzeCollectionRequest(
            new Collection([helmet], [item]), Rules(SlotType.Head)), CancellationToken.None);

        var assessment = Assert.Single(result.Assessments);
        Assert.Equal(4, result.AnalyzedTrios.Count);
        Assert.Equal(FinalAction.Keep, assessment.FinalAction);
        Assert.NotEmpty(assessment.RepresentativeUses);
    }

    [Fact]
    public async Task ValuableExtractableExaltation_NeverReturnsPlainDisposeCandidate()
    {
        var helm = Item("helm", "Donor Helm", ClassSet.Of("Bard", "Ranger", "Warrior"), SlotType.Head, 0);
        var item = Owned(helm, [new InstalledExaltation("spell-damage", helm.Classes, helm.Classes, Guid.NewGuid())]);
        var result = await Analyzer().ExecuteAsync(new AnalyzeCollectionRequest(
            new Collection([helm], [item]), Rules(SlotType.Head),
            new Dictionary<string, ExaltationDefinition> { ["spell-damage"] = new("spell-damage", "Spell Damage", helm.Classes, true) }), CancellationToken.None);

        Assert.NotEqual(FinalAction.DisposeCandidate, Assert.Single(result.Assessments).FinalAction);
    }

    [Fact]
    public async Task UnknownData_BlocksAPlainDisposalRecommendation()
    {
        var helm = Item("helm", "Unknown Helm", ClassSet.Of("Bard", "Ranger", "Warrior"), SlotType.Head, 0);
        var item = Owned(helm);
        var result = await Analyzer().ExecuteAsync(new AnalyzeCollectionRequest(
            new Collection([helm], [item]), Rules(SlotType.Head), UnknownAssetIds: new HashSet<Guid> { item.InstanceId }), CancellationToken.None);

        var assessment = Assert.Single(result.Assessments);
        Assert.Equal(RecommendationConfidence.Blocked, assessment.Confidence);
        Assert.NotEqual(FinalAction.DisposeCandidate, assessment.FinalAction);
    }

    [Fact]
    public async Task InstalledExaltation_BlocksDisposalUntilExplicitlySafelyResolved()
    {
        var weak = Item("weak", "Weak Helm", ClassSet.Of("Bard", "Ranger", "Warrior"), SlotType.Head, 1);
        var strong = Item("strong", "Strong Helm", ClassSet.Of("Bard", "Ranger", "Warrior"), SlotType.Head, 10);
        var weakItem = Owned(weak, [new InstalledExaltation("inert", weak.Classes, weak.Classes, Guid.NewGuid())]);
        var collection = new Collection([weak, strong], [weakItem, Owned(strong)]);
        var definitions = new Dictionary<string, ExaltationDefinition> { ["inert"] = new("inert", "Inert", weak.Classes) };

        var blocked = await Analyzer().ExecuteAsync(new AnalyzeCollectionRequest(collection, Rules(SlotType.Head), definitions), CancellationToken.None);
        var resolved = await Analyzer().ExecuteAsync(new AnalyzeCollectionRequest(collection, Rules(SlotType.Head), definitions, SafelyResolvedExaltationIds: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "inert" }), CancellationToken.None);

        Assert.NotEqual(FinalAction.DisposeCandidate, blocked.Assessments.Single(assessment => assessment.AssetInstanceId == weakItem.InstanceId).FinalAction);
        Assert.Equal(FinalAction.DisposeCandidate, resolved.Assessments.Single(assessment => assessment.AssetInstanceId == weakItem.InstanceId).FinalAction);
    }

    [Fact]
    public async Task MaterialityTolerance_RequiresRemovalRegressionAcrossEveryTrioBeforeRetention()
    {
        var strong = Item("strong", "Strong Helm", ClassSet.Of("Bard", "Ranger", "Warrior"), SlotType.Head, 10);
        var close = Item("close", "Close Helm", ClassSet.Of("Bard", "Ranger", "Warrior"), SlotType.Head, 9);
        var strongItem = Owned(strong);
        var collection = new Collection([strong, close], [strongItem, Owned(close)]);

        var material = await Analyzer().ExecuteAsync(new AnalyzeCollectionRequest(collection, Rules(SlotType.Head), MaterialityTolerance: 0), CancellationToken.None);
        var tolerated = await Analyzer().ExecuteAsync(new AnalyzeCollectionRequest(collection, Rules(SlotType.Head), MaterialityTolerance: 1), CancellationToken.None);

        Assert.Equal(FinalAction.Keep, material.Assessments.Single(assessment => assessment.AssetInstanceId == strongItem.InstanceId).FinalAction);
        Assert.Equal(FinalAction.DisposeCandidate, tolerated.Assessments.Single(assessment => assessment.AssetInstanceId == strongItem.InstanceId).FinalAction);
    }

    [Fact]
    public async Task RemovalOfSoleEffectOrMetNamedRequirement_RemainsMaterialRegardlessOfTolerance()
    {
        var vital = new CatalogItem("a-vital", "Vital Helm", ClassSet.Of("Bard", "Ranger", "Warrior"), [new EquipmentPosition(SlotType.Head)],
            new Dictionary<string, int> { ["DEX"] = 10, ["STR"] = 1 }, effects: [new GearEffect("Spell Focus")]);
        var fallback = new CatalogItem("z-fallback", "Fallback Helm", ClassSet.Of("Bard", "Ranger", "Warrior"), [new EquipmentPosition(SlotType.Head)]);
        var vitalItem = Owned(vital);
        var rules = new Ruleset(requiredPositions: [new EquipmentPosition(SlotType.Head)], requirements: [new NamedRequirement("Dexterity", "DEX", 10)]);

        var result = await Analyzer().ExecuteAsync(new AnalyzeCollectionRequest(new Collection([vital, fallback], [vitalItem, Owned(fallback)]), rules, MaterialityTolerance: double.MaxValue), CancellationToken.None);

        var assessment = result.Assessments.Single(candidate => candidate.AssetInstanceId == vitalItem.InstanceId);
        Assert.True(assessment.BaseUseful);
        Assert.Equal(FinalAction.Keep, assessment.FinalAction);
    }

    [Fact]
    public async Task ExportAsync_ExportsOnlyCompleteDisposalsInLocationOrder()
    {
        var first = Item("first", "First", ClassSet.Of("Bard", "Ranger", "Warrior"), SlotType.Head, 0);
        var second = Item("second", "Second", ClassSet.Of("Bard", "Ranger", "Warrior"), SlotType.Face, 0);
        var firstItem = new OwnedItemInstance(Guid.NewGuid(), first.CatalogItemId, 0, new InventoryLocation("Zeta"), []);
        var secondItem = new OwnedItemInstance(Guid.NewGuid(), second.CatalogItemId, 0, new InventoryLocation("Alpha"), []);
        var export = await new DisposalExportUseCase().ExecuteAsync(new DisposalExportRequest(
            [
                new Assessment(firstItem.InstanceId, FinalAction.DisposeCandidate, RecommendationConfidence.Complete, "Safe"),
                new Assessment(secondItem.InstanceId, FinalAction.DisposeCandidate, RecommendationConfidence.Complete, "Safe"),
                new Assessment(Guid.NewGuid(), FinalAction.DisposeCandidate, RecommendationConfidence.Blocked, "Unknown")
            ],
            new Dictionary<Guid, InventoryLocation> { [firstItem.InstanceId] = firstItem.Location, [secondItem.InstanceId] = secondItem.Location }), CancellationToken.None);

        Assert.Equal([secondItem.InstanceId, firstItem.InstanceId], export.Rows.Select(row => row.AssetInstanceId));
    }

    private static AnalyzeCollectionUseCase Analyzer() => new(new LoadoutAssignmentService(), new LoadoutEvaluator());

    private static Ruleset Rules(params SlotType[] positions) => new(requiredPositions: positions.Select((type, index) => new EquipmentPosition(type, index)).ToArray());

    private static CatalogItem Item(string id, string name, ClassSet classes, SlotType position, int strength) =>
        new(id, name, classes, [new EquipmentPosition(position)], new Dictionary<string, int> { ["STR"] = strength });

    private static OwnedItemInstance Owned(CatalogItem item, IReadOnlyList<InstalledExaltation>? exaltations = null) =>
        new(Guid.NewGuid(), item.CatalogItemId, 0, InventoryLocation.Carried, exaltations ?? []);
}
