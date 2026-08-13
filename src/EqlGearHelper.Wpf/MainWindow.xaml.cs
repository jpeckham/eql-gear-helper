using System.Windows;
using EqlGearHelper.Wpf;
using EqlGearHelper.Wpf.ViewModels;

public partial class MainWindow : Window
{
    public MainWindow() : this(WorkflowComposition.CreateMainViewModel()) { }

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
