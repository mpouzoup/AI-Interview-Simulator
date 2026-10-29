using AIInterviewSimulator.Data.Entities;

namespace AIInterviewSimulator.Engine.Feedback;

public class ScriptFeedbackEngine : IFeedbackEngine
{
    public Task<string> GenerateFeedbackAsync(
        int stageNumber,
        UserAnswer answer)
    {
        return Task.FromResult(AnalyzeAnswer(stageNumber, answer.AnswerText).Text);
    }

    internal static ScriptFeedbackAnalysis AnalyzeAnswer(int stageNumber, string? answerText)
    {
        if (stageNumber == 3) return AnalyzeProblemAnswer(answerText);
        if (!ScriptFeedbackDefinitions.Stages.TryGetValue(stageNumber, out var stage))
            return new(Array.Empty<CriterionEvidence>(), "unknown", "Ευχαριστούμε για την απάντησή σας.");
        try
        {
            return AnalyzeStage(stageNumber, stage, GreekTextMatcher.Normalize(answerText));
        }
        catch (System.Text.RegularExpressions.RegexMatchTimeoutException)
        {
            return AnalyzeStage(stageNumber, stage, string.Empty);
        }
    }

    private static ScriptFeedbackAnalysis AnalyzeStage(int stageNumber, ScriptFeedbackDefinitions.Stage stage, string text)
    {
        var criteria = stage.Criteria.Select(c =>
            GreekTextMatcher.Evaluate(c.Id, text, c.Patterns, c.Denial, c.AllowIntent)
                with { Required = c.Required }).ToArray();
        var feedback = ScriptFeedbackDefinitions.Select(stageNumber, stage, criteria);
        return new(Array.AsReadOnly(criteria), feedback.Id, feedback.Text);
    }

    internal static ScriptFeedbackAnalysis AnalyzeProblemAnswer(string? answerText)
    {
        try
        {
            return AnalyzeNormalizedProblemAnswer(GreekTextMatcher.Normalize(answerText));
        }
        catch (System.Text.RegularExpressions.RegexMatchTimeoutException)
        {
            // A matcher budget exhaustion must never become a positive inference.
            return AnalyzeNormalizedProblemAnswer(string.Empty);
        }
    }

    private static ScriptFeedbackAnalysis AnalyzeNormalizedProblemAnswer(string text)
    {
        CriterionEvidence[] criteria =
        {
            GreekTextMatcher.Evaluate("Problem", text, ScriptFeedbackDefinitions.Problem, ScriptFeedbackDefinitions.ProblemDenial),
            GreekTextMatcher.Evaluate("Action", text, ScriptFeedbackDefinitions.Action, ScriptFeedbackDefinitions.ActionDenial),
            GreekTextMatcher.Evaluate("Result", text, ScriptFeedbackDefinitions.Result, ScriptFeedbackDefinitions.ResultDenial)
        };
        var feedback = ScriptFeedbackDefinitions.Select(criteria);
        return new(Array.AsReadOnly(criteria), feedback.Id, feedback.Text);
    }
}
