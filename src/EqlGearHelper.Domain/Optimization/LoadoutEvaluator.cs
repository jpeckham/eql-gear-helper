using System.Collections.Frozen;

namespace EqlGearHelper.Domain;

public sealed record EffectCoverage(string EffectId, int HighestTier, int ActiveSources, bool IsStacking);

public sealed record RequirementCoverage(string Name, string Statistic, int ActualValue, int RequiredValue)
{
    public bool IsMet => ActualValue >= RequiredValue;
}

public sealed record LoadoutEvaluation(
    IReadOnlyDictionary<string, int> StatisticTotals,
    IReadOnlyDictionary<string, double> UtilityTotals,
    IReadOnlyList<EffectCoverage> EffectCoverage,
    IReadOnlyList<RequirementCoverage> Requirements,
    IReadOnlyList<string> ExplanationEvidence)
{
    public double UtilityScore => UtilityTotals.Values.Sum();
}

public sealed class LoadoutEvaluator
{
    public LoadoutEvaluation Evaluate(Loadout loadout, ClassTrio trio, Ruleset ruleset)
    {
        ArgumentNullException.ThrowIfNull(loadout);
        ArgumentNullException.ThrowIfNull(trio);
        ArgumentNullException.ThrowIfNull(ruleset);
        if (!loadout.Trio.Classes.Equals(trio.Classes)) throw new ArgumentException("The loadout trio must match the evaluated trio.", nameof(trio));

        var totals = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var effects = new List<(GearEffect Effect, string Source)>();
        var evidence = new List<string>();
        foreach (var assignment in loadout.Assignments)
        {
            var definition = assignment.ItemDefinition;
            if (definition is null) continue;
            evidence.Add($"{assignment.Position.Type}[{assignment.Position.Index}] uses {definition.Name} ({assignment.Item.InstanceId:D}).");
            foreach (var statistic in definition.Statistics)
            {
                totals[statistic.Key] = totals.GetValueOrDefault(statistic.Key) + statistic.Value;
            }

            effects.AddRange(definition.Effects.Select(effect => (effect, definition.Name)));
            foreach (var exaltation in assignment.Item.InstalledExaltations.Where(exaltation => exaltation.EffectiveClasses.Intersect(trio.Classes).Count > 0))
            {
                evidence.Add($"Exaltation {exaltation.ExaltationId} is class-compatible with the selected trio.");
            }
        }

        var utility = totals.ToFrozenDictionary(
            pair => pair.Key,
            pair => ruleset.UtilityFor(pair.Key, pair.Value, trio),
            StringComparer.OrdinalIgnoreCase);
        var coverage = effects
            .GroupBy(pair => pair.Effect.EffectId, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var stacking = group.Any(pair => pair.Effect.IsStacking);
                var active = stacking ? group.Count() : 1;
                return new EffectCoverage(group.First().Effect.EffectId, group.Max(pair => pair.Effect.Tier), active, stacking);
            })
            .OrderBy(value => value.EffectId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var requirements = ruleset.Requirements
            .Select(requirement => new RequirementCoverage(requirement.Name, requirement.Statistic, totals.GetValueOrDefault(requirement.Statistic), requirement.MinimumValue))
            .ToArray();
        evidence.AddRange(requirements.Select(requirement => $"Requirement {requirement.Name}: {requirement.ActualValue}/{requirement.RequiredValue} {requirement.Statistic}."));
        return new LoadoutEvaluation(totals.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase), utility, coverage, requirements, evidence);
    }
}
