using AI_Interview_Simulator.Data.Context;
using AI_Interview_Simulator.Data.Entities;
using AI_Interview_Simulator.Engine;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace InterviewSimulator.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class InterviewController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly InterviewManager _interviewManager;

    public InterviewController(ApplicationDbContext context, InterviewManager interviewManager)
    {
        _context = context;
        _interviewManager = interviewManager;
    }

    [HttpPost("users")]
    public async Task<IActionResult> CreateUser([FromBody] User user)
    {
        _context.Users.Add(user);
        await _context.SaveChangesAsync();
        return Ok(user);
    }

    [HttpPost("sessions")]
    public async Task<IActionResult> StartSession(int userId, string condition)
    {
        var session = new InterviewSession
        {
            UserId = userId,
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

        if (session == null) return NotFound("Session not found.");

        int currentStage = session.Answers.Count + 1;

        if (currentStage > 7)
        {
            session.FinishedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            return Ok(new { isFinished = true, message = "The interview session is completed." });
        }

        string questionText = await _interviewManager.GetNextQuestionAsync(session.Condition, currentStage, session.Answers);

        return Ok(new
        {
            sessionId = session.Id,
            stageNumber = currentStage,
            questionText = questionText,
            questionShownAt = DateTime.UtcNow,
            isFinished = false
        });
    }

    [HttpPost("answers")]
    public async Task<IActionResult> SubmitAnswer([FromBody] UserAnswer answer)
    {
        if (answer.AnswerSubmittedAt == default)
        {
            answer.AnswerSubmittedAt = DateTime.UtcNow;
        }

        _context.UserAnswers.Add(answer);
        await _context.SaveChangesAsync();

        return Ok(answer);
    }
}