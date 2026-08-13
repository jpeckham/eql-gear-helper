using EqlGearHelper.Domain;

namespace EqlGearHelper.Application;

public interface IRepository<T>
{
    Task ReplaceAsync(T value, CancellationToken token);
    Task<T?> GetCurrentAsync(CancellationToken token);
}

public interface IInventoryParser
{
    InventorySnapshotDraft Parse(Stream input);
}

public interface IInventorySnapshotRepository
{
    Task ReplaceWithAsync(InventorySnapshotDraft snapshot, CancellationToken token);
    Task<InventorySnapshot?> GetCurrentAsync(CancellationToken token);
}

public interface ICatalogPackageImporter
{
    CatalogPackage Parse(Stream input);
}

public interface ICatalogRepository : IRepository<CatalogPackage>
{
}

public enum MappingStatus
{
    Unknown,
    ExaltationCandidate,
    Empty
}

public enum StorageAvailability
{
    Available,
    Unavailable
}

public sealed record CatalogPackage(
    string CatalogVersion,
    string RulesetVersion,
    IReadOnlyList<CatalogItem> Items,
    IReadOnlyList<ExaltationDefinition> Exaltations)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(CatalogVersion)) throw new ArgumentException("A catalog version is required.", nameof(CatalogVersion));
        if (string.IsNullOrWhiteSpace(RulesetVersion)) throw new ArgumentException("A ruleset version is required.", nameof(RulesetVersion));
        if (Items is null || Items.Count == 0) throw new ArgumentException("At least one catalog item is required.", nameof(Items));
        if (Items.Any(item => string.IsNullOrWhiteSpace(item.CatalogItemId))) throw new ArgumentException("Catalog item identities are required.", nameof(Items));
    }
}

public sealed record RawInventoryRow(
    int RowNumber,
    string Path,
    string Name,
    string ItemId,
    int Count,
    int Slots,
    MappingStatus MappingStatus,
    string RawLine);

public sealed record InventoryItemDraft(
    Guid InstanceId,
    string Path,
    string Name,
    string ItemId,
    int Count,
    int UpgradeLevel,
    MappingStatus MappingStatus);

public sealed record InventorySocketDraft(
    string Path,
    string HostPath,
    string HostItemId,
    string SocketItemId,
    string Name,
    bool IsExaltation,
    bool IsTransferred,
    MappingStatus MappingStatus);

public sealed record InventoryStorage(string Name, StorageAvailability Availability);

public sealed record InventorySnapshotDraft(
    IReadOnlyList<RawInventoryRow> Rows,
    IReadOnlyList<InventoryItemDraft> Items,
    IReadOnlyList<InventorySocketDraft> Sockets,
    IReadOnlyList<InventoryStorage> Storage)
{
    public void Validate()
    {
        if (Rows is null || Rows.Count == 0) throw new ArgumentException("An inventory snapshot must contain rows.", nameof(Rows));
        if (Rows.Any(row => string.IsNullOrWhiteSpace(row.Path))) throw new ArgumentException("Every inventory row requires a path.", nameof(Rows));
        if (Rows.GroupBy(row => row.RowNumber).Any(group => group.Count() > 1)) throw new ArgumentException("Inventory row numbers must be unique.", nameof(Rows));
    }
}

public sealed record InventorySnapshot(
    Guid SnapshotId,
    DateTimeOffset ImportedAtUtc,
    IReadOnlyList<RawInventoryRow> Rows,
    IReadOnlyList<InventoryItemDraft> Items,
    IReadOnlyList<InventorySocketDraft> Sockets,
    IReadOnlyList<InventoryStorage> Storage);
