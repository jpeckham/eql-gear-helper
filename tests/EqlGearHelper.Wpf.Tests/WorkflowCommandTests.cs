using EqlGearHelper.Wpf.ViewModels;

namespace EqlGearHelper.Wpf.Tests;

public sealed class WorkflowCommandTests
{
    [Fact]
    public async Task ImportCommand_ExposesLoadingThenFailureState()
    {
        var entered = new TaskCompletionSource();
        var release = new TaskCompletionSource();
        var model = new InventoryViewModel();
        model.ConfigureImport(async _ =>
        {
            entered.SetResult();
            await release.Task;
            throw new InvalidOperationException("Unreadable inventory file.");
        });

        model.ImportCommand.Execute(null);
        await entered.Task;

        Assert.True(model.IsLoading);
        Assert.Equal(WorkflowState.Loading, model.State);

        release.SetResult();
        await model.ImportCommand.Completion;

        Assert.Equal(WorkflowState.Failed, model.State);
        Assert.Contains("Unreadable inventory file", model.Status);
    }

    [Fact]
    public async Task BuildCommand_BlocksUntilThreeDistinctClassesAreSelected()
    {
        var invoked = false;
        var model = new BuildPlannerViewModel { ClassOne = "Bard", ClassTwo = "Bard", ClassThree = "Warrior" };
        model.ConfigureBuild(_ => { invoked = true; return Task.CompletedTask; });

        model.BuildSetCommand.Execute(null);
        await model.BuildSetCommand.Completion;

        Assert.False(invoked);
        Assert.Equal(WorkflowState.Blocked, model.State);
        Assert.Contains("three distinct", model.Status, StringComparison.OrdinalIgnoreCase);
    }
}
