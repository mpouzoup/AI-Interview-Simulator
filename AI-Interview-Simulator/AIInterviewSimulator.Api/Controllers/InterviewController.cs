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

        var condition = request.Condition.Trim().ToUpperInvariant();

        if (condition != "A" && condition != "B")
        {
            return BadRequest("Condition must be A or B.");
        }

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
            var lastAnswer = previousAnswers.Last();

            feedback = await _feedbackManager.GenerateFeedbackAsync(
                session.Condition,
                lastAnswer.StageNumber,
                lastAnswer);
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

        return Ok(answer);
    }
}