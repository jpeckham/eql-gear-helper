using EqlGearHelper.Wpf.ViewModels;

namespace EqlGearHelper.Wpf.Tests;

public sealed class WorkflowCompositionTests
{
    [Fact]
    public void BuildPlanner_ReceivesTheAvailableRulesetClasses()
    {
        var model = new BuildPlannerViewModel();

        model.SetClassChoices(["Bard", "Ranger", "Warrior"]);

        Assert.Equal(["Bard", "Ranger", "Warrior"], model.Classes);
    }
}
