namespace AIInterviewSimulator.Api.DTOs.Requests;

// Browser observations may be unavailable; never substitute server timestamps for them.
public class FlowTimingRequest
{
    public DateTime? BrowserOccurredAt { get; set; }
    public double? ElapsedMilliseconds { get; set; }
    public double? WaitMilliseconds { get; set; }
    public double? StageElapsedMilliseconds { get; set; }
}
