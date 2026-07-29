using System.Text;
using AIInterviewSimulator.Data.Entities;
using AIInterviewSimulator.Engine.Services;

namespace AIInterviewSimulator.Engine.Feedback;

public class LlmFeedbackEngine : IFeedbackEngine
{
    private readonly GeminiService _geminiService;

    public LlmFeedbackEngine(GeminiService geminiService)
    {
        _geminiService = geminiService;
    }

    public async Task<string> GenerateFeedbackAsync(
        int stageNumber,
        UserAnswer answer)
    {
        var promptBuilder = new StringBuilder();

        promptBuilder.AppendLine(
            "Είσαι ένας έμπειρος σύμβουλος προετοιμασίας για συνεντεύξεις εργασίας.");

        promptBuilder.AppendLine(
            "Αξιολόγησε την απάντηση του υποψηφίου και δώσε σύντομη, συγκεκριμένη και υποστηρικτική ανατροφοδότηση στα ελληνικά.");

        promptBuilder.AppendLine($"Στάδιο συνέντευξης: {stageNumber}");
        promptBuilder.AppendLine($"Όνομα σταδίου: {answer.StageName}");
        promptBuilder.AppendLine($"Ερώτηση: {answer.QuestionText}");
        promptBuilder.AppendLine($"Απάντηση υποψηφίου: {answer.AnswerText}");

        promptBuilder.AppendLine();
        promptBuilder.AppendLine("Οδηγίες:");
        promptBuilder.AppendLine("- Γράψε το πολύ 3 σύντομες προτάσεις.");
        promptBuilder.AppendLine("- Ανέφερε ένα θετικό στοιχείο της απάντησης.");
        promptBuilder.AppendLine("- Πρότεινε μία συγκεκριμένη βελτίωση.");
        promptBuilder.AppendLine("- Μην δώσεις αριθμητική βαθμολογία.");
        promptBuilder.AppendLine("- Μην δημιουργήσεις νέα ερώτηση.");
        promptBuilder.AppendLine("- Μην επαναλάβεις ολόκληρη την απάντηση του υποψηφίου.");

        return await _geminiService.GenerateResponseAsync(
            promptBuilder.ToString());
    }
}