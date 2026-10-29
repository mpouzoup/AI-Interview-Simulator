> Ενημέρωση 2026-10-29: η πρόσβαση, η συναίνεση, η generic pre/post υποδομή και το .NET 10 υλοποιήθηκαν. Η τρέχουσα κατάσταση και τα εναπομένοντα gates περιγράφονται στο [SECURITY_IMPLEMENTATION.md](SECURITY_IMPLEMENTATION.md). Το παρακάτω κείμενο κρατά την προηγούμενη απογραφή 2026-10-23.

# Ετοιμότητα ασφαλούς deployment και live Gemini

2026-10-23. Έλεγχος κώδικα και δημοσίων πηγών, όχι έλεγχος λογαριασμών.
**Δεν έγινε Azure deployment, ανάγνωση πραγματικού secret store ή live κλήση.**

## Απογραφή

| Θέμα | Υπάρχει | Τι πρέπει να κλείσει πριν από δημόσιο pilot |
|---|---|---|
| Export | Έλεγχος Export:Key με ελάχιστο μήκος στην Production, constant-time σύγκριση, no-store | Διαχειριστική πρόσβαση και ασφαλής φύλαξη/rotation του key |
| Άλλα API endpoints | Έλεγχοι ροής, όχι authentication/ownership | Αποτροπή ανάγνωσης/μεταβολής ξένης συνεδρίας και χρεώσιμων αιτημάτων από τρίτους |
| CORS | AllowAnyOrigin/Method/Header στην κανονική API εφαρμογή | Περιορισμός σύμφωνα με hosting. CORS δεν είναι authentication |
| Frontend | Blazor InteractiveServer, API URL μέσω InterviewApi:BaseUrl | Προστατευμένη server-to-server διαδρομή, HTTPS/WebSockets/SignalR πίσω από proxy |
| Secrets | Δεν εντοπίστηκαν τιμές GeminiApiKey ή Export:Key στα ελεγχθέντα appsettings | Δεν ελέγχθηκαν user-secrets/environment/Key Vault, άρα η ενεργή ρύθμιση είναι άγνωστη |
| Gemini | Header key, κοινό timeout 30s, έλεγχος μη έγκυρης απόκρισης, χωριστά failures, χωρίς Β→Α fallback | Έλεγχος ενεργού paid project, model availability, quotas και πραγματικής συμπεριφοράς κατόπιν άδειας |
| Ανταγωνισμός | DB lease 60s, χωρίς SQLite write transaction κατά την παραγωγή | Load test στο πραγματικό staging· έλεγχος επανεκκίνησης και λήξης lease |
| Logs | Feedback outcome/model/attempts/duration χωρίς provider body/answer στο συγκεκριμένο log | Έλεγχος υπολοίπων logs, request tracing και exception telemetry πριν δημοσίευση |
| Βάση | SQLite/EF, nullable v2 δεδομένα, backup/migration ήδη ελεγμένα τοπικά | Μόνιμος δίσκος, δικαιώματα, restore test και χωριστή βάση pilot |

Οποιοσδήποτε γνωρίζει/μαντεύει ένα answer id δεν πρέπει να μπορεί να λάβει
κείμενο ή να προκαλέσει generation. Σήμερα δεν υπάρχει έλεγχος ownership στα
endpoints αυτά. Το UseAuthorization χωρίς εφαρμοσμένη πολιτική δεν λύνει το
πρόβλημα. Δεν αρκεί ότι το UI κρύβει κουμπιά ή ότι τα session ids είναι GUID.
[Microsoft: server-side enforcement](https://learn.microsoft.com/en-us/aspnet/core/blazor/security/?view=aspnetcore-8.0).

Προτεινόμενη ελάχιστη προστασία προς υλοποίηση: participant-scoped μηχανισμός
πρόσβασης/ιδιοκτησίας και προστασία των registrations/generation από κατάχρηση,
μαζί με ιδιωτικό API όπου είναι εφικτό. Δεν απαιτεί συλλογή ονόματος/email ή
αλλαγή της τυχαιοποίησης. Δεν υλοποιήθηκε ως «μικρή» αλλαγή αυτού του κύκλου,
επειδή αγγίζει ολόκληρη την πρόσβαση/resume και χρειάζεται στοχευμένους ελέγχους.

## Runtime — συγκεκριμένη χρονική εκκρεμότητα

Το project στοχεύει net8.0 και αναφέρει EF 8.0.12. Η υποστήριξη .NET 8 λήγει
στις **10 Νοεμβρίου 2026**. Για περίοδο έρευνας που περνά αυτή την ημερομηνία,
χρειάζεται μετάβαση σε υποστηριζόμενο runtime και επανέλεγχος πριν τη δημοσίευση.
Δεν έγινε αναβάθμιση SDK/packages εδώ. Το προηγούμενο NU1900 σημαίνει ότι δεν
έχει ολοκληρωθεί online vulnerability audit, όχι ότι γνωρίζουμε ήδη ευπάθεια.
[Επίσημη ανακοίνωση Microsoft](https://devblogs.microsoft.com/dotnet/dotnet-8-9-end-of-support/).

## Azure — ελάχιστο μονοπάτι που διατηρεί το SQLite

Πρόταση staging: μία μόνο API instance, SQLite σε μόνιμο τοπικό δίσκο και
περιορισμένη πρόσβαση. Μια μικρή Azure VM με reverse proxy και ελεγχόμενα backups
είναι υποψήφια επιλογή που κρατά τις SQLite-specific συναλλαγές, αλλά απαιτεί
διαχείριση OS/TLS. Δεν έχει επιλεγεί/κοστολογηθεί υπηρεσία ή περιοχή στον λογαριασμό.
Δεν αποθηκεύουμε τη βάση στο wwwroot, σε εφήμερο δίσκο ή μέσα στο deployed package.

Δεν τεκμαίρεται ότι ένα Azure Files mount είναι ασφαλές για τις SQLite locks/WAL.
Η SQLite προειδοποιεί για network filesystems. Μόνιμη αποθήκευση δεν αρκεί από
μόνη της: χρειάζονται κατάλληλα locking/durability semantics.
[SQLite](https://www.sqlite.org/useovernet.html).
App Service παραμένει επιλογή μόνο μετά επιβεβαίωση κατάλληλου storage· εναλλακτική
βάση θα χρειαζόταν προσαρμογή των SQLite-specific συναλλαγών, όχι απλή αλλαγή URL.

Πριν publish: τρέχον backup, migration σε staging αντίγραφο, restore, HTTPS,
σωστό reverse-proxy forwarding, όρια αιτημάτων/rate limiting, χωρίς δημόσιο Swagger
στην Production, περιορισμένο API και export, χωρίς δημόσια test routes ή test host.
Τα test routes βρίσκονται στο project Tests και δεν πρέπει να φιλοξενηθούν ως εφαρμογή.

## Secrets χωρίς τιμές ή αρχεία

Αναγκαία ονόματα ρυθμίσεων, **όχι πραγματικές τιμές**:

| Ρύθμιση | Θέση |
|---|---|
| GeminiApiKey | Μόνο API server, ασφαλής secret provider |
| Export__Key | Μόνο API/admin πρόσβαση, ανεξάρτητο από το Gemini key |
| ConnectionStrings__DefaultConnection | Απόλυτη διαδρομή μόνιμης βάσης στο επιλεγμένο hosting |
| InterviewApi__BaseUrl | Frontend server προς το προστατευμένο API |

Για App Service προτιμώνται Key Vault references με managed identity και
περιορισμένα δικαιώματα. Η ασφαλής διανομή Azure credentials δεν σημαίνει ότι
το Gemini key εξαφανίζεται: αυτό παραμένει εξωτερικό secret του API server.
[Key Vault references](https://learn.microsoft.com/en-us/azure/app-service/app-service-key-vault-references),
[App Service settings](https://learn.microsoft.com/en-us/azure/app-service/configure-common).
Το User Secrets προορίζεται για development, όχι ασφαλές production vault.
[Microsoft](https://learn.microsoft.com/en-us/aspnet/core/security/app-secrets?view=aspnetcore-8.0).
Μην επικολλάτε κλειδιά σε repository, shell history, συνομιλίες ή logs.

## Gemini — πριν από άδεια live smoke test

- Ο τρέχων κώδικας δοκιμάζει gemini-3.6-flash, gemini-3.5-flash,
  gemini-3.7-flash και gemini-flash-latest. Δεν επιβεβαιώθηκε διαθεσιμότητα
  όλων στον λογαριασμό· δημόσια τεκμηρίωση δεν αποδεικνύει project entitlement.
- Το latest alias μπορεί να αλλάξει μοντέλο. Χρειάζεται τελική καταγραφή/πάγωμα
  ρυθμίσεων και παρακολούθηση του μοντέλου που όντως χρησιμοποιήθηκε. Δεν αλλάχθηκε
  η εγκεκριμένη λογική fallback μεταξύ Gemini μοντέλων ή το prompt.
  [Ονομασίες εκδόσεων](https://ai.google.dev/gemini-api/docs/models).
- Paid project δεν σημαίνει απεριόριστα αιτήματα. Ελέγχονται τα ενεργά project
  limits και ο ταυτόχρονος φόρτος. [Rate limits](https://ai.google.dev/gemini-api/docs/rate-limits).
- Επτά έως δεκατέσσερις επιτυχείς παραγωγές ανά συμμετέχοντα Β, συν αποτυχημένες
  προσπάθειες/επαναλήψεις. Δεν δίδεται μη επαληθευμένη εκτίμηση τελικού κόστους.
- Οι paid όροι περιγράφουν διαφορετική χρήση δεδομένων από unpaid υπηρεσίες.
  Επιβεβαίωση active billing και ισχυόντων όρων/DPA από τους αρμοδίους· όχι
  υπόσχεση μηδενικής διατήρησης ή συγκεκριμένης γεωγραφικής θέσης επεξεργασίας.
  [Όροι Google](https://ai.google.dev/gemini-api/terms).
- Μετά από άδεια: λίγα συνθετικά ελληνικά κείμενα χωρίς πραγματικά προσωπικά
  στοιχεία, έλεγχος δύο γύρων, μοντέλου, μη κενών/πλήρων αποκρίσεων, latency,
  αποθήκευσης, κόστους και failure/retry. Σήμερα αυτό δεν εκτελέστηκε.

**Συμπέρασμα:** δεν είναι ακόμη έτοιμη για δημόσια συλλογή πραγματικών δεδομένων.
Ο προηγούμενος τεχνικός έλεγχος του πυρήνα δεν είναι έλεγχος ασφαλούς production.
