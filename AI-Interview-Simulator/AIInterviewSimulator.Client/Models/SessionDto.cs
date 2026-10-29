namespace AIInterviewSimulator.Client.Models;

public class SessionDto
{
    public int FlowVersion { get; set; }
    public Guid Id { get; set; }
    public string Condition { get; set; } = string.Empty;
}