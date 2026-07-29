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

    public Task<string> GenerateFeedbackAsync(
        string condition,
        int stageNumber,
        UserAnswer answer)
    {
        string normalizedCondition = condition
            .Trim()
            .ToUpperInvariant();

        return normalizedCondition switch
        {
            "A" => _scriptFeedbackEngine.GenerateFeedbackAsync(
                stageNumber,
                answer),

            "B" => _llmFeedbackEngine.GenerateFeedbackAsync(
                stageNumber,
                answer),

            _ => throw new ArgumentException(
                "Condition must be A or B.",
                nameof(condition))
        };
    }
}