using AIInterviewSimulator.Data.Entities;

namespace AIInterviewSimulator.Engine.Feedback;

public interface IFeedbackEngine
{
    Task<string> GenerateFeedbackAsync(
        int stageNumber,
        UserAnswer answer);
}