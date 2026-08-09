using Xunit;

namespace EqlGearHelper.Tests;

public class CleanBusinessRuleTests
{
    [Fact]
    public async Task SearchByNameResultAsync_EmptyOrWhitespaceQuery_ReturnsHelpfulFeedback()
    {
        var empty = await GearLookupService.SearchByNameResultAsync("   ");
        Assert.Equal("Type an item name to search.", empty.QueryFeedback);

        var punctuation = await GearLookupService.SearchByNameResultAsync("!!!");
        Assert.Equal("Try a clearer item name.", punctuation.QueryFeedback);
    }

    [Fact]
    public async Task ItemLookupUseCase_NotifiesPresenter_SearchThenResult()
    {
        var gateway = new FakeGearLookupGateway(
            searchByName: (query, _) =>
                Task.FromResult(new ItemLookupSearchResult { QueryFeedback = "ok" }));
        var presenter = new RecordingItemLookupPresenter();
        var useCase = new ItemLookupUseCase(gateway);

        await useCase.ExecuteAsync(new ItemLookupRequest("storm"), presenter);

        Assert.Equal(["searching", "result"], presenter.CallSequence);
        Assert.Single(gateway.SearchCalls);
        Assert.Equal("storm", gateway.SearchCalls[0]);
        Assert.NotNull(presenter.LastSearchResult);
        Assert.Equal("ok", presenter.LastSearchResult!.QueryFeedback);
    }

    [Fact]
    public async Task ItemLookupUseCase_WhenGatewayFails_ReportsFailureAndRethrows()
    {
        var gateway = new FakeGearLookupGateway(
            searchByName: (_, _) => throw new InvalidOperationException("boom"));
        var presenter = new RecordingItemLookupPresenter();
        var useCase = new ItemLookupUseCase(gateway);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            useCase.ExecuteAsync(new ItemLookupRequest("storm"), presenter));

        Assert.Equal("failure", presenter.LastStatus);
        Assert.Equal("boom", ex.Message);
        Assert.Equal(["searching", "failure"], presenter.CallSequence);
    }

    [Fact]
    public async Task ItemLookupPresenter_MapsStatusAndResultToView()
    {
        var view = new RecordingItemLookupView();
        var presenter = new ItemLookupPresenter(view);

        presenter.PresentSearching();
        Assert.Equal("Searching...", view.ItemLookupStatusText);
        Assert.True(view.DidClearResults);

        var searchResult = new ItemLookupSearchResult { QueryFeedback = "done" };
        presenter.PresentResult(searchResult);
        Assert.Equal("Search complete.", view.ItemLookupStatusText);
        Assert.Same(searchResult, view.LookupResult);

        presenter.PresentFailure("bad");
        Assert.Equal("Search failed.", view.ItemLookupStatusText);
        Assert.Equal("bad", view.LookupResult!.QueryFeedback);
    }

    [Fact]
    public async Task InventoryAnalysisUseCase_ExecutesLifecycleAndPassesFailureOnCancellation()
    {
        var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var gateway = new FakeInventoryAnalysisGateway(
            analyzeInventory: (_, token) =>
                Task.FromCanceled<InventoryAnalysisResult>(token));
        var presenter = new RecordingInventoryAnalysisPresenter();
        var useCase = new InventoryAnalysisUseCase(gateway);

        var ex = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            useCase.ExecuteAsync(new InventoryAnalysisRequest("inv.txt"), presenter, cancellation.Token));

        Assert.Equal("Inventory analysis canceled.", presenter.LastMessage);
        Assert.Equal(["analyzing", "failure"], presenter.CallSequence);
        Assert.IsAssignableFrom<OperationCanceledException>(ex);
    }

    private sealed class FakeGearLookupGateway(
        Func<string?, CancellationToken, Task<ItemLookupSearchResult>> searchByName) : IGearLookupGateway
    {
        public List<string?> SearchCalls { get; } = new();

        public Task<ItemLookupSearchResult> SearchByNameAsync(string? query, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SearchCalls.Add(query);
            return searchByName(query, cancellationToken);
        }

        public Task<InventoryAnalysisResult> AnalyzeInventoryAsync(string? requestedPath, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new InventoryAnalysisResult(string.Empty));
        }

        public string GetDefaultInventoryFilePath() => string.Empty;
        public string GetDefaultInventoryDirectory() => string.Empty;
    }

    private sealed class FakeInventoryAnalysisGateway(
        Func<string?, CancellationToken, Task<InventoryAnalysisResult>> analyzeInventory) : IGearLookupGateway
    {
        public Task<ItemLookupSearchResult> SearchByNameAsync(string? query, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new ItemLookupSearchResult());
        }

        public Task<InventoryAnalysisResult> AnalyzeInventoryAsync(string? requestedPath, CancellationToken cancellationToken = default)
        {
            return analyzeInventory(requestedPath, cancellationToken);
        }

        public string GetDefaultInventoryFilePath() => string.Empty;
        public string GetDefaultInventoryDirectory() => string.Empty;
    }

    private sealed class RecordingItemLookupPresenter : IItemLookupPresenter
    {
        public List<string> CallSequence { get; } = new();
        public string LastStatus { get; private set; } = string.Empty;
        public ItemLookupSearchResult? LastSearchResult { get; private set; }

        public void PresentSearching()
        {
            LastStatus = "searching";
            CallSequence.Add("searching");
        }

        public void PresentResult(ItemLookupSearchResult result)
        {
            LastSearchResult = result;
            LastStatus = "result";
            CallSequence.Add("result");
        }

        public void PresentFailure(string message)
        {
            LastStatus = "failure";
            LastSearchResult = new ItemLookupSearchResult { QueryFeedback = message };
            CallSequence.Add("failure");
        }
    }

    private sealed class RecordingInventoryAnalysisPresenter : IInventoryAnalysisPresenter
    {
        public List<string> CallSequence { get; } = new();
        public string LastMessage { get; private set; } = string.Empty;

        public void PresentAnalyzing()
        {
            LastMessage = "analyzing";
            CallSequence.Add("analyzing");
        }

        public void PresentResult(InventoryAnalysisResult result)
        {
            LastMessage = "result";
            CallSequence.Add("result");
        }

        public void PresentFailure(string message)
        {
            LastMessage = message;
            CallSequence.Add("failure");
        }
    }

    private sealed class RecordingItemLookupView : IItemLookupView
    {
        public bool DidClearResults { get; private set; }
        public string ItemLookupStatusText { get; set; } = string.Empty;
        public ItemLookupSearchResult? LookupResult { get; set; }

        public void ClearLookupResults()
        {
            DidClearResults = true;
            LookupResult = null;
        }
    }
}
