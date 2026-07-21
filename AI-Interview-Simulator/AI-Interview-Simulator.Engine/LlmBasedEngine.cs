using System.Collections.Generic;
using System.Threading.Tasks;
using AI_Interview_Simulator.Data.Entities;

namespace AI_Interview_Simulator.Engine;

public class LlmBasedEngine
{
    private readonly List<string> _stages = new()
    {
        "Καλώς ορίσατε! (Gemini AI Mode). Θα θέλατε να μας κάνετε μια μικρή αυτοπαρουσίαση;",
        "Ωραία. Με βάση όσα είπατε, ποια θεωρείτε τη μεγαλύτερή σας πρόκληση μέχρι τώρα;",
        "Πώς αντιμετωπίζετε διαφωνίες σε μια ομάδα;",
        "Περιγράψτε μου ένα project που σας δυσκόλεψε.",
        "Πώς διαχειρίζεστε το άγχος σε κρίσιμες προθεσμίες;",
        "Πού φαντάζεστε τον εαυτό σας επαγγελματικά σε 3 χρόνια;",
        "Έχετε κάποια ερώτηση για εμάς;"
    };

    public async Task<string> GenerateNextQuestionAsync(int stageNumber, List<UserAnswer> previousAnswers)
    {
        await Task.Delay(100);

        if (stageNumber - 1 < _stages.Count)
        {
            return $"[Gemini AI] {_stages[stageNumber - 1]}";
        }

        return "[Gemini AI] Η συνέντευξη έχει ολοκληρωθεί. Σας ευχαριστούμε πολύ!";
    }
}