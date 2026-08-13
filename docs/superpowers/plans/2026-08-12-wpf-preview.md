# EQL Gear Analyzer WPF Preview Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a separately runnable, zero-backend WPF preview for the Build Planner, Cleanup, and Inventory screens so the user can approve the visual design before production services are connected.

**Architecture:** Add an isolated `EqlGearHelper.Preview` WPF executable that references no production project and obtains all display state from deterministic mock view models. Use one shared resource theme and three focused UserControls hosted by a left-positioned native `TabControl`; presentation-only tests validate the mock contracts while compiled XAML and a process smoke check validate the executable.

**Tech Stack:** .NET 8, C# 12, WPF/XAML, xUnit 2.9.3, Microsoft.NET.Test.Sdk 17.14.1.

## Global Constraints

- The preview targets `net8.0-windows`, uses WPF, and runs as a Windows `WinExe`.
- `EqlGearHelper.Preview` has no project reference to Domain, Application, Infrastructure, or the production WPF project.
- The preview performs no HTTP, SQLite, filesystem, catalog, or inventory-import work; all displayed values are deterministic mock data.
- Build Planner displays Current State, Best Available, and editable Goal State for every visible equipment position.
- Manual Goal State selection changes only the selected position and never recomputes another row.
- Acquisition filters are visual controls in this preview; mock dropdown items demonstrate allowed, filtered-only, mixed-route, and unresolved-source states.
- A mixed-route item is shown as Auto-BiS eligible when at least one route is unfiltered; an unresolved-source item is also shown as eligible.
- Cleanup copy explains that usefulness is evaluated across every legal race and three-class combination.
- Inventory groups equivalent variants for display while expandable child rows preserve physical copies and locations.
- Duplicate warnings appear above two copies for rings, earrings, wrists, and compatible one-handed weapons, and above one copy for ordinary single-position gear; warnings do not claim an item is disposable.
- Controls use native WPF tabs, checkboxes, combo boxes, expanders, and tooltips. Preview action buttons are visibly labeled `Preview only` through their tooltips and execute no production operation.
- Preserve unrelated working-tree changes and stage only files belonging to the task being committed.
- After every code-changing task, run `dotnet build eql-gear-helper.sln`, then `dotnet test eql-gear-helper.sln --no-build`, then launch the preview executable and verify that it remains running and responsive for five seconds through startup initialization.

---

## Target file structure

```text
src/EqlGearHelper.Preview/
  EqlGearHelper.Preview.csproj
  App.xaml
  App.xaml.cs
  MainWindow.xaml
  MainWindow.xaml.cs
  Models/PreviewModels.cs
  ViewModels/PreviewViewModel.cs
  ViewModels/MockPreviewData.cs
  Views/BuildPlannerPreview.xaml
  Views/BuildPlannerPreview.xaml.cs
  Views/CleanupPreview.xaml
  Views/CleanupPreview.xaml.cs
  Views/InventoryPreview.xaml
  Views/InventoryPreview.xaml.cs
  Themes/Colors.xaml
  Themes/Controls.xaml
tests/EqlGearHelper.Preview.Tests/
  EqlGearHelper.Preview.Tests.csproj
  PreviewContractTests.cs
docs/review/wpf-preview.md
```

### Task 1: Create the isolated preview shell and shared visual language

**Files:**
- Create: `src/EqlGearHelper.Preview/EqlGearHelper.Preview.csproj`
- Create: `src/EqlGearHelper.Preview/App.xaml`
- Create: `src/EqlGearHelper.Preview/App.xaml.cs`
- Create: `src/EqlGearHelper.Preview/MainWindow.xaml`
- Create: `src/EqlGearHelper.Preview/MainWindow.xaml.cs`
- Create: `src/EqlGearHelper.Preview/Themes/Colors.xaml`
- Create: `src/EqlGearHelper.Preview/Themes/Controls.xaml`
- Create: `tests/EqlGearHelper.Preview.Tests/EqlGearHelper.Preview.Tests.csproj`
- Create: `tests/EqlGearHelper.Preview.Tests/PreviewContractTests.cs`
- Modify: `eql-gear-helper.sln`

**Interfaces:**
- Produces: `EqlGearHelper.Preview.MainWindow`, the standalone preview executable, and resource keys consumed by all preview views.
- Produces resource keys: `AppBackgroundBrush`, `SurfaceBrush`, `BorderBrush`, `PrimaryBrush`, `SuccessBrush`, `WarningBrush`, `DangerBrush`, `MutedTextBrush`, `PanelStyle`, `SectionTitleStyle`, `PillStyle`, and `PreviewButtonStyle`.
- Consumes: no production assembly.

- [ ] **Step 1: Add the failing isolation test and test project**

Create the test project with only a preview project reference:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0-windows</TargetFramework>
    <UseWPF>true</UseWPF>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.14.1" />
    <PackageReference Include="xunit" Version="2.9.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="3.1.4" />
  </ItemGroup>
  <ItemGroup><Using Include="Xunit" /></ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\EqlGearHelper.Preview\EqlGearHelper.Preview.csproj" />
  </ItemGroup>
</Project>
```

Add this contract test:

```csharp
using EqlGearHelper.Preview;

namespace EqlGearHelper.Preview.Tests;

public sealed class PreviewContractTests
{
    [Fact]
    public void PreviewAssembly_DoesNotReferenceProductionAssemblies()
    {
        var names = typeof(MainWindow).Assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .ToArray();

        Assert.DoesNotContain("EqlGearHelper.Domain", names);
        Assert.DoesNotContain("EqlGearHelper.Application", names);
        Assert.DoesNotContain("EqlGearHelper.Infrastructure", names);
        Assert.DoesNotContain("EqlGearHelper.Wpf", names);
    }
}
```

- [ ] **Step 2: Run the isolation test and verify it fails**

Run:

```powershell
dotnet test tests/EqlGearHelper.Preview.Tests/EqlGearHelper.Preview.Tests.csproj --filter PreviewAssembly_DoesNotReferenceProductionAssemblies
```

Expected: FAIL because `EqlGearHelper.Preview.csproj` and `MainWindow` do not exist.

- [ ] **Step 3: Create the standalone WPF project and add both projects to the solution**

Use this project definition with no `ProjectReference` item:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net8.0-windows</TargetFramework>
    <RootNamespace>EqlGearHelper.Preview</RootNamespace>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <LangVersion>preview</LangVersion>
    <UseWPF>true</UseWPF>
  </PropertyGroup>
</Project>
```

Add the projects:

```powershell
dotnet sln eql-gear-helper.sln add src/EqlGearHelper.Preview/EqlGearHelper.Preview.csproj --solution-folder src
dotnet sln eql-gear-helper.sln add tests/EqlGearHelper.Preview.Tests/EqlGearHelper.Preview.Tests.csproj --solution-folder tests
```

- [ ] **Step 4: Define the shared palette and control styles**

In `Colors.xaml`, define explicit light-theme colors and brushes:

```xml
<Color x:Key="AppBackgroundColor">#F5F7FB</Color>
<Color x:Key="SurfaceColor">#FFFFFFFF</Color>
<Color x:Key="BorderColor">#FFDCE2EA</Color>
<Color x:Key="PrimaryColor">#FF2563D9</Color>
<Color x:Key="SuccessColor">#FF2E7D32</Color>
<Color x:Key="WarningColor">#FFC77800</Color>
<Color x:Key="DangerColor">#FFC83E3E</Color>
<Color x:Key="MutedTextColor">#FF5F6B7A</Color>
<SolidColorBrush x:Key="AppBackgroundBrush" Color="{StaticResource AppBackgroundColor}" />
<SolidColorBrush x:Key="SurfaceBrush" Color="{StaticResource SurfaceColor}" />
<SolidColorBrush x:Key="BorderBrush" Color="{StaticResource BorderColor}" />
<SolidColorBrush x:Key="PrimaryBrush" Color="{StaticResource PrimaryColor}" />
<SolidColorBrush x:Key="SuccessBrush" Color="{StaticResource SuccessColor}" />
<SolidColorBrush x:Key="WarningBrush" Color="{StaticResource WarningColor}" />
<SolidColorBrush x:Key="DangerBrush" Color="{StaticResource DangerColor}" />
<SolidColorBrush x:Key="MutedTextBrush" Color="{StaticResource MutedTextColor}" />
```

In `Controls.xaml`, define focused styles for panels, section headers, pills, buttons, text inputs, and a left-navigation `TabItem`. Keep touch targets at least 32 device-independent pixels high and use `Segoe UI` throughout.

- [ ] **Step 5: Create the application resources and native tab shell**

Merge both dictionaries in `App.xaml`, instantiate `MainWindow` in `App.OnStartup`, and build a 1440x900 centered window with:

```xml
<TabControl TabStripPlacement="Left" Background="{StaticResource AppBackgroundBrush}">
  <TabItem Header="Build Planner">
    <Border Style="{StaticResource PanelStyle}">
      <TextBlock Text="Build Planner preview loads in Task 2" />
    </Border>
  </TabItem>
  <TabItem Header="Cleanup">
    <Border Style="{StaticResource PanelStyle}">
      <TextBlock Text="Cleanup preview loads in Task 3" />
    </Border>
  </TabItem>
  <TabItem Header="Inventory">
    <Border Style="{StaticResource PanelStyle}">
      <TextBlock Text="Inventory preview loads in Task 4" />
    </Border>
  </TabItem>
</TabControl>
```

- [ ] **Step 6: Run the task verification gate**

Run:

```powershell
dotnet build eql-gear-helper.sln
dotnet test eql-gear-helper.sln --no-build
$previewProcess = Start-Process -FilePath 'src\EqlGearHelper.Preview\bin\Debug\net8.0-windows\EqlGearHelper.Preview.exe' -PassThru
Start-Sleep -Seconds 5
$previewProcess.Refresh()
if ($previewProcess.HasExited -or -not $previewProcess.Responding) { throw 'WPF preview failed startup smoke check.' }
Stop-Process -Id $previewProcess.Id
```

Expected: build succeeds, all tests pass, and the preview remains running and responsive for five seconds.

- [ ] **Step 7: Commit the isolated shell**

```powershell
git add -- eql-gear-helper.sln src/EqlGearHelper.Preview tests/EqlGearHelper.Preview.Tests
git commit -m "feat: add isolated WPF preview shell"
```

### Task 2: Build the three-state Build Planner preview

**Files:**
- Create: `src/EqlGearHelper.Preview/Models/PreviewModels.cs`
- Create: `src/EqlGearHelper.Preview/ViewModels/PreviewViewModel.cs`
- Create: `src/EqlGearHelper.Preview/ViewModels/MockPreviewData.cs`
- Create: `src/EqlGearHelper.Preview/Views/BuildPlannerPreview.xaml`
- Create: `src/EqlGearHelper.Preview/Views/BuildPlannerPreview.xaml.cs`
- Modify: `src/EqlGearHelper.Preview/MainWindow.xaml`
- Modify: `src/EqlGearHelper.Preview/MainWindow.xaml.cs`
- Modify: `tests/EqlGearHelper.Preview.Tests/PreviewContractTests.cs`

**Interfaces:**
- Produces: `PreviewViewModel.BuildPlanner` of type `BuildPlannerPreviewViewModel`.
- Produces: `SlotComparisonPreview.Goal` as the only mutable item choice in a slot row.
- Produces: `ItemChoicePreview.Tooltip` containing acquisition-route evidence and Auto-BiS eligibility text.
- Consumes: shared resource keys from Task 1.

- [ ] **Step 1: Write failing mock-contract tests**

Add tests that establish the visual semantics:

```csharp
[Fact]
public void ChangingOneGoal_DoesNotChangeAnyOtherSlot()
{
    var planner = MockPreviewData.Create().BuildPlanner;
    var head = planner.Slots.Single(row => row.Slot == "Head");
    var chest = planner.Slots.Single(row => row.Slot == "Chest");
    var originalHeadGoal = head.Goal;
    var originalChestGoal = chest.Goal;

    var lowerScoringChoice = head.GoalChoices.MinBy(choice => choice.Score)!;
    Assert.True(lowerScoringChoice.Score < originalHeadGoal.Score);
    head.Goal = lowerScoringChoice;

    Assert.Same(originalChestGoal, chest.Goal);
    Assert.Same(lowerScoringChoice, head.Goal);
}

[Fact]
public void AcquisitionExamples_CoverMixedFilteredAndUnresolvedStates()
{
    var choices = MockPreviewData.Create().BuildPlanner.Slots
        .SelectMany(row => row.GoalChoices)
        .ToArray();

    Assert.Contains(choices, item => item.Badge == "Allowed route remains");
    Assert.Contains(choices, item => item.Badge == "Filtered from Auto-BiS");
    Assert.Contains(choices, item => item.Badge == "Source unresolved" && item.AutoBisEligible);
}
```

- [ ] **Step 2: Run the Build Planner contract tests and verify they fail**

Run:

```powershell
dotnet test tests/EqlGearHelper.Preview.Tests/EqlGearHelper.Preview.Tests.csproj --filter "ChangingOneGoal|AcquisitionExamples"
```

Expected: FAIL because the preview models and mock data do not exist.

- [ ] **Step 3: Implement presentation-only models**

Define these exact contracts in `PreviewModels.cs`:

```csharp
public enum RouteVisualState { Allowed, Filtered, Unresolved }

public sealed record AcquisitionRoutePreview(
    string Label,
    string Evidence,
    RouteVisualState State);

public sealed record ItemChoicePreview(
    string Name,
    int Score,
    string Summary,
    string Badge,
    bool AutoBisEligible,
    IReadOnlyList<AcquisitionRoutePreview> Routes)
{
    public string Tooltip => string.Join(Environment.NewLine,
        new[] { AutoBisEligible ? "Auto-BiS eligible" : "Filtered from Auto-BiS" }
            .Concat(Routes.Select(route =>
                $"{route.State}: {route.Label} — {route.Evidence}")));
}

public sealed class SlotComparisonPreview : INotifyPropertyChanged
{
    private ItemChoicePreview _goal = null!;

    public required string Slot { get; init; }
    public required ItemChoicePreview Current { get; init; }
    public required ItemChoicePreview BestAvailable { get; init; }
    public required IReadOnlyList<ItemChoicePreview> GoalChoices { get; init; }
    public required string OwnedLocation { get; init; }
    public required string Delta { get; init; }

    public ItemChoicePreview Goal
    {
        get => _goal;
        set { _goal = value; PropertyChanged?.Invoke(this, new(nameof(Goal))); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

public sealed record SummaryCardPreview(string Label, string Value, string Tone);

public sealed class ToggleOptionPreview
{
    public required string Label { get; init; }
    public bool IsChecked { get; set; }
}
```

- [ ] **Step 4: Create deterministic Build Planner mock data**

Define the root and Build Planner view models in `PreviewViewModel.cs`:

```csharp
public sealed class PreviewViewModel
{
    public required BuildPlannerPreviewViewModel BuildPlanner { get; init; }
    public required CleanupPreviewViewModel Cleanup { get; init; }
    public required InventoryPreviewViewModel Inventory { get; init; }
}

public sealed class BuildPlannerPreviewViewModel : INotifyPropertyChanged
{
    private SlotComparisonPreview _selectedSlot = null!;

    public required IReadOnlyList<string> Races { get; init; }
    public required string SelectedRace { get; set; }
    public required IReadOnlyList<string> Classes { get; init; }
    public required string ClassOne { get; set; }
    public required string ClassTwo { get; set; }
    public required string ClassThree { get; set; }
    public required IReadOnlyList<string> Upgrades { get; init; }
    public required string SelectedUpgrade { get; set; }
    public required IReadOnlyList<ToggleOptionPreview> PriorityStats { get; init; }
    public required IReadOnlyList<ToggleOptionPreview> AcquisitionFilters { get; init; }
    public required IReadOnlyList<SummaryCardPreview> SummaryCards { get; init; }
    public required IReadOnlyList<SlotComparisonPreview> Slots { get; init; }
    public SlotComparisonPreview SelectedSlot
    {
        get => _selectedSlot;
        set { _selectedSlot = value; PropertyChanged?.Invoke(this, new(nameof(SelectedSlot))); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

public sealed class CleanupPreviewViewModel { }

public sealed class InventoryPreviewViewModel { }
```

During Task 2, `MockPreviewData.Create()` initializes the two empty shells with
`new CleanupPreviewViewModel()` and `new InventoryPreviewViewModel()`. Tasks 3
and 4 expand those exact classes with their complete presentation contracts.

`MockPreviewData.Create()` returns a `PreviewViewModel` with race `Ogre`, classes `Shadow Knight`, `Enchanter`, and `Wizard`, upgrade `+6`, and checked priority stats `AC`, `HP`, and `INT`. Include checked acquisition filters for Plane of Sky, Plane of Fear, quest rewards, and player-crafted items.

Populate at least Head, Ear 1, Ear 2, Wrist 1, Wrist 2, Chest, Primary, Secondary, Ring 1, and Ring 2. On Head and Chest, make Current differ from Best Available; give Current the location `Equipped` and Best Available a bank or nested-bag location. Give Head at least two compatible Goal choices where the initially selected item has a higher score than the manually selectable alternative. Include these source demonstrations:

```csharp
var mixedRoute = new ItemChoicePreview(
    "Anthemion Breastplate +6", 120, "+AC · +HP · +INT",
    "Allowed route remains", true,
    [
        new("Plane of Fear", "Drops From: Plane of Fear: various mobs", RouteVisualState.Filtered),
        new("Plane of Hate", "Drops From: Plane of Hate: an elite dragoon", RouteVisualState.Allowed)
    ]);

var filteredOnly = new ItemChoicePreview(
    "Wind Walker's Mantle +6", 118, "+HP · +CHA",
    "Filtered from Auto-BiS", false,
    [new("Plane of Sky quest", "Enchanter Test of Metamorphism", RouteVisualState.Filtered)]);

var unresolved = new ItemChoicePreview(
    "Mystery Breastplate +6", 125, "+AC · +STA",
    "Source unresolved", true,
    [new("Source unresolved", "No acquisition route resolved after enrichment", RouteVisualState.Unresolved)]);
```

- [ ] **Step 5: Build the Build Planner XAML**

Use three vertical areas:

1. Header and configuration panel with Race, Class 1/2/3, Upgrade, priority-stat checkboxes, acquisition-filter checkboxes, and an `Auto-BiS` button whose tooltip says `Preview only — scoring is not connected`.
2. A central `DataGrid` with explicit columns for Slot, Current State, Best Available, Goal State, Score Delta, and Owned Location. Goal State uses a `ComboBox` bound to `GoalChoices` and `Goal`.
3. A right details panel showing the selected item's stat summary, Auto-BiS badge, all acquisition routes, and the explanation `Manual selection changes this slot only.`

Use this dropdown item pattern so filtered evidence is visible without blocking manual selection:

```xml
<ComboBox ItemsSource="{Binding GoalChoices}"
          SelectedItem="{Binding Goal, Mode=TwoWay}"
          IsTextSearchEnabled="True"
          TextSearch.TextPath="Name">
  <ComboBox.ItemTemplate>
    <DataTemplate>
      <Grid ToolTip="{Binding Tooltip}" MinWidth="360">
        <Grid.ColumnDefinitions>
          <ColumnDefinition Width="*" />
          <ColumnDefinition Width="Auto" />
        </Grid.ColumnDefinitions>
        <StackPanel>
          <TextBlock Text="{Binding Name}" FontWeight="SemiBold" />
          <TextBlock Text="{Binding Summary}" Foreground="{StaticResource MutedTextBrush}" />
        </StackPanel>
        <Border Grid.Column="1" Style="{StaticResource PillStyle}">
          <TextBlock Text="{Binding Badge}" />
        </Border>
      </Grid>
    </DataTemplate>
  </ComboBox.ItemTemplate>
</ComboBox>
```

- [ ] **Step 6: Wire the shell to the root mock view model**

Set `MainWindow.DataContext = MockPreviewData.Create()` and replace the Build Planner temporary text with:

```xml
<views:BuildPlannerPreview DataContext="{Binding BuildPlanner}" />
```

The Auto-BiS button remains enabled for visual evaluation but has no command. Goal dropdowns remain interactive through local property binding.

- [ ] **Step 7: Run the task verification gate**

Run the global build, test, and five-second startup commands from Task 1. Expected: all commands pass, the Build Planner tab opens, each Goal dropdown is usable, and source/filter details appear in tooltips.

- [ ] **Step 8: Commit the Build Planner preview**

```powershell
git add -- src/EqlGearHelper.Preview tests/EqlGearHelper.Preview.Tests/PreviewContractTests.cs
git commit -m "feat: preview three-state build planner"
```

### Task 3: Build the every-build Cleanup preview

**Files:**
- Create: `src/EqlGearHelper.Preview/Views/CleanupPreview.xaml`
- Create: `src/EqlGearHelper.Preview/Views/CleanupPreview.xaml.cs`
- Modify: `src/EqlGearHelper.Preview/Models/PreviewModels.cs`
- Modify: `src/EqlGearHelper.Preview/ViewModels/PreviewViewModel.cs`
- Modify: `src/EqlGearHelper.Preview/ViewModels/MockPreviewData.cs`
- Modify: `src/EqlGearHelper.Preview/MainWindow.xaml`
- Modify: `tests/EqlGearHelper.Preview.Tests/PreviewContractTests.cs`

**Interfaces:**
- Produces: `PreviewViewModel.Cleanup` of type `CleanupPreviewViewModel`.
- Produces: `CleanupItemPreview` with `Name`, `Slot`, `Quantity`, `Action`, `BestUse`, `Why`, `Locations`, `Confidence`, `Explanation`, `LegalBuildExamples`, and `ComparableItems`.
- Consumes: shared styles and deterministic root view model from Tasks 1 and 2.

- [ ] **Step 1: Write the failing cleanup-data test**

```csharp
[Fact]
public void CleanupPreview_DistinguishesUselessFromDuplicateWarning()
{
    var cleanup = MockPreviewData.Create().Cleanup;

    Assert.Contains(cleanup.Items, item =>
        item.Action == "Dispose candidate" &&
        item.Explanation.Contains("no legal race and three-class build", StringComparison.OrdinalIgnoreCase));
    Assert.Contains(cleanup.Items, item =>
        item.Action == "Needs review" && item.Quantity > 2);
}
```

- [ ] **Step 2: Run the cleanup test and verify it fails**

Run:

```powershell
dotnet test tests/EqlGearHelper.Preview.Tests/EqlGearHelper.Preview.Tests.csproj --filter CleanupPreview_DistinguishesUselessFromDuplicateWarning
```

Expected: FAIL because `CleanupPreviewViewModel` and its rows do not exist.

- [ ] **Step 3: Add representative cleanup mock rows**

Replace the empty `CleanupPreviewViewModel` shell from Task 2 with these Cleanup contracts before creating the rows:

```csharp
public sealed record CleanupItemPreview(
    string Name,
    string Slot,
    int Quantity,
    string Action,
    string BestUse,
    string Why,
    IReadOnlyList<string> Locations,
    string Confidence,
    string Explanation,
    IReadOnlyList<string> LegalBuildExamples,
    IReadOnlyList<string> ComparableItems,
    IReadOnlyList<string> PreservationReasons);

public sealed class CleanupPreviewViewModel : INotifyPropertyChanged
{
    private CleanupItemPreview _selectedItem = null!;

    public required IReadOnlyList<SummaryCardPreview> SummaryCards { get; init; }
    public required IReadOnlyList<ToggleOptionPreview> ActionFilters { get; init; }
    public required IReadOnlyList<ToggleOptionPreview> SourceFilters { get; init; }
    public required IReadOnlyList<string> Locations { get; init; }
    public required IReadOnlyList<CleanupItemPreview> Items { get; init; }

    public CleanupItemPreview SelectedItem
    {
        get => _selectedItem;
        set { _selectedItem = value; PropertyChanged?.Invoke(this, new(nameof(SelectedItem))); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
```

Create at least six rows covering:

- Keep: useful in several legal race/trio builds.
- Keep: lower score but supplies a specialized effect.
- Needs review: three equivalent one-handed weapons with maximum simultaneous capacity two.
- Extract then dispose: valuable transferred Exaltation.
- Dispose candidate: no material use in any legal race/trio build and no preservation reason.
- Blocked: unresolved catalog identity or race restriction.

The selected Keep row explanation must include legal-build examples; the Dispose candidate must explicitly say that all legal races and three-class combinations were evaluated.

- [ ] **Step 4: Build the Cleanup XAML**

Match the supplied cleanup hierarchy using:

- title `Items to Keep or Toss` and subtitle `Evaluated across every legal race and three-class combination`;
- search box and preview-only Import, Reanalyze, and Export buttons;
- five summary cards: Equippable, Keep, Needs Review, Exaltation Source, Dispose Candidate;
- left filters for action, source, and location;
- explicit-column owned-item `DataGrid` in the center; and
- right analysis panel with action, confidence, explanation, best-use builds, preservation reasons, and comparable owned alternatives.

Bind the grid's selected row to `CleanupPreviewViewModel.SelectedItem`. Checkboxes and selection are locally interactive; filtering and buttons remain presentation-only.

- [ ] **Step 5: Replace the Cleanup shell temporary text**

```xml
<views:CleanupPreview DataContext="{Binding Cleanup}" />
```

- [ ] **Step 6: Run the task verification gate**

Run the global build, test, and five-second startup commands from Task 1. Expected: all pass, the Cleanup tab renders all statuses, and selecting a row updates the analysis panel.

- [ ] **Step 7: Commit the Cleanup preview**

```powershell
git add -- src/EqlGearHelper.Preview tests/EqlGearHelper.Preview.Tests/PreviewContractTests.cs
git commit -m "feat: preview every-build cleanup analysis"
```

### Task 4: Build the grouped physical-copy Inventory preview

**Files:**
- Create: `src/EqlGearHelper.Preview/Views/InventoryPreview.xaml`
- Create: `src/EqlGearHelper.Preview/Views/InventoryPreview.xaml.cs`
- Modify: `src/EqlGearHelper.Preview/Models/PreviewModels.cs`
- Modify: `src/EqlGearHelper.Preview/ViewModels/PreviewViewModel.cs`
- Modify: `src/EqlGearHelper.Preview/ViewModels/MockPreviewData.cs`
- Modify: `src/EqlGearHelper.Preview/MainWindow.xaml`
- Modify: `tests/EqlGearHelper.Preview.Tests/PreviewContractTests.cs`

**Interfaces:**
- Produces: `PreviewViewModel.Inventory` of type `InventoryPreviewViewModel`.
- Produces: `InventoryGroupPreview` with `Name`, `Variant`, `Slot`, `Quantity`, `UsefulCapacity`, `DuplicateStatus`, `CleanupStatus`, and `Instances`.
- Produces: `InventoryInstancePreview` with `Location`, `ItemId`, `Upgrade`, and `ExaltationState`.

- [ ] **Step 1: Write failing grouping and threshold tests**

```csharp
[Fact]
public void InventoryPreview_PreservesPhysicalCopiesInsideEquivalentGroups()
{
    var groups = MockPreviewData.Create().Inventory.Groups;
    var swords = groups.Single(group => group.Name == "Short Sword of the Ykesha");

    Assert.Equal(swords.Quantity, swords.Instances.Sum(instance => instance.Quantity));
    Assert.Equal(3, swords.Quantity);
    Assert.Equal(2, swords.UsefulCapacity);
    Assert.Equal("Excess copies — cleanup decides", swords.DuplicateStatus);
}

[Fact]
public void TwoRings_DoNotTriggerExcessCopyWarning()
{
    var rings = MockPreviewData.Create().Inventory.Groups
        .Single(group => group.Name == "Moonstone Ring");

    Assert.Equal(2, rings.Quantity);
    Assert.Equal(string.Empty, rings.DuplicateStatus);
}
```

- [ ] **Step 2: Run the inventory tests and verify they fail**

Run:

```powershell
dotnet test tests/EqlGearHelper.Preview.Tests/EqlGearHelper.Preview.Tests.csproj --filter "InventoryPreview|TwoRings"
```

Expected: FAIL because grouped inventory mock types do not exist.

- [ ] **Step 3: Add grouped inventory mock data**

Replace the empty `InventoryPreviewViewModel` shell from Task 2 with these Inventory contracts before creating the groups:

```csharp
public sealed record InventoryInstancePreview(
    string Location,
    int ItemId,
    string Upgrade,
    string ExaltationState,
    int Quantity);

public sealed record InventoryGroupPreview(
    string Name,
    string Variant,
    string Slot,
    int Quantity,
    int UsefulCapacity,
    string DuplicateStatus,
    string CleanupStatus,
    IReadOnlyList<InventoryInstancePreview> Instances);

public sealed class InventoryPreviewViewModel
{
    public required IReadOnlyList<SummaryCardPreview> SummaryCards { get; init; }
    public required IReadOnlyList<InventoryGroupPreview> Groups { get; init; }
    public required IReadOnlyList<string> ResolutionQueue { get; init; }
}
```

Include:

- three `Short Sword of the Ykesha +4` physical copies across two banks, with distinct Exaltation states;
- two `Moonstone Ring +6` copies without an excess warning;
- three equivalent earrings with useful capacity two and an excess warning;
- three `Runed Mithril Bracer +4` copies with an excess warning;
- two equivalent ordinary chest items with useful capacity one and an excess warning;
- an unresolved item group with cleanup status `Needs review`; and
- a stacked consumable group clearly labeled `Not equipment` without a duplicate-gear warning.

Use the sample inventory's location style, including nested locations such as `Bank9-Slot3-Slot7`.

- [ ] **Step 4: Build the Inventory XAML with expandable groups**

Use:

- title, import coverage subtitle, search, and preview-only Import button;
- summary cards for Physical Copies, Equivalent Groups, Excess-Copy Warnings, and Resolution Queue;
- an `ItemsControl` whose item template is an `Expander`;
- group header columns for Item/Variant, Slot, Quantity, Useful Capacity, Duplicate Status, and Cleanup Status; and
- expanded child rows for Location, Item ID, Upgrade, Exaltation State, and Quantity.

The duplicate-status tooltip must say `Quantity exceeds simultaneous equipment capacity; this is not a disposal decision.`

- [ ] **Step 5: Replace the Inventory shell temporary text**

```xml
<views:InventoryPreview DataContext="{Binding Inventory}" />
```

- [ ] **Step 6: Run the task verification gate**

Run the global build, test, and five-second startup commands from Task 1. Expected: all pass, inventory groups expand/collapse, locations remain visible, and warnings appear only on the intended mock groups.

- [ ] **Step 7: Commit the Inventory preview**

```powershell
git add -- src/EqlGearHelper.Preview tests/EqlGearHelper.Preview.Tests/PreviewContractTests.cs
git commit -m "feat: preview grouped inventory copies"
```

### Task 5: Prepare the runnable visual-review artifact

**Files:**
- Create: `docs/review/wpf-preview.md`
- Modify: `src/EqlGearHelper.Preview/MainWindow.xaml`
- Modify: `src/EqlGearHelper.Preview/Themes/Controls.xaml`
- Test: `tests/EqlGearHelper.Preview.Tests/PreviewContractTests.cs`

**Interfaces:**
- Consumes all preview views and mock contracts from Tasks 1 through 4.
- Produces a documented command and visual checklist for user review; it does not connect production services.

- [ ] **Step 1: Add the final completeness test**

```csharp
[Fact]
public void PreviewRoot_ContainsAllThreeReviewScreens()
{
    var preview = MockPreviewData.Create();

    Assert.NotEmpty(preview.BuildPlanner.Slots);
    Assert.NotEmpty(preview.Cleanup.Items);
    Assert.NotEmpty(preview.Inventory.Groups);
}
```

- [ ] **Step 2: Run the completeness test**

Run:

```powershell
dotnet test tests/EqlGearHelper.Preview.Tests/EqlGearHelper.Preview.Tests.csproj --filter PreviewRoot_ContainsAllThreeReviewScreens
```

Expected: PASS because all three screens are now populated.

- [ ] **Step 3: Perform the 1440x900 visual pass**

Launch the preview and check all three tabs at the default 1440x900 window size. Correct only these measurable defects:

- clipped headers or controls;
- horizontal overlap between Current, Best Available, and Goal State;
- unreadable status contrast;
- dropdowns narrower than their item names;
- details panels that cannot scroll at 900-pixel window height; and
- inventory expanders whose child columns do not align with their group header.

Do not connect commands or add production dependencies during this pass.

- [ ] **Step 4: Document how to run and what to review**

Create `docs/review/wpf-preview.md` with:

```markdown
# WPF Preview Review

Run:

`dotnet run --project src/EqlGearHelper.Preview/EqlGearHelper.Preview.csproj`

Review:

- Build Planner information density and Current / Best Available / Goal State comparison.
- Goal dropdown filtered-route badges and hover evidence.
- Cleanup status clarity and selected-item explanation.
- Inventory grouping, quantity, locations, and excess-copy warning language.

This executable uses mock data. Auto-BiS, Import, Reanalyze, and Export are visual-only.
```

- [ ] **Step 5: Run the final verification gate and leave the preview open for review**

Run:

```powershell
dotnet build eql-gear-helper.sln
dotnet test eql-gear-helper.sln --no-build
$previewProcess = Start-Process -FilePath 'src\EqlGearHelper.Preview\bin\Debug\net8.0-windows\EqlGearHelper.Preview.exe' -PassThru
Start-Sleep -Seconds 5
$previewProcess.Refresh()
if ($previewProcess.HasExited -or -not $previewProcess.Responding) { throw 'WPF preview failed startup smoke check.' }
Write-Output "Preview PID $($previewProcess.Id) is running and responsive."
```

Expected: build and tests pass; the preview remains open and responsive so the user can inspect it.

- [ ] **Step 6: Commit the review artifact**

```powershell
git add -- src/EqlGearHelper.Preview tests/EqlGearHelper.Preview.Tests/PreviewContractTests.cs docs/review/wpf-preview.md
git commit -m "docs: prepare WPF preview review"
```

## Follow-on plans after visual approval

The preview is intentionally the end of this plan. After the user approves or revises it, create separate implementation plans in this order:

1. catalog identity, EQ Legends Tools/EQLWiki enrichment, acquisition-route provenance, and race restrictions;
2. shared eligibility, scoring, physical-copy assignment, Current State, Best Available, Goal State, and Auto-BiS;
3. every-race/every-trio cleanup and grouped production inventory; and
4. production WPF integration plus final acceptance audit.
