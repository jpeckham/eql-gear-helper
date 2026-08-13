namespace EqlGearHelper.Domain;

public sealed class Collection
{
    private readonly IReadOnlyDictionary<string, CatalogItem> _catalogItems;
    private readonly IReadOnlyList<OwnedItemInstance> _ownedItems;

    public Collection(IReadOnlyList<CatalogItem> catalogItems, IReadOnlyList<OwnedItemInstance> ownedItems)
    {
        ArgumentNullException.ThrowIfNull(catalogItems);
        ArgumentNullException.ThrowIfNull(ownedItems);
        if (catalogItems.GroupBy(item => item.CatalogItemId, StringComparer.OrdinalIgnoreCase).Any(group => group.Count() > 1))
        {
            throw new ArgumentException("Catalog item identities must be unique.", nameof(catalogItems));
        }

        _catalogItems = catalogItems.ToDictionary(item => item.CatalogItemId, StringComparer.OrdinalIgnoreCase);
        _ownedItems = Array.AsReadOnly(ownedItems.ToArray());
        if (_ownedItems.Any(item => !_catalogItems.ContainsKey(item.CatalogItemId)))
        {
            throw new ArgumentException("Every owned item must have a catalog definition.", nameof(ownedItems));
        }
    }

    public IReadOnlyDictionary<string, CatalogItem> CatalogItems => _catalogItems;
    public IReadOnlyList<OwnedItemInstance> OwnedItems => _ownedItems;

    public CatalogItem GetCatalogItem(OwnedItemInstance item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return _catalogItems[item.CatalogItemId];
    }
}

public sealed record InventoryLocation
{
    public static InventoryLocation Carried { get; } = new("Carried");

    public InventoryLocation(string container, string? subLocation = null)
    {
        if (string.IsNullOrWhiteSpace(container))
        {
            throw new ArgumentException("An inventory container is required.", nameof(container));
        }

        Container = container.Trim();
        SubLocation = string.IsNullOrWhiteSpace(subLocation) ? null : subLocation.Trim();
    }

    public string Container { get; }
    public string? SubLocation { get; }
}

public sealed record InstalledExaltation
{
    public InstalledExaltation(
        string exaltationId,
        ClassSet hostClasses,
        ClassSet exaltationClasses,
        Guid sourceInstanceId,
        bool isTransferred = false)
    {
        if (string.IsNullOrWhiteSpace(exaltationId))
        {
            throw new ArgumentException("An Exaltation identity is required.", nameof(exaltationId));
        }

        ExaltationId = exaltationId.Trim();
        HostClasses = hostClasses ?? throw new ArgumentNullException(nameof(hostClasses));
        ExaltationClasses = exaltationClasses ?? throw new ArgumentNullException(nameof(exaltationClasses));
        EffectiveClasses = HostClasses.Intersect(ExaltationClasses);
        if (EffectiveClasses.Count == 0)
        {
            throw new ArgumentException("An installed Exaltation must support at least one host class.", nameof(exaltationClasses));
        }

        if (sourceInstanceId == Guid.Empty)
        {
            throw new ArgumentException("A physical source-instance identity is required.", nameof(sourceInstanceId));
        }

        SourceInstanceId = sourceInstanceId;
        IsTransferred = isTransferred;
    }

    public string ExaltationId { get; }
    public ClassSet HostClasses { get; }
    public ClassSet ExaltationClasses { get; }
    public ClassSet EffectiveClasses { get; }
    public Guid SourceInstanceId { get; }
    public bool IsTransferred { get; }
}

public sealed record OwnedItemInstance
{
    public OwnedItemInstance(
        Guid instanceId,
        string catalogItemId,
        int upgradeLevel,
        InventoryLocation location,
        IReadOnlyList<InstalledExaltation> installedExaltations)
    {
        if (instanceId == Guid.Empty)
        {
            throw new ArgumentException("A physical-copy identity is required.", nameof(instanceId));
        }

        if (string.IsNullOrWhiteSpace(catalogItemId))
        {
            throw new ArgumentException("A catalog item identity is required.", nameof(catalogItemId));
        }

        if (upgradeLevel < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(upgradeLevel));
        }

        InstanceId = instanceId;
        CatalogItemId = catalogItemId.Trim();
        UpgradeLevel = upgradeLevel;
        Location = location ?? throw new ArgumentNullException(nameof(location));
        InstalledExaltations = Array.AsReadOnly((installedExaltations ?? throw new ArgumentNullException(nameof(installedExaltations))).ToArray());
    }

    public Guid InstanceId { get; }
    public string CatalogItemId { get; }
    public int UpgradeLevel { get; }
    public InventoryLocation Location { get; }
    public IReadOnlyList<InstalledExaltation> InstalledExaltations { get; }
}

public sealed record OwnedExaltationInstance
{
    public OwnedExaltationInstance(Guid instanceId, string exaltationId, InventoryLocation location)
    {
        if (instanceId == Guid.Empty)
        {
            throw new ArgumentException("A physical-copy identity is required.", nameof(instanceId));
        }

        if (string.IsNullOrWhiteSpace(exaltationId))
        {
            throw new ArgumentException("An Exaltation identity is required.", nameof(exaltationId));
        }

        InstanceId = instanceId;
        ExaltationId = exaltationId.Trim();
        Location = location ?? throw new ArgumentNullException(nameof(location));
    }

    public Guid InstanceId { get; }
    public string ExaltationId { get; }
    public InventoryLocation Location { get; }
}
