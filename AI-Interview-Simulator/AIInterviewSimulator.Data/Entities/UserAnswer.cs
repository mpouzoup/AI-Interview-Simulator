using System;
using System.Collections.Generic;

namespace AIInterviewSimulator.Data.Entities;

public class UserAnswer
{
    public int Id { get; set; }
    
    public Guid InterviewSessionId { get; set; }
    public InterviewSession InterviewSession { get; set; }

    public int StageNumber { get; set; } 
    public string StageName { get; set; }

    public string QuestionText { get; set; }
    public string AnswerText { get; set; }

    public DateTime QuestionShownAt { get; set; }
    public DateTime AnswerSubmittedAt { get; set; }

    public double ResponseLatencyInSeconds => (AnswerSubmittedAt - QuestionShownAt).TotalSeconds;
}