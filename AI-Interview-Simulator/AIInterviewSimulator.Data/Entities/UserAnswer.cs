using System;
using System.Collections.Generic;

namespace AIInterviewSimulator.Data.Entities;

public class UserAnswer
{
    public int Id { get; set; }
    public Guid SubmissionId { get; set; }
    
    public Guid InterviewSessionId { get; set; }
    public InterviewSession? InterviewSession { get; set; }

    public int StageNumber { get; set; } 
    public string StageName { get; set; } = string.Empty;

    public string QuestionText { get; set; } = string.Empty;
    public string AnswerText { get; set; } = string.Empty;

    public DateTime QuestionShownAt { get; set; }
    public DateTime AnswerSubmittedAt { get; set; }

    public string? FeedbackText { get; set; }
    public DateTime? FeedbackShownAt { get; set; }

    public bool? ChoseToRevise { get; set; }
    public DateTime? RevisionDecisionAt { get; set; }
    
    public string? RevisedAnswerText { get; set; }
    public DateTime? RevisionSubmittedAt { get; set; }

    // V2 only. Browser times are observations, not proof of reading or attention.
    public string? RevisedFeedbackText { get; set; }
    public DateTime? FeedbackReadyAt { get; set; }
    public DateTime? RevisedFeedbackReadyAt { get; set; }
    public DateTime? FeedbackDisplayAcknowledgedAt { get; set; }
    public DateTime? RevisedFeedbackDisplayAcknowledgedAt { get; set; }
    public DateTime? FeedbackBrowserDisplayedAt { get; set; }
    public DateTime? RevisedFeedbackBrowserDisplayedAt { get; set; }
    public DateTime? QuestionBrowserDisplayedAt { get; set; }
    public DateTime? OriginalBrowserSubmittedAt { get; set; }
    public DateTime? RevisionEditorBrowserDisplayedAt { get; set; }
    public DateTime? RevisionBrowserSubmittedAt { get; set; }
    public DateTime? StageCompletedAt { get; set; }
    public DateTime? FeedbackLeaseUntil { get; set; }
    public double? OriginalWritingMilliseconds { get; set; }
    public double? RevisionWritingMilliseconds { get; set; }
    public double? DecisionMilliseconds { get; set; }
    public double? RevisedFeedbackReadingMilliseconds { get; set; }
    public double? StageElapsedMilliseconds { get; set; }
    public double? FeedbackWaitMilliseconds { get; set; }
    public double? RevisedFeedbackWaitMilliseconds { get; set; }
    public double? FeedbackGenerationMilliseconds { get; set; }
    public double? RevisedFeedbackGenerationMilliseconds { get; set; }
    public int? FeedbackGenerationAttempts { get; set; }
    public int? RevisedFeedbackGenerationAttempts { get; set; }
    public int? FeedbackLeaseRound { get; set; }
    public Guid? FeedbackLeaseId { get; set; }

    public double ResponseLatencyInSeconds => (AnswerSubmittedAt - QuestionShownAt).TotalSeconds;
    
    public double? DecisionLatencyInSeconds =>
        RevisionDecisionAt.HasValue && FeedbackShownAt.HasValue
            ? (RevisionDecisionAt.Value - FeedbackShownAt.Value).TotalSeconds
            : null;

    public double? RevisionLatencyInSeconds =>
        RevisionSubmittedAt.HasValue && RevisionDecisionAt.HasValue
            ? (RevisionSubmittedAt.Value - RevisionDecisionAt.Value).TotalSeconds
            : null;
}
