using AIInterviewSimulator.Data.Entities;
using AIInterviewSimulator.Engine.Feedback;

internal static class ScriptFeedbackTests
{
    internal static async Task Run(Action<bool, string> check)
    {
        void Case(string name, string text, string states, string feedback)
        {
            var analysis = ScriptFeedbackEngine.AnalyzeProblemAnswer(text);
            var actual = string.Concat(analysis.Criteria.Select(c => c.State == CriterionState.Detected ? "D" : "U"));
            check(actual == states, $"script Q3/{name}: criteria {actual} == {states}");
            check(analysis.FeedbackId == feedback, $"script Q3/{name}: feedback {feedback}");
        }

        Case("complete", "Αντιμετώπισα ένα πρόβλημα στην εργασία. Έλεγξα τα δεδομένα και διόρθωσα το σφάλμα. Τελικά παραδώσαμε εγκαίρως.", "DDD", "q3.complete");
        Case("paraphrase", "Η εφαρμογή δεν άνοιγε. Αντικατέστησα το αρχείο. Η εφαρμογή λειτούργησε.", "DDD", "q3.complete");
        Case("missing result", "Συνάντησα μια δυσκολία. Ζήτησα βοήθεια από συμφοιτητή.", "DDU", "q3.result");
        Case("missing action", "Ο υπολογιστής χάλασε. Το αρχείο ανακτήθηκε.", "DUD", "q3.action");
        Case("missing problem", "Έλεγξα τα δεδομένα. Ολοκληρώσαμε το έργο.", "UDD", "q3.problem");
        Case("negative outcome", "Αντιμετωπίσαμε ένα πρόβλημα. Δοκίμασα άλλη λύση. Δεν καταφέραμε να ολοκληρώσουμε το έργο.", "DDD", "q3.complete");
        Case("unresolved outcome", "Αντιμετώπισα δυσκολία. Ερεύνησα την αιτία. Το πρόβλημα παρέμεινε.", "DDD", "q3.complete");
        Case("negated action", "Αντιμετώπισα πρόβλημα. Δεν διόρθωσα το σφάλμα.", "DUU", "q3.action");
        Case("late delivery is outcome", "Αντιμετώπισα πρόβλημα. Έλεγξα τα δεδομένα. Δεν παραδώσαμε εγκαίρως.", "DDD", "q3.complete");
        Case("negative completion", "Αντιμετώπισα πρόβλημα. Έλεγξα δεδομένα. Δεν ολοκληρώσαμε το έργο.", "DDD", "q3.complete");
        Case("not resolved", "Αντιμετώπισα πρόβλημα. Έλεγξα δεδομένα. Το πρόβλημα δεν λύθηκε.", "DDD", "q3.complete");
        Case("local negation", "Δεν ήξερα την αιτία. Αντιμετώπισα πρόβλημα και έλεγξα τα δεδομένα. Το πρόβλημα λύθηκε.", "DDD", "q3.complete");
        Case("bare keywords", "Πρόβλημα δράση αποτέλεσμα δυσκολία λύση συνεργασία", "UUU", "q3.fallback");
        Case("verb list", "Έλεγξα, διόρθωσα, οργάνωσα.", "UUU", "q3.fallback");
        Case("hypothetical", "Αν αντιμετώπισα πρόβλημα θα έλεγξα τα δεδομένα. Θα παραδώσαμε εγκαίρως.", "UUU", "q3.fallback");
        Case("advice", "Πρέπει να αντιμετώπισα πρόβλημα και έλεγξα τα δεδομένα.", "UUU", "q3.fallback");
        Case("quoted example", "Το παράδειγμα είναι «Αντιμετώπισα πρόβλημα. Έλεγξα δεδομένα. Παραδώσαμε εγκαίρως.»", "UUU", "q3.fallback");
        Case("irrelevant", "Μου αρέσουν οι βόλτες στη θάλασσα.", "UUU", "q3.fallback");
        Case("non answer", "Δεν έχω σχετική εμπειρία.", "UUU", "q3.example");
        Case("conflict", "Αντιμετώπισα πρόβλημα. Έλεγξα δεδομένα. Δεν έκανα τίποτα.", "DUU", "q3.fallback");
        Case("unknown result", "Αντιμετώπισα πρόβλημα. Έλεγξα δεδομένα. Δεν ξέρω το αποτέλεσμα.", "DDU", "q3.result");
        Case("question rather than report", "Αντιμετώπισα πρόβλημα; Έλεγξα δεδομένα; Παραδώσαμε εγκαίρως;", "UUU", "q3.fallback");
        Case("hypothetical non answer", "Αν δεν έχω σχετική εμπειρία θα μιλήσω για σπουδές.", "UUU", "q3.fallback");
        Case("negated problem", "Δεν αντιμετώπισα πρόβλημα. Έλεγξα δεδομένα.", "UDU", "q3.fallback");
        Case("substring", "Ξέλεγξα δεδομένα. Προπαραδώσαμε εγκαίρως.", "UUU", "q3.fallback");
        Case("empty", "", "UUU", "q3.fallback");
        Case("accentless capitals", "ΑΝΤΙΜΕΤΩΠΙΣΑ ΠΡΟΒΛΗΜΑ. ΕΛΕΓΞΑ ΔΕΔΟΜΕΝΑ. ΠΑΡΑΔΩΣΑΜΕ ΕΓΚΑΙΡΩΣ.", "DDD", "q3.complete");

        var negated = ScriptFeedbackEngine.AnalyzeProblemAnswer("Δεν διόρθωσα το σφάλμα.");
        check(negated.Criteria[1].Reason == EvidenceReason.NegatedEvidence, "script Q3: negation reason");
        var conflict = ScriptFeedbackEngine.AnalyzeProblemAnswer("Έλεγξα δεδομένα. Δεν έκανα τίποτα.");
        check(conflict.Criteria[1].Reason == EvidenceReason.ConflictingEvidence, "script Q3: conflict reason");
        var unknown = ScriptFeedbackEngine.AnalyzeProblemAnswer("Δεν ξέρω το αποτέλεσμα.");
        check(unknown.Criteria[2].Reason == EvidenceReason.ExplicitNonAnswer, "script Q3: explicit non-answer reason");
        check(GreekTextMatcher.Normalize("ΔΥΣΚΟΛΊΕΣ").Equals(GreekTextMatcher.Normalize("δυσκολιες")), "script Q3: accents and sigma");
        check(GreekTextMatcher.Normalize("έλεγξα".Normalize(System.Text.NormalizationForm.FormD)) == GreekTextMatcher.Normalize("έλεγξα"), "script Q3: decomposed Unicode");
        var engine = new ScriptFeedbackEngine();
        var answer = new UserAnswer { AnswerText = "Αντιμετώπισα πρόβλημα. Έλεγξα δεδομένα." };
        var expected = ScriptFeedbackEngine.AnalyzeProblemAnswer(answer.AnswerText);
        check(await engine.GenerateFeedbackAsync(3, answer) == expected.Text, "script Q3: public method uses answer");
        check(await engine.GenerateFeedbackAsync(3, answer) == await engine.GenerateFeedbackAsync(3, answer), "script Q3: deterministic output");
        check(ScriptFeedbackEngine.AnalyzeProblemAnswer(null).FeedbackId == "q3.fallback", "script Q3: null fallback");
        // Fixed cases test recognition and selection separately from real-world validity.
        void StageCase(int stage, string name, string text, string states, string feedback)
        {
            var analysis = ScriptFeedbackEngine.AnalyzeAnswer(stage, text);
            var actual = string.Concat(analysis.Criteria.Select(c => c.State == CriterionState.Detected ? "D" : "U"));
            check(actual == states, $"script Q{stage}/{name}: criteria {actual} == {states}");
            check(analysis.FeedbackId == $"q{stage}.{feedback}", $"script Q{stage}/{name}: feedback {analysis.FeedbackId}");
            check(ScriptFeedbackEngine.AnalyzeAnswer(stage, text).Text == analysis.Text, $"script Q{stage}/{name}: deterministic");
            check(ScriptFeedbackEngine.AnalyzeAnswer(stage, GreekTextMatcher.Normalize(text).ToUpperInvariant()).FeedbackId == analysis.FeedbackId,
                $"script Q{stage}/{name}: casing and accents invariant");
        }

        StageCase(1, "complete", "Σπουδάζω πληροφορική. Γνωρίζω Python. Με ενδιαφέρει η τεχνολογία.", "DDD", "complete");
        StageCase(1, "paraphrase", "Εργάστηκα ως βοηθός. Δημιούργησα μια ιστοσελίδα. Θέλω να εργαστώ στην εκπαίδευση.", "DDD", "complete");
        StageCase(1, "optional absent", "Σπούδασα οικονομικά. Χρησιμοποιώ Excel.", "DDU", "enrich");
        StageCase(1, "background only", "Είμαι φοιτήτρια πληροφορικής.", "DUU", "SkillsExperience");
        StageCase(1, "skills only", "Έχω εμπειρία στην εξυπηρέτηση.", "UDU", "Background");
        StageCase(1, "negated", "Δεν σπούδασα πληροφορική. Δεν γνωρίζω Python.", "UUU", "fallback");
        StageCase(1, "conflict", "Σπουδάζω πληροφορική. Γνωρίζω Python. Δεν διαθέτω σχετικές δεξιότητες.", "DUU", "fallback");
        StageCase(1, "explicit non-answer", "Δεν περιγράφω το υπόβαθρο. Γνωρίζω Python.", "UDU", "Background");
        StageCase(1, "optional denial", "Σπουδάζω πληροφορική. Γνωρίζω Python. Δεν έχω επαγγελματική κατεύθυνση.", "DDU", "enrich");

        StageCase(2, "complete", "Με ενδιαφέρει η συνεργασία. Οι δεξιότητές μου ταιριάζουν με τα καθήκοντα. Θέλω να αποκτήσω εμπειρία.", "DDD", "complete");
        StageCase(2, "paraphrase and future", "Η θέση με ενδιαφέρει επειδή συνδυάζει επικοινωνία και οργάνωση. Στη συγκεκριμένη θέση θα μπορώ να αξιοποιήσω την εμπειρία μου. Αναζητώ οικονομική ανεξαρτησία.", "DDD", "complete");
        StageCase(2, "missing fit", "Μου αρέσει η εξυπηρέτηση. Θέλω σταθερή εργασία.", "DUD", "PositionFit");
        StageCase(2, "missing interest", "Η εμπειρία μου ταιριάζει με τη θέση. Θέλω να εξελιχθώ επαγγελματικά.", "UDD", "Interest");
        StageCase(2, "missing motivation", "Με ελκύει η οργάνωση. Οι δεξιότητές μου ταιριάζουν με τη θέση.", "DDU", "Motivation");
        StageCase(2, "negated", "Δεν με ενδιαφέρει η συνεργασία. Οι δεξιότητές μου δεν ταιριάζουν με τη θέση. Δεν θέλω να αποκτήσω εμπειρία.", "UUU", "fallback");
        StageCase(2, "conflict", "Με ενδιαφέρει η συνεργασία. Δεν με ενδιαφέρει η θέση.", "UUU", "fallback");
        StageCase(2, "explicit non-answer", "Με ενδιαφέρει η συνεργασία. Δεν έχω στόχο.", "DUU", "PositionFit");

        StageCase(4, "complete", "Συνεργάστηκα με συμφοιτητές για μια παρουσίαση. Ανέλαβα την οργάνωση. Παραδώσαμε εγκαίρως.", "DDD", "complete");
        StageCase(4, "paraphrase", "Στο ομαδικό έργο είχαμε κοινό στόχο. Ήμουν υπεύθυνη για την παρουσίαση. Δεν ολοκληρώσαμε το έργο.", "DDD", "complete");
        StageCase(4, "optional absent", "Δούλεψα μαζί με συναδέλφους. Συντόνισα τις συναντήσεις.", "DDU", "enrich");
        StageCase(4, "missing contribution", "Συνεργάστηκα με την ομάδα. Ολοκληρώσαμε το έργο.", "DUD", "Contribution");
        StageCase(4, "missing context", "Ο ρόλος μου ήταν να συγκεντρώσω δεδομένα.", "UDU", "Collaboration");
        StageCase(4, "negated", "Δεν συνεργάστηκα με συμφοιτητές. Δεν ανέλαβα την οργάνωση.", "UUU", "fallback");
        StageCase(4, "conflict", "Συνεργάστηκα με συμφοιτητές. Ανέλαβα την οργάνωση. Δεν είχα προσωπική συμβολή.", "DUU", "fallback");
        StageCase(4, "explicit non-answer", "Δεν θυμάμαι κάποιο περιστατικό.", "UUU", "fallback");
        StageCase(4, "collective not personal", "Συνεργάστηκα με την ομάδα. Οργανώσαμε τις συναντήσεις.", "DUU", "Contribution");

        StageCase(5, "complete", "Είχα στενή προθεσμία. Οργάνωσα τον χρόνο. Παρέδωσα εγκαίρως.", "DDD", "complete");
        StageCase(5, "paraphrase", "Αντιμετώπισα πίεση χρόνου. Έθεσα προτεραιότητες. Δεν καταφέραμε να παραδώσουμε.", "DDD", "complete");
        StageCase(5, "optional absent", "Είχαμε πολλές υποχρεώσεις. Ζήτησα βοήθεια.", "DDU", "enrich");
        StageCase(5, "missing coping", "Είχα μεγάλο φόρτο. Δεν παραδώσαμε εγκαίρως.", "DUD", "Coping");
        StageCase(5, "missing pressure", "Έκανα διαλείμματα. Ολοκλήρωσα την εργασία.", "UDD", "Pressure");
        StageCase(5, "negated", "Δεν είχα στενή προθεσμία. Δεν έβαλα προτεραιότητες.", "UUU", "fallback");
        StageCase(5, "conflict", "Είχα πίεση χρόνου. Έβαλα προτεραιότητες. Δεν έκανα τίποτα.", "DUU", "fallback");
        StageCase(5, "explicit non-answer", "Δεν θυμάμαι πιεστική κατάσταση.", "UUU", "fallback");
        StageCase(5, "hypothetical strategy", "Είχα στενή προθεσμία. Θα έβαλα προτεραιότητες.", "DUU", "Coping");

        StageCase(6, "complete", "Άλλαξαν οι απαιτήσεις. Προσάρμοσα το σχέδιο. Έμαθα ότι χρειάζεται ευελιξία.", "DDD", "complete");
        StageCase(6, "paraphrase", "Το έργο δεν εξελίχθηκε όπως περίμενα. Αναζήτησα εναλλακτική λύση. Κατάλαβα τη σημασία της προετοιμασίας.", "DDD", "complete");
        StageCase(6, "missing learning despite result", "Άλλαξε η προθεσμία. Άλλαξα το πρόγραμμα. Παραδώσαμε εγκαίρως.", "DDU", "Learning");
        StageCase(6, "missing adaptation", "Το σχέδιο απέτυχε. Συνειδητοποίησα ότι χρειάζεται προετοιμασία.", "DUD", "Adaptation");
        StageCase(6, "missing situation", "Δοκίμασα άλλη λύση. Έμαθα τη σημασία της οργάνωσης.", "UDD", "UnexpectedSituation");
        StageCase(6, "negated", "Δεν άλλαξαν οι απαιτήσεις. Δεν άλλαξα το σχέδιο. Δεν έμαθα ότι χρειάζεται ευελιξία.", "UUU", "fallback");
        StageCase(6, "conflict", "Άλλαξαν οι απαιτήσεις. Προσάρμοσα το σχέδιο. Έμαθα ότι χρειάζεται ευελιξία. Δεν έμαθα τίποτα.", "DDU", "fallback");
        StageCase(6, "explicit non-answer", "Άλλαξαν οι απαιτήσεις. Δεν προσαρμόστηκα.", "DUU", "Adaptation");
        StageCase(6, "negative original outcome", "Δεν πετύχαμε τον στόχο. Αναθεώρησα την προσέγγιση. Κατάλαβα ότι χρειαζόμαστε χρόνο.", "DDD", "complete");

        StageCase(7, "complete", "Θέλω να γίνω αναλυτής. Η θέση θα με βοηθήσει να αποκτήσω εμπειρία. Θα παρακολουθήσω σεμινάρια για ανάλυση.", "DDD", "complete");
        StageCase(7, "paraphrase", "Στοχεύω να εργαστώ στον τομέα της εκπαίδευσης. Μέσα από τη θέση θα μάθω συνεργασία. Εξασκούμαι στην επικοινωνία.", "DDD", "complete");
        StageCase(7, "optional absent", "Θέλω να εξελιχθώ σε υπεύθυνη. Η συγκεκριμένη θέση μου επιτρέπει να αναπτύξω δεξιότητες.", "DDU", "enrich");
        StageCase(7, "missing position link", "Επιδιώκω να γίνω προγραμματιστής. Παρακολουθώ μαθήματα για προγραμματισμό.", "DUD", "PositionContribution");
        StageCase(7, "missing goal", "Αυτή η θέση με βοηθά να μάθω οργάνωση.", "UDU", "CareerGoal");
        StageCase(7, "negated", "Δεν θέλω να γίνω αναλυτής. Η θέση δεν θα με βοηθήσει να αποκτήσω εμπειρία. Δεν θα παρακολουθήσω σεμινάρια για ανάλυση.", "UUU", "fallback");
        StageCase(7, "conflict", "Θέλω να γίνω αναλυτής. Δεν έχω επαγγελματικό στόχο.", "UUU", "fallback");
        StageCase(7, "explicit non-answer", "Δεν ξέρω τι θέλω επαγγελματικά. Η θέση θα με βοηθήσει να αποκτήσω εμπειρία.", "UDU", "CareerGoal");

        StageCase(1, "same assertion contradicted", "Σπουδάζω πληροφορική. Δεν σπουδάζω πληροφορική. Γνωρίζω Python.", "UDU", "fallback");
        StageCase(2, "same assertion contradicted", "Με ενδιαφέρει η συνεργασία. Δεν με ενδιαφέρει η συνεργασία.", "UUU", "fallback");
        StageCase(4, "same assertion contradicted", "Συνεργάστηκα με την ομάδα. Ανέλαβα την οργάνωση. Δεν ανέλαβα την οργάνωση.", "DUU", "fallback");
        StageCase(5, "same assertion contradicted", "Είχα στενή προθεσμία. Δεν είχα στενή προθεσμία. Ζήτησα βοήθεια.", "UDU", "fallback");
        StageCase(6, "same assertion contradicted", "Άλλαξαν οι απαιτήσεις. Άλλαξα το σχέδιο. Δεν άλλαξα το σχέδιο.", "DUU", "fallback");
        StageCase(7, "same assertion contradicted", "Θέλω να γίνω αναλυτής. Δεν θέλω να γίνω αναλυτής.", "UUU", "fallback");
        StageCase(3, "different actions not contradictory", "Αντιμετώπισα πρόβλημα. Δεν διόρθωσα το σφάλμα αλλά ζήτησα βοήθεια. Το πρόβλημα λύθηκε.", "DDD", "complete");
        StageCase(1, "degree paraphrase", "Έχω πτυχίο στην πληροφορική. Έχω εμπειρία στην ανάλυση.", "DDU", "enrich");
        StageCase(4, "past perfect", "Είχα συνεργαστεί με συναδέλφους. Ετοίμασα την παρουσίαση.", "DDU", "enrich");
        StageCase(6, "retrospective lesson not advice", "Άλλαξαν οι προδιαγραφές. Άλλαξα το σχέδιο. Έμαθα ότι πρέπει να ζητώ διευκρινίσεις.", "DDD", "complete");
        StageCase(6, "learned practice", "Ακυρώθηκε η παρουσίαση. Δοκίμασα άλλη λύση. Έμαθα να οργανώνω καλύτερα τον χρόνο.", "DDD", "complete");
        StageCase(6, "generic advice not a lesson", "Άλλαξαν οι απαιτήσεις. Άλλαξα το σχέδιο. Πρέπει να οργανώνω τον χρόνο.", "DDU", "Learning");
        var separateActions = ScriptFeedbackEngine.AnalyzeAnswer(4, "Συνεργάστηκα με συμφοιτητές. Δεν ετοίμασα την παρουσίαση αλλά ανέλαβα την οργάνωση.");
        check(separateActions.Criteria[1].State == CriterionState.Detected, "script Q4: different negated role does not erase actual contribution");
        var negatedGoal = ScriptFeedbackEngine.AnalyzeAnswer(7, "Δεν θέλω να γίνω αναλυτής.");
        check(negatedGoal.Criteria[0].Reason == EvidenceReason.NegatedEvidence, "script Q7: negated intention reason");
        var contradictedInterest = ScriptFeedbackEngine.AnalyzeAnswer(2, "Με ενδιαφέρει η συνεργασία. Δεν με ενδιαφέρει η συνεργασία.");
        check(contradictedInterest.Criteria[0].Reason == EvidenceReason.ConflictingEvidence, "script Q2: contradictory expression reason");
        check(ScriptFeedbackEngine.AnalyzeAnswer(6, "Δεν προσαρμόστηκα.").Criteria[1].Reason == EvidenceReason.ExplicitNonAnswer,
            "script Q6: explicit non-adaptation reason");

        var examples = new Dictionary<int, string>
        {
            [1] = "Σπουδάζω πληροφορική. Γνωρίζω Python. Με ενδιαφέρει η τεχνολογία.",
            [2] = "Με ενδιαφέρει η συνεργασία. Οι δεξιότητές μου ταιριάζουν με τη θέση. Θέλω να αποκτήσω εμπειρία.",
            [4] = "Συνεργάστηκα με συμφοιτητές. Ανέλαβα την οργάνωση. Παραδώσαμε εγκαίρως.",
            [5] = "Είχα στενή προθεσμία. Έβαλα προτεραιότητες. Παραδώσαμε εγκαίρως.",
            [6] = "Άλλαξαν οι απαιτήσεις. Άλλαξα το σχέδιο. Έμαθα ότι χρειάζεται ευελιξία.",
            [7] = "Θέλω να γίνω αναλυτής. Η θέση θα με βοηθήσει να αποκτήσω εμπειρία. Θα παρακολουθήσω μαθήματα για ανάλυση."
        };
        foreach (var (stage, text) in examples)
        {
            StageCase(stage, "irrelevant", "Χθες πήγα μια βόλτα στη θάλασσα.", "UUU", "fallback");
            StageCase(stage, "keywords", "υπόβαθρο δεξιότητες κίνητρα θέση στόχος συνεργασία πίεση προσαρμογή μάθηση αποτέλεσμα", "UUU", "fallback");
            StageCase(stage, "empty", "", "UUU", "fallback");
            StageCase(stage, "quoted", $"«{text}»", "UUU", "fallback");
            StageCase(stage, "conditional", "Αν " + text.Replace(". ", " και "), "UUU", "fallback");
            var analysis = ScriptFeedbackEngine.AnalyzeAnswer(stage, text);
            check(await engine.GenerateFeedbackAsync(stage, new UserAnswer { AnswerText = text }) == analysis.Text,
                $"script Q{stage}: public method uses analysis");
            check(await engine.GenerateFeedbackAsync(stage, new UserAnswer { AnswerText = text }) != await engine.GenerateFeedbackAsync(stage, new UserAnswer()),
                $"script Q{stage}: response contingent");
            check(ScriptFeedbackEngine.AnalyzeAnswer(stage, null).FeedbackId == $"q{stage}.fallback", $"script Q{stage}: null fallback");
            check(analysis.Criteria.Count == 3 && analysis.Criteria.Select(c => c.CriterionId).Distinct().Count() == 3,
                $"script Q{stage}: three distinct criteria");
        }
        foreach (var stage in new[] { 1, 4, 5, 7 })
        {
            var definition = ScriptFeedbackDefinitions.Stages[stage];
            check(definition.Criteria.Count(c => c.Required) == 2 && !definition.Criteria[2].Required,
                $"script Q{stage}: enrichment is optional");
            var analysis = ScriptFeedbackEngine.AnalyzeAnswer(stage, examples[stage]);
            check(!analysis.Criteria[2].Required, $"script Q{stage}: diagnostics preserve optional flag");
            check(definition.Enrichment.Contains("Αν θέλετε") || definition.Enrichment.Contains("Προαιρετικά"),
                $"script Q{stage}: enrichment explicitly optional");
        }
        foreach (var stage in new[] { 2, 6 })
            check(ScriptFeedbackDefinitions.Stages[stage].Criteria.All(c => c.Required), $"script Q{stage}: all requested criteria required");
        check(await engine.GenerateFeedbackAsync(8, answer) == "Ευχαριστούμε για την απάντησή σας.", "script: unknown stage unchanged");
    }
}
