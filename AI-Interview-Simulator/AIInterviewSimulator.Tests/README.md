# Isolated interview regression checks

Run from the repository directory:

```sh
dotnet run --project AIInterviewSimulator.Tests/AIInterviewSimulator.Tests.csproj
```

This dependency-free console test runner exits nonzero on a failed assertion.
It applies the existing migrations only to a newly generated temporary SQLite
database, then deletes that database. It starts isolated loopback HTTP hosts for security tests, never loads production
configuration/secrets, and never contacts Gemini. Feedback tests construct the service with a stub HttpMessageHandler and a dummy key.

Backend checks exercise the real controller with independent SQLite contexts,
including concurrent submissions, idempotent retries, invalid stages, completed
sessions, and pre-existing duplicate test rows. Frontend checks render the real
Home component with HtmlRenderer and stub HTTP/JavaScript services, then exercise
submission and feedback-only recovery from HTTP, connection, timeout, empty,
null, and malformed-response failures. Reflection is limited to accessing the
component's private event handlers/state; rendered controls are also checked.

The tests intentionally are run separately from the application solution. They
do not test browser reconnection, live Gemini behavior, or production data.

Decision/revision checks exercise both conditions at stages 1 and 7, using the
real controller behind a stub HTTP handler that can discard the response AFTER
committing the action. They check successful continuation, immutable retry
payloads, unchanged persisted timestamps/latencies and completion time, rejection
of different or disallowed actions, and concurrent identical/conflicting actions.
SQLite DateTime comparisons use ticks because reloading can drop DateTime.Kind.

Feedback generation checks cover success, missing key, HTTP/transport failure,
expiration of the shared 30-second budget, JSON null, null/empty/whitespace text,
malformed/missing candidates, truncated output and legacy failure messages.
They exercise the real service/manager/controller chain at stages 1 and 7,
verify structured outcome logs, unchanged original timestamps and answer count,
successful recovery, cached success, and rejection of a retry for the wrong answer.
The controlled TimeProvider expires the actual budget instantly; no production
clock, real network call, or existing database is used.

Production policy: the existing model order and maximum attempts are retained,
with a shared 30-second deadline including the retry delay. Invalid successful
HTTP responses stop the generation attempt; they do not become feedback. A new
participant retry starts a new bounded generation for the same answer. Condition
A is never used as a fallback. Success/failure is logged with session/answer IDs,
condition, status, error code, model, attempt count and duration; answer text and
provider response bodies are omitted. The legacy flow keeps these logs outside its dataset fields. Flow v2 adds the separate generation summaries described below.

Not implemented: a persistent FeedbackAttempts table (AnswerId, attempted time,
status, error code, model, request count and duration), requiring dataset approval
and a migration. The v2 migration below adds nullable summaries to UserAnswer rather than a separate attempts table.


## Revision feedback flow (v2)

New API-created sessions have `FlowVersion = 2`; migrated sessions retain version
1 and their original behavior/data. The additive `AddRevisionFeedbackFlow`
migration does not backfill feedback or timestamps. Apply it to the application
database through the normal deployment migration procedure; the test runner
only migrates isolated temporary databases.

V2 stores the original answer/feedback and revised answer/feedback separately.
The second generation receives only the revised text in an untracked input
object. The question, ScriptFeedbackEngine rules and Gemini prompt are unchanged.
There is no comparison instruction and no third answer. Identical deterministic
feedback across the two rounds is allowed.

The API enforces saved answer -> first feedback -> display acknowledgement ->
decision -> optional single revision -> second feedback -> display acknowledgement
-> continue. Choosing not to revise completes the stage directly. The final
stage completes the session only on the corresponding continuation action.
A GET of next-question is read-only in v2 and returns the current flow state.
Feedback is explicitly requested with POST answers/{id}/feedback/{1|2}.
Repeated successful generations return the persisted text. Display acknowledgements
and action replays preserve the first stored observations.

Generation uses a database-backed 60-second lease, separate from the existing
30-second provider budget. No SQLite write transaction is held over a provider
call. Another request receives InProgress, and a crashed process's lease may be
reclaimed after expiry. A superseded lease cannot overwrite the winning result.
A crash before finalization can leave an unknown-duration attempt; generation
summaries are not a complete persistent attempt ledger. Condition B never falls
back to A.

### Timing interpretation

* Existing timestamp fields and legacy latency formulas remain unchanged.
  `FeedbackShownAt` retains its legacy server-ready meaning for v1; v2 does not
  populate it as a fabricated display time.
* V2 `FeedbackReadyAt` / `RevisedFeedbackReadyAt` are API-server observations.
  Generation milliseconds accumulate elapsed manager-call time, including
  provider retries/delays, across finalized generation requests. GenerationAttempts
  counts claimed generation requests, not individual HTTP calls to model providers.
* Browser display timestamps are approximate post-render observations. The server
  stores receipt acknowledgements separately. Neither proves reading/attention.
* OriginalWritingMilliseconds is elapsed time from browser question display to
  submission; RevisionWritingMilliseconds starts at revision-editor display.
  They include participant idle time, not just keystrokes. They exclude subsequent
  feedback production. DecisionMilliseconds and RevisedFeedbackReadingMilliseconds
  are elapsed proxies from feedback display to the corresponding action.
* FeedbackWaitMilliseconds covers the browser's submission-to-feedback-display
  interval, including answer persistence, transport, provider calls, retries and
  participant delay before retry. It is not pure Gemini execution time.
* StageElapsedMilliseconds includes waits and idle time. Browser durations use
  performance.now rather than differences between browser/server wall clocks.
  They are unavailable if timing initialization fails or browser state is lost;
  missing observations remain null. JS maps are in-memory, not durable recovery.
* CSV appends v2 columns without removing/reordering the original columns.
  Do not pool v1 legacy latency formulas with v2 browser intervals as if they had
  identical definitions.

V2 checks exercise both conditions across seven stages, provider failure and
feedback-only recovery, true concurrent generation/actions, expired leases,
post-commit response loss, display acknowledgement retries, read-only state
retrieval, preservation of historical rows and the EF snapshot. UI checks render
the real Home component with stub HTTP/JS; they do not simulate a real browser
paint or a live SignalR reconnection.

Run the browser timing module checks separately:

```sh
node AIInterviewSimulator.Tests/browser-timing.test.mjs
```

These use the actual JS module with a controlled clock and verify phase separation
and immutable retry observations. They do not replace a browser pilot or a live
Gemini/deployment check.


## Real browser end-to-end checks (isolated local services)

The test runner includes an explicit `--browser-host` mode. It starts a real API
on 127.0.0.1:18555, with a new temporary SQLite database and an in-process HTTP
provider stub. It never loads production API settings/secrets. Its `/test/*`
routes exist only in this test host, not in the production API.

1. Build this test project, then run `dotnet run --project AIInterviewSimulator.Tests --no-build -- --browser-host`.
2. Run the actual Blazor client with `InterviewApi__BaseUrl=http://127.0.0.1:18555/`
   and a local URL such as http://127.0.0.1:18665. The normal default API URL stays
   http://localhost:5055/ when this configuration is absent.
3. Start an isolated Chrome profile with remote debugging on 127.0.0.1:19223.
4. Run `node AIInterviewSimulator.Tests/browser-e2e.mjs` (requires Node with
   built-in fetch/WebSocket). `INTERVIEW_E2E_UI` can override the frontend URL and
   `INTERVIEW_E2E_ARTIFACT_DIR` selects the screenshot/JSON/CSV output directory.

The driver uses real browser controls, runs A/B with and without revisions over
all seven questions, verifies persisted answers/feedback/timings, injects a B
failure followed by retry, and checks refresh recovery mid-stage and after final
completion. It clears cookies and tab sessionStorage before each new synthetic
participant and accepts the synthetic consent checkbox. This is a functional browser check, not a live-Gemini quality test.

Access recovery now uses a protected HttpOnly SameSite cookie and an authoritative
participant profile. IDs or sessionStorage never grant access. The underlying API
token stays in the protected cookie/server circuit, never in JavaScript storage or
CSV. Fixed expiry is 24 hours; explicit logout revokes API access. Closing the browser
or deleting its cookie can lose access; there is no email/login recovery. Unsent
drafts and lost monotonic timing origins remain unavailable after reload.

The full runner also checks anonymous/foreign/expired/revoked access, anti-CSRF
registration/logout, disabled research gate, versioned consent, pre/post snapshots,
raw numeric validation, idempotency, ownership and administrative exports. All
questionnaire items used by tests are synthetic fixtures.

A maintenance-only read-only export can be generated with:

```sh
dotnet AIInterviewSimulator.Tests/bin/Debug/net10.0/AIInterviewSimulator.Tests.dll --export-readonly /absolute/path/database.db /private/path/export.csv
```

This calls the actual CSV controller through a SQLite read-only connection and a
synthetic local export key; it never invokes feedback generation. Keep exports
private: real database rows can contain participant data.
