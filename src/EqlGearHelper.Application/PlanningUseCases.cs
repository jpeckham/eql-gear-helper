using EqlGearHelper.Domain;

namespace EqlGearHelper.Application;

public sealed class BuildPlannerUseCase(LoadoutAssignmentService assignmentService, LoadoutEvaluator evaluator)
{
    private LoadoutPlan? _lastCompleteResult;

    public LoadoutPlan? LastCompleteResult => _lastCompleteResult;

    public Task<LoadoutPlan> ExecuteAsync(ClassTrio trio, Collection collection, Ruleset ruleset, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var assigned = assignmentService.FindBestOwned(trio, collection, ruleset, token);
        token.ThrowIfCancellationRequested();
        var result = assigned.Loadout is null
            ? assigned
            : assigned with { Evaluation = evaluator.Evaluate(assigned.Loadout, trio, ruleset) };
        if (result.IsComplete) _lastCompleteResult = result;
        return Task.FromResult(result);
    }
}
