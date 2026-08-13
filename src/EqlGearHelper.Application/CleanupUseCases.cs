using EqlGearHelper.Domain;

namespace EqlGearHelper.Application;

public sealed record AcquisitionSource
{
    public AcquisitionSource(string name, bool isQuestReward = false, bool isConfirmed = true)
    {
        Name = string.IsNullOrWhiteSpace(name) ? throw new ArgumentException("A source name is required.", nameof(name)) : name.Trim();
        IsQuestReward = isQuestReward;
        IsConfirmed = isConfirmed;
    }

    public string Name { get; }
    public bool IsQuestReward { get; }
    public bool IsConfirmed { get; }
}

public sealed record BuildTargetRequest(
    ClassTrio Trio,
    Collection Collection,
    Ruleset Ruleset,
    IReadOnlyDictionary<string, IReadOnlyList<AcquisitionSource>>? Sources = null);

public sealed record TargetRecommendation(
    EquipmentPosition Position,
    CatalogItem Target,
    IReadOnlyList<CatalogItem> Alternatives,
    IReadOnlyList<AcquisitionSource> AcquisitionSources);

public sealed record TargetGap(EquipmentPosition Position, string? CurrentCatalogItemId, string TargetCatalogItemId, IReadOnlyList<string> MissingEffects);

public sealed record TargetPlan(
    LoadoutPlan BestOwned,
    IReadOnlyList<TargetRecommendation> Targets,
    IReadOnlyList<TargetGap> Gaps);

public sealed class BuildTargetPlanUseCase
{
    private readonly LoadoutAssignmentService _assignmentService = new();

    public Task<TargetPlan> ExecuteAsync(BuildTargetRequest request, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(request);
        token.ThrowIfCancellationRequested();
        var bestOwned = _assignmentService.FindBestOwned(request.Trio, request.Collection, request.Ruleset, token);
        var positions = request.Ruleset.RequiredPositions.Count > 0
            ? request.Ruleset.RequiredPositions
            : request.Collection.CatalogItems.Values.SelectMany(item => item.Positions).Distinct().ToArray();
        var targets = new List<TargetRecommendation>();
        var gaps = new List<TargetGap>();
        foreach (var position in positions)
        {
            token.ThrowIfCancellationRequested();
            var candidates = request.Collection.CatalogItems.Values
                .Where(item => item.Classes.Intersect(request.Trio.Classes).Count > 0)
                .Where(item => item.Positions.Any(candidate => candidate.Equals(position) || candidate.Type == SlotType.Any || position.Type == SlotType.Any))
                .Where(item => IsPractical(item, request.Sources))
                .OrderByDescending(item => Score(item, request.Ruleset, request.Trio))
                .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (candidates.Length == 0) continue;
            var target = candidates[0];
            var sources = request.Sources is not null && request.Sources.TryGetValue(target.CatalogItemId, out var found) ? found : [];
            targets.Add(new TargetRecommendation(position, target, candidates.Skip(1).ToArray(), sources));
            var current = bestOwned.Loadout?.Assignments.FirstOrDefault(assignment => assignment.Position.Equals(position));
            var currentDefinition = current?.ItemDefinition ?? (current is null ? null : request.Collection.GetCatalogItem(current.Item));
            var currentEffects = currentDefinition?.Effects.Select(effect => effect.EffectId).ToHashSet(StringComparer.OrdinalIgnoreCase) ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var missingEffects = target.Effects.Select(effect => effect.EffectId).Where(effect => !currentEffects.Contains(effect)).ToArray();
            if (!string.Equals(currentDefinition?.CatalogItemId, target.CatalogItemId, StringComparison.OrdinalIgnoreCase) || missingEffects.Length > 0)
                gaps.Add(new TargetGap(position, currentDefinition?.CatalogItemId, target.CatalogItemId, missingEffects));
        }
        return Task.FromResult(new TargetPlan(bestOwned, targets, gaps));
    }

    private static bool IsPractical(CatalogItem item, IReadOnlyDictionary<string, IReadOnlyList<AcquisitionSource>>? sources)
    {
        if (sources is null) return true;
        return sources.TryGetValue(item.CatalogItemId, out var itemSources) && itemSources.Any(source => source.IsConfirmed && !source.IsQuestReward);
    }

    private static double Score(CatalogItem item, Ruleset ruleset, ClassTrio trio) =>
        item.Statistics.Sum(statistic => ruleset.UtilityFor(statistic.Key, statistic.Value, trio)) + item.Effects.Sum(effect => effect.Tier);
}

public sealed record AnalyzeCollectionRequest(
    Collection Collection,
    Ruleset Ruleset,
    IReadOnlyDictionary<string, ExaltationDefinition>? Exaltations = null,
    IReadOnlySet<Guid>? UnknownAssetIds = null,
    IReadOnlySet<string>? SafelyResolvedExaltationIds = null,
    double MaterialityTolerance = 0);

public sealed record RepresentativeUse(ClassTrio Trio, EquipmentPosition Position);

public sealed record CollectionAssetAssessment(
    Assessment Assessment,
    bool BaseUseful,
    bool IsRedundant,
    IReadOnlyList<string> PreservationReasons,
    IReadOnlyList<RepresentativeUse> RepresentativeUses)
{
    public Guid AssetInstanceId => Assessment.AssetInstanceId;
    public FinalAction FinalAction => Assessment.FinalAction;
    public RecommendationConfidence Confidence => Assessment.Confidence;
}

public sealed record CollectionAnalysisResult(
    IReadOnlyList<CollectionAssetAssessment> Assessments,
    IReadOnlyList<ClassTrio> AnalyzedTrios);

public interface IAnalysisRepository
{
    Task SaveAsync(Guid snapshotId, IReadOnlyList<Assessment> assessments, CancellationToken token);
    Task<IReadOnlyList<Assessment>?> GetAsync(Guid snapshotId, CancellationToken token);
}

public sealed class AnalyzeCollectionUseCase(LoadoutAssignmentService assignmentService, LoadoutEvaluator evaluator)
{
    public Task<CollectionAnalysisResult> ExecuteAsync(AnalyzeCollectionRequest request, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Collection);
        ArgumentNullException.ThrowIfNull(request.Ruleset);
        if (request.MaterialityTolerance < 0) throw new ArgumentOutOfRangeException(nameof(request.MaterialityTolerance));
        token.ThrowIfCancellationRequested();

        var trios = GetLegalTrios(request.Collection).ToArray();
        var plans = trios.Select(trio =>
        {
            var plan = assignmentService.FindBestOwned(trio, request.Collection, request.Ruleset, token);
            return new PlannedTrio(trio, plan.Loadout is null ? plan : plan with { Evaluation = evaluator.Evaluate(plan.Loadout, trio, request.Ruleset) });
        }).ToArray();
        var assessments = request.Collection.OwnedItems.Select(item => Assess(item, request, plans, token)).ToArray();
        return Task.FromResult(new CollectionAnalysisResult(assessments, trios));
    }

    private CollectionAssetAssessment Assess(OwnedItemInstance item, AnalyzeCollectionRequest request, IReadOnlyList<PlannedTrio> plans, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var representativeUses = plans
            .Where(plan => plan.Plan.Loadout is not null)
            .SelectMany(plan => plan.Plan.Loadout!.Assignments
                .Where(assignment => assignment.Item.InstanceId == item.InstanceId)
                .Select(assignment => new RepresentativeUse(plan.Trio, assignment.Position)))
            .ToArray();

        // This order is intentional: final action is never used to infer usefulness.
        var baseUseful = IsMateriallyUseful(item, request, plans, token);
        var redundant = !baseUseful;
        var preservationReasons = PreservationReasons(item, request).ToArray();
        var blocked = preservationReasons.Any(reason => reason.StartsWith("Unknown", StringComparison.Ordinal));
        var confidence = blocked ? RecommendationConfidence.Blocked : RecommendationConfidence.Complete;
        var action = blocked
            ? FinalAction.Investigate
            : baseUseful ? FinalAction.Keep
            : preservationReasons.Length > 0 ? FinalAction.ExtractExaltation
            : FinalAction.DisposeCandidate;
        var explanation = $"Base usefulness: {(baseUseful ? "material representative use found" : "no material representative use")}; " +
            $"redundancy: {(redundant ? "redundant" : "retained")}; preservation: " +
            (preservationReasons.Length == 0 ? "none" : string.Join(", ", preservationReasons)) + "; " +
            $"action: {action}; confidence: {confidence}.";
        return new CollectionAssetAssessment(new Assessment(item.InstanceId, action, confidence, explanation), baseUseful, redundant, preservationReasons, representativeUses);
    }

    private bool IsMateriallyUseful(OwnedItemInstance item, AnalyzeCollectionRequest request, IReadOnlyList<PlannedTrio> plans, CancellationToken token)
    {
        var withoutItem = new Collection(request.Collection.CatalogItems.Values.ToArray(), request.Collection.OwnedItems.Where(candidate => candidate.InstanceId != item.InstanceId).ToArray());
        foreach (var planned in plans.Where(candidate => candidate.Plan.Loadout?.Assignments.Any(assignment => assignment.Item.InstanceId == item.InstanceId) == true))
        {
            token.ThrowIfCancellationRequested();
            var replacement = assignmentService.FindBestOwned(planned.Trio, withoutItem, request.Ruleset, token);
            if (!replacement.IsComplete) return true;
            var replacementEvaluation = replacement.Loadout is null ? null : evaluator.Evaluate(replacement.Loadout, planned.Trio, request.Ruleset);
            if (HasStructuralRegression(planned.Plan.Evaluation, replacementEvaluation)) return true;
            if (Score(planned.Plan.Evaluation) - Score(replacementEvaluation) > request.MaterialityTolerance) return true;
        }

        return false;
    }

    private static double Score(LoadoutEvaluation? evaluation) => evaluation is null
        ? double.NegativeInfinity
        : evaluation.UtilityTotals.Values.Sum();

    private static bool HasStructuralRegression(LoadoutEvaluation? baseline, LoadoutEvaluation? replacement)
    {
        if (baseline is null || replacement is null) return baseline is not null;
        var lostEffectCoverage = baseline.EffectCoverage.Any(effect => !replacement.EffectCoverage.Any(candidate =>
            string.Equals(candidate.EffectId, effect.EffectId, StringComparison.OrdinalIgnoreCase) &&
            candidate.HighestTier >= effect.HighestTier &&
            candidate.ActiveSources >= effect.ActiveSources));
        var lostRequirement = baseline.Requirements.Where(requirement => requirement.IsMet).Any(requirement =>
            !replacement.Requirements.Any(candidate =>
                string.Equals(candidate.Name, requirement.Name, StringComparison.OrdinalIgnoreCase) && candidate.IsMet));
        return lostEffectCoverage || lostRequirement;
    }

    private static IEnumerable<string> PreservationReasons(OwnedItemInstance item, AnalyzeCollectionRequest request)
    {
        if (request.UnknownAssetIds?.Contains(item.InstanceId) == true) yield return "Unknown asset data";
        foreach (var installed in item.InstalledExaltations)
        {
            var isSafelyResolved = request.SafelyResolvedExaltationIds?.Contains(installed.ExaltationId) == true;
            if (request.Exaltations is null || !request.Exaltations.TryGetValue(installed.ExaltationId, out var definition))
            {
                yield return $"Unknown Exaltation {installed.ExaltationId}";
            }
            else if (definition.IsValuable)
            {
                yield return $"Valuable Exaltation source: {definition.Name}";
            }
            else if (!isSafelyResolved)
            {
                yield return $"Installed Exaltation requires safe resolution: {definition.Name}";
            }
        }
    }

    private static IEnumerable<ClassTrio> GetLegalTrios(Collection collection)
    {
        var classes = collection.CatalogItems.Values.SelectMany(item => item.Classes).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray();
        for (var first = 0; first < classes.Length - 2; first++)
        for (var second = first + 1; second < classes.Length - 1; second++)
        for (var third = second + 1; third < classes.Length; third++)
            yield return new ClassTrio(classes[first], classes[second], classes[third]);
    }

    private sealed record PlannedTrio(ClassTrio Trio, LoadoutPlan Plan);
}
