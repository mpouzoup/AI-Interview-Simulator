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
    public string AnswerText { get; set; } = string.Empty;

    public DateTime QuestionShownAt { get; set; }
    public DateTime AnswerSubmittedAt { get; set; }

    public string? FeedbackText { get; set; }
    public DateTime? FeedbackShownAt { get; set; }

    public bool? ChoseToRevise { get; set; }
    public DateTime? RevisionDecisionAt { get; set; }
    
    public string? RevisedAnswerText { get; set; }
    public DateTime? RevisionSubmittedAt { get; set; }

    public double ResponseLatencyInSeconds => (AnswerSubmittedAt - QuestionShownAt).TotalSeconds;
    
    public double? RevisionLatencyInSeconds =>
        RevisionSubmittedAt.HasValue && RevisionDecisionAt.HasValue
            ? (RevisionSubmittedAt.Value - RevisionDecisionAt.Value).TotalSeconds
            : null;
}