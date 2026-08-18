using AIInterviewSimulator.Data.Entities;
using AIInterviewSimulator.Engine.Engines;

namespace AIInterviewSimulator.Engine.Managers;

public class InterviewManager
{
    private readonly ScriptBasedEngine _scriptEngine;

    public InterviewManager(ScriptBasedEngine scriptEngine)
    {
        _scriptEngine = scriptEngine;
    }

    public async Task<string> GetNextQuestionAsync(
        string condition,
        int stageNumber,
        List<UserAnswer> previousAnswers)
    {
        var normalizedCondition = condition
            .Trim()
            .ToUpperInvariant();

        if (normalizedCondition != "A" &&
            normalizedCondition != "B")
        {
            throw new ArgumentException(
                "Condition must be A or B.",
                nameof(condition));
        }

        return await _scriptEngine.GetNextQuestionAsync(
            stageNumber,
            previousAnswers);
    }
}