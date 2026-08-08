using System;
using System.Threading;
using System.Threading.Tasks;

public interface IGearLookupGateway
{
    Task<ItemLookupSearchResult> SearchByNameAsync(string? query, CancellationToken cancellationToken = default);
    Task<InventoryAnalysisResult> AnalyzeInventoryAsync(string? requestedPath, CancellationToken cancellationToken = default);
    string GetDefaultInventoryFilePath();
    string GetDefaultInventoryDirectory();
}

public sealed class GearLookupGateway : IGearLookupGateway
{
    public async Task<ItemLookupSearchResult> SearchByNameAsync(string? query, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await GearLookupService.SearchByNameResultAsync(query).ConfigureAwait(false);
    }

    public async Task<InventoryAnalysisResult> AnalyzeInventoryAsync(
        string? requestedPath,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await GearLookupService.RunInventoryAnalysisResultAsync(requestedPath).ConfigureAwait(false);
    }

    public string GetDefaultInventoryFilePath() => GearLookupService.GetDefaultInventoryFilePath() ?? string.Empty;

    public string GetDefaultInventoryDirectory() => GearLookupService.GetDefaultInventoryDirectory();
}

public sealed class ItemLookupRequest(string query)
{
    public string Query { get; } = query;
}

public interface IItemLookupUseCase
{
    Task ExecuteAsync(ItemLookupRequest request, IItemLookupPresenter presenter, CancellationToken cancellationToken = default);
}

public sealed class ItemLookupUseCase : IItemLookupUseCase
{
    private readonly IGearLookupGateway _gateway;

    public ItemLookupUseCase(IGearLookupGateway gateway)
    {
        _gateway = gateway;
    }

    public async Task ExecuteAsync(
        ItemLookupRequest request,
        IItemLookupPresenter presenter,
        CancellationToken cancellationToken = default)
    {
        presenter.PresentSearching();

        try
        {
            var result = await _gateway.SearchByNameAsync(request.Query, cancellationToken).ConfigureAwait(false);
            presenter.PresentResult(result);
        }
        catch (OperationCanceledException)
        {
            presenter.PresentFailure("Search canceled.");
            throw;
        }
        catch (Exception ex)
        {
            presenter.PresentFailure(ex.Message);
            throw;
        }
    }
}

public interface IInventoryAnalysisUseCase
{
    Task ExecuteAsync(InventoryAnalysisRequest request, IInventoryAnalysisPresenter presenter, CancellationToken cancellationToken = default);
}

public sealed class InventoryAnalysisUseCase : IInventoryAnalysisUseCase
{
    private readonly IGearLookupGateway _gateway;

    public InventoryAnalysisUseCase(IGearLookupGateway gateway)
    {
        _gateway = gateway;
    }

    public async Task ExecuteAsync(
        InventoryAnalysisRequest request,
        IInventoryAnalysisPresenter presenter,
        CancellationToken cancellationToken = default)
    {
        presenter.PresentAnalyzing();

        try
        {
            var result = await _gateway.AnalyzeInventoryAsync(request.RequestedPath, cancellationToken).ConfigureAwait(false);
            presenter.PresentResult(result);
        }
        catch (OperationCanceledException)
        {
            presenter.PresentFailure("Inventory analysis canceled.");
            throw;
        }
        catch (Exception ex)
        {
            presenter.PresentFailure(ex.Message);
            throw;
        }
    }
}

public sealed class InventoryAnalysisRequest(string requestedPath)
{
    public string RequestedPath { get; } = requestedPath;
}

public interface IItemLookupPresenter
{
    void PresentSearching();
    void PresentResult(ItemLookupSearchResult result);
    void PresentFailure(string message);
}

public interface IInventoryAnalysisPresenter
{
    void PresentAnalyzing();
    void PresentResult(InventoryAnalysisResult result);
    void PresentFailure(string message);
}

public interface IItemLookupView
{
    string ItemLookupStatusText { set; }
    ItemLookupSearchResult? LookupResult { set; }
    void ClearLookupResults();
}

public interface IInventoryAnalysisView
{
    string InventoryStatusText { set; }
    string InventoryOutputText { set; }
    IReadOnlyList<ItemLookupMatchSummary>? InventoryItemLookupResults { set; }
}

public sealed class ItemLookupPresenter : IItemLookupPresenter
{
    private readonly IItemLookupView _view;

    public ItemLookupPresenter(IItemLookupView view)
    {
        _view = view;
    }

    public void PresentSearching()
    {
        _view.ItemLookupStatusText = "Searching...";
        _view.ClearLookupResults();
    }

    public void PresentResult(ItemLookupSearchResult result)
    {
        _view.ItemLookupStatusText = "Search complete.";
        _view.LookupResult = result;
    }

    public void PresentFailure(string message)
    {
        _view.ItemLookupStatusText = "Search failed.";
        _view.LookupResult = new ItemLookupSearchResult { QueryFeedback = message };
    }
}

public sealed class InventoryAnalysisPresenter : IInventoryAnalysisPresenter
{
    private readonly IInventoryAnalysisView _view;

    public InventoryAnalysisPresenter(IInventoryAnalysisView view)
    {
        _view = view;
    }

    public void PresentAnalyzing()
    {
        _view.InventoryStatusText = "Analyzing inventory...";
        _view.InventoryOutputText = "Running lookup...";
        _view.InventoryItemLookupResults = Array.Empty<ItemLookupMatchSummary>();
    }

    public void PresentResult(InventoryAnalysisResult result)
    {
        _view.InventoryStatusText = "Analysis complete.";
        _view.InventoryOutputText = result.Output;
        _view.InventoryItemLookupResults = result.ItemLookups;
    }

    public void PresentFailure(string message)
    {
        _view.InventoryStatusText = "Analysis failed.";
        _view.InventoryOutputText = message;
        _view.InventoryItemLookupResults = Array.Empty<ItemLookupMatchSummary>();
    }
}

public sealed class ItemLookupController
{
    private readonly IItemLookupUseCase _useCase;
    private readonly IItemLookupPresenter _presenter;

    public ItemLookupController(IItemLookupUseCase useCase, IItemLookupPresenter presenter)
    {
        _useCase = useCase;
        _presenter = presenter;
    }

    public Task SearchAsync(string query, CancellationToken cancellationToken = default)
    {
        return _useCase.ExecuteAsync(new ItemLookupRequest(query), _presenter, cancellationToken);
    }
}

public sealed class InventoryAnalysisController
{
    private readonly IInventoryAnalysisUseCase _useCase;
    private readonly IInventoryAnalysisPresenter _presenter;

    public InventoryAnalysisController(IInventoryAnalysisUseCase useCase, IInventoryAnalysisPresenter presenter)
    {
        _useCase = useCase;
        _presenter = presenter;
    }

    public Task AnalyzeAsync(string requestedPath, CancellationToken cancellationToken = default)
    {
        return _useCase.ExecuteAsync(new InventoryAnalysisRequest(requestedPath), _presenter, cancellationToken);
    }
}

public sealed class InventoryAnalysisResult(string output, IReadOnlyList<ItemLookupMatchSummary>? itemLookups = null)
{
    public string Output { get; } = output;
    public IReadOnlyList<ItemLookupMatchSummary> ItemLookups { get; } = itemLookups ?? new List<ItemLookupMatchSummary>();
}
