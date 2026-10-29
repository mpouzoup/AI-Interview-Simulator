# Αναπαραγωγή της επιθεώρησης UI

Χρησιμοποιήθηκαν τα ήδη διαθέσιμα net10 test/client binaries. Ο BrowserTestHost δημιουργεί νέα προσωρινή SQLite, καθαρίζει production configuration sources και αντικαθιστά τον provider με in-process HTTP stub. Τα /test endpoints υπάρχουν μόνο στο test host. Η τεχνητή ενίσχυση counts στο /test/prepare αφορά αποκλειστικά τη fixture βάση για ελεγχόμενη επιλογή condition· ο production αλγόριθμος δεν αλλάζει.

1. Εκκίνηση test runner με --browser-host στη loopback 18555.
2. Εκκίνηση πραγματικού Client με Development, InterviewApi__BaseUrl=http://127.0.0.1:18555/, URL loopback 18665 και απόλυτο contentRoot στον φάκελο Client.
3. Ξεχωριστό headless Chrome profile, loopback DevTools 19223. Όχι το προσωπικό browser profile.
4. Από τη ρίζα repository: node docs/hax/tools/capture-evidence.mjs.
5. Τα screenshots καταγράφονται από Page.captureScreenshot χωρίς αντικατάσταση DOM ή διακόσμηση εικόνων. Viewport 1100×1050. Καταγράφονται επίσης DOM text, UTC χρόνοι και SHA-256. Στο Word χρησιμοποιούνται μόνο γεωμετρικές περικοπές πρωτοτύπων όπου αναφέρεται.
6. Σταμάτημα των test services. Μη φιλοξενείτε το test host δημοσίως.

Στον παρόντα κύκλο το Α ολοκληρώθηκε σε επτά stages, έγινε refresh μετά από δεύτερο feedback στο στάδιο 3 και δοκιμάστηκε άσχετο κείμενο σε χωριστή συνθετική συνεδρία. Στο Β ελέγχθηκε το πρώτο στάδιο με αναθεώρηση, ελεγχόμενη αποτυχία provider και feedback-only retry. Δεν ισχυριζόμαστε ότι επανεκτελέστηκε ολόκληρη η συνέντευξη Β σε αυτόν τον κύκλο τεκμηρίωσης.

Δεν φορτώθηκε πραγματικό key, δεν έγινε εξωτερική κλήση Gemini, δεν χρησιμοποιήθηκαν πραγματικοί συμμετέχοντες και δεν μεταβλήθηκε η πραγματική βάση. Το κείμενο BROWSER_REVISED είναι marker της υπάρχουσας fixture, όχι οδηγία προς πραγματικό συμμετέχοντα.

Τα source-baseline.json και source-integrity.json ελέγχουν αμετάβλητα application sources. Η ύπαρξη docs από προηγούμενους κύκλους δεν χρησιμοποιήθηκε ως απόδειξη τρέχουσας UI λειτουργίας.
