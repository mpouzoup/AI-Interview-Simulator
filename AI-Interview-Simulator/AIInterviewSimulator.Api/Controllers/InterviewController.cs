using System.Security.Cryptography;
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
public class InterviewController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly InterviewManager _interviewManager;
    private readonly FeedbackManager _feedbackManager;

    public InterviewController(
        ApplicationDbContext context,
        InterviewManager interviewManager,
        FeedbackManager feedbackManager)
    {
        _context = context;
        _interviewManager = interviewManager;
        _feedbackManager = feedbackManager;
    }

    [HttpPost("users")]
    public async Task<IActionResult> CreateUser([FromBody] CreateUserRequest request)
    {
        var user = new User
        {
            Nickname = request.Nickname,
            Age = request.Age,
            Gender = request.Gender,
            AiFamiliarityLevel = request.AiFamiliarityLevel,
            CreatedAt = DateTime.UtcNow
        };

        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        return Ok(user);
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

            var existingSession = await _context.InterviewSessions
                .AnyAsync(session => session.UserId == request.UserId);

            if (existingSession)
            {
                return Conflict(
                    "Ο συγκεκριμένος χρήστης έχει ήδη συμμετάσχει σε συνέντευξη.");
            }

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
                StartedAt = DateTime.UtcNow
            };

            _context.InterviewSessions.Add(session);
            await _context.SaveChangesAsync();

            await efTransaction.CommitAsync();

            return Ok(session);
        }
        finally
        {
            await _context.Database.CloseConnectionAsync();
        }
    }

    [HttpGet("sessions/{sessionId:guid}/next-question")]
    public async Task<IActionResult> GetNextQuestion(Guid sessionId)
    {
        var session = await _context.InterviewSessions
            .Include(s => s.Answers)
            .FirstOrDefaultAsync(s => s.Id == sessionId);

        if (session == null)
            return NotFound("Session not found.");

        var previousAnswers = session.Answers?
            .OrderBy(a => a.StageNumber)
            .ToList() ?? new List<UserAnswer>();

        string? feedback = null;

        // If there are previous answers, ensure feedback is generated for the latest answer
        if (previousAnswers.Count > 0)
        {
            var lastAnswer = previousAnswers.Last();

            if (string.IsNullOrWhiteSpace(lastAnswer.FeedbackText))
            {
                feedback = await _feedbackManager.GenerateFeedbackAsync(
                    session.Condition,
                    lastAnswer.StageNumber,
                    lastAnswer);

                lastAnswer.FeedbackText = feedback;
                lastAnswer.FeedbackShownAt = DateTime.UtcNow;

                await _context.SaveChangesAsync();
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

            // If user hasn't made a revision decision yet for Stage 7, present feedback
            if (!lastAnswer.ChoseToRevise.HasValue)
            {
                return Ok(new
                {
                    sessionId = session.Id,
                    stageNumber = 7,
                    questionText = (string?)null,
                    feedback,
                    questionShownAt = DateTime.UtcNow,
                    isFinished = false
                });
            }

            // If decision is recorded, conclude session
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
            questionShownAt = DateTime.UtcNow,
            isFinished = false
        });
    }

    [HttpPost("answers")]
    public async Task<IActionResult> SubmitAnswer(
        [FromBody] SubmitAnswerRequest request)
    {
        var sessionExists = await _context.InterviewSessions
            .AnyAsync(session => session.Id == request.SessionId);

        if (!sessionExists)
        {
            return NotFound("Session not found.");
        }

        if (string.IsNullOrWhiteSpace(request.AnswerText))
        {
            return BadRequest("Answer cannot be empty.");
        }

        var answer = new UserAnswer
        {
            InterviewSessionId = request.SessionId,
            StageNumber = request.StageNumber,
            StageName = request.StageName,
            QuestionText = request.QuestionText,
            AnswerText = request.AnswerText.Trim(),
            QuestionShownAt = request.QuestionShownAt,
            AnswerSubmittedAt = DateTime.UtcNow
        };

        _context.UserAnswers.Add(answer);
        await _context.SaveChangesAsync();

        return Ok(new
        {
            answerId = answer.Id
        });
    }
    
    [HttpPut("answers/{answerId:int}/revision")]
    public async Task<IActionResult> ReviseAnswer(
        int answerId,
        [FromBody] ReviseAnswerRequest request)
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
            return Conflict("This answer has already been revised.");
        }

        answer.RevisedAnswerText = request.RevisedAnswerText.Trim();
        answer.RevisionSubmittedAt = DateTime.UtcNow;

        if (answer.StageNumber >= 7)
        {
            var session = await _context.InterviewSessions
                .FirstOrDefaultAsync(s => s.Id == answer.InterviewSessionId);
            if (session != null)
            {
                session.FinishedAt ??= DateTime.UtcNow;
            }
        }

        await _context.SaveChangesAsync();

        return Ok(new
        {
            answer.Id,
            answer.ChoseToRevise,
            answer.RevisedAnswerText,
            answer.RevisionDecisionAt,
            answer.RevisionSubmittedAt,
            answer.RevisionLatencyInSeconds
        });
    }
    
    [HttpPut("answers/{answerId:int}/decision")]
    public async Task<IActionResult> SaveAnswerDecision(
        int answerId,
        [FromBody] AnswerDecisionRequest request)
    {
        var answer = await _context.UserAnswers
            .FirstOrDefaultAsync(a => a.Id == answerId);

        if (answer == null)
        {
            return NotFound("Answer not found.");
        }

        if (answer.ChoseToRevise.HasValue)
        {
            return Conflict("A decision has already been recorded.");
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

        await _context.SaveChangesAsync();

        return Ok(new
        {
            answer.Id,
            answer.ChoseToRevise,
            answer.RevisionDecisionAt
        });
    }

    [HttpGet("export/csv")]
    public async Task<IActionResult> ExportDataAsCsv()
    {
        var users = await _context.Users
            .Include(u => u.Sessions)
                .ThenInclude(s => s.Answers)
            .OrderBy(u => u.Id)
            .ToListAsync();

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
                      "LengthDeltaChars,WordCountDelta");

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
                        EscapeCsv(wordsDelta?.ToString() ?? "")
                    ));
                }
            }
        }

        var preamble = System.Text.Encoding.UTF8.GetPreamble();
        var dataBytes = System.Text.Encoding.UTF8.GetBytes(sb.ToString());
        var bytes = new byte[preamble.Length + dataBytes.Length];
        Buffer.BlockCopy(preamble, 0, bytes, 0, preamble.Length);
        Buffer.BlockCopy(dataBytes, 0, bytes, preamble.Length, dataBytes.Length);

        return File(bytes, "text/csv; charset=utf-8", $"interview_data_export_{DateTime.UtcNow:yyyyMMdd_HHmmss}.csv");
    }

    private static string EscapeCsv(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "\"\"";
        return $"\"{value.Replace("\"", "\"\"")}\"";
    }
}
