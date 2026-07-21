using System;
using System.Collections.Generic;

namespace AI_Interview_Simulator.Data.Entities;

public class User
{
    public int Id { get; set; }
    public string Nickname { get; set; } = string.Empty;
    public int Age { get; set; }
    public string Gender { get; set; } = string.Empty;
    public string AiFamiliarityLevel { get; set; } = string.Empty; 
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public List<InterviewSession> Sessions { get; set; } = new();
}