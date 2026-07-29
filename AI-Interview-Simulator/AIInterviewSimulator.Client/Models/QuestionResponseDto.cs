namespace AIInterviewSimulator.Client.Models;

public class QuestionResponseDto
{
    public int StageNumber { get; set; }

    public string QuestionText { get; set; } = string.Empty;

    public string? Feedback { get; set; }

    public DateTime QuestionShownAt { get; set; }

    public bool IsFinished { get; set; }
}