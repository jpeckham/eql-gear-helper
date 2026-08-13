# EQL Gear Analyzer Refactor Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the current item lookup utility with a local-first, explainable EQL collection cleanup and three-class build-planning application.

**Architecture:** Split the WPF-only application into Domain, Application, Infrastructure, and WPF projects with inward-pointing dependencies. SQLite and inventory/catalog import remain replaceable adapters; complete-loadout analysis is pure Domain/Application logic.

**Tech Stack:** .NET 8, C# WPF, Microsoft.Data.Sqlite, xUnit, ArchUnitNET.

## Global Constraints

- Local-only Windows 10/11 x64 application; no application-owned backend, login, or cloud storage.
- Domain and Application have no WPF, SQLite, HTTP, filesystem, or persistence-record dependencies.
- Local versioned catalog packages are authoritative; EQL Legends Tools enrichment is optional and never elevates confidence.
- Cleanup evaluates every legal trio without requiring a selected trio.
- Every owned physical copy is independent; duplicate positions and Any 1/2 require valid distinct assignments.
- DEX utility is always zero; CHA utility stops at the configured ruleset target for applicable builds.
- Unknown data or valuable Exaltations must never result in a complete-confidence plain disposal recommendation.
- Run `dotnet build` after every implementation task; after a successful build, run `dotnet test`.

---

## Target file structure

```text
src/EqlGearHelper.Domain/
  Catalog.cs, Collection.cs, Loadouts.cs, Rulesets.cs, Assessments.cs
  Optimization/LoadoutEvaluator.cs, Optimization/LoadoutAssignmentService.cs
src/EqlGearHelper.Application/
  Ports.cs, CatalogUseCases.cs, InventoryUseCases.cs, PlanningUseCases.cs
  CleanupUseCases.cs, ExportUseCases.cs
src/EqlGearHelper.Infrastructure/
  Sqlite/DatabaseInitializer.cs, Sqlite/*Repository.cs
  Import/CatalogPackageImporter.cs, Import/InventoryParser.cs
  External/EqlLegendsToolsEnrichmentGateway.cs, Backup/CollectionBackupService.cs
src/EqlGearHelper.Wpf/
  Views/*.xaml, ViewModels/*.cs, Controllers/*.cs, Presenters/*.cs
tests/EqlGearHelper.Domain.Tests/
tests/EqlGearHelper.Application.Tests/
tests/EqlGearHelper.Infrastructure.Tests/
tests/EqlGearHelper.Architecture.Tests/
```

### Task 1: Establish the Clean Architecture solution

**Files:**
- Create: `src/EqlGearHelper.Domain/EqlGearHelper.Domain.csproj`
- Create: `src/EqlGearHelper.Application/EqlGearHelper.Application.csproj`
- Create: `src/EqlGearHelper.Infrastructure/EqlGearHelper.Infrastructure.csproj`
- Create: `src/EqlGearHelper.Wpf/EqlGearHelper.Wpf.csproj`
- Create: `tests/EqlGearHelper.Architecture.Tests/ArchitectureTests.cs`
- Modify: `eql-gear-helper.sln`
- Modify: `eql-gear-helper.csproj`

**Interfaces:**
- Produces project references: Wpf -> Application -> Domain and Infrastructure -> Application/Domain.
- Produces test project references that never pull WPF or Infrastructure into Domain/Application tests.

- [ ] **Step 1: Write failing architecture tests**

```csharp
[Fact]
public void Domain_HasNoFrameworkDependencies() =>
    Classes().That().ResideInNamespace("EqlGearHelper.Domain", true)
        .Should().NotDependOnAny(Classes().That().ResideInNamespace("System.Windows", true))
        .Check(Architecture);
```

- [ ] **Step 2: Run the architecture test and verify it fails**

Run: `dotnet test tests/EqlGearHelper.Architecture.Tests --filter Domain_HasNoFrameworkDependencies`

Expected: FAIL until the project split and namespace dependencies are corrected.

- [ ] **Step 3: Create the four projects and move current UI/service code to its outer layer**

```xml
<ProjectReference Include="..\EqlGearHelper.Application\EqlGearHelper.Application.csproj" />
```

Delete obsolete `GearLookupService`, ranking-profile, and lookup-view paths only after their behavior is replaced by an Application port or WPF adapter.

- [ ] **Step 4: Add dependency-rule tests and compile the solution**

Run: `dotnet build`

Expected: successful build with no Domain/Application reference to WPF, SQLite, HTTP, or filesystem assemblies.

- [ ] **Step 5: Run the full test suite**

Run: `dotnet test`

Expected: PASS.

### Task 2: Model rules, catalog assets, collection copies, and assessments

**Files:**
- Create: `src/EqlGearHelper.Domain/Catalog.cs`
- Create: `src/EqlGearHelper.Domain/Collection.cs`
- Create: `src/EqlGearHelper.Domain/Rulesets.cs`
- Create: `src/EqlGearHelper.Domain/Loadouts.cs`
- Create: `src/EqlGearHelper.Domain/Assessments.cs`
- Create: `tests/EqlGearHelper.Domain.Tests/DomainModelTests.cs`

**Interfaces:**
- Produces `CatalogItem`, `ExaltationDefinition`, `OwnedItemInstance`, `OwnedExaltationInstance`, `Ruleset`, `EquipmentPosition`, `ClassTrio`, `Loadout`, `Assessment`, and `RecommendationConfidence`.
- Consumed by all later import, planning, cleanup, persistence, and presentation tasks.

- [ ] **Step 1: Write failing domain tests for physical copies and class intersection**

```csharp
[Fact]
public void InstalledExaltation_NarrowsHostClassesByIntersection()
{
    var effective = ClassSet.Of("Ranger", "Bard", "Warrior")
        .Intersect(ClassSet.Of("Ranger"));
    Assert.Equal(ClassSet.Of("Ranger"), effective);
}
```

- [ ] **Step 2: Run the domain tests and verify they fail**

Run: `dotnet test tests/EqlGearHelper.Domain.Tests --filter FullyQualifiedName~DomainModelTests`

Expected: FAIL because the domain types do not exist.

- [ ] **Step 3: Implement immutable domain types and invariants**

```csharp
public sealed record OwnedItemInstance(
    Guid InstanceId, string CatalogItemId, int UpgradeLevel,
    InventoryLocation Location, IReadOnlyList<InstalledExaltation> InstalledExaltations);
```

Validate three distinct classes, nonnegative upgrade levels, and the invariant that an installed configuration with an empty effective class set is invalid.

- [ ] **Step 4: Add ruleset tests for DEX, CHA, positions, and final-action separation**

```csharp
Assert.Equal(0, ruleset.UtilityFor("DEX", 500, trio));
Assert.Equal(0, ruleset.UtilityFor("CHA", 90, chaUsingTrio));
```

- [ ] **Step 5: Build and test**

Run: `dotnet build; dotnet test`

Expected: PASS.

### Task 3: Implement catalog package, inventory parsing, snapshot safety, and SQLite persistence

**Files:**
- Create: `src/EqlGearHelper.Application/Ports.cs`
- Create: `src/EqlGearHelper.Application/CatalogUseCases.cs`
- Create: `src/EqlGearHelper.Application/InventoryUseCases.cs`
- Create: `src/EqlGearHelper.Infrastructure/Import/CatalogPackageImporter.cs`
- Create: `src/EqlGearHelper.Infrastructure/Import/InventoryParser.cs`
- Create: `src/EqlGearHelper.Infrastructure/Sqlite/DatabaseInitializer.cs`
- Create: `src/EqlGearHelper.Infrastructure/Sqlite/CatalogRepository.cs`
- Create: `src/EqlGearHelper.Infrastructure/Sqlite/CollectionRepository.cs`
- Create: `tests/EqlGearHelper.Infrastructure.Tests/InventoryParserTests.cs`

**Interfaces:**
- Produces `IRepository<T>`, `IInventoryParser`, `ImportInventorySnapshotUseCase`, and `CatalogPackageImportUseCase`.
- `IInventoryParser.Parse(Stream input)` returns an uncommitted `InventorySnapshotDraft`; the use case persists it transactionally only after validation.

- [ ] **Step 1: Write the fixture parser tests**

```csharp
[Fact]
public void ParsesTransferredExaltationAndPreservesBothIds()
{
    var snapshot = ParseFixture();
    var boots = snapshot.Items.Single(x => x.Name == "Pristine Studded Leather Boots +4");
    Assert.Contains(boots.InstalledExaltations, x => x.IsTransferred && x.SourceItemId != boots.CatalogItemId);
}
```

Include tests for every fixture row, nested locations, duplicate swords with distinct socket states, native Mask exaltation, unavailable alternate storage, and failed-import rollback.

- [ ] **Step 2: Run parser tests and verify failure**

Run: `dotnet test tests/EqlGearHelper.Infrastructure.Tests --filter FullyQualifiedName~InventoryParserTests`

Expected: FAIL because parser and persistence adapters do not exist.

- [ ] **Step 3: Define application ports and build SQLite adapters**

```csharp
public interface IInventorySnapshotRepository
{
    Task ReplaceWithAsync(InventorySnapshotDraft snapshot, CancellationToken token);
    Task<InventorySnapshot?> GetCurrentAsync(CancellationToken token);
}
```

Use one SQLite transaction for catalog/snapshot persistence. Store raw subordinate rows and mapping status even when they cannot become a known Exaltation.

- [ ] **Step 4: Implement local versioned catalog import and optional enrichment**

Reject a package without catalog version, ruleset version, and item identities. The enrichment gateway returns supplemental observations and cannot overwrite confirmed package facts.

- [ ] **Step 5: Build and test**

Run: `dotnet build; dotnet test`

Expected: PASS, including AC-01 fixture tests.

### Task 4: Implement valid complete-loadout assignment and evaluation

**Files:**
- Create: `src/EqlGearHelper.Domain/Optimization/LoadoutAssignmentService.cs`
- Create: `src/EqlGearHelper.Domain/Optimization/LoadoutEvaluator.cs`
- Create: `src/EqlGearHelper.Application/PlanningUseCases.cs`
- Create: `tests/EqlGearHelper.Domain.Tests/LoadoutAssignmentTests.cs`
- Create: `tests/EqlGearHelper.Application.Tests/BuildPlannerUseCaseTests.cs`

**Interfaces:**
- `LoadoutAssignmentService.FindBestOwned(ClassTrio, Collection, Ruleset)` returns `LoadoutPlan`.
- `LoadoutEvaluator.Evaluate(Loadout, ClassTrio, Ruleset)` returns totals, effect coverage, requirements, and explanation evidence.

- [ ] **Step 1: Write failing assignment and whole-set tests**

```csharp
[Fact]
public void SecondBestRingIsRetainedWhenTwoRingPositionsRequireTwoCopies()
{
    var plan = Planner.FindBestOwned(trio, collectionWithTwoRings, ruleset);
    Assert.Equal(2, plan.Assignments.Count(x => x.Position.Type == SlotType.Ring));
}
```

Cover Any 1/2, single-copy exclusivity, lore rules, two-handed conflicts, class intersection, DEX zero utility, CHA cap, critical effects, and non-stacking duplicate suppression.

- [ ] **Step 2: Run optimization tests and verify failure**

Run: `dotnet test tests/EqlGearHelper.Domain.Tests --filter FullyQualifiedName~LoadoutAssignmentTests`

Expected: FAIL because the planner is absent.

- [ ] **Step 3: Implement deterministic candidate generation and branch-and-bound assignment**

```csharp
public sealed record CandidateAssignment(EquipmentPosition Position, OwnedItemInstance Item);
```

Generate only legal candidates, include every otherwise-equippable candidate for each Any position, and track consumed instance IDs while exploring assignments.

- [ ] **Step 4: Implement whole-set evaluator and selected-trio use case**

Return named requirement coverage and contribution evidence rather than an opaque universal score. Surface cancellation and retain the last complete result outside this pure service.

- [ ] **Step 5: Build and test**

Run: `dotnet build; dotnet test`

Expected: PASS, including AC-03 through AC-09 and AC-11 best-owned assertions.

### Task 5: Implement targets, cleanup, actions, export, backup, and recovery

**Files:**
- Create: `src/EqlGearHelper.Application/CleanupUseCases.cs`
- Create: `src/EqlGearHelper.Application/ExportUseCases.cs`
- Create: `src/EqlGearHelper.Infrastructure/Backup/CollectionBackupService.cs`
- Create: `src/EqlGearHelper.Infrastructure/Sqlite/AnalysisRepository.cs`
- Create: `tests/EqlGearHelper.Application.Tests/CleanupAnalyzerTests.cs`
- Create: `tests/EqlGearHelper.Application.Tests/TargetPlannerTests.cs`

**Interfaces:**
- `BuildTargetPlanUseCase.ExecuteAsync(BuildTargetRequest, CancellationToken)` returns target, gaps, alternatives, and acquisition sources.
- `AnalyzeCollectionUseCase.ExecuteAsync(AnalyzeCollectionRequest, CancellationToken)` returns one `Assessment` per owned item/exaltation asset.

- [ ] **Step 1: Write failing cleanup, target-policy, and donor tests**

```csharp
[Fact]
public async Task ValuableExtractableExaltation_NeverReturnsPlainDisposeCandidate()
{
    var result = await Analyze(itemWithSpellDamageDonor);
    Assert.NotEqual(FinalAction.DisposeCandidate, result.FinalAction);
}
```

Cover automatic all-trio analysis, representative uses, practical quest exclusion, missing-effect gaps, unknown-data blocking, and location-sorted export eligibility.

- [ ] **Step 2: Run cleanup tests and verify failure**

Run: `dotnet test tests/EqlGearHelper.Application.Tests --filter FullyQualifiedName~CleanupAnalyzerTests`

Expected: FAIL because collection-wide analysis is absent.

- [ ] **Step 3: Implement target planning and cached all-trio cleanup**

The target planner selects catalog configurations under visible policy. Cleanup reuses per-trio results, applies materiality tolerance, then derives usefulness, redundancy, preservation, final action, and confidence in that order.

- [ ] **Step 4: Implement export, backup, and recovery ports**

Export only `DisposeCandidate` rows with complete confidence. Back up database, catalog version, ruleset version, and active snapshot identity; validate before replacing local data during recovery.

- [ ] **Step 5: Build and test**

Run: `dotnet build; dotnet test`

Expected: PASS, including AC-02, AC-10, and AC-12 through AC-14.

### Task 6: Replace the WPF lookup interface with product workflows

**Files:**
- Create: `src/EqlGearHelper.Wpf/Views/CleanupView.xaml`
- Create: `src/EqlGearHelper.Wpf/Views/InventoryView.xaml`
- Create: `src/EqlGearHelper.Wpf/Views/BuildPlannerView.xaml`
- Create: `src/EqlGearHelper.Wpf/Views/ExaltationsView.xaml`
- Create: `src/EqlGearHelper.Wpf/Views/DataView.xaml`
- Create: `src/EqlGearHelper.Wpf/ViewModels/CleanupViewModel.cs`
- Create: `src/EqlGearHelper.Wpf/ViewModels/BuildPlannerViewModel.cs`
- Create: `src/EqlGearHelper.Wpf/Presenters/*Presenter.cs`
- Modify: `src/EqlGearHelper.Wpf/MainWindow.xaml`
- Modify: `src/EqlGearHelper.Wpf/App.xaml.cs`
- Create: `tests/EqlGearHelper.Wpf.Tests/PresentationTests.cs`

**Interfaces:**
- WPF controllers invoke Application use cases and presenters map use-case responses to view models.
- Views bind only to view models and commands; they do not query SQLite or implement optimization decisions.

- [ ] **Step 1: Write failing presentation tests**

```csharp
[Fact]
public void CleanupPresenter_MapsBlockedConfidenceToNonDisposableViewState()
{
    var model = presenter.Present(blockedAssessment);
    Assert.False(model.CanExportForDisposal);
}
```

- [ ] **Step 2: Run presentation tests and verify failure**

Run: `dotnet test tests/EqlGearHelper.Wpf.Tests --filter FullyQualifiedName~PresentationTests`

Expected: FAIL because the new presenter/view models do not exist.

- [ ] **Step 3: Implement workflow navigation and data-bound screens**

Implement Cleanup’s cards, filters, location tree, table, and explanation panel. Implement Inventory’s import/coverage/resolution states; Build Planner’s three class selectors, requirement list, loadout table, and selected-slot gap panel; Exaltations and Data/backup screens.

- [ ] **Step 4: Add accessible async states and error presentation**

Every command has disabled/loading state, keyboard-accessible labels, cancellation, and explicit empty, failure, stale, partial, and blocked copy. Do not display a zero-item cleanup as successful analysis.

- [ ] **Step 5: Build and test**

Run: `dotnet build; dotnet test`

Expected: PASS.

### Task 7: Execute final acceptance and regression audit

**Files:**
- Create: `tests/EqlGearHelper.Acceptance.Tests/PrdAcceptanceTests.cs`
- Modify: `README.md`
- Modify: `docs/superpowers/specs/2026-08-09-eql-gear-analyzer-design.md`

**Interfaces:**
- Consumes all prior use cases and fixture data.
- Produces an explicit AC-01 through AC-15 audit record and local setup/backup instructions.

- [ ] **Step 1: Write acceptance tests grouped by PRD criteria**

```csharp
[Theory]
[InlineData("AC-01")]
[InlineData("AC-15")]
public void ProductAcceptanceCriteria_AreCovered(string criterion) =>
    Assert.True(coverage.HasExecutableEvidence(criterion));
```

- [ ] **Step 2: Run acceptance tests and verify any missing evidence fails**

Run: `dotnet test tests/EqlGearHelper.Acceptance.Tests`

Expected: FAIL until every criterion has direct test or architecture evidence.

- [ ] **Step 3: Complete missing coverage and document local operation**

Document catalog-package initialization, inventory import, coverage limits, reanalysis, disposal export, backup/recovery, catalog attribution, and known ruleset confidence limitations.

- [ ] **Step 4: Run the complete verification gate**

Run: `dotnet build; dotnet test`

Expected: PASS with AC-01 through AC-15 represented by direct evidence.
