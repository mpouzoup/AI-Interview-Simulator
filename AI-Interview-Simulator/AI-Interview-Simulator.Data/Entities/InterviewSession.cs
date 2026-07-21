using System;
using System.Collections.Generic;

namespace AI_Interview_Simulator.Data.Entities;

public class InterviewSession
{
    public Guid Id { get; set; } = Guid.NewGuid();
    
    public int UserId { get; set; }
    public User User { get; set; }

    public string Condition { get; set; } 

    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? FinishedAt { get; set; }

    public List<UserAnswer> Answers { get; set; } = new();
}