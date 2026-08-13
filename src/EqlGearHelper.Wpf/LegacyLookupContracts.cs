// Compatibility contracts retained for the legacy test project. They contain no
// catalog, ranking, SQLite, filesystem, or network implementation.
public interface IGearLookupGateway
{
    Task<ItemLookupSearchResult> SearchByNameAsync(string? query, CancellationToken cancellationToken = default);
    Task<InventoryAnalysisResult> AnalyzeInventoryAsync(string? requestedPath, CancellationToken cancellationToken = default);
    string GetDefaultInventoryFilePath();
    string GetDefaultInventoryDirectory();
}

public sealed class ItemLookupSearchResult { public string QueryFeedback { get; set; } = string.Empty; }
public sealed class InventoryAnalysisResult(string output) { public string Output { get; } = output; }
public sealed class ItemLookupRequest(string query) { public string Query { get; } = query; }
public sealed class InventoryAnalysisRequest(string requestedPath) { public string RequestedPath { get; } = requestedPath; }

public interface IItemLookupPresenter { void PresentSearching(); void PresentResult(ItemLookupSearchResult result); void PresentFailure(string message); }
public interface IInventoryAnalysisPresenter { void PresentAnalyzing(); void PresentResult(InventoryAnalysisResult result); void PresentFailure(string message); }
public interface IItemLookupView { string ItemLookupStatusText { set; } ItemLookupSearchResult? LookupResult { set; } void ClearLookupResults(); }

public sealed class ItemLookupUseCase(IGearLookupGateway gateway)
{
    public async Task ExecuteAsync(ItemLookupRequest request, IItemLookupPresenter presenter, CancellationToken cancellationToken = default)
    {
        presenter.PresentSearching();
        try { presenter.PresentResult(await gateway.SearchByNameAsync(request.Query, cancellationToken)); }
        catch (Exception exception) { presenter.PresentFailure(exception.Message); throw; }
    }
}

public sealed class InventoryAnalysisUseCase(IGearLookupGateway gateway)
{
    public async Task ExecuteAsync(InventoryAnalysisRequest request, IInventoryAnalysisPresenter presenter, CancellationToken cancellationToken = default)
    {
        presenter.PresentAnalyzing();
        try { presenter.PresentResult(await gateway.AnalyzeInventoryAsync(request.RequestedPath, cancellationToken)); }
        catch (OperationCanceledException) { presenter.PresentFailure("Inventory analysis canceled."); throw; }
        catch (Exception exception) { presenter.PresentFailure(exception.Message); throw; }
    }
}

public sealed class ItemLookupPresenter(IItemLookupView view) : IItemLookupPresenter
{
    public void PresentSearching() { view.ItemLookupStatusText = "Searching..."; view.ClearLookupResults(); }
    public void PresentResult(ItemLookupSearchResult result) { view.ItemLookupStatusText = "Search complete."; view.LookupResult = result; }
    public void PresentFailure(string message) { view.ItemLookupStatusText = "Search failed."; view.LookupResult = new ItemLookupSearchResult { QueryFeedback = message }; }
}

public static class GearLookupService
{
    public static Task<ItemLookupSearchResult> SearchByNameResultAsync(string? query)
    {
        var trimmed = query?.Trim() ?? string.Empty;
        var feedback = string.IsNullOrWhiteSpace(trimmed) ? "Type an item name to search." : trimmed.All(character => !char.IsLetterOrDigit(character)) ? "Try a clearer item name." : "Item lookup has been replaced by the Build Planner workflow.";
        return Task.FromResult(new ItemLookupSearchResult { QueryFeedback = feedback });
    }
}
