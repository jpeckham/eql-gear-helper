using System.Windows;
using EqlGearHelper.Wpf;

public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var operations = await LocalWorkflowComposition.CreateAsync();
        var model = WorkflowComposition.CreateMainViewModel(operations);
        model.Inventory.Apply(await operations.LoadInventory(CancellationToken.None));
        MainWindow = new MainWindow(model);
        MainWindow.Show();
    }
}
