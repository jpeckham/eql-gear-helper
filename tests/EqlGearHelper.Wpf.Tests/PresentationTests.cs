using EqlGearHelper.Application;
using EqlGearHelper.Domain;
using EqlGearHelper.Wpf.Presenters;

namespace EqlGearHelper.Wpf.Tests;

public sealed class PresentationTests
{
    [Fact]
    public void CleanupPresenter_MapsBlockedConfidenceToNonDisposableViewState()
    {
        var assessment = new CollectionAssetAssessment(
            new Assessment(Guid.NewGuid(), FinalAction.Investigate, RecommendationConfidence.Blocked, "Unknown Exaltation mapping."),
            false,
            true,
            ["Unknown Exaltation mapping"],
            []);

        var model = new CleanupPresenter().Present(new CollectionAnalysisResult([assessment], []));

        Assert.False(model.Assessments[0].CanExportForDisposal);
        Assert.Equal("Blocked", model.Assessments[0].Confidence);
    }

    [Fact]
    public void BuildPlannerPresenter_MapsGapsForTheSelectedSlot()
    {
        var item = new CatalogItem("target", "Target Helm", ClassSet.Of("Bard", "Ranger", "Warrior"), [new EquipmentPosition(SlotType.Head)]);
        var gap = new TargetGap(new EquipmentPosition(SlotType.Head), null, item.CatalogItemId, ["Focus"]);
        var plan = new TargetPlan(new LoadoutPlan(null, null, false, [], []), [new TargetRecommendation(new EquipmentPosition(SlotType.Head), item, [], [])], [gap]);

        var model = new BuildPlannerPresenter().Present(plan);

        Assert.Single(model.Gaps);
        Assert.Equal("Head", model.Gaps[0].Slot);
        Assert.Contains("Focus", model.Gaps[0].MissingEffects);
    }
}
