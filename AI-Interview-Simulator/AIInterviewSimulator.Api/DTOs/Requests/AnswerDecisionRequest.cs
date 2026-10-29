namespace AIInterviewSimulator.Api.DTOs.Requests;

public class AnswerDecisionRequest
{
    public FlowTimingRequest? Timing { get; set; }
    public bool ChoseToRevise { get; set; }
}