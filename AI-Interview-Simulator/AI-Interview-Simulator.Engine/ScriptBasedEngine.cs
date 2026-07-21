using System.Collections.Generic;
using System.Threading.Tasks;

namespace AI_Interview_Simulator.Engine;

public class ScriptBasedEngine : IInterviewEngine
{
    private record InterviewStage(string Question, string StaticFeedback);

    private readonly List<InterviewStage> _stages;
    private int _currentStageIndex = 0;
    private bool _isShowingFeedback = false;

    public ScriptBasedEngine()
    {
        _stages = new List<InterviewStage>
        {
            new InterviewStage(
                "Καλώς ορίσατε στη συνέντευξη. Θα θέλατε να μας κάνετε μια μικρή αυτοπαρουσίαση;", 
                "Ευχαριστούμε για την αυτοπαρουσίαση. Είναι πάντα σημαντικό να ξεκινάμε με μια καθαρή εικόνα του υπόβαθρού σας."
            ),
            new InterviewStage(
                "Ποια είναι τα κίνητρά σας για τη συγκεκριμένη θέση και τι γνωρίζετε για την εταιρεία μας;", 
                "Πολύ ενδιαφέρον. Η κατανόηση των κινήτρων βοηθάει πάντα στη σωστή συνεργασία."
            ),
            new InterviewStage(
                "Πώς θα αξιολογούσατε τις ικανότητές σας σε σχέση με τις απαιτήσεις της θέσης;", 
                "Κατανοητό. Η ειλικρινής αυτοαξιολόγηση είναι βασικό προσόν για την εξέλιξή σας."
            ),
            new InterviewStage(
                "Πείτε μας για μια εμπειρία σας που αφορά τη συνεργασία και την ομαδικότητα.", 
                "Ευχαριστούμε. Η ομαδικότητα αποτελεί θεμέλιο λίθο για την κουλτούρα μας."
            ),
            new InterviewStage(
                "Πώς διαχειρίζεστε τις δύσκολες καταστάσεις ή τις πιέσεις στον εργασιακό χώρο;", 
                "Σημαντική τοποθέτηση. Η διαχείριση της πίεσης είναι κρίσιμη στην καθημερινότητά μας."
            ),
            new InterviewStage(
                "Περιγράψτε μας μια περίπτωση αποτυχίας και πώς προσαρμοστήκατε σε αυτήν.", 
                "Πολύ ώριμη προσέγγιση. Η προσαρμοστικότητα μέσα από τα λάθη μάς πάει μπροστά."
            ),
            new InterviewStage(
                "Έχετε κάποιες ερωτήσεις να κάνετε εσείς προς εμάς για τη θέση ή την εταιρεία;", 
                "Σας ευχαριστούμε πολύ για τις ερωτήσεις σας και για τον χρόνο που διαθέσατε για αυτή τη συνέντευξη."
            )
        };
    }

    public Task<string> GetQuestionForStageAsync(int stageNumber)
    {
        int index = stageNumber - 1;
        if (index >= 0 && index < _stages.Count)
        {
            return Task.FromResult(_stages[index].Question);
        }

        return Task.FromResult("Η συνέντευξη έχει ολοκληρωθεί. Σας ευχαριστούμε πολύ!");
    }

    public string GetCurrentPrompt()
    {
        if (IsInterviewFinished())
            return "Η συνέντευξη έχει ολοκληρωθεί.";

        return _isShowingFeedback 
            ? _stages[_currentStageIndex].StaticFeedback 
            : _stages[_currentStageIndex].Question;
    }

    public void ProcessUserResponse(string userResponse, double responseLatencySeconds)
    {
        if (IsInterviewFinished()) return;

        if (!_isShowingFeedback)
        {
            _isShowingFeedback = true;
        }
        else
        {
            _currentStageIndex++;
            _isShowingFeedback = false;
        }
    }

    public bool IsInterviewFinished()
    {
        return _currentStageIndex >= _stages.Count;
    }
}