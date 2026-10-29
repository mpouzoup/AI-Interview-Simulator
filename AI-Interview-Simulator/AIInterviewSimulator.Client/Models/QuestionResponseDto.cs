namespace AIInterviewSimulator.Client.Models;

public class QuestionResponseDto
{
    public int FlowVersion { get; set; }
    public string? FlowState { get; set; }
    public int Round { get; set; }
    public string? AnswerText { get; set; }
    public string? RevisedAnswerText { get; set; }

    public int StageNumber { get; set; }

    public string QuestionText { get; set; } = string.Empty;

    public string? Feedback { get; set; }
    public int? AnswerId { get; set; }
    public string? FeedbackStatus { get; set; }
    public string? FeedbackErrorCode { get; set; }

    public DateTime QuestionShownAt { get; set; }

    public bool IsFinished { get; set; }
}