using AIInterviewSimulator.Data.Entities;
using AIInterviewSimulator.Engine.Engines;

namespace AIInterviewSimulator.Engine.Managers;

public class InterviewManager
{
    private readonly ScriptBasedEngine _scriptEngine;
    private readonly LlmBasedEngine _llmEngine;

    public InterviewManager(
        ScriptBasedEngine scriptEngine,
        LlmBasedEngine llmEngine)
    {
        _scriptEngine = scriptEngine;
        _llmEngine = llmEngine;
    }

    public async Task<string> GetNextQuestionAsync(
        string condition,
        int stageNumber,
        List<UserAnswer> previousAnswers)
    {
        var normalizedCondition = condition.Trim().ToUpperInvariant();

        if (normalizedCondition == "A")
        {
            return await _scriptEngine.GetNextQuestionAsync(
                stageNumber,
                previousAnswers);
        }

        if (normalizedCondition == "B")
        {
            return await _llmEngine.GetNextQuestionAsync(
                stageNumber,
                previousAnswers);
        }

        throw new ArgumentException(
            "Condition must be A or B.",
            nameof(condition));
    }
}