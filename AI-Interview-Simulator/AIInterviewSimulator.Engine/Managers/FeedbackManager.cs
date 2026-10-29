using AIInterviewSimulator.Data.Entities;
using AIInterviewSimulator.Engine.Feedback;

namespace AIInterviewSimulator.Engine.Managers;

public class FeedbackManager
{
    private readonly ScriptFeedbackEngine _scriptFeedbackEngine;
    private readonly LlmFeedbackEngine _llmFeedbackEngine;

    public FeedbackManager(
        ScriptFeedbackEngine scriptFeedbackEngine,
        LlmFeedbackEngine llmFeedbackEngine)
    {
        _scriptFeedbackEngine = scriptFeedbackEngine;
        _llmFeedbackEngine = llmFeedbackEngine;
    }

    public async Task<FeedbackResult> GenerateFeedbackAsync(
        string condition,
        int stageNumber,
        UserAnswer answer,
        CancellationToken cancellationToken = default)
    {
        string normalizedCondition = condition
            .Trim()
            .ToUpperInvariant();

        return normalizedCondition switch
        {
            "A" => FeedbackResult.FromText(await _scriptFeedbackEngine.GenerateFeedbackAsync(
                stageNumber,
                answer)),

            "B" => await _llmFeedbackEngine.GenerateFeedbackAsync(
                stageNumber,
                answer, cancellationToken),

            _ => throw new ArgumentException(
                "Condition must be A or B.",
                nameof(condition))
        };
    }
}