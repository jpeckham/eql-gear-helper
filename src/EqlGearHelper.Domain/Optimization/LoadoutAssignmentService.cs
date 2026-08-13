namespace EqlGearHelper.Domain;

public sealed record LoadoutPlan(
    Loadout? Loadout,
    LoadoutEvaluation? Evaluation,
    bool IsComplete,
    IReadOnlyList<EquipmentPosition> UnfilledPositions,
    IReadOnlyList<string>? Conflicts = null)
{
    public IReadOnlyList<string> ExplanationEvidence => Evaluation?.ExplanationEvidence ?? UnfilledPositions
        .Select(position => $"No legal owned item can fill {position.Type}[{position.Index}].")
        .Concat(Conflicts ?? [])
        .ToArray();
}

public sealed class LoadoutAssignmentService
{
    private readonly LoadoutEvaluator _evaluator = new();

    public LoadoutPlan FindBestOwned(ClassTrio trio, Collection collection, Ruleset ruleset) =>
        FindBestOwned(trio, collection, ruleset, CancellationToken.None);

    public LoadoutPlan FindBestOwned(ClassTrio trio, Collection collection, Ruleset ruleset, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(trio);
        ArgumentNullException.ThrowIfNull(collection);
        ArgumentNullException.ThrowIfNull(ruleset);
        var positions = (ruleset.RequiredPositions.Count > 0 ? ruleset.RequiredPositions : collection.CatalogItems.Values
                .Where(item => IsClassCompatible(item, trio))
                .SelectMany(item => item.Positions)
                .Distinct())
            .OrderBy(position => position.Type == SlotType.Primary ? 0 : position.Type == SlotType.Secondary ? 2 : 1)
            .ThenBy(position => position.Type)
            .ThenBy(position => position.Index)
            .ToArray();
        var candidates = positions.ToDictionary(position => position, position => collection.OwnedItems
            .Select(item => (Item: item, Definition: collection.GetCatalogItem(item)))
            .Where(candidate => IsCandidate(candidate.Definition, position, trio, ruleset))
            .OrderByDescending(candidate => CandidateValue(candidate.Definition, trio, ruleset))
            .ThenBy(candidate => candidate.Item.InstanceId)
            .ToArray());
        var best = default((Loadout Loadout, LoadoutEvaluation Evaluation, string TieKey)?);
        var failures = new Dictionary<EquipmentPosition, HashSet<string>>();
        Search(0, [], new HashSet<Guid>(), new HashSet<string>(StringComparer.OrdinalIgnoreCase), new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase));
        if (best is null)
        {
            var unfilled = failures.Keys.OrderBy(position => position.Type).ThenBy(position => position.Index).ToArray();
            var conflicts = failures.Values.SelectMany(values => values).OrderBy(value => value, StringComparer.Ordinal).ToArray();
            return new LoadoutPlan(null, null, false, unfilled, conflicts);
        }

        return new LoadoutPlan(best.Value.Loadout, best.Value.Evaluation, true, []);

        void Search(
            int index,
            List<LoadoutAssignment> assignments,
            HashSet<Guid> consumed,
            HashSet<string> loreIds,
            Dictionary<string, int> statistics)
        {
            token.ThrowIfCancellationRequested();
            if (best is not null && OptimisticUtility(index, statistics) < best.Value.Evaluation.UtilityScore)
            {
                return;
            }

            if (index == positions.Length)
            {
                var loadout = new Loadout(trio, assignments.ToArray());
                var evaluation = _evaluator.Evaluate(loadout, trio, ruleset);
                var tieKey = string.Join("|", loadout.Assignments.Select(assignment => assignment.Item.InstanceId.ToString("D")));
                if (best is null || evaluation.UtilityScore > best.Value.Evaluation.UtilityScore ||
                    (evaluation.UtilityScore == best.Value.Evaluation.UtilityScore && string.CompareOrdinal(tieKey, best.Value.TieKey) < 0))
                {
                    best = (loadout, evaluation, tieKey);
                }

                return;
            }

            var position = positions[index];
            var assigned = false;
            var reasons = new HashSet<string>(StringComparer.Ordinal);
            foreach (var candidate in candidates[position])
            {
                if (consumed.Contains(candidate.Item.InstanceId))
                {
                    reasons.Add($"{position.Type}[{position.Index}] has only physical copies already consumed by the loadout.");
                    continue;
                }

                if (candidate.Definition.IsLore && loreIds.Contains(candidate.Definition.CatalogItemId))
                {
                    reasons.Add($"{position.Type}[{position.Index}] conflicts with the lore rule for {candidate.Definition.Name}.");
                    continue;
                }

                if (position.Type == SlotType.Secondary && assignments.Any(assignment => assignment.Position.Type == SlotType.Primary && assignment.ItemDefinition?.IsTwoHanded == true))
                {
                    reasons.Add($"{position.Type}[{position.Index}] conflicts with the two-handed primary assignment.");
                    continue;
                }

                if (candidate.Definition.IsTwoHanded && position.Type == SlotType.Primary && assignments.Any(assignment => assignment.Position.Type == SlotType.Secondary))
                {
                    reasons.Add($"{position.Type}[{position.Index}] is two-handed and conflicts with the secondary assignment.");
                    continue;
                }

                assigned = true;
                assignments.Add(new LoadoutAssignment(position, candidate.Item, candidate.Definition));
                consumed.Add(candidate.Item.InstanceId);
                if (candidate.Definition.IsLore) loreIds.Add(candidate.Definition.CatalogItemId);
                AddStatistics(statistics, candidate.Definition, 1);
                Search(index + 1, assignments, consumed, loreIds, statistics);
                AddStatistics(statistics, candidate.Definition, -1);
                if (candidate.Definition.IsLore) loreIds.Remove(candidate.Definition.CatalogItemId);
                consumed.Remove(candidate.Item.InstanceId);
                assignments.RemoveAt(assignments.Count - 1);
            }

            if (!assigned)
            {
                if (reasons.Count == 0) reasons.Add($"{position.Type}[{position.Index}] has no legal owned candidate.");
                failures.TryAdd(position, []);
                failures[position].UnionWith(reasons);
            }
        }

        double OptimisticUtility(int index, IReadOnlyDictionary<string, int> currentStatistics)
        {
            var upperStatistics = new Dictionary<string, int>(currentStatistics, StringComparer.OrdinalIgnoreCase);
            for (var remaining = index; remaining < positions.Length; remaining++)
            {
                foreach (var statistic in candidates[positions[remaining]].SelectMany(candidate => candidate.Definition.Statistics.Keys).Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    var maximum = candidates[positions[remaining]].Max(candidate => candidate.Definition.Statistics.GetValueOrDefault(statistic));
                    upperStatistics[statistic] = upperStatistics.GetValueOrDefault(statistic) + Math.Max(maximum, 0);
                }
            }

            return upperStatistics.Sum(statistic => ruleset.UtilityFor(statistic.Key, statistic.Value, trio));
        }

        static void AddStatistics(Dictionary<string, int> totals, CatalogItem item, int direction)
        {
            foreach (var statistic in item.Statistics)
            {
                totals[statistic.Key] = totals.GetValueOrDefault(statistic.Key) + (statistic.Value * direction);
            }
        }
    }

    private static bool IsCandidate(CatalogItem item, EquipmentPosition required, ClassTrio trio, Ruleset ruleset) =>
        IsClassCompatible(item, trio) && item.Positions.Any(position =>
            ruleset.IsPositionAllowed(position, trio) && (required.Type == SlotType.Any || position.Type == SlotType.Any || position.Type == required.Type));

    private static bool IsClassCompatible(CatalogItem item, ClassTrio trio) => item.Classes.Intersect(trio.Classes).Count > 0;

    private static double CandidateValue(CatalogItem item, ClassTrio trio, Ruleset ruleset) =>
        item.Statistics.Sum(statistic => ruleset.UtilityFor(statistic.Key, statistic.Value, trio));
}
