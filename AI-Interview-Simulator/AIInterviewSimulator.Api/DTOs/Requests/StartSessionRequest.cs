namespace AIInterviewSimulator.Api.DTOs.Requests;

public class StartSessionRequest
{
    public int UserId { get; set; }
    public string Condition { get; set; } = string.Empty;
}