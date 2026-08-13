namespace EqlGearHelper.Application;

public sealed class ImportInventorySnapshotUseCase(IInventoryParser parser, IInventorySnapshotRepository repository)
{
    public async Task<InventorySnapshot> ExecuteAsync(Stream input, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(input);
        var draft = parser.Parse(input);
        draft.Validate();
        await repository.ReplaceWithAsync(draft, token);
        return await repository.GetCurrentAsync(token)
            ?? throw new InvalidOperationException("The inventory snapshot was not persisted.");
    }
}
