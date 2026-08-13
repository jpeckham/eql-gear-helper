using System.Collections.Frozen;

namespace EqlGearHelper.Domain;

public sealed class ClassSet : IEquatable<ClassSet>, IReadOnlyCollection<string>
{
    private readonly HashSet<string> _classes;

    private ClassSet(IEnumerable<string> classes)
    {
        _classes = new HashSet<string>(classes.Select(Normalize), StringComparer.OrdinalIgnoreCase);
    }

    public int Count => _classes.Count;

    public static ClassSet Empty { get; } = new([]);

    public static ClassSet Of(params string[] classes) => new(classes);

    public bool Contains(string className) => _classes.Contains(Normalize(className));

    public ClassSet Intersect(ClassSet other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return new ClassSet(_classes.Intersect(other._classes, StringComparer.OrdinalIgnoreCase));
    }

    public bool Equals(ClassSet? other) => other is not null && _classes.SetEquals(other._classes);

    public override bool Equals(object? obj) => obj is ClassSet other && Equals(other);

    public override int GetHashCode()
    {
        var hash = 0;
        foreach (var className in _classes)
        {
            hash ^= StringComparer.OrdinalIgnoreCase.GetHashCode(className);
        }

        return hash;
    }

    public IEnumerator<string> GetEnumerator() => _classes.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).GetEnumerator();

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();

    private static string Normalize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A class name is required.", nameof(value));
        }

        return value.Trim();
    }
}

public enum SlotType
{
    Head,
    Face,
    Ear,
    Neck,
    Shoulders,
    Arms,
    Wrist,
    Hands,
    Finger,
    Ring,
    Chest,
    Back,
    Waist,
    Legs,
    Feet,
    Primary,
    Secondary,
    Ranged,
    Any
}

public sealed record EquipmentPosition
{
    public EquipmentPosition(SlotType type, int index = 0)
    {
        if (index < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        Type = type;
        Index = index;
    }

    public SlotType Type { get; }

    public int Index { get; }
}

public sealed record GearEffect
{
    public GearEffect(string effectId, int tier = 1, bool isStacking = false)
    {
        if (string.IsNullOrWhiteSpace(effectId))
        {
            throw new ArgumentException("An effect identity is required.", nameof(effectId));
        }

        if (tier < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(tier));
        }

        EffectId = effectId.Trim();
        Tier = tier;
        IsStacking = isStacking;
    }

    public string EffectId { get; }
    public int Tier { get; }
    public bool IsStacking { get; }
}

public sealed record CatalogItem
{
    public CatalogItem(
        string catalogItemId,
        string name,
        ClassSet classes,
        IReadOnlyList<EquipmentPosition> positions,
        IReadOnlyDictionary<string, int>? statistics = null,
        bool isLore = false,
        bool isTwoHanded = false,
        IReadOnlyList<GearEffect>? effects = null)
    {
        CatalogItemId = Require(catalogItemId, nameof(catalogItemId));
        Name = Require(name, nameof(name));
        Classes = classes ?? throw new ArgumentNullException(nameof(classes));
        if (classes.Count == 0)
        {
            throw new ArgumentException("A catalog item must support at least one class.", nameof(classes));
        }

        Positions = Array.AsReadOnly((positions ?? throw new ArgumentNullException(nameof(positions))).ToArray());
        if (Positions.Count == 0)
        {
            throw new ArgumentException("A catalog item must have at least one position.", nameof(positions));
        }

        Statistics = (statistics ?? new Dictionary<string, int>())
            .ToFrozenDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
        IsLore = isLore;
        IsTwoHanded = isTwoHanded;
        Effects = Array.AsReadOnly((effects ?? []).ToArray());
    }

    public string CatalogItemId { get; }
    public string Name { get; }
    public ClassSet Classes { get; }
    public IReadOnlyList<EquipmentPosition> Positions { get; }
    public IReadOnlyDictionary<string, int> Statistics { get; }
    public bool IsLore { get; }
    public bool IsTwoHanded { get; }
    public IReadOnlyList<GearEffect> Effects { get; }

    private static string Require(string value, string parameterName) =>
        string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("A value is required.", parameterName) : value.Trim();
}

public sealed record ExaltationDefinition
{
    public ExaltationDefinition(string exaltationId, string name, ClassSet classes, bool isValuable = false)
    {
        ExaltationId = Require(exaltationId, nameof(exaltationId));
        Name = Require(name, nameof(name));
        Classes = classes ?? throw new ArgumentNullException(nameof(classes));
        if (classes.Count == 0)
        {
            throw new ArgumentException("An Exaltation must support at least one class.", nameof(classes));
        }

        IsValuable = isValuable;
    }

    public string ExaltationId { get; }
    public string Name { get; }
    public ClassSet Classes { get; }
    public bool IsValuable { get; }

    private static string Require(string value, string parameterName) =>
        string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("A value is required.", parameterName) : value.Trim();
}
