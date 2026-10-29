using System.Globalization;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AIInterviewSimulator.Api.Security;
using AIInterviewSimulator.Data.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AIInterviewSimulator.Api.Controllers;

public sealed record QuestionnaireItemSpec(string Code, string Text, double Minimum, double Maximum, double Step);
public sealed record QuestionnaireValue(string ItemCode, double Value);
public sealed class QuestionnaireSubmission
{
    public Guid SubmissionId { get; set; }
    public List<QuestionnaireValue> Answers { get; set; } = new();
}

public partial class InterviewController
{
    [HttpGet("study")]
    public IActionResult StudyInformation()
    {
        Response.Headers.CacheControl = "no-store";
        return Ok(new { enabled = _study.Enabled, testMode = _study.IsLocalTest, version = _study.Enabled ? _study.Version : "",
            text = _study.Enabled ? _study.Text : "", textHash = _study.Enabled ? _study.TextHash : "" });
    }

    [HttpGet("participant")]
    public async Task<IActionResult> ParticipantProfile()
    {
        var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!, CultureInfo.InvariantCulture);
        var user = await _context.Users.AsNoTracking().SingleAsync(u => u.Id == userId);
        var session = await _context.InterviewSessions.AsNoTracking().SingleOrDefaultAsync(s => s.UserId == userId);
        Response.Headers.CacheControl = "no-store";
        return Ok(new { userId, user.Nickname, sessionId = session?.Id, condition = session?.Condition, flowVersion = session?.FlowVersion });
    }

    [HttpPost("participant/revoke")]
    public async Task<IActionResult> RevokeParticipant()
    {
        var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!, CultureInfo.InvariantCulture);
        await _context.ParticipantCredentials.Where(c => c.UserId == userId && c.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.RevokedAt, DateTime.UtcNow));
        return Ok();
    }

    private static QuestionnaireItemSpec[] ReadItems(string json)
    {
        var items = JsonSerializer.Deserialize<QuestionnaireItemSpec[]>(json) ?? Array.Empty<QuestionnaireItemSpec>();
        if (items.Length is < 1 or > 100 || items.Select(i => i.Code).Distinct(StringComparer.Ordinal).Count() != items.Length ||
            items.Any(i => string.IsNullOrWhiteSpace(i.Text) || !Regex.IsMatch(i.Code ?? "", @"^[a-zA-Z0-9_.-]{1,64}$") ||
                !double.IsFinite(i.Minimum) || !double.IsFinite(i.Maximum) || !double.IsFinite(i.Step) || i.Minimum >= i.Maximum || i.Step <= 0))
            throw new InvalidOperationException("Invalid approved questionnaire definition.");
        return items;
    }

    private async Task SnapshotQuestionnaires(InterviewSession session)
    {
        if (!_study.QuestionnairesEnabled) return;
        var definitions = await _context.QuestionnaireDefinitions.AsNoTracking().Where(d => d.Approved).ToListAsync();
        if (definitions.GroupBy(d => new { d.Code, d.Phase }).Any(g => g.Count() > 1))
            throw new InvalidOperationException("Only one approved version per instrument and phase can be assigned.");
        foreach (var definition in definitions)
        {
            if (definition.Phase is not ("pre" or "post") || string.IsNullOrWhiteSpace(definition.Code) || string.IsNullOrWhiteSpace(definition.Version))
                throw new InvalidOperationException("Invalid approved questionnaire phase/version.");
            ReadItems(definition.ItemsJson);
            _context.QuestionnaireAdministrations.Add(new QuestionnaireAdministration
            {
                InterviewSessionId = session.Id, UserId = session.UserId, Condition = session.Condition,
                Code = definition.Code, Version = definition.Version, Phase = definition.Phase, Title = definition.Title,
                Instructions = definition.Instructions, Required = definition.Required, ItemsSnapshotJson = definition.ItemsJson
            });
        }
    }

    private Task<bool> HasPendingQuestionnaires(Guid sessionId, string phase) => _context.QuestionnaireAdministrations
        .AnyAsync(a => a.InterviewSessionId == sessionId && a.Phase == phase && a.Required && a.CompletedAt == null);

    private async Task CompleteStudyIfReady(Guid sessionId)
    {
        var session = await _context.InterviewSessions.FindAsync(sessionId);
        if (session is { FlowVersion: >= 2, FinishedAt: not null } && !await _context.QuestionnaireAdministrations.AnyAsync(a =>
            a.InterviewSessionId == sessionId && a.Required && a.CompletedAt == null))
            session.StudyCompletedAt ??= DateTime.UtcNow;
    }

    [HttpGet("sessions/{sessionId:guid}/questionnaires/{phase}")]
    public async Task<IActionResult> GetQuestionnaires(Guid sessionId, string phase)
    {
        if (phase is not ("pre" or "post")) return BadRequest("Invalid phase.");
        var session = await _context.InterviewSessions.FindAsync(sessionId);
        if (session == null) return NotFound();
        if (phase == "post" && session.FinishedAt == null) return Conflict("The interview is not finished.");
        var administrations = await _context.QuestionnaireAdministrations.AsNoTracking()
            .Where(a => a.InterviewSessionId == sessionId && a.Phase == phase).OrderBy(a => a.Code).ToListAsync();
        return Ok(administrations.Select(a => new { a.Id, a.Code, a.Version, a.Phase, a.Title, a.Instructions, a.Required,
            a.CompletedAt, items = ReadItems(a.ItemsSnapshotJson) }));
    }

    [HttpPut("questionnaires/{administrationId:guid}")]
    public async Task<IActionResult> SubmitQuestionnaire(Guid administrationId, [FromBody] QuestionnaireSubmission request) =>
        await ExecuteAnswerAction(async () =>
        {
            if (request.SubmissionId == Guid.Empty || request.Answers == null || request.Answers.Any(a => a == null)) return BadRequest("Invalid submission.");
            var administration = await _context.QuestionnaireAdministrations.FirstOrDefaultAsync(a => a.Id == administrationId);
            if (administration == null) return NotFound();
            var items = ReadItems(administration.ItemsSnapshotJson);
            if (request.Answers.Count != items.Length || request.Answers.Select(a => a.ItemCode).Distinct(StringComparer.Ordinal).Count() != items.Length)
                return BadRequest("Each assigned item requires exactly one response.");
            foreach (var answer in request.Answers)
            {
                var item = items.SingleOrDefault(i => i.Code == answer.ItemCode);
                if (item == null || !double.IsFinite(answer.Value) || answer.Value < item.Minimum || answer.Value > item.Maximum ||
                    Math.Abs((answer.Value - item.Minimum) / item.Step - Math.Round((answer.Value - item.Minimum) / item.Step)) > 0.000001)
                    return BadRequest("Response outside the assigned scale.");
            }
            var hash = StudyPolicy.Hash(JsonSerializer.Serialize(request.Answers.OrderBy(a => a.ItemCode, StringComparer.Ordinal)));
            if (administration.CompletedAt != null)
                return administration.SubmissionId == request.SubmissionId && administration.SubmissionHash == hash ? Ok() : Conflict("Questionnaire already submitted.");
            var session = await _context.InterviewSessions.FindAsync(administration.InterviewSessionId);
            if (session == null || (administration.Phase == "post" && session.FinishedAt == null) ||
                (administration.Phase == "pre" && await _context.UserAnswers.AnyAsync(a => a.InterviewSessionId == session.Id)))
                return Conflict("Questionnaire not allowed in this phase.");
            var now = DateTime.UtcNow;
            foreach (var answer in request.Answers) _context.QuestionnaireResponses.Add(new QuestionnaireResponse
                { AdministrationId = administration.Id, ItemCode = answer.ItemCode, Value = answer.Value, SubmittedAt = now });
            administration.SubmissionId = request.SubmissionId; administration.SubmissionHash = hash; administration.CompletedAt = now;
            await _context.SaveChangesAsync();
            await CompleteStudyIfReady(administration.InterviewSessionId);
            await _context.SaveChangesAsync();
            return Ok();
        });

    [HttpGet("export/questionnaires/csv")]
    public async Task<IActionResult> ExportQuestionnaires()
    {
        if (string.IsNullOrWhiteSpace(_exportKey) || _exportKey.Length < 32) return StatusCode(500);
        if (!Request.Headers.TryGetValue("X-Export-Key", out var key) || key.Count != 1 || !IsValidExportKey(key[0])) return Unauthorized();
        var rows = await (from a in _context.QuestionnaireAdministrations.AsNoTracking()
                          join r in _context.QuestionnaireResponses.AsNoTracking() on a.Id equals r.AdministrationId
                          orderby a.InterviewSessionId, a.Phase, a.Code, r.ItemCode
                          select new { a.InterviewSessionId, a.UserId, a.Condition, a.Code, a.Version, a.Phase, a.CompletedAt, r.ItemCode, r.Value, r.SubmittedAt }).ToListAsync();
        var csv = new StringBuilder("SessionId,UserId,Condition,Instrument,Version,Phase,CompletedAt,Item,Value,SubmittedAt\n");
        foreach (var r in rows) csv.AppendLine(string.Join(",", new[] { r.InterviewSessionId.ToString(), r.UserId.ToString(CultureInfo.InvariantCulture),
            r.Condition, r.Code, r.Version, r.Phase, r.CompletedAt?.ToString("o"), r.ItemCode, r.Value.ToString("R", CultureInfo.InvariantCulture), r.SubmittedAt.ToString("o") }.Select(EscapeCsv)));
        Response.Headers.CacheControl = "no-store";
        return File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray(), "text/csv; charset=utf-8", "questionnaires.csv");
    }
}
