using System.Diagnostics;
using AIInterviewSimulator.Api.DTOs.Requests;
using AIInterviewSimulator.Data.Entities;
using AIInterviewSimulator.Engine.Feedback;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AIInterviewSimulator.Api.Controllers;

public partial class InterviewController
{
    private static bool ValidTiming(FlowTimingRequest? timing) => timing == null ||
        new[] { timing.ElapsedMilliseconds, timing.WaitMilliseconds, timing.StageElapsedMilliseconds }
            .All(v => v == null || double.IsFinite(v.Value) && v >= 0 && v <= 604800000);

    private async Task<bool> IsActiveAnswer(UserAnswer answer)
    {
        var session = await _context.InterviewSessions.FindAsync(answer.InterviewSessionId);
        return session is { FlowVersion: >= 2, FinishedAt: null } && answer.StageCompletedAt == null &&
            !await _context.UserAnswers.AnyAsync(a => a.InterviewSessionId == answer.InterviewSessionId && a.StageNumber > answer.StageNumber);
    }

    private async Task<IActionResult> CurrentFlowState(InterviewSession session)
    {
        if (await HasPendingQuestionnaires(session.Id, "pre"))
            return Ok(new { flowVersion = 2, flowState = "PreQuestionnaires", stageNumber = 1, isFinished = false });
        var answers = session.Answers.OrderBy(a => a.StageNumber).ToList();
        var last = answers.LastOrDefault();
        if (last != null && last.StageCompletedAt == null)
        {
            if (last.ChoseToRevise == true && last.RevisedAnswerText == null)
                return FlowReply(last, 1, "Revising");
            var round = last.RevisedAnswerText != null ? 2 : 1;
            var text = round == 1 ? last.FeedbackText : last.RevisedFeedbackText;
            return FlowReply(last, round, FeedbackResult.IsUsable(text)
                ? round == 1 ? "WaitingForDecision" : "WaitingForContinue"
                : round == 1 ? "AwaitingFeedback" : "AwaitingRevisedFeedback", "NotRequired");
        }
        if (session.FinishedAt != null && await HasPendingQuestionnaires(session.Id, "post"))
            return Ok(new { flowVersion = 2, flowState = "PostQuestionnaires", stageNumber = 7, isFinished = false });
        if (session.FinishedAt != null)
            return Ok(new { flowVersion = 2, flowState = "Finished", isFinished = true });
        var stage = answers.Count + 1;
        if (stage > 7) return Conflict("Invalid incomplete session state.");
        return Ok(new
        {
            flowVersion = 2, flowState = "Answering", stageNumber = stage,
            questionText = await _interviewManager.GetNextQuestionAsync(session.Condition, stage, answers),
            feedbackStatus = "NotRequired", isFinished = false
        });
    }

    private OkObjectResult FlowReply(UserAnswer answer, int round, string state, string status = "Success", string? error = null) => Ok(new
    {
        flowVersion = 2, flowState = state, answerId = answer.Id, stageNumber = answer.StageNumber,
        questionText = answer.QuestionText, answerText = answer.AnswerText,
        revisedAnswerText = answer.RevisedAnswerText, round,
        feedback = round == 1 ? answer.FeedbackText : answer.RevisedFeedbackText,
        feedbackStatus = status, feedbackErrorCode = error, isFinished = false
    });

    // A short, database-backed lease serializes generation across API instances.
    // No write transaction remains open while awaiting the provider.
    [HttpPost("answers/{answerId:int}/feedback/{round:int}")]
    public async Task<IActionResult> GenerateAnswerFeedback(int answerId, int round, CancellationToken cancellationToken = default)
    {
        if (round is not (1 or 2)) return BadRequest("Feedback round must be 1 or 2.");
        UserAnswer? source = null;
        string? condition = null;
        var lease = Guid.NewGuid();
        var claimed = false;
        var claimResult = await ExecuteAnswerAction(async () =>
        {
            var answer = await _context.UserAnswers.Include(a => a.InterviewSession).FirstOrDefaultAsync(a => a.Id == answerId);
            if (answer == null) return NotFound();
            if (answer.InterviewSession?.FlowVersion != 2) return Conflict("This endpoint requires flow v2.");
            var cached = round == 1 ? answer.FeedbackText : answer.RevisedFeedbackText;
            if (FeedbackResult.IsUsable(cached))
                return FlowReply(answer, round, round == 1 ? "WaitingForDecision" : "WaitingForContinue");
            if (!await IsActiveAnswer(answer) ||
                (round == 1 && answer.ChoseToRevise != null) ||
                (round == 2 && (answer.ChoseToRevise != true || answer.RevisedAnswerText == null)))
                return Conflict("Feedback is not allowed in this state.");
            if (answer.FeedbackLeaseId != null && answer.FeedbackLeaseUntil > DateTime.UtcNow)
                return FlowReply(answer, round, round == 1 ? "AwaitingFeedback" : "AwaitingRevisedFeedback", "InProgress");
            answer.FeedbackLeaseId = lease;
            answer.FeedbackLeaseRound = round;
            answer.FeedbackLeaseUntil = DateTime.UtcNow.AddSeconds(60);
            if (round == 1) answer.FeedbackGenerationAttempts = (answer.FeedbackGenerationAttempts ?? 0) + 1;
            else answer.RevisedFeedbackGenerationAttempts = (answer.RevisedFeedbackGenerationAttempts ?? 0) + 1;
            await _context.SaveChangesAsync();
            condition = answer.InterviewSession.Condition;
            // The existing engines see ONLY this round's answer, never a comparison prompt.
            source = new UserAnswer
            {
                StageNumber = answer.StageNumber, StageName = answer.StageName,
                QuestionText = answer.QuestionText,
                AnswerText = round == 1 ? answer.AnswerText : answer.RevisedAnswerText!
            };
            claimed = true;
            return Ok();
        });
        if (!claimed) return claimResult;

        var watch = Stopwatch.StartNew();
        FeedbackResult result;
        try
        {
            result = await _feedbackManager.GenerateFeedbackAsync(condition!, source!.StageNumber, source, cancellationToken);
            if (result.Status == FeedbackStatus.Success && !FeedbackResult.IsUsable(result.Text))
                result = new(FeedbackStatus.InvalidResult, ErrorCode: "empty_or_unavailable_text");
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Feedback exception {ExceptionType} AnswerId {AnswerId} Round {Round}", ex.GetType().Name, answerId, round);
            result = new(FeedbackStatus.Unavailable, ErrorCode: "generation_exception");
        }
        watch.Stop();
        // Complete even if the HTTP caller disconnected: preserve a valid result and release the lease.
        _context.ChangeTracker.Clear();
        return await ExecuteAnswerAction(async () =>
        {
            var answer = await _context.UserAnswers.FirstAsync(a => a.Id == answerId);
            if (answer.FeedbackLeaseId != lease || answer.FeedbackLeaseRound != round)
                return Conflict("This generation lease was superseded; retrieve current feedback.");
            answer.FeedbackLeaseId = null;
            answer.FeedbackLeaseRound = null;
            answer.FeedbackLeaseUntil = null;
            if (round == 1)
            {
                answer.FeedbackGenerationMilliseconds = (answer.FeedbackGenerationMilliseconds ?? 0) + watch.Elapsed.TotalMilliseconds;
                if (result.Status == FeedbackStatus.Success)
                {
                    answer.FeedbackText = result.Text;
                    answer.FeedbackReadyAt = DateTime.UtcNow;
                }
            }
            else
            {
                answer.RevisedFeedbackGenerationMilliseconds = (answer.RevisedFeedbackGenerationMilliseconds ?? 0) + watch.Elapsed.TotalMilliseconds;
                if (result.Status == FeedbackStatus.Success)
                {
                    answer.RevisedFeedbackText = result.Text;
                    answer.RevisedFeedbackReadyAt = DateTime.UtcNow;
                }
            }
            await _context.SaveChangesAsync();
            _logger.LogInformation("FeedbackGeneration AnswerId {AnswerId} Round {Round} Condition {Condition} Status {Status} ErrorCode {ErrorCode} Model {Model} Attempts {Attempts} DurationMs {DurationMs}",
                answerId, round, condition, result.Status, result.ErrorCode, result.Model, result.Attempts, watch.ElapsedMilliseconds);
            var state = result.Status == FeedbackStatus.Success
                ? round == 1 ? "WaitingForDecision" : "WaitingForContinue"
                : round == 1 ? "AwaitingFeedback" : "AwaitingRevisedFeedback";
            return FlowReply(answer, round, state, result.Status.ToString(), result.ErrorCode);
        });
    }

    [HttpPut("answers/{answerId:int}/feedback/{round:int}/shown")]
    public async Task<IActionResult> AcknowledgeFeedback(int answerId, int round, [FromBody] FlowTimingRequest request) =>
        await ExecuteAnswerAction(async () =>
        {
            if (round is not (1 or 2) || !ValidTiming(request)) return BadRequest("Invalid feedback acknowledgement.");
            var answer = await _context.UserAnswers.FirstOrDefaultAsync(a => a.Id == answerId);
            if (answer == null) return NotFound();
            // Valid replays never change the original observation.
            var acknowledged = round == 1 ? answer.FeedbackDisplayAcknowledgedAt : answer.RevisedFeedbackDisplayAcknowledgedAt;
            if (acknowledged != null) return Ok();
            var text = round == 1 ? answer.FeedbackText : answer.RevisedFeedbackText;
            if (!await IsActiveAnswer(answer) || !FeedbackResult.IsUsable(text) ||
                (round == 1 && answer.ChoseToRevise != null) || (round == 2 && answer.RevisedAnswerText == null))
                return Conflict("Feedback has not been produced for this state.");
            if (round == 1)
            {
                answer.FeedbackDisplayAcknowledgedAt = DateTime.UtcNow;
                answer.FeedbackBrowserDisplayedAt = request.BrowserOccurredAt;
                answer.FeedbackWaitMilliseconds = request.WaitMilliseconds;
            }
            else
            {
                answer.RevisedFeedbackDisplayAcknowledgedAt = DateTime.UtcNow;
                answer.RevisedFeedbackBrowserDisplayedAt = request.BrowserOccurredAt;
                answer.RevisedFeedbackWaitMilliseconds = request.WaitMilliseconds;
            }
            await _context.SaveChangesAsync();
            return Ok();
        });

    [HttpPut("answers/{answerId:int}/revision-editor/shown")]
    public async Task<IActionResult> AcknowledgeRevisionEditor(int answerId, [FromBody] FlowTimingRequest request) =>
        await ExecuteAnswerAction(async () =>
        {
            if (!ValidTiming(request)) return BadRequest("Invalid browser timing.");
            var answer = await _context.UserAnswers.FirstOrDefaultAsync(a => a.Id == answerId);
            if (answer == null) return NotFound();
            if (answer.RevisionEditorBrowserDisplayedAt != null) return Ok();
            if (!await IsActiveAnswer(answer) || answer.ChoseToRevise != true || answer.RevisedAnswerText != null)
                return Conflict("Revision editor is not active.");
            answer.RevisionEditorBrowserDisplayedAt = request.BrowserOccurredAt;
            await _context.SaveChangesAsync();
            return Ok();
        });

    [HttpPut("answers/{answerId:int}/continue")]
    public async Task<IActionResult> ContinueAfterRevision(int answerId, [FromBody] FlowTimingRequest request) =>
        await ExecuteAnswerAction(async () =>
        {
            if (!ValidTiming(request)) return BadRequest("Invalid browser timing.");
            var answer = await _context.UserAnswers.FirstOrDefaultAsync(a => a.Id == answerId);
            if (answer == null) return NotFound();
            if (answer.StageCompletedAt != null) return Ok();
            if (!await IsActiveAnswer(answer) || answer.ChoseToRevise != true || answer.RevisedAnswerText == null ||
                answer.RevisedFeedbackDisplayAcknowledgedAt == null || !FeedbackResult.IsUsable(answer.RevisedFeedbackText))
                return Conflict("Read the revised feedback before continuing.");
            answer.StageCompletedAt = DateTime.UtcNow;
            answer.RevisedFeedbackReadingMilliseconds = request.ElapsedMilliseconds;
            answer.StageElapsedMilliseconds = request.StageElapsedMilliseconds;
            var session = await _context.InterviewSessions.FindAsync(answer.InterviewSessionId);
            if (answer.StageNumber == 7) session!.FinishedAt ??= answer.StageCompletedAt;
            await CompleteStudyIfReady(answer.InterviewSessionId);
            await _context.SaveChangesAsync();
            return Ok();
        });
}
