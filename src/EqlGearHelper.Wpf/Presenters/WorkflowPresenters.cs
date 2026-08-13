using System.Collections;
using EqlGearHelper.Wpf.ViewModels;

namespace EqlGearHelper.Wpf.Presenters;

public sealed class CleanupPresenter
{
    public CleanupViewModel Present(object result, object? request = null)
    {
        var model = new CleanupViewModel();
        var locations = Projection.List(Projection.Value(request, "Collection"), "OwnedItems")
            .Where(item => Projection.Value(item, "InstanceId") is not null)
            .ToDictionary(item => Projection.Guid(item, "InstanceId"), item => Projection.Text(Projection.Value(item, "Location"), "Container", "Unresolved location"));
        var assessments = Projection.List(result, "Assessments");
        foreach (var item in assessments)
        {
            var assessment = Projection.Value(item, "Assessment");
            var action = Projection.Text(item, "FinalAction");
            var confidence = Projection.Text(item, "Confidence");
            var id = Projection.Guid(item, "AssetInstanceId");
            model.Assessments.Add(new AssessmentRow(id, action, confidence, Projection.Text(assessment, "Explanation"), Projection.List(item, "PreservationReasons").Select(value => value?.ToString() ?? string.Empty).ToArray(), locations.GetValueOrDefault(id, "Unresolved location")));
        }
        foreach (var group in model.Assessments.GroupBy(item => item.Action)) model.Cards.Add(new SummaryCard(group.Key, group.Count().ToString()));
        foreach (var row in model.Assessments.OrderBy(item => item.Id)) model.Locations.Add(new LocationNode(row.Location));
        var state = model.Assessments.Count == 0 ? WorkflowState.Empty : model.Assessments.Any(item => item.Confidence == "Blocked") ? WorkflowState.Blocked : model.Assessments.Any(item => item.Confidence == "Partial") ? WorkflowState.Partial : WorkflowState.Ready;
        model.ApplyState(state, state == WorkflowState.Empty ? "No owned items were available for cleanup analysis." : $"Analyzed {Projection.List(result, "AnalyzedTrios").Count} class trios and {model.Assessments.Count} physical items.");
        return model;
    }
}

public sealed class InventoryPresenter
{
    public InventoryViewModel Present(object snapshot)
    {
        var model = new InventoryViewModel();
        foreach (var item in Projection.List(snapshot, "Items")) model.Items.Add(new InventoryRow(Projection.Text(item, "Path"), Projection.Text(item, "Name"), Projection.Text(item, "MappingStatus")));
        foreach (var storage in Coverage(snapshot)) model.Coverage.Add(storage);
        foreach (var item in model.Items.Where(item => item.MappingState == "Unknown")) model.ResolutionQueue.Add(item.Name);
        model.ApplyState(model.ResolutionQueue.Count > 0 ? WorkflowState.Partial : model.Items.Count == 0 ? WorkflowState.Empty : WorkflowState.Ready, model.Items.Count == 0 ? "The import completed but did not contain physical items." : $"Imported {model.Items.Count} physical items.");
        return model;
    }

    private static IReadOnlyList<string> Coverage(object snapshot)
    {
        var rows = Projection.List(snapshot, "Rows");
        var unavailable = Projection.List(snapshot, "Storage")
            .Where(storage => Projection.Text(storage, "Availability") == "Unavailable")
            .Select(storage => Projection.Text(storage, "Name"))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return new[] { "Equipped", "Inventory", "Bank", "Shared Bank", "Dragon Hoard", "Item Storage", "Exaltation Storage" }
            .Select(storage => $"{storage}: {CoverageState(storage, rows, unavailable)}")
            .ToArray();
    }

    private static string CoverageState(string storage, IReadOnlyList<object?> rows, IReadOnlySet<string> unavailable)
    {
        var storageRows = rows.Where(row => BelongsToStorage(storage, Projection.Text(row, "Path"))).ToArray();
        if (storageRows.Length == 0) return unavailable.Contains(storage) ? "NotAvailable" : "Unknown";
        return storageRows.All(row => Projection.Text(row, "MappingStatus") == "Empty") ? "KnownEmpty" : "Imported";
    }

    private static bool BelongsToStorage(string storage, string path) => storage switch
    {
        "Bank" => path.StartsWith("Bank", StringComparison.OrdinalIgnoreCase),
        "Shared Bank" => path.StartsWith("SharedBank", StringComparison.OrdinalIgnoreCase) || path.StartsWith("Shared Bank", StringComparison.OrdinalIgnoreCase),
        "Dragon Hoard" => path.StartsWith("DragonHoard", StringComparison.OrdinalIgnoreCase) || path.StartsWith("Dragon Hoard", StringComparison.OrdinalIgnoreCase),
        "Item Storage" => path.StartsWith("ItemStorage", StringComparison.OrdinalIgnoreCase) || path.StartsWith("Item Storage", StringComparison.OrdinalIgnoreCase),
        "Exaltation Storage" => path.StartsWith("ExaltationStorage", StringComparison.OrdinalIgnoreCase) || path.StartsWith("Exaltation Storage", StringComparison.OrdinalIgnoreCase),
        "Inventory" => path.StartsWith("Inventory", StringComparison.OrdinalIgnoreCase),
        "Equipped" => !path.StartsWith("Bank", StringComparison.OrdinalIgnoreCase) && !path.StartsWith("Shared", StringComparison.OrdinalIgnoreCase) && !path.StartsWith("Dragon", StringComparison.OrdinalIgnoreCase) && !path.StartsWith("Item", StringComparison.OrdinalIgnoreCase) && !path.StartsWith("Exaltation", StringComparison.OrdinalIgnoreCase) && !path.StartsWith("Inventory", StringComparison.OrdinalIgnoreCase),
        _ => false
    };
}

public sealed class BuildPlannerPresenter
{
    public BuildPlannerViewModel Present(object plan)
    {
        var model = new BuildPlannerViewModel();
        var bestOwned = Projection.Value(plan, "BestOwned");
        var loadout = Projection.Value(bestOwned, "Loadout");
        var evaluation = Projection.Value(bestOwned, "Evaluation");
        foreach (var assignment in Projection.List(loadout, "Assignments"))
        {
            var item = Projection.Value(assignment, "Item");
            var definition = Projection.Value(assignment, "ItemDefinition");
            var location = Projection.Value(item, "Location");
            model.Loadout.Add(new LoadoutRow(Projection.Position(Projection.Value(assignment, "Position")), Projection.Text(definition, "Name", Projection.Text(item, "CatalogItemId")), Projection.Text(location, "Container")));
        }
        foreach (var gap in Projection.List(plan, "Gaps")) model.Gaps.Add(new GapRow(Projection.Position(Projection.Value(gap, "Position")), Projection.Text(gap, "TargetCatalogItemId"), string.Join(", ", Projection.List(gap, "MissingEffects").Select(item => item?.ToString()))));
        foreach (var requirement in Projection.List(evaluation, "Requirements")) model.Requirements.Add($"{Projection.Text(requirement, "Name")}: {(Projection.Bool(requirement, "IsMet") ? "met" : "missing")}");
        var state = Projection.Bool(bestOwned, "IsComplete") ? WorkflowState.Ready : WorkflowState.Blocked;
        model.ApplyState(state, state == WorkflowState.Ready ? "Complete owned loadout calculated." : "A complete owned loadout is not available for this trio.");
        return model;
    }
}

public sealed class ExaltationsPresenter
{
    public ExaltationsViewModel Present(object? result)
    {
        var model = new ExaltationsViewModel();
        foreach (var row in Projection.List(result, "Rows")) model.Items.Add(new ExaltationRow(Projection.Text(row, "Name"), Projection.Text(row, "HostPath"), Projection.Text(row, "MappingStatus")));
        model.ApplyState(model.Items.Count == 0 ? WorkflowState.Empty : model.Items.Any(item => item.Resolution == "Unknown") ? WorkflowState.Blocked : WorkflowState.Ready, model.Items.Count == 0 ? "No installed Exaltations are available." : $"{model.Items.Count} installed Exaltations require review.");
        return model;
    }
}

public sealed class DataPresenter
{
    public DataViewModel Present(object? catalog, object? snapshot)
    {
        var model = new DataViewModel { CatalogVersion = Projection.Text(catalog, "CatalogVersion", "Catalog unavailable"), RulesetVersion = Projection.Text(catalog, "RulesetVersion", "Ruleset unavailable"), SnapshotIdentity = Projection.Text(snapshot, "SnapshotId", "No inventory snapshot") };
        var state = catalog is null ? WorkflowState.Stale : snapshot is null ? WorkflowState.Partial : WorkflowState.Ready;
        model.ApplyState(state, state == WorkflowState.Ready ? "Catalog, ruleset, and inventory snapshot are available for backup." : "Data is incomplete; backup and analysis remain limited.");
        return model;
    }
}

internal static class Projection
{
    public static object? Value(object? source, string property) => source?.GetType().GetProperty(property)?.GetValue(source);
    public static string Text(object? source, string property, string fallback = "") => Value(source, property)?.ToString() ?? fallback;
    public static bool Bool(object? source, string property) => Value(source, property) is bool value && value;
    public static Guid Guid(object? source, string property) => Value(source, property) is Guid value ? value : System.Guid.Empty;
    public static IReadOnlyList<object?> List(object? source, string property) => Value(source, property) is IEnumerable values ? values.Cast<object?>().ToArray() : [];
    public static string Position(object? position)
    {
        var type = Text(position, "Type", "Unknown slot");
        var index = Text(position, "Index", "0");
        return index == "0" ? type : $"{type} {index}";
    }
}
