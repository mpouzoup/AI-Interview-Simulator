using AIInterviewSimulator.Data.Entities;
using AIInterviewSimulator.Engine.Interfaces;

namespace AIInterviewSimulator.Engine.Engines;

public class ScriptBasedEngine : IInterviewEngine
{
    private readonly List<string> _questions;

    public ScriptBasedEngine()
    {
        _questions = new List<string>
        {
            "Καλώς ορίσατε στη συνέντευξη. Θα θέλατε να μας κάνετε μια μικρή αυτοπαρουσίαση;",

            "Ποια είναι τα κίνητρά σας για τη συγκεκριμένη θέση και τι γνωρίζετε για την εταιρεία μας;",

            "Πώς θα αξιολογούσατε τις ικανότητές σας σε σχέση με τις απαιτήσεις της θέσης;",

            "Πείτε μας για μια εμπειρία σας που αφορά τη συνεργασία και την ομαδικότητα.",

            "Πώς διαχειρίζεστε τις δύσκολες καταστάσεις ή τις πιέσεις στον εργασιακό χώρο;",

            "Περιγράψτε μας μια περίπτωση αποτυχίας και πώς προσαρμοστήκατε σε αυτήν.",

            "Πού θα θέλατε να βρίσκεστε επαγγελματικά τα επόμενα χρόνια και πώς πιστεύετε ότι αυτή η θέση μπορεί να συμβάλει στην εξέλιξή σας;"
        };
    }

    public Task<string> GetNextQuestionAsync(
        int stageNumber,
        List<UserAnswer> previousAnswers)
    {
        int index = stageNumber - 1;

        if (index >= 0 && index < _questions.Count)
        {
            return Task.FromResult(_questions[index]);
        }

        return Task.FromResult(
            "Η συνέντευξη έχει ολοκληρωθεί. Σας ευχαριστούμε πολύ!");
    }
}