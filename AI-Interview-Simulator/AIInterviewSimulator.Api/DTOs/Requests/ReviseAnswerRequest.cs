namespace AIInterviewSimulator.Api.DTOs.Requests;

public class ReviseAnswerRequest
{
    public FlowTimingRequest? Timing { get; set; }
    public string RevisedAnswerText { get; set; } = string.Empty;
}