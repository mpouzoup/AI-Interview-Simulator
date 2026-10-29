# Fast-track verification — 2026-10-23

## Real database

- Database: AIInterviewSimulator.Api/interview_simulator.db.
- Pre-migration SQLite backup: backups/sqlite/pre_revision_flow_20261022T162913Z.db (private, git-ignored).
- Before: 24 users, 24 sessions, 75 answers, four applied migrations.
- Rehearsed AddRevisionFeedbackFlow on a backup copy, compared every pre-existing
  column value by ordered row hashes, then applied it to the real database.
- After: five migrations; the same historical rows/values; FlowVersion=1 on historical
  sessions; no invented revised feedback/browser times. Integrity and foreign keys pass.
- Real database was not used for synthetic participant interviews.

## Executed checks

- Solution and test-project builds succeed (zero errors). NU1900 warns that NuGet's
  online vulnerability feed was unavailable in the restricted environment; a clean
  dependency-security audit is not claimed.
- Full .NET runner: 2,253 assertions pass, including API/controller/Blazor component,
  retries after commit, concurrent actions/generation, lease expiry, migration/snapshot
  preservation and stage-seven completion.
- browser-timing.test.mjs passes, including session-storage validation and unavailable storage.
- Real isolated Chrome at 1280x1000 against actual Blazor and real test API/controllers:
  A/no-revision, A/revision, B/no-revision, B/revision all pass seven stages.
- The four final scenarios also refresh at stage three and after completion: the
  same session continues, without duplicate answers. Lost browser origins remain null.
- B uses an in-process provider HTTP stub only. A deliberately failed second feedback
  leaves the revision stored, blocks completion and recovers through feedback-only retry.
- No real Gemini API key or live Gemini call was used.
- CSV: 57 unique columns; seven unique answer rows per completed browser scenario;
  condition/stage/session/text/feedback/timings match persisted rows. Historical export
  uses the real controller with SQLite ReadOnly: all 75 rows match database text and
  identifiers, with empty v2-only historical values. Formula escaping is preserved.
- Final ordered hashes confirm all historical database values remain unchanged.
- Protected questions, ScriptFeedbackEngine definitions/matcher and Gemini mechanism
  match their pre-cycle hashes.

## Fixes and test infrastructure

- Made the client API base URL configurable, retaining the existing localhost default.
- Added same-tab v2 session recovery from server state after refresh. It recovers saved
  work, not unsent drafts, and never fabricates browser intervals after losing clock origins.
- Clarified the existing instructions: one optional revision, a second feedback, then continue.
- Added a test-only API host, real Chrome CDP driver and read-only export utility.
- No questionnaire, research variable, fixed interview question or feedback rule changed.

## Limits and readiness

The interview core is ready for a controlled technical pilot. A complete research
pilot still needs consent/demographics/questionnaire integration and the agreed study
materials. Live Gemini availability/quality/cost, deployed Azure behavior, mobile and
other browsers, and real participant comprehension have not been established here.
Browser intervals measure elapsed observation-to-action time, not attention or active
typing. Same-tab recovery depends on sessionStorage; tab closure/restrictive storage and
unsent drafts remain limitations. There is no claim of empirical recognition validity.
The main study was not started.
