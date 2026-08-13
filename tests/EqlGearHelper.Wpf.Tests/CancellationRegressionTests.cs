using EqlGearHelper.Wpf.ViewModels;

namespace EqlGearHelper.Wpf.Tests;

public sealed class CancellationRegressionTests
{
    [Fact]
    public async Task CancelCommand_CancelsTheCurrentlyRunningCommandInsteadOfTheLastConfiguredCommand()
    {
        var entered = new TaskCompletionSource();
        var model = new CleanupViewModel();
        model.ConfigureImport(async token =>
        {
            entered.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
        });
        model.ConfigureReanalysis(_ => Task.CompletedTask);

        model.ImportCommand.Execute(null);
        await entered.Task;
        model.CancelCommand.Execute(null);

        var completed = await Task.WhenAny(model.ImportCommand.Completion, Task.Delay(TimeSpan.FromSeconds(1)));

        Assert.Same(model.ImportCommand.Completion, completed);
        Assert.Equal(WorkflowState.Stale, model.State);
    }
}
