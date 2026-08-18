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

        promptBuilder.AppendLine(
            "- Γράψε το πολύ 3 σύντομες προτάσεις.");

        promptBuilder.AppendLine(
            "- Αν υπάρχει ουσιαστικό θετικό στοιχείο στην απάντηση, ανέφερέ το σύντομα.");

        promptBuilder.AppendLine(
            "- Πρότεινε μία συγκεκριμένη βελτίωση.");

        promptBuilder.AppendLine(
            "- Σύνδεσε την ανατροφοδότηση με συγκεκριμένο στοιχείο που υπάρχει στην απάντηση του υποψηφίου, χωρίς να επαναλαμβάνεις ολόκληρη την απάντηση.");

        promptBuilder.AppendLine(
            "- Χρησιμοποίησε ευγενικό, επαγγελματικό και ουδέτερο ύφος.");

        promptBuilder.AppendLine(
            "- Απόφυγε απόλυτες κρίσεις για την ικανότητα ή την καταλληλότητα του υποψηφίου.");

        promptBuilder.AppendLine(
            "- Βάσισε την ανατροφοδότηση αποκλειστικά στο περιεχόμενο της απάντησης και στη σχετική ερώτηση.");

        promptBuilder.AppendLine(
            "- Μην κάνεις υποθέσεις ή κρίσεις για τον υποψήφιο με βάση φύλο, ηλικία, καταγωγή, όνομα ή άλλα προσωπικά χαρακτηριστικά.");

        promptBuilder.AppendLine(
            "- Αν η απάντηση είναι πολύ ασαφής, άσχετη ή δεν περιέχει αρκετές πληροφορίες για ουσιαστική αξιολόγηση, δήλωσέ το ευγενικά και πρότεινε τι είδους πληροφορία θα μπορούσε να προσθέσει ο υποψήφιος.");

        promptBuilder.AppendLine(
            "- Μην επινοείς θετικά στοιχεία ή πληροφορίες που δεν υπάρχουν στην απάντηση.");

        promptBuilder.AppendLine(
            "- Μην δώσεις αριθμητική βαθμολογία.");

        promptBuilder.AppendLine(
            "- Μην δημιουργήσεις νέα ερώτηση.");

        promptBuilder.AppendLine(
            "- Μην επαναλάβεις ολόκληρη την απάντηση του υποψηφίου.");

        return await _geminiService.GenerateResponseAsync(
            promptBuilder.ToString());
    }
}