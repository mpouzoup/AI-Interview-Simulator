using AIInterviewSimulator.Api.DTOs.Requests;
using AIInterviewSimulator.Data.Context;
using AIInterviewSimulator.Data.Entities;
using AIInterviewSimulator.Engine.Managers;
using Microsoft.AspNetCore.Mvc;
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
        var userExists = await _context.Users
            .AnyAsync(user => user.Id == request.UserId);

        if (!userExists)
        {
            return NotFound("User not found.");
        }

        var existingSession = await _context.InterviewSessions
            .FirstOrDefaultAsync(session => session.UserId == request.UserId);

        if (existingSession != null)
        {
            return Conflict(
                "Ο συγκεκριμένος χρήστης έχει ήδη συμμετάσχει σε συνέντευξη.");
        }

        var totalSessions = await _context.InterviewSessions.CountAsync();

        var condition = totalSessions % 2 == 0
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

        return Ok(session);
    }

    [HttpGet("sessions/{sessionId:guid}/next-question")]
    public async Task<IActionResult> GetNextQuestion(Guid sessionId)
    {
        var session = await _context.InterviewSessions
            .Include(s => s.Answers)
            .FirstOrDefaultAsync(s => s.Id == sessionId);

        if (session == null)
            return NotFound("Session not found.");

        int currentStage = (session.Answers?.Count ?? 0) + 1;

        if (currentStage > 7)
        {
            session.FinishedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            return Ok(new
            {
                stageNumber = currentStage,
                isFinished = true,
                message = "The interview session is completed."
            });
        }

        var previousAnswers = session.Answers?.ToList()
                              ?? new List<UserAnswer>();

        string? feedback = null;

        if (previousAnswers.Count > 0)
        {
            var lastAnswer = previousAnswers
                .OrderBy(a => a.StageNumber)
                .Last();

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

        await _context.SaveChangesAsync();

        return Ok(new
        {
            answer.Id,
            answer.ChoseToRevise,
            answer.RevisionDecisionAt
        });
    }
}