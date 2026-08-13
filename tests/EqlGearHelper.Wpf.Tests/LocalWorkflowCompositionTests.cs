using EqlGearHelper.Wpf;

namespace EqlGearHelper.Wpf.Tests;

public sealed class LocalWorkflowCompositionTests
{
    [Fact]
    public async Task CreateAsync_LoadsLocalInfrastructureAndSqliteProvider()
    {
        var operations = await LocalWorkflowComposition.CreateAsync();

        Assert.NotNull(operations);
        Assert.NotNull(operations.LoadClassChoices);
        var inventory = await operations.LoadInventory(CancellationToken.None);
        Assert.NotEmpty(inventory.Items);
        Assert.Contains(inventory.Coverage, coverage => coverage == "Bank: Imported");
        Assert.Contains(inventory.Coverage, coverage => coverage == "Dragon Hoard: NotAvailable");
    }
}
