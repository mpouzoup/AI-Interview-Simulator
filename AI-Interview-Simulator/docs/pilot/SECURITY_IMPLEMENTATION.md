# Τεχνική προετοιμασία — 2026-10-29

## Πρόσβαση και recovery

Κάθε νέα εγγραφή αποκτά τυχαίο credential 256 bit. Η βάση κρατά μόνο SHA-256,
ιδιοκτήτη, χρόνο έκδοσης, σταθερή λήξη 24 ωρών και ανάκληση. Κάθε API αίτημα
ελέγχει ξανά λήξη/ανάκληση και ο global MVC filter ελέγχει ιδιοκτησία συνεδρίας,
απάντησης ή questionnaire administration. Άγνωστη διαδρομή απορρίπτεται.
Τα admin CSV έχουν χωριστό Export key, όχι participant privileges.

Το Blazor gateway παραλαμβάνει το credential server-side και το τοποθετεί σε
κρυπτογραφικά προστατευμένο HttpOnly/SameSite=Strict cookie. Στην Production
χρησιμοποιείται Secure cookie __Host-Interview. Η JS δεν παραλαμβάνει το token.
Η εγγραφή/λήξη προστατεύονται με antiforgery. Τα API responses είναι no-store.
Το refresh ζητά authoritative profile και τρέχουσα κατάσταση. Το session ID ή
παλιό sessionStorage δεν δίνουν δικαίωμα. Δεν επαναφέρονται μη υποβληθέντα drafts
ή χαμένες browser timing origins. Η ρητή λήξη ανακαλεί την πρόσβαση, δεν διαγράφει
δεδομένα. Παλιά δεδομένα δεν απέκτησαν credentials ή συναίνεση αναδρομικά.

Δεν υπάρχει ανάκτηση σε άλλη συσκευή ή μετά από απώλεια/λήξη cookie. Αυτό είναι
σκόπιμο για την τρέχουσα συμμετοχή μίας συνεδρίας χωρίς email/password. Αν απαιτηθεί
διαφορετική πολιτική, χρειάζεται χωριστή σχεδίαση, όχι πρόσβαση μέσω nickname/ID.

## Συναίνεση και surveys

Η συλλογή είναι κλειστή από προεπιλογή. Για άνοιγμα απαιτούνται ταυτόχρονα
Study:CollectionEnabled=true, Study:Consent:Approved=true και εγκεκριμένα μη κενά
Version/Text. Κανένα προσχέδιο δεν φορτώθηκε ως εγκεκριμένο. Ο server απαιτεί ρητή
αποδοχή της τρέχουσας έκδοσης και hash πριν δημιουργήσει χρήστη/credential/session.
Αποθηκεύονται έκδοση, hash ακριβούς κειμένου και UTC αποδοχή. Αρχειοθετήστε το τελικό
κείμενο ανά έκδοση πριν την ενεργοποίηση· το hash μόνο του δεν ανασυνθέτει κείμενο.

Προστέθηκαν versioned definitions, immutable per-session snapshots, administrations
και raw αριθμητικές responses, χωριστά ανά χρήστη/condition/phase pre/post.
Δεν υπάρχουν πραγματικά MASI/SUS/NASA-TLX items, scoring ή seed. Το flag
Study:QuestionnairesEnabled παραμένει false. Μόνο approved definitions ανατίθενται
κατά την έναρξη· απορρίπτονται πολλαπλές approved εκδόσεις του ίδιου εργαλείου/phase.
Υπάρχει generic φόρμα, server validation, idempotent υποβολή και χωριστό raw CSV.
Τα required surveys ελέγχουν τη μετάβαση pre→συνέντευξη→post. Προαιρετικές φόρμες
μόνες τους δεν προκαλούν υποχρεωτικό βήμα. Η τελική διάταξη/anchors πρέπει να ελεγχθεί
για κάθε εγκεκριμένο εργαλείο· η generic αριθμητική φόρμα δεν θεωρείται επικυρωμένη
υλοποίηση συγκεκριμένης κλίμακας. Το StudyCompletedAt είναι χωριστό από FinishedAt.

## Runtime και migration

Όλα τα projects στοχεύουν net10.0, EF Core 10.0.14. MudBlazor 9.6.0 υποστηρίζει
net10 και διατηρήθηκε. Οι υπόλοιπες εξαρτήσεις διατηρήθηκαν και μεταγλωττίστηκαν.
Δεν άλλαξαν οι ερωτήσεις, τα patterns A, το Gemini prompt ή η τυχαιοποίηση.
Πηγές συμβατότητας: [Microsoft .NET policy](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core),
[EF package](https://www.nuget.org/packages/Microsoft.EntityFrameworkCore/10.0.14),
[MudBlazor](https://www.nuget.org/packages/MudBlazor/9.6.0).

Πριν τη migration έγινε SQLite backup με backup API στο ignored backups/sqlite,
με περιορισμένα file permissions. Η νέα migration δοκιμάστηκε σε αντίγραφο πριν
εφαρμοστεί στην πραγματική βάση. Προσθέτει nullable StudyCompletedAt και νέους
πίνακες, χωρίς διαγραφή/μεταγραφή ιστορικών δεδομένων. Επαληθεύτηκαν hashes όλων
των παλιών στηλών (25 Users,25 Sessions,77 Answers), integrity_check και foreign keys.
Κανένα πραγματικό consent/access/questionnaire row δεν δημιουργήθηκε.
Το design-time factory δεν φορτώνει secrets και χρησιμοποιεί προσωρινή βάση εκτός
αν δοθεί ρητά --connection. EF CLI 10.0.9 προειδοποίησε ότι είναι παλιότερο από
runtime 10.0.14· η migration ολοκληρώθηκε και επαληθεύτηκε. Ευθυγραμμίστε CLI στο hosting.

## Έλεγχοι και όρια

Build: 0 errors/0 warnings. Πλήρες runner: 2.373 assertions. JS timing tests πέρασαν.
Πραγματικός Chrome: τέσσερα σενάρια A/B × με/χωρίς αναθεώρηση, επτά stages,
refresh στη μέση/τέλος, B failure/retry, persisted timings και CSV. Το B είχε μόνο
τοπικό stub. Τα HTTP security tests καλύπτουν foreign/anonymous/expired/revoked
access, cookie/CSRF, έγκριση συναίνεσης, pre/post και exports. Τα survey tests είχαν
μόνο συνθετικά items· δεν πραγματοποιήθηκε browser pilot με πραγματικά εργαλεία.

Πριν το pilot: εγκεκριμένα κείμενα/εργαλεία και φάσεις, ξεχωριστή βάση pilot,
HTTPS και trusted proxy configuration, μόνιμο προστατευμένο Data Protection key
store (Security:DataProtectionKeysPath, απαραίτητο στην Production), private API,
διαχειριστικό export secret, όρια εγγραφής/φόρτου στο hosting, ασφαλή backups και
δοκιμή restart/restore. Αποκλείστε cookie/Authorization headers από proxy telemetry.
Οι production Data Protection keys χρειάζονται δικαιώματα/προστασία στο storage·
η ρύθμιση διαδρομής δεν ισοδυναμεί με πλήρη διαχείριση κλειδιών Azure.
Απαιτείται χωριστή άδεια για deployment και μικρό πραγματικό Gemini smoke test.
Δεν έγινε δημόσιο deployment, χρεώσιμη κλήση ή συλλογή συμμετεχόντων.

Ήδη εκκινημένα παλιά development processes δεν αναβαθμίζονται από το build.
Απαιτείται επανεκκίνηση API/Client από τα net10 binaries πριν από οποιαδήποτε
χρήση· ο τρέχων κύκλος δεν επανεκκίνησε τις παλιές διεργασίες του IDE.
