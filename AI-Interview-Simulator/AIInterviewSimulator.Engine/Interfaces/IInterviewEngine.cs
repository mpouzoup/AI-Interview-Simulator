using AIInterviewSimulator.Data.Entities;

namespace AIInterviewSimulator.Engine.Interfaces;

public interface IInterviewEngine
{
    Task<string> GetNextQuestionAsync(
        int stageNumber,
        List<UserAnswer> previousAnswers);
}