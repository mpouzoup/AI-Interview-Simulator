using System.Diagnostics;
using AIInterviewSimulator.Api.Security;
using AIInterviewSimulator.Engine.Feedback;
using Microsoft.Extensions.Logging.Abstractions;
using System.Security.Cryptography;
using System.Text;
using AIInterviewSimulator.Api.DTOs.Requests;
using AIInterviewSimulator.Data.Context;
using AIInterviewSimulator.Data.Entities;
using AIInterviewSimulator.Engine.Managers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace AIInterviewSimulator.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public partial class InterviewController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly InterviewManager _interviewManager;
    private readonly FeedbackManager _feedbackManager;
    private readonly string _exportKey;
    private readonly ILogger<InterviewController> _logger;
    private readonly StudyPolicy _study;

    public InterviewController(
        ApplicationDbContext context,
        InterviewManager interviewManager,
        FeedbackManager feedbackManager,
        IConfiguration configuration,
        ILogger<InterviewController>? logger = null)
    {
        _logger = logger ?? NullLogger<InterviewController>.Instance;
        _study = new StudyPolicy(configuration);
        _context = context;
        _interviewManager = interviewManager;
        _feedbackManager = feedbackManager;
        _exportKey = configuration["Export:Key"] ?? string.Empty;
    }

    [HttpPost("users")]
    public async Task<IActionResult> CreateUser([FromBody] CreateUserRequest request)
    {
        // Mirror the existing UI eligibility validation; direct API calls must not bypass it.
        if (request.Age is < 18 or > 99 || string.IsNullOrWhiteSpace(request.Nickname))
            return BadRequest("A non-empty nickname and an age from 18 to 99 are required.");

        if (!_study.Enabled) return StatusCode(503, "Research collection has not been enabled with approved consent.");
        if (!request.ConsentAccepted || request.ConsentVersion != _study.Version || request.ConsentTextHash != _study.TextHash)
            return BadRequest("Accept the current approved consent before registration.");
        return await ExecuteAnswerAction(async () =>
        {
            var user = new User { Nickname = request.Nickname.Trim(), Age = request.Age, Gender = request.Gender,
                AiFamiliarityLevel = request.AiFamiliarityLevel, CreatedAt = DateTime.UtcNow };
            _context.Users.Add(user);
            await _context.SaveChangesAsync();
            var issued = StudyPolicy.NewCredential();
            var expires = DateTime.UtcNow.AddHours(24);
            _context.ConsentRecords.Add(new ConsentRecord { UserId = user.Id, Version = _study.Version, TextHash = _study.TextHash, AcceptedAt = DateTime.UtcNow });
            _context.ParticipantCredentials.Add(new ParticipantCredential { UserId = user.Id, TokenHash = issued.Hash, IssuedAt = DateTime.UtcNow, ExpiresAt = expires });
            await _context.SaveChangesAsync();
            if (ControllerContext.HttpContext != null) Response.Headers.CacheControl = "no-store";
            // Returned only to the frontend's server-side enrollment gateway, never to browser JS.
            return Ok(new { user.Id, user.Nickname, user.Age, user.Gender, user.AiFamiliarityLevel, user.CreatedAt, AccessToken = issued.Token, ExpiresAt = expires });
        });
    }

    [HttpPost("sessions")]
    public async Task<IActionResult> StartSession(
        [FromBody] StartSessionRequest request)
    {
        await _context.Database.OpenConnectionAsync();

        try
        {
            var connection =
                (SqliteConnection)_context.Database.GetDbConnection();

            await using var sqliteTransaction =
                connection.BeginTransaction(deferred: false);

            await using var efTransaction =
                await _context.Database.UseTransactionAsync(sqliteTransaction);

            var userExists = await _context.Users
                .AnyAsync(user => user.Id == request.UserId);

            if (!userExists)
            {
                return NotFound("User not found.");
            }

            var existingSession = await _context.InterviewSessions.AsNoTracking()
                .SingleOrDefaultAsync(session => session.UserId == request.UserId);
            if (existingSession != null) return Ok(existingSession);
            if (!_study.Enabled || !await _context.ConsentRecords.AnyAsync(c => c.UserId == request.UserId &&
                c.Version == _study.Version && c.TextHash == _study.TextHash))
                return Conflict("Current approved consent is required before creating a session.");

            var conditionCounts = await _context.InterviewSessions
                .GroupBy(session => session.Condition)
                .Select(group => new
                {
                    Condition = group.Key,
                    Count = group.Count()
                })
                .ToDictionaryAsync(
                    item => item.Condition,
                    item => item.Count);

            conditionCounts.TryGetValue("A", out var countA);
            conditionCounts.TryGetValue("B", out var countB);

            var condition = countA < countB
                ? "A"
                : countB < countA
                    ? "B"
                    : RandomNumberGenerator.GetInt32(2) == 0
                        ? "A"
                        : "B";

            var session = new InterviewSession
            {
                Id = Guid.NewGuid(),
                UserId = request.UserId,
                Condition = condition,
                FlowVersion = 2,
                StartedAt = DateTime.UtcNow
            };

            _context.InterviewSessions.Add(session);
            await _context.SaveChangesAsync();

            await SnapshotQuestionnaires(session);
            await _context.SaveChangesAsync();
            await efTransaction!.CommitAsync();

            return Ok(session);
        }
        finally
        {
            await _context.Database.CloseConnectionAsync();
        }
    }

    [HttpGet("sessions/{sessionId:guid}/next-question")]
    public async Task<IActionResult> GetNextQuestion(Guid sessionId, [FromQuery] int? answerId = null, CancellationToken cancellationToken = default)
    {
        var session = await _context.InterviewSessions
            .Include(s => s.Answers)
            .FirstOrDefaultAsync(s => s.Id == sessionId);

        if (session == null)
            return NotFound("Session not found.");

        if (session.FlowVersion >= 2)
        {
            if (answerId.HasValue && answerId != session.Answers.OrderBy(a => a.StageNumber).LastOrDefault()?.Id)
                return Conflict("The request does not concern the latest saved answer.");
            return await CurrentFlowState(session);
        }

        var previousAnswers = session.Answers?
            .OrderBy(a => a.StageNumber)
            .ToList() ?? new List<UserAnswer>();

        string? feedback = null;
        int? feedbackAnswerId = previousAnswers.LastOrDefault()?.Id;
        if (answerId.HasValue && answerId != feedbackAnswerId)
            return Conflict("The feedback request does not concern the latest saved answer.");

        // If there are previous answers, ensure feedback is generated for the latest answer
        if (previousAnswers.Count > 0)
        {
            var lastAnswer = previousAnswers.Last();

            if (!FeedbackResult.IsUsable(lastAnswer.FeedbackText))
            {
                var stopwatch = Stopwatch.StartNew();
                FeedbackResult result;
                try
                {
                    result = await _feedbackManager.GenerateFeedbackAsync(
                        session.Condition, lastAnswer.StageNumber, lastAnswer, cancellationToken);
                }
                catch (Exception ex)
                {
                    // No answer content or provider response body is included in the log.
                    _logger.LogWarning("Feedback generation exception {ExceptionType} for AnswerId {AnswerId}",
                        ex.GetType().Name, lastAnswer.Id);
                    result = new(FeedbackStatus.Unavailable, ErrorCode: "generation_exception");
                }
                if (result.Status == FeedbackStatus.Success && !FeedbackResult.IsUsable(result.Text))
                    result = new(FeedbackStatus.InvalidResult, ErrorCode: "empty_or_unavailable_text");

                _logger.LogInformation(
                    "FeedbackGeneration SessionId {SessionId} AnswerId {AnswerId} Condition {Condition} Status {Status} ErrorCode {ErrorCode} Model {Model} Attempts {Attempts} DurationMs {DurationMs}",
                    session.Id, lastAnswer.Id, session.Condition, result.Status.ToString(), result.ErrorCode,
                    result.Model, result.Attempts, stopwatch.ElapsedMilliseconds);

                if (result.Status != FeedbackStatus.Success)
                {
                    return Ok(new
                    {
                        sessionId = session.Id,
                        answerId = lastAnswer.Id,
                        stageNumber = lastAnswer.StageNumber,
                        questionText = (string?)null,
                        feedback = (string?)null,
                        feedbackStatus = result.Status.ToString(),
                        feedbackErrorCode = result.ErrorCode,
                        isFinished = false
                    });
                }

                feedback = result.Text;
                lastAnswer.FeedbackText = feedback;
                lastAnswer.FeedbackShownAt = DateTime.UtcNow;
                await _context.SaveChangesAsync(cancellationToken);
            }
            else
            {
                feedback = lastAnswer.FeedbackText;
            }
        }

        int currentStage = previousAnswers.Count + 1;

        // If all 7 answers have been submitted
        if (previousAnswers.Count >= 7)
        {
            var lastAnswer = previousAnswers.Last();

            // Keep Stage 7 open until the decision and any chosen revision are saved.
            if (!lastAnswer.ChoseToRevise.HasValue ||
                (lastAnswer.ChoseToRevise == true &&
                 (string.IsNullOrWhiteSpace(lastAnswer.RevisedAnswerText) ||
                  !lastAnswer.RevisionSubmittedAt.HasValue)))
            {
                return Ok(new
                {
                    sessionId = session.Id,
                    stageNumber = 7,
                    questionText = (string?)null,
                    feedback,
                    answerId = feedbackAnswerId,
                    feedbackStatus = FeedbackStatus.Success.ToString(),
                    questionShownAt = DateTime.UtcNow,
                    isFinished = false
                });
            }

            // The final decision and any chosen revision are complete.
            session.FinishedAt ??= DateTime.UtcNow;
            await _context.SaveChangesAsync();

            return Ok(new
            {
                sessionId = session.Id,
                stageNumber = currentStage,
                isFinished = true,
                message = "The interview session is completed."
            });
        }

        string questionText = await _interviewManager.GetNextQuestionAsync(
            session.Condition,
            currentStage,
            previousAnswers);

        return Ok(new
        {
            sessionId = session.Id,
            stageNumber = currentStage,
            questionText,
            feedback,
            answerId = feedbackAnswerId,
            feedbackStatus = feedback == null ? "NotRequired" : FeedbackStatus.Success.ToString(),
            questionShownAt = DateTime.UtcNow,
            isFinished = false
        });
    }

    [HttpPost("answers")]
    public async Task<IActionResult> SubmitAnswer(
        [FromBody] SubmitAnswerRequest request)
    {
        if (request.SubmissionId == Guid.Empty)
        {
            return BadRequest("SubmissionId is required.");
        }

        if (string.IsNullOrWhiteSpace(request.AnswerText))
        {
            return BadRequest("Answer cannot be empty.");
        }

        if (request.OriginalAnswerSubmittedAt == default)
        {
            return BadRequest("Original answer submission time is required.");
        }

        if (request.OriginalAnswerSubmittedAt < request.QuestionShownAt)
        {
            return BadRequest(
                "Original answer submission time cannot be earlier than the question time.");
        }

        if (request.OriginalAnswerSubmittedAt > DateTime.UtcNow.AddMinutes(5))
        {
            return BadRequest(
                "Original answer submission time is too far in the future.");
        }

        if (!ValidTiming(request.Timing)) return BadRequest("Invalid browser timing.");

        // Serialize validation and insertion across SQLite connections. A deferred
        // transaction would allow two requests to validate the same empty stage.
        await _context.Database.OpenConnectionAsync();
        try
        {
            var connection = (SqliteConnection)_context.Database.GetDbConnection();
            await using var sqliteTransaction = connection.BeginTransaction(deferred: false);
            await using var transaction =
                await _context.Database.UseTransactionAsync(sqliteTransaction);

            var result = await SubmitAnswerWithinTransaction(request);
            await transaction!.CommitAsync();
            return result;
        }
        finally
        {
            await _context.Database.CloseConnectionAsync();
        }
    }

    private async Task<IActionResult> SubmitAnswerWithinTransaction(SubmitAnswerRequest request)
    {
        var normalizedAnswerText = request.AnswerText.Trim();

        var existingAnswer = await _context.UserAnswers
            .AsNoTracking()
            .FirstOrDefaultAsync(answer =>
                answer.SubmissionId == request.SubmissionId);

        if (existingAnswer != null)
        {
            return HasConflictingSubmission(existingAnswer, request, normalizedAnswerText)
                ? Conflict("SubmissionId has already been used for different answer data.")
                : Ok(new { answerId = existingAnswer.Id });
        }

        var session = await _context.InterviewSessions
            .AsNoTracking()
            .FirstOrDefaultAsync(session => session.Id == request.SessionId);

        if (session == null)
        {
            return NotFound("Session not found.");
        }

        var stages = await _context.UserAnswers
            .Where(answer => answer.InterviewSessionId == request.SessionId)
            .OrderBy(answer => answer.StageNumber)
            .Select(answer => answer.StageNumber)
            .ToListAsync();

        if (stages.Contains(request.StageNumber))
        {
            return Conflict("An initial answer has already been recorded for this stage.");
        }

        if (session.FinishedAt.HasValue ||
            request.StageNumber is < 1 or > 7 ||
            !stages.SequenceEqual(Enumerable.Range(1, stages.Count)) ||
            request.StageNumber != stages.Count + 1)
        {
            return Conflict("The answer does not concern the expected interview stage.");
        }

        if (session.FlowVersion >= 2 && stages.Count > 0 &&
            !await _context.UserAnswers.AnyAsync(a => a.InterviewSessionId == session.Id &&
                a.StageNumber == stages.Count && a.StageCompletedAt != null))
            return Conflict("The previous stage has not been completed.");

        if (session.FlowVersion >= 2 && request.QuestionText !=
            await _interviewManager.GetNextQuestionAsync(session.Condition, request.StageNumber, new List<UserAnswer>()))
            return BadRequest("Question text does not match the fixed interview question.");

        if (await HasPendingQuestionnaires(session.Id, "pre"))
            return Conflict("Complete the assigned pre questionnaires before answering.");

        var answer = new UserAnswer
        {
            SubmissionId = request.SubmissionId,
            InterviewSessionId = request.SessionId,
            StageNumber = request.StageNumber,
            StageName = request.StageName,
            QuestionText = request.QuestionText,
            AnswerText = normalizedAnswerText,
            QuestionShownAt = request.QuestionShownAt,
            AnswerSubmittedAt = request.OriginalAnswerSubmittedAt,
            QuestionBrowserDisplayedAt = session.FlowVersion >= 2 ? request.QuestionBrowserDisplayedAt : null,
            OriginalBrowserSubmittedAt = session.FlowVersion >= 2 ? request.Timing?.BrowserOccurredAt : null,
            OriginalWritingMilliseconds = session.FlowVersion >= 2 ? request.Timing?.ElapsedMilliseconds : null
        };

        _context.UserAnswers.Add(answer);

        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            _context.Entry(answer).State = EntityState.Detached;

            existingAnswer = await _context.UserAnswers
                .AsNoTracking()
                .FirstOrDefaultAsync(existing =>
                    existing.SubmissionId == request.SubmissionId);

            if (existingAnswer == null)
            {
                throw;
            }

            return HasConflictingSubmission(existingAnswer, request, normalizedAnswerText)
                ? Conflict("SubmissionId has already been used for different answer data.")
                : Ok(new { answerId = existingAnswer.Id });
        }

        return Ok(new
        {
            answerId = answer.Id
        });
    }

    private static bool HasConflictingSubmission(
        UserAnswer existingAnswer,
        SubmitAnswerRequest request,
        string normalizedAnswerText)
    {
        return existingAnswer.InterviewSessionId != request.SessionId ||
               existingAnswer.StageNumber != request.StageNumber ||
               !string.Equals(existingAnswer.StageName, request.StageName, StringComparison.Ordinal) ||
               !string.Equals(existingAnswer.QuestionText, request.QuestionText, StringComparison.Ordinal) ||
               !string.Equals(existingAnswer.AnswerText, normalizedAnswerText, StringComparison.Ordinal) ||
               existingAnswer.QuestionShownAt != request.QuestionShownAt ||
               existingAnswer.AnswerSubmittedAt != request.OriginalAnswerSubmittedAt;
    }
    
    [HttpPut("answers/{answerId:int}/revision")]
    public async Task<IActionResult> ReviseAnswer(
        int answerId,
        [FromBody] ReviseAnswerRequest request) =>
        await ExecuteAnswerAction(() => ReviseAnswerWithinTransaction(answerId, request));

    private async Task<IActionResult> ReviseAnswerWithinTransaction(int answerId, ReviseAnswerRequest request)
    {
        var answer = await _context.UserAnswers
            .FirstOrDefaultAsync(a => a.Id == answerId);

        if (answer == null)
        {
            return NotFound("Answer not found.");
        }

        if (string.IsNullOrWhiteSpace(request.RevisedAnswerText))
        {
            return BadRequest("Revised answer cannot be empty.");
        }

        if (answer.ChoseToRevise != true)
        {
            return BadRequest(
                "The user must choose to revise before submitting a revised answer.");
        }

        if (!string.IsNullOrWhiteSpace(answer.RevisedAnswerText))
        {
            return string.Equals(answer.RevisedAnswerText, request.RevisedAnswerText.Trim(), StringComparison.Ordinal)
                ? RevisionResult(answer)
                : Conflict("This answer has already been revised with different text.");
        }

        var revisionSession = await _context.InterviewSessions.FindAsync(answer.InterviewSessionId);
        if (revisionSession?.FlowVersion >= 2)
        {
            if (!ValidTiming(request.Timing)) return BadRequest("Invalid browser timing.");
            if (!await IsActiveAnswer(answer) || answer.FeedbackDisplayAcknowledgedAt == null)
                return Conflict("Revision is not allowed in the current state.");
            answer.RevisionBrowserSubmittedAt = request.Timing?.BrowserOccurredAt;
            answer.RevisionWritingMilliseconds = request.Timing?.ElapsedMilliseconds;
        }

        answer.RevisedAnswerText = request.RevisedAnswerText.Trim();
        answer.RevisionSubmittedAt = DateTime.UtcNow;

        if (answer.StageNumber >= 7 && revisionSession?.FlowVersion < 2)
        {
            var session = await _context.InterviewSessions
                .FirstOrDefaultAsync(s => s.Id == answer.InterviewSessionId);
            if (session != null)
            {
                session.FinishedAt ??= DateTime.UtcNow;
            }
        }

        await _context.SaveChangesAsync();

        return RevisionResult(answer);
    }
    
    [HttpPut("answers/{answerId:int}/decision")]
    public async Task<IActionResult> SaveAnswerDecision(
        int answerId,
        [FromBody] AnswerDecisionRequest request) =>
        await ExecuteAnswerAction(() => SaveAnswerDecisionWithinTransaction(answerId, request));

    private async Task<IActionResult> SaveAnswerDecisionWithinTransaction(int answerId, AnswerDecisionRequest request)
    {
        var answer = await _context.UserAnswers
            .FirstOrDefaultAsync(a => a.Id == answerId);

        if (answer == null)
        {
            return NotFound("Answer not found.");
        }

        if (answer.ChoseToRevise.HasValue)
        {
            return answer.ChoseToRevise == request.ChoseToRevise
                ? DecisionResult(answer)
                : Conflict("A different decision has already been recorded.");
        }

        var decisionSession = await _context.InterviewSessions.FindAsync(answer.InterviewSessionId);
        if (decisionSession?.FlowVersion >= 2)
        {
            if (!ValidTiming(request.Timing)) return BadRequest("Invalid browser timing.");
            if (!await IsActiveAnswer(answer) || answer.FeedbackDisplayAcknowledgedAt == null ||
                !FeedbackResult.IsUsable(answer.FeedbackText))
                return Conflict("Read the initial feedback before deciding.");
            answer.DecisionMilliseconds = request.Timing?.ElapsedMilliseconds;
            if (!request.ChoseToRevise)
            {
                answer.StageCompletedAt = DateTime.UtcNow;
                answer.StageElapsedMilliseconds = request.Timing?.StageElapsedMilliseconds;
            }
        }

        answer.ChoseToRevise = request.ChoseToRevise;
        answer.RevisionDecisionAt = DateTime.UtcNow;

        if (answer.StageNumber >= 7 && request.ChoseToRevise == false)
        {
            var session = await _context.InterviewSessions
                .FirstOrDefaultAsync(s => s.Id == answer.InterviewSessionId);
            if (session != null)
            {
                session.FinishedAt ??= DateTime.UtcNow;
            }
        }

        await CompleteStudyIfReady(answer.InterviewSessionId);
        await _context.SaveChangesAsync();

        return DecisionResult(answer);
    }

    // Lock before reading so concurrent retries cannot overwrite the first action or its timestamps.
    private async Task<IActionResult> ExecuteAnswerAction(Func<Task<IActionResult>> action)
    {
        await _context.Database.OpenConnectionAsync();
        try
        {
            var connection = (SqliteConnection)_context.Database.GetDbConnection();
            await using var sqliteTransaction = connection.BeginTransaction(deferred: false);
            await using var transaction = await _context.Database.UseTransactionAsync(sqliteTransaction);
            var result = await action();
            await transaction!.CommitAsync();
            return result;
        }
        finally
        {
            await _context.Database.CloseConnectionAsync();
        }
    }

    private IActionResult RevisionResult(UserAnswer answer) =>
        Ok(new
        {
            answer.Id,
            answer.ChoseToRevise,
            answer.RevisedAnswerText,
            answer.RevisionDecisionAt,
            answer.RevisionSubmittedAt,
            answer.RevisionLatencyInSeconds
        });

    private IActionResult DecisionResult(UserAnswer answer) =>
        Ok(new
        {
            answer.Id,
            answer.ChoseToRevise,
            answer.RevisionDecisionAt
        });

    [HttpGet("export/csv")]
    public async Task<IActionResult> ExportDataAsCsv()
    {
        if (string.IsNullOrWhiteSpace(_exportKey) || _exportKey.Length < 32)
        {
            return StatusCode(
                StatusCodes.Status500InternalServerError,
                "Export endpoint is not configured.");
        }

        if (!Request.Headers.TryGetValue("X-Export-Key", out var suppliedKeys) ||
            suppliedKeys.Count != 1 ||
            !IsValidExportKey(suppliedKeys[0]))
        {
            return Unauthorized();
        }

        var users = await _context.Users
            .Include(u => u.Sessions)
                .ThenInclude(s => s.Answers)
            .OrderBy(u => u.Id)
            .ToListAsync();

        var consents = await _context.ConsentRecords.AsNoTracking().ToDictionaryAsync(c => c.UserId);
        var sb = new System.Text.StringBuilder();

        sb.AppendLine("UserId,Nickname,Age,Gender,AiFamiliarityLevel,UserCreatedAt," +
                      "SessionId,Condition,SessionStartedAt,SessionFinishedAt,SessionDurationSeconds," +
                      "AnswerId,StageNumber,StageName,QuestionText,AnswerText," +
                      "QuestionShownAt,AnswerSubmittedAt,ResponseLatencyInSeconds," +
                      "FeedbackText,FeedbackShownAt," +
                      "ChoseToRevise,RevisionDecisionAt,DecisionLatencyInSeconds," +
                      "RevisedAnswerText,RevisionSubmittedAt,RevisionLatencyInSeconds," +
                      "OriginalAnswerLengthChars,OriginalAnswerWordCount," +
                      "RevisedAnswerLengthChars,RevisedAnswerWordCount," +
                      "LengthDeltaChars,WordCountDelta,FlowVersion,RevisedFeedbackText,FeedbackReadyAt,RevisedFeedbackReadyAt,FeedbackDisplayAcknowledgedAt,RevisedFeedbackDisplayAcknowledgedAt,FeedbackBrowserDisplayedAt,RevisedFeedbackBrowserDisplayedAt,QuestionBrowserDisplayedAt,OriginalBrowserSubmittedAt,RevisionEditorBrowserDisplayedAt,RevisionBrowserSubmittedAt,StageCompletedAt,OriginalWritingMilliseconds,RevisionWritingMilliseconds,DecisionMilliseconds,RevisedFeedbackReadingMilliseconds,StageElapsedMilliseconds,FeedbackWaitMilliseconds,RevisedFeedbackWaitMilliseconds,FeedbackGenerationMilliseconds,RevisedFeedbackGenerationMilliseconds,FeedbackGenerationAttempts,RevisedFeedbackGenerationAttempts,StudyCompletedAt,ConsentVersion,ConsentAcceptedAt,ConsentTextHash");

        foreach (var user in users)
        {
            foreach (var session in user.Sessions.OrderBy(s => s.StartedAt))
            {
                double? sessionDurationSeconds = session.FinishedAt.HasValue
                    ? (session.FinishedAt.Value - session.StartedAt).TotalSeconds
                    : null;

                foreach (var answer in session.Answers.OrderBy(a => a.StageNumber))
                {
                    int originalLen = answer.AnswerText?.Length ?? 0;
                    int originalWords = string.IsNullOrWhiteSpace(answer.AnswerText)
                        ? 0
                        : answer.AnswerText.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).Length;

                    int? revisedLen = answer.RevisedAnswerText?.Length;
                    int? revisedWords = string.IsNullOrWhiteSpace(answer.RevisedAnswerText)
                        ? (int?)null
                        : answer.RevisedAnswerText.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).Length;

                    int? lenDelta = revisedLen.HasValue ? revisedLen.Value - originalLen : null;
                    int? wordsDelta = revisedWords.HasValue ? revisedWords.Value - originalWords : null;

                    sb.AppendLine(string.Join(",",
                        EscapeCsv(user.Id.ToString()),
                        EscapeCsv(user.Nickname),
                        EscapeCsv(user.Age.ToString()),
                        EscapeCsv(user.Gender),
                        EscapeCsv(user.AiFamiliarityLevel),
                        EscapeCsv(user.CreatedAt.ToString("o")),
                        EscapeCsv(session.Id.ToString()),
                        EscapeCsv(session.Condition),
                        EscapeCsv(session.StartedAt.ToString("o")),
                        EscapeCsv(session.FinishedAt?.ToString("o") ?? ""),
                        EscapeCsv(sessionDurationSeconds?.ToString("F2") ?? ""),
                        EscapeCsv(answer.Id.ToString()),
                        EscapeCsv(answer.StageNumber.ToString()),
                        EscapeCsv(answer.StageName),
                        EscapeCsv(answer.QuestionText),
                        EscapeCsv(answer.AnswerText),
                        EscapeCsv(answer.QuestionShownAt.ToString("o")),
                        EscapeCsv(answer.AnswerSubmittedAt.ToString("o")),
                        EscapeCsv(answer.ResponseLatencyInSeconds.ToString("F2")),
                        EscapeCsv(answer.FeedbackText ?? ""),
                        EscapeCsv(answer.FeedbackShownAt?.ToString("o") ?? ""),
                        EscapeCsv(answer.ChoseToRevise.HasValue ? (answer.ChoseToRevise.Value ? "1" : "0") : ""),
                        EscapeCsv(answer.RevisionDecisionAt?.ToString("o") ?? ""),
                        EscapeCsv(answer.DecisionLatencyInSeconds?.ToString("F2") ?? ""),
                        EscapeCsv(answer.RevisedAnswerText ?? ""),
                        EscapeCsv(answer.RevisionSubmittedAt?.ToString("o") ?? ""),
                        EscapeCsv(answer.RevisionLatencyInSeconds?.ToString("F2") ?? ""),
                        EscapeCsv(originalLen.ToString()),
                        EscapeCsv(originalWords.ToString()),
                        EscapeCsv(revisedLen?.ToString() ?? ""),
                        EscapeCsv(revisedWords?.ToString() ?? ""),
                        EscapeCsv(lenDelta?.ToString() ?? ""),
                        EscapeCsv(wordsDelta?.ToString() ?? ""),
                        EscapeCsv(session.FlowVersion.ToString()),
                        EscapeCsv(answer.RevisedFeedbackText ?? ""),
                        EscapeCsv(answer.FeedbackReadyAt?.ToString("o") ?? ""),
                        EscapeCsv(answer.RevisedFeedbackReadyAt?.ToString("o") ?? ""),
                        EscapeCsv(answer.FeedbackDisplayAcknowledgedAt?.ToString("o") ?? ""),
                        EscapeCsv(answer.RevisedFeedbackDisplayAcknowledgedAt?.ToString("o") ?? ""),
                        EscapeCsv(answer.FeedbackBrowserDisplayedAt?.ToString("o") ?? ""),
                        EscapeCsv(answer.RevisedFeedbackBrowserDisplayedAt?.ToString("o") ?? ""),
                        EscapeCsv(answer.QuestionBrowserDisplayedAt?.ToString("o") ?? ""),
                        EscapeCsv(answer.OriginalBrowserSubmittedAt?.ToString("o") ?? ""),
                        EscapeCsv(answer.RevisionEditorBrowserDisplayedAt?.ToString("o") ?? ""),
                        EscapeCsv(answer.RevisionBrowserSubmittedAt?.ToString("o") ?? ""),
                        EscapeCsv(answer.StageCompletedAt?.ToString("o") ?? ""),
                        EscapeCsv(answer.OriginalWritingMilliseconds?.ToString("R", System.Globalization.CultureInfo.InvariantCulture) ?? ""),
                        EscapeCsv(answer.RevisionWritingMilliseconds?.ToString("R", System.Globalization.CultureInfo.InvariantCulture) ?? ""),
                        EscapeCsv(answer.DecisionMilliseconds?.ToString("R", System.Globalization.CultureInfo.InvariantCulture) ?? ""),
                        EscapeCsv(answer.RevisedFeedbackReadingMilliseconds?.ToString("R", System.Globalization.CultureInfo.InvariantCulture) ?? ""),
                        EscapeCsv(answer.StageElapsedMilliseconds?.ToString("R", System.Globalization.CultureInfo.InvariantCulture) ?? ""),
                        EscapeCsv(answer.FeedbackWaitMilliseconds?.ToString("R", System.Globalization.CultureInfo.InvariantCulture) ?? ""),
                        EscapeCsv(answer.RevisedFeedbackWaitMilliseconds?.ToString("R", System.Globalization.CultureInfo.InvariantCulture) ?? ""),
                        EscapeCsv(answer.FeedbackGenerationMilliseconds?.ToString("R", System.Globalization.CultureInfo.InvariantCulture) ?? ""),
                        EscapeCsv(answer.RevisedFeedbackGenerationMilliseconds?.ToString("R", System.Globalization.CultureInfo.InvariantCulture) ?? ""),
                        EscapeCsv(answer.FeedbackGenerationAttempts?.ToString() ?? ""),
                        EscapeCsv(answer.RevisedFeedbackGenerationAttempts?.ToString() ?? ""),
                        EscapeCsv(session.StudyCompletedAt?.ToString("o") ?? ""),
                        EscapeCsv(consents.GetValueOrDefault(user.Id)?.Version ?? ""),
                        EscapeCsv(consents.GetValueOrDefault(user.Id)?.AcceptedAt.ToString("o") ?? ""),
                        EscapeCsv(consents.GetValueOrDefault(user.Id)?.TextHash ?? "")
                    ));
                }
            }
        }

        var preamble = System.Text.Encoding.UTF8.GetPreamble();
        var dataBytes = System.Text.Encoding.UTF8.GetBytes(sb.ToString());
        var bytes = new byte[preamble.Length + dataBytes.Length];
        Buffer.BlockCopy(preamble, 0, bytes, 0, preamble.Length);
        Buffer.BlockCopy(dataBytes, 0, bytes, preamble.Length, dataBytes.Length);

        Response.Headers.CacheControl = "no-store";

        return File(bytes, "text/csv; charset=utf-8", $"interview_data_export_{DateTime.UtcNow:yyyyMMdd_HHmmss}.csv");
    }

    private bool IsValidExportKey(string? suppliedKey)
    {
        if (suppliedKey == null)
        {
            return false;
        }

        var suppliedBytes = Encoding.UTF8.GetBytes(suppliedKey);
        var configuredBytes = Encoding.UTF8.GetBytes(_exportKey);

        return suppliedBytes.Length == configuredBytes.Length &&
               CryptographicOperations.FixedTimeEquals(
                   suppliedBytes,
                   configuredBytes);
    }

    private static string EscapeCsv(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "\"\"";

        var safeValue = value[0] is '=' or '+' or '-' or '@'
            ? $"'{value}"
            : value;

        return $"\"{safeValue.Replace("\"", "\"\"")}\"";
    }
}
