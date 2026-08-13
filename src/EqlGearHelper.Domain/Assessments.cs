namespace EqlGearHelper.Domain;

public enum RecommendationConfidence
{
    Complete,
    Partial,
    Blocked
}

public enum FinalAction
{
    Keep,
    ExtractExaltation,
    DisposeCandidate,
    Investigate
}

public sealed record Assessment
{
    public Assessment(
        Guid assetInstanceId,
        FinalAction finalAction,
        RecommendationConfidence confidence,
        string explanation)
    {
        if (assetInstanceId == Guid.Empty)
        {
            throw new ArgumentException("An assessed asset identity is required.", nameof(assetInstanceId));
        }

        if (string.IsNullOrWhiteSpace(explanation))
        {
            throw new ArgumentException("An assessment explanation is required.", nameof(explanation));
        }

        AssetInstanceId = assetInstanceId;
        FinalAction = finalAction;
        Confidence = confidence;
        Explanation = explanation.Trim();
    }

    public Guid AssetInstanceId { get; }
    public FinalAction FinalAction { get; }
    public RecommendationConfidence Confidence { get; }
    public string Explanation { get; }
}
