namespace AIInterviewSimulator.Api.DTOs.Requests;

public class SubmitAnswerRequest
{
    public Guid SessionId { get; set; }

    public int StageNumber { get; set; }

    public string StageName { get; set; } = string.Empty;

    public string QuestionText { get; set; } = string.Empty;

    public string AnswerText { get; set; } = string.Empty;

    public DateTime QuestionShownAt { get; set; }
}