using System.Collections.Frozen;

namespace EqlGearHelper.Domain;

public sealed record ClassTrio
{
    public ClassTrio(string first, string second, string third)
    {
        Classes = ClassSet.Of(first, second, third);
        if (Classes.Count != 3)
        {
            throw new ArgumentException("A class trio requires three distinct classes.");
        }
    }

    public ClassSet Classes { get; }
}

public sealed record NamedRequirement
{
    public NamedRequirement(string name, string statistic, int minimumValue)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("A requirement name is required.", nameof(name));
        if (string.IsNullOrWhiteSpace(statistic)) throw new ArgumentException("A statistic is required.", nameof(statistic));
        if (minimumValue < 0) throw new ArgumentOutOfRangeException(nameof(minimumValue));
        Name = name.Trim();
        Statistic = statistic.Trim();
        MinimumValue = minimumValue;
    }

    public string Name { get; }
    public string Statistic { get; }
    public int MinimumValue { get; }
}

public sealed class Ruleset
{
    private readonly FrozenDictionary<string, int> _charismaTargets;
    private readonly FrozenDictionary<string, IReadOnlySet<SlotType>> _allowedPositions;
    private readonly IReadOnlyList<EquipmentPosition> _requiredPositions;
    private readonly IReadOnlyList<NamedRequirement> _requirements;

    public Ruleset(
        IReadOnlyDictionary<string, int>? charismaTargets = null,
        IReadOnlyDictionary<string, IReadOnlySet<SlotType>>? allowedPositions = null,
        IReadOnlyList<EquipmentPosition>? requiredPositions = null,
        IReadOnlyList<NamedRequirement>? requirements = null)
    {
        _charismaTargets = (charismaTargets ?? new Dictionary<string, int>())
            .ToFrozenDictionary(pair => RequireClassName(pair.Key), pair => ValidateTarget(pair.Value), StringComparer.OrdinalIgnoreCase);
        _allowedPositions = (allowedPositions ?? new Dictionary<string, IReadOnlySet<SlotType>>())
            .ToFrozenDictionary(
                pair => RequireClassName(pair.Key),
                pair => (IReadOnlySet<SlotType>)(pair.Value ?? throw new ArgumentNullException(nameof(allowedPositions))).ToFrozenSet(),
                StringComparer.OrdinalIgnoreCase);
        _requiredPositions = Array.AsReadOnly((requiredPositions ?? []).ToArray());
        if (_requiredPositions.Distinct().Count() != _requiredPositions.Count)
        {
            throw new ArgumentException("Required equipment positions must be unique.", nameof(requiredPositions));
        }

        _requirements = Array.AsReadOnly((requirements ?? []).ToArray());
    }

    public static Ruleset Default { get; } = new(
        charismaTargets: new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["Bard"] = 75 });

    public IReadOnlyDictionary<string, int> CharismaTargets => _charismaTargets;
    public IReadOnlyDictionary<string, IReadOnlySet<SlotType>> AllowedPositions => _allowedPositions;
    public IReadOnlyList<EquipmentPosition> RequiredPositions => _requiredPositions;
    public IReadOnlyList<NamedRequirement> Requirements => _requirements;

    public double UtilityFor(string statistic, int currentValue, ClassTrio trio)
    {
        ArgumentNullException.ThrowIfNull(trio);
        if (string.Equals(statistic, "DEX", StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        if (!string.Equals(statistic, "CHA", StringComparison.OrdinalIgnoreCase))
        {
            return currentValue;
        }

        var applicableTargets = trio.Classes
            .Where(_charismaTargets.ContainsKey)
            .Select(className => _charismaTargets[className])
            .ToArray();
        if (applicableTargets.Length == 0)
        {
            return 0;
        }

        var target = applicableTargets.Max();
        return Math.Clamp(currentValue, 0, target);
    }

    public bool IsPositionAllowed(EquipmentPosition position, ClassTrio trio)
    {
        ArgumentNullException.ThrowIfNull(position);
        ArgumentNullException.ThrowIfNull(trio);
        return _allowedPositions.Count == 0 || trio.Classes.Any(className =>
            _allowedPositions.TryGetValue(className, out var positions) && positions.Contains(position.Type));
    }

    private static int ValidateTarget(int value) => value >= 0 ? value : throw new ArgumentOutOfRangeException(nameof(value));

    private static string RequireClassName(string value) =>
        string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("A class name is required.", nameof(value)) : value.Trim();
}
