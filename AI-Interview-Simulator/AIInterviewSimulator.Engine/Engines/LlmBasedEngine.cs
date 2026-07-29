using System.Text;
using AIInterviewSimulator.Data.Entities;
using AIInterviewSimulator.Engine.Interfaces;
using AIInterviewSimulator.Engine.Services;

namespace AIInterviewSimulator.Engine.Engines;

public class LlmBasedEngine : IInterviewEngine
{
    private readonly GeminiService _geminiService;

    public LlmBasedEngine(GeminiService geminiService)
    {
        _geminiService = geminiService;
    }

    public async Task<string> GetNextQuestionAsync(
        int stageNumber,
        List<UserAnswer> previousAnswers)
    {
        var promptBuilder = new StringBuilder();
        promptBuilder.AppendLine("Είσαι ένας επαγγελματίας Interviewer HR σε μια τεχνολογική εταιρεία.");
        promptBuilder.AppendLine("Διεξάγεις μια συνέντευξη εργασίας 7 σταδίων στα ελληνικά.");
        promptBuilder.AppendLine($"Αυτή τη στιγμή βρίσκεστε στο Στάδιο {stageNumber} από 7.");
        promptBuilder.AppendLine(
            "Κάνε ΜΟΝΟ την επόμενη ερώτηση (σύντομα και καθαρά). Να είσαι ευγενικός, επαγγελματικός και να λαμβάνεις υπόψη τις προηγούμενες απαντήσεις του υποψηφίου.");

        if (previousAnswers != null && previousAnswers.Count > 0)
        {
            promptBuilder.AppendLine("\nΠροηγούμενες Ερωτήσεις και Απαντήσεις Υποψηφίου:");
            foreach (var ans in previousAnswers)
            {
                promptBuilder.AppendLine($"Ερώτηση: {ans.QuestionText}");
                promptBuilder.AppendLine($"Απάντηση Υποψηφίου: {ans.AnswerText}");
            }
        }

        promptBuilder.AppendLine($"\nΤώρα κάνε την ερώτηση για το Στάδιο {stageNumber}:");

        return await _geminiService.GenerateResponseAsync(
            promptBuilder.ToString());
    }
}