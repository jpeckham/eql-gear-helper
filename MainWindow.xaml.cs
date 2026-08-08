using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;

public partial class MainWindow : Window, IItemLookupView, IInventoryAnalysisView
{
    private string? _selectedInventoryPath;
    private readonly IGearLookupGateway _gateway;
    private readonly ItemLookupController _itemLookupController;
    private readonly InventoryAnalysisController _inventoryAnalysisController;

    public MainWindow()
    {
        InitializeComponent();

        _gateway = new GearLookupGateway();
        _itemLookupController = new ItemLookupController(
            new ItemLookupUseCase(_gateway),
            new ItemLookupPresenter(this));
        _inventoryAnalysisController = new InventoryAnalysisController(
            new InventoryAnalysisUseCase(_gateway),
            new InventoryAnalysisPresenter(this));

        SetDefaultInventoryPath();
        ItemQueryTextBox.KeyDown += ItemQueryTextBox_KeyDown;
    }

    private void SetDefaultInventoryPath()
    {
        var defaultPath = _gateway.GetDefaultInventoryFilePath();
        if (string.IsNullOrWhiteSpace(defaultPath) || !File.Exists(defaultPath))
        {
            return;
        }

        _selectedInventoryPath = defaultPath;
        InventoryPathTextBox.Text = defaultPath;
        AnalyzeButton.IsEnabled = true;
    }

    private async void SearchButton_Click(object sender, RoutedEventArgs e)
    {
        await RunItemSearchAsync();
    }

    private async void ItemQueryTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            await RunItemSearchAsync();
            e.Handled = true;
        }
    }

    private async Task RunItemSearchAsync()
    {
        SearchButton.IsEnabled = false;

        try
        {
            await _itemLookupController.SearchAsync(ItemQueryTextBox.Text);
        }
        catch
        {
        }
        finally
        {
            SearchButton.IsEnabled = true;
        }
    }

    private void RenderItemLookupResults(ItemLookupSearchResult? result)
    {
        ItemResultsPanel.Children.Clear();

        if (result is null)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(result.QueryFeedback))
        {
            AddTextLine(result.QueryFeedback!);
        }

        if (!string.IsNullOrWhiteSpace(result.NoGearMatchesMessage))
        {
            AddTextLine(result.NoGearMatchesMessage!);
        }

        if (result.GearMatches.Count > 0)
        {
            foreach (var match in result.GearMatches)
            {
                ItemResultsPanel.Children.Add(BuildItemLookupListItem(match));
            }

            return;
        }

        foreach (var headerLine in result.WeaponResultLines)
        {
            AddTextLine(headerLine);
        }

        foreach (var line in result.WikiResultLines)
        {
            AddTextLine(line);
        }

        if (result.NoWeaponMatchesMessage is not null)
        {
            AddTextLine(result.NoWeaponMatchesMessage);
        }
    }

    private void RenderInventoryLookupResults(IReadOnlyList<ItemLookupMatchSummary> results)
    {
        InventoryResultsPanel.Children.Clear();

        if (results.Count == 0)
        {
            AddInventoryTextLine("No inventory items matched analyzable criteria.");
            return;
        }

        foreach (var match in results.OrderByDescending(match => match.CompositeScore).ThenBy(match => match.ItemName))
        {
            InventoryResultsPanel.Children.Add(BuildItemLookupListItem(match));
        }
    }

    private UIElement BuildItemLookupListItem(ItemLookupMatchSummary match)
    {
        var card = new System.Windows.Controls.Border
        {
            BorderBrush = Brushes.DarkGray,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10),
            Margin = new Thickness(0, 0, 0, 12)
        };

        var panel = new System.Windows.Controls.StackPanel();
        var header = new System.Windows.Controls.Grid
        {
            Margin = new Thickness(0, 0, 0, 4)
        };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        card.Child = panel;
        var nameText = new System.Windows.Controls.TextBlock
        {
            Text = match.ItemName,
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            Foreground = SystemColors.ControlTextBrush
        };
        Grid.SetColumn(nameText, 0);

        var composite = match.CompositeScore;
        var barBrush = GetQualityBrush(match.CompositeScore, higherIsBetter: true);
        var scorePanel = new System.Windows.Controls.StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center
        };
        var scoreBar = new ProgressBar
        {
            Width = 140,
            Height = 16,
            Minimum = 0,
            Maximum = 100,
            Value = composite,
            Foreground = barBrush,
            Background = Brushes.DimGray,
            VerticalAlignment = VerticalAlignment.Center
        };
        var scoreText = new System.Windows.Controls.TextBlock
        {
            Text = $"{composite:0.0}%",
            Margin = new Thickness(8, 0, 0, 0),
            Foreground = barBrush,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };
        scorePanel.Children.Add(scoreBar);
        scorePanel.Children.Add(scoreText);
        Grid.SetColumn(scorePanel, 1);

        var detailsButton = new System.Windows.Controls.Button
        {
            Content = "Details",
            Padding = new Thickness(10, 2, 10, 2),
            Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(detailsButton, 2);
        header.Children.Add(nameText);
        header.Children.Add(scorePanel);
        header.Children.Add(detailsButton);

        var detailsPanel = BuildItemDetailPanel(match);
        detailsPanel.Visibility = Visibility.Collapsed;

        detailsButton.Click += (_, _) =>
        {
            if (detailsPanel.Visibility == Visibility.Visible)
            {
                detailsPanel.Visibility = Visibility.Collapsed;
                detailsButton.Content = "Details";
            }
            else
            {
                detailsPanel.Visibility = Visibility.Visible;
                detailsButton.Content = "Hide";
            }
        };

        panel.Children.Add(header);
        panel.Children.Add(detailsPanel);
        return card;
    }

    private UIElement BuildItemDetailPanel(ItemLookupMatchSummary match)
    {
        var detailsRoot = new System.Windows.Controls.StackPanel
        {
            Margin = new Thickness(0, 8, 0, 0)
        };

        if (!string.IsNullOrWhiteSpace(match.Source))
        {
            AddTextLine(detailsRoot, $"Source: {match.Source}");
        }
        else
        {
            AddTextLine(detailsRoot, "Source: n/a");
        }

        AddTextLine(detailsRoot, $"Slots: {match.Slots}");
        AddTextLine(detailsRoot, $"AC: {match.Ac:0}");
        AddTextLine(detailsRoot, $"Quick read: {match.QualityLabel}");
        if (match.NotableStats.Count > 0)
        {
            AddTextLine(detailsRoot, $"Notable stats: {string.Join(", ", match.NotableStats)}");
        }

        AddTextLine(detailsRoot, "Per class score:", isBold: true, margin: new Thickness(0, 6, 0, 4));
        foreach (var classScore in match.ClassCompositeScores.OrderBy(item => item.ClassName))
        {
            var classRow = new System.Windows.Controls.StackPanel
            {
                Margin = new Thickness(0, 0, 0, 8)
            };

            AddTextLine(classRow, classScore.ClassName, isBold: true, margin: new Thickness(0, 0, 0, 3));
            var classScoreBrush = GetQualityBrush(classScore.CompositeScore, higherIsBetter: true);
            var classScorePanel = new System.Windows.Controls.StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 0, 0, 2),
                VerticalAlignment = VerticalAlignment.Center
            };

            var classScoreBar = new ProgressBar
            {
                Width = 180,
                Height = 12,
                Minimum = 0,
                Maximum = 100,
                Value = classScore.CompositeScore,
                Foreground = classScoreBrush,
                Background = Brushes.DimGray,
                VerticalAlignment = VerticalAlignment.Center
            };

            var classScoreText = new System.Windows.Controls.TextBlock
            {
                Text = $"{classScore.CompositeScore:0.0}%",
                Margin = new Thickness(8, 0, 0, 0),
                Foreground = classScoreBrush,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            };

            classScorePanel.Children.Add(classScoreBar);
            classScorePanel.Children.Add(classScoreText);
            classRow.Children.Add(classScorePanel);

            var betterText = string.IsNullOrWhiteSpace(classScore.BetterItem)
                ? "Better option: none"
                : $"Better option: {classScore.BetterItem}";
            AddTextLine(classRow, betterText, margin: new Thickness(6, 2, 0, 0), foreground: classScoreBrush);

            detailsRoot.Children.Add(classRow);
        }

        if (match.ClassCompositeScores.Count == 0)
        {
            AddTextLine(detailsRoot, "Per-class scores unavailable.", margin: new Thickness(0, 4, 0, 0));
        }

        return detailsRoot;
    }

    private static Brush GetQualityBrush(double qualityPercentile, bool higherIsBetter)
    {
        if (higherIsBetter)
        {
            if (qualityPercentile >= 90.0)
            {
                return Brushes.Green;
            }

            if (qualityPercentile >= 50.0)
            {
                return Brushes.Goldenrod;
            }

            return Brushes.Red;
        }

        if (qualityPercentile <= 25.0)
        {
            return Brushes.Green;
        }

        if (qualityPercentile <= 50.0)
        {
            return Brushes.Goldenrod;
        }

        return Brushes.Red;
    }

    private void AddTextLine(
        string text,
        FontWeight fontWeight = default,
        Brush? foreground = null,
        Thickness? margin = null)
    {
        if (ItemResultsPanel is null)
        {
            return;
        }

        var line = new System.Windows.Controls.TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            FontWeight = fontWeight == default ? FontWeights.Normal : fontWeight,
            Foreground = foreground ?? SystemColors.ControlTextBrush,
            Margin = margin ?? new Thickness(0, 0, 0, 2)
        };
        ItemResultsPanel.Children.Add(line);
    }

    private void AddTextLine(
        System.Windows.Controls.StackPanel panel,
        string text,
        bool isBold = false,
        Brush? foreground = null,
        Thickness? margin = null)
    {
        var line = new System.Windows.Controls.TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            FontWeight = isBold ? FontWeights.SemiBold : FontWeights.Normal,
            Foreground = foreground ?? SystemColors.ControlTextBrush,
            Margin = margin ?? new Thickness(0, 0, 0, 2)
        };

        panel.Children.Add(line);
    }

    private void AddInventoryTextLine(string text)
    {
        if (InventoryResultsPanel is null)
        {
            return;
        }

        var line = new System.Windows.Controls.TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 2)
        };
        InventoryResultsPanel.Children.Add(line);
    }

    private void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select your inventory export",
            Filter = "EQ Inventory Files (*.txt)|*.txt|All Files (*.*)|*.*",
            FilterIndex = 1,
            DefaultExt = "txt",
            CheckFileExists = true,
            CheckPathExists = true
        };

        var defaultDirectory = _gateway.GetDefaultInventoryDirectory();
        if (!string.IsNullOrWhiteSpace(defaultDirectory) && Directory.Exists(defaultDirectory))
        {
            dialog.InitialDirectory = defaultDirectory;
        }

        var defaultFile = _gateway.GetDefaultInventoryFilePath();
        if (!string.IsNullOrWhiteSpace(defaultFile) && File.Exists(defaultFile))
        {
            dialog.FileName = Path.GetFileName(defaultFile);
        }

        if (dialog.ShowDialog(this) == true)
        {
            _selectedInventoryPath = dialog.FileName;
            InventoryPathTextBox.Text = _selectedInventoryPath;
            AnalyzeButton.IsEnabled = true;
            StatusTextBlock.Text = $"Selected file: {Path.GetFileName(_selectedInventoryPath)}";
        }
    }

    private async void AnalyzeButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_selectedInventoryPath) || !File.Exists(_selectedInventoryPath))
        {
            InventoryStatusText = "Pick a valid .txt file first.";
            return;
        }

        AnalyzeButton.IsEnabled = false;
        try
        {
            await _inventoryAnalysisController.AnalyzeAsync(_selectedInventoryPath);
        }
        catch
        {
        }
        finally
        {
            AnalyzeButton.IsEnabled = true;
        }
    }

    public string ItemLookupStatusText
    {
        set => RunUiAction(() => ItemStatusTextBlock.Text = value);
    }

    public ItemLookupSearchResult? LookupResult
    {
        set => RunUiAction(() => RenderItemLookupResults(value));
    }

    public void ClearLookupResults()
    {
        RunUiAction(() => ItemResultsPanel.Children.Clear());
    }

    public string InventoryStatusText
    {
        set => RunUiAction(() => StatusTextBlock.Text = value);
    }

    public IReadOnlyList<ItemLookupMatchSummary>? InventoryItemLookupResults
    {
        set => RunUiAction(() => RenderInventoryLookupResults(value ?? Array.Empty<ItemLookupMatchSummary>()));
    }

    public string InventoryOutputText
    {
        set
        {
            // kept for interface compatibility with potential non-list-based presentation paths
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }
        }
    }

    private void RunUiAction(Action action)
    {
        if (Dispatcher.CheckAccess())
        {
            action();
            return;
        }

        Dispatcher.Invoke(action);
    }
}
