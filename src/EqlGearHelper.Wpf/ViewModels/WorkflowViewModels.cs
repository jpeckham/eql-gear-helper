using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace EqlGearHelper.Wpf.ViewModels;

public enum WorkflowState { Empty, Loading, Ready, Failed, Stale, Partial, Blocked }

public abstract class ObservableViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void Notify([CallerMemberName] string? propertyName = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed class AsyncCommand : ICommand
{
    private Func<CancellationToken, Task> _execute = _ => Task.CompletedTask;
    private CancellationTokenSource? _cancellation;
    public bool IsRunning { get; private set; }
    public Task Completion { get; private set; } = Task.CompletedTask;
    public bool CanExecute(object? parameter) => !IsRunning;
    public event EventHandler? CanExecuteChanged;
    public void Configure(Func<CancellationToken, Task> execute) => _execute = execute ?? throw new ArgumentNullException(nameof(execute));
    public async void Execute(object? parameter) => await ExecuteAsync();
    public async Task ExecuteAsync()
    {
        if (IsRunning) return;
        IsRunning = true;
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        _cancellation = new CancellationTokenSource();
        Completion = ExecuteCoreAsync(_cancellation.Token);
        await Completion;
    }
    private async Task ExecuteCoreAsync(CancellationToken token)
    {
        try { await _execute(token); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch { }
        finally
        {
            IsRunning = false;
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
    }
    public void Cancel() => _cancellation?.Cancel();
}

public sealed class MainViewModel
{
    public CleanupViewModel Cleanup { get; } = new();
    public InventoryViewModel Inventory { get; } = new();
    public BuildPlannerViewModel BuildPlanner { get; } = new();
    public ExaltationsViewModel Exaltations { get; } = new();
    public DataViewModel Data { get; } = new();
}

public abstract class WorkflowViewModel : ObservableViewModel
{
    protected AsyncCommand? ActiveCommand;
    private WorkflowState _state = WorkflowState.Empty;
    private string _status = "No data is available yet.";
    public WorkflowState State { get => _state; private set { _state = value; Notify(); Notify(nameof(IsLoading)); } }
    public string Status { get => _status; private set { _status = value; Notify(); } }
    public bool IsLoading => State == WorkflowState.Loading;
    public AsyncCommand CancelCommand { get; } = new();
    protected WorkflowViewModel() => CancelCommand.Configure(_ => { ActiveCommand?.Cancel(); return Task.CompletedTask; });
    protected void SetState(WorkflowState state, string status) { State = state; Status = status; }
    public void ApplyState(WorkflowState state, string status) => SetState(state, status);
    protected void Configure(AsyncCommand command, string loadingText, Func<CancellationToken, Task> operation, string completedText)
    {
        command.Configure(async token =>
        {
            ActiveCommand = command;
            SetState(WorkflowState.Loading, loadingText);
            try
            {
                await operation(token);
                if (State == WorkflowState.Loading) SetState(WorkflowState.Ready, completedText);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { SetState(WorkflowState.Stale, "Operation cancelled. Existing results may be stale."); }
            catch (Exception exception) { SetState(WorkflowState.Failed, exception.Message); }
            finally { if (ReferenceEquals(ActiveCommand, command)) ActiveCommand = null; }
        });
    }
    public void Initialize(string status) => SetState(WorkflowState.Empty, status);
}

public sealed class SummaryCard(string label, string value) { public string Label { get; } = label; public string Value { get; } = value; }
public sealed class LocationNode(string name, string filterValue = "") { public string Name { get; } = name; public string FilterValue { get; } = filterValue; public ObservableCollection<LocationNode> Children { get; } = []; }
public sealed class AssessmentRow(Guid id, string action, string confidence, string explanation, IReadOnlyList<string> reasons, string location = "Unresolved location")
{
    public Guid Id { get; } = id; public string Action { get; } = action; public string Confidence { get; } = confidence; public string Explanation { get; } = explanation; public IReadOnlyList<string> Reasons { get; } = reasons; public string Location { get; } = location;
    public bool CanExportForDisposal => Action == "DisposeCandidate" && Confidence == "Complete";
}

public sealed class CleanupViewModel : WorkflowViewModel
{
    private readonly List<AssessmentRow> _allAssessments = [];
    private string _searchText = string.Empty, _selectedAction = "All actions", _selectedLocation = "All locations";
    private LocationNode? _selectedLocationNode;
    public ObservableCollection<SummaryCard> Cards { get; } = [];
    public ObservableCollection<LocationNode> Locations { get; } = [new("All locations", "All locations")];
    public ObservableCollection<AssessmentRow> Assessments { get; } = [];
    public ObservableCollection<string> Actions { get; } = ["All actions", "Keep", "Investigate", "ExtractExaltation", "DisposeCandidate"];
    public AsyncCommand ImportCommand { get; } = new(); public AsyncCommand ReanalyzeCommand { get; } = new(); public AsyncCommand ExportCommand { get; } = new();
    public string SearchText { get => _searchText; set { _searchText = value ?? string.Empty; Notify(); ApplyFilters(); } }
    public string SelectedAction { get => _selectedAction; set { _selectedAction = value ?? "All actions"; Notify(); ApplyFilters(); } }
    public string SelectedLocation { get => _selectedLocation; set { _selectedLocation = value ?? "All locations"; Notify(); ApplyFilters(); } }
    public LocationNode? SelectedLocationNode { get => _selectedLocationNode; set { _selectedLocationNode = value; Notify(); SelectedLocation = value?.FilterValue ?? "All locations"; } }
    public AssessmentRow? SelectedAssessment { get; set; }
    public void ConfigureImport(Func<CancellationToken, Task> operation) => Configure(ImportCommand, "Importing inventory...", operation, "Inventory import complete.");
    public void ConfigureReanalysis(Func<CancellationToken, Task> operation) => Configure(ReanalyzeCommand, "Analyzing every legal trio...", operation, "Cleanup analysis complete.");
    public void ConfigureExport(Func<CancellationToken, Task> operation) => Configure(ExportCommand, "Creating disposal export...", operation, "Disposal export complete.");
    public void Apply(CleanupViewModel source)
    {
        _allAssessments.Clear(); _allAssessments.AddRange(source.Assessments);
        Cards.Clear(); foreach (var card in source.Cards) Cards.Add(card);
        Locations.Clear(); Locations.Add(new LocationNode("All locations", "All locations"));
        foreach (var location in _allAssessments.Select(item => item.Location).Distinct(StringComparer.OrdinalIgnoreCase).Order()) AddLocationPath(location);
        SetState(source.State, source.Status); ApplyFilters();
    }
    private void ApplyFilters()
    {
        var filtered = _allAssessments.Where(item => (SelectedAction == "All actions" || item.Action == SelectedAction) && (SelectedLocation == "All locations" || item.Location.StartsWith(SelectedLocation, StringComparison.OrdinalIgnoreCase)) && (string.IsNullOrWhiteSpace(SearchText) || item.Explanation.Contains(SearchText, StringComparison.OrdinalIgnoreCase) || item.Id.ToString().Contains(SearchText, StringComparison.OrdinalIgnoreCase))).ToArray();
        Assessments.Clear(); foreach (var row in filtered) Assessments.Add(row);
    }
    private void AddLocationPath(string location)
    {
        var branch = Locations; var accumulated = "";
        foreach (var part in location.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries))
        {
            accumulated = string.IsNullOrEmpty(accumulated) ? part : $"{accumulated}/{part}";
            var node = branch.FirstOrDefault(item => item.Name.Equals(part, StringComparison.OrdinalIgnoreCase));
            if (node is null) { node = new LocationNode(part, accumulated); branch.Add(node); }
            branch = node.Children;
        }
    }
}

public sealed class InventoryRow(string path, string name, string mappingState) { public string Path { get; } = path; public string Name { get; } = name; public string MappingState { get; } = mappingState; }
public sealed class InventoryViewModel : WorkflowViewModel
{
    public ObservableCollection<InventoryRow> Items { get; } = []; public ObservableCollection<string> Coverage { get; } = []; public ObservableCollection<string> ResolutionQueue { get; } = [];
    public AsyncCommand ImportCommand { get; } = new(); public AsyncCommand ManageStorageCommand { get; } = new();
    public void ConfigureImport(Func<CancellationToken, Task> operation) => Configure(ImportCommand, "Importing inventory...", operation, "Inventory import complete.");
    public void ConfigureStorage(Func<CancellationToken, Task> operation) => Configure(ManageStorageCommand, "Updating alternate storage...", operation, "Alternate storage updated.");
    public void Apply(InventoryViewModel source)
    {
        Items.Clear(); foreach (var item in source.Items) Items.Add(item);
        Coverage.Clear(); foreach (var coverage in source.Coverage) Coverage.Add(coverage);
        ResolutionQueue.Clear(); foreach (var row in source.ResolutionQueue) ResolutionQueue.Add(row);
        SetState(source.State, source.Status);
    }
}

public sealed class LoadoutRow(string slot, string item, string location) { public string Slot { get; } = slot; public string Item { get; } = item; public string Location { get; } = location; }
public sealed class GapRow(string slot, string target, string missingEffects) { public string Slot { get; } = slot; public string Target { get; } = target; public string MissingEffects { get; } = missingEffects; }
public sealed class BuildPlannerViewModel : WorkflowViewModel
{
    private string? _classOne, _classTwo, _classThree;
    public ObservableCollection<string> Classes { get; } = []; public ObservableCollection<string> Requirements { get; } = []; public ObservableCollection<LoadoutRow> Loadout { get; } = []; public ObservableCollection<GapRow> Gaps { get; } = [];
    public AsyncCommand BuildSetCommand { get; } = new(); public AsyncCommand CompareCommand { get; } = new();
    public string? ClassOne { get => _classOne; set { _classOne = value; Notify(); } } public string? ClassTwo { get => _classTwo; set { _classTwo = value; Notify(); } } public string? ClassThree { get => _classThree; set { _classThree = value; Notify(); } }
    public GapRow? SelectedGap { get; set; }
    public bool HasExactlyThreeDistinctClasses => !string.IsNullOrWhiteSpace(ClassOne) && !string.IsNullOrWhiteSpace(ClassTwo) && !string.IsNullOrWhiteSpace(ClassThree) && new[] { ClassOne, ClassTwo, ClassThree }.Distinct(StringComparer.OrdinalIgnoreCase).Count() == 3;
    public void SetClassChoices(IEnumerable<string> choices) { Classes.Clear(); foreach (var choice in choices.Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase)) Classes.Add(choice); }
    public void ConfigureBuild(Func<CancellationToken, Task> operation) => BuildSetCommand.Configure(async token =>
    {
        if (!HasExactlyThreeDistinctClasses) { SetState(WorkflowState.Blocked, "Select three distinct classes before building a loadout."); return; }
        ActiveCommand = BuildSetCommand;
        SetState(WorkflowState.Loading, "Building complete owned and target loadouts...");
        try { await operation(token); if (State == WorkflowState.Loading) SetState(WorkflowState.Ready, "Build plan complete."); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { SetState(WorkflowState.Stale, "Build planning was cancelled; displayed results may be stale."); }
        catch (Exception exception) { SetState(WorkflowState.Failed, exception.Message); }
        finally { if (ReferenceEquals(ActiveCommand, BuildSetCommand)) ActiveCommand = null; }
    });
    public void ConfigureComparison(Func<CancellationToken, Task> operation) => Configure(CompareCommand, "Comparing alternatives...", operation, "Alternative comparison complete.");
    public void Apply(BuildPlannerViewModel source)
    {
        Requirements.Clear(); foreach (var requirement in source.Requirements) Requirements.Add(requirement);
        Loadout.Clear(); foreach (var row in source.Loadout) Loadout.Add(row);
        Gaps.Clear(); foreach (var gap in source.Gaps) Gaps.Add(gap);
        SetState(source.State, source.Status);
    }
}

public sealed class ExaltationRow(string name, string host, string resolution) { public string Name { get; } = name; public string Host { get; } = host; public string Resolution { get; } = resolution; }
public sealed class ExaltationsViewModel : WorkflowViewModel { public ObservableCollection<ExaltationRow> Items { get; } = []; public AsyncCommand ResolveCommand { get; } = new(); public void ConfigureResolution(Func<CancellationToken, Task> operation) => Configure(ResolveCommand, "Resolving Exaltation mappings...", operation, "Exaltation resolution complete."); public void Apply(ExaltationsViewModel source) { Items.Clear(); foreach (var item in source.Items) Items.Add(item); SetState(source.State, source.Status); } }
public sealed class DataViewModel : WorkflowViewModel { public string CatalogVersion { get; set; } = "Not imported"; public string SnapshotIdentity { get; set; } = "No snapshot"; public string RulesetVersion { get; set; } = "Not loaded"; public AsyncCommand ImportCatalogCommand { get; } = new(); public AsyncCommand BackupCommand { get; } = new(); public AsyncCommand RecoverCommand { get; } = new(); public void ConfigureCatalog(Func<CancellationToken, Task> operation) => Configure(ImportCatalogCommand, "Importing catalog...", operation, "Catalog import complete."); public void ConfigureBackup(Func<CancellationToken, Task> operation) => Configure(BackupCommand, "Creating backup...", operation, "Backup complete."); public void ConfigureRecovery(Func<CancellationToken, Task> operation) => Configure(RecoverCommand, "Recovering backup...", operation, "Recovery complete."); public void Apply(DataViewModel source) { CatalogVersion = source.CatalogVersion; SnapshotIdentity = source.SnapshotIdentity; RulesetVersion = source.RulesetVersion; SetState(source.State, source.Status); Notify(nameof(CatalogVersion)); Notify(nameof(SnapshotIdentity)); Notify(nameof(RulesetVersion)); } }
