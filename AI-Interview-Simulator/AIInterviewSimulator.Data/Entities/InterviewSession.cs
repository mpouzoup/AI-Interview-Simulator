using System;
using System.Collections.Generic;

namespace AIInterviewSimulator.Data.Entities;

public class InterviewSession
{
    public Guid Id { get; set; } = Guid.NewGuid();
    
    public int UserId { get; set; }
    public User? User { get; set; }

    // Existing sessions retain v1; only the API creates v2 sessions.
    public int FlowVersion { get; set; } = 1;

    public string Condition { get; set; } = string.Empty; 

    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? StudyCompletedAt { get; set; }

    public DateTime? FinishedAt { get; set; }

    public List<UserAnswer> Answers { get; set; } = new();
}