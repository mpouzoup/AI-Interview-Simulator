using System.Collections.Generic;
using System.Threading.Tasks;
using AI_Interview_Simulator.Data.Entities;

namespace AI_Interview_Simulator.Engine;

public class InterviewManager
{
    private readonly ScriptBasedEngine _scriptEngine;
    private readonly LlmBasedEngine _llmEngine;

    public InterviewManager(ScriptBasedEngine scriptEngine, LlmBasedEngine llmEngine)
    {
        _scriptEngine = scriptEngine;
        _llmEngine = llmEngine;
    }

    public async Task<string> GetNextQuestionAsync(string condition, int stageNumber, List<UserAnswer> previousAnswers)
    {
        if (condition.ToUpper() == "A")
        {
            return await _scriptEngine.GetQuestionForStageAsync(stageNumber);
        }
        else if (condition.ToUpper() == "B")
        {
            return await _llmEngine.GenerateNextQuestionAsync(stageNumber, previousAnswers);
        }
        else
        {
            return "Invalid condition specified.";
        }
    }
}