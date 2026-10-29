namespace AIInterviewSimulator.Data.Entities;

// No plaintext participant credential is persisted.
public class ParticipantCredential
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int UserId { get; set; }
    public string TokenHash { get; set; } = "";
    public DateTime IssuedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? RevokedAt { get; set; }
}

public class ConsentRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int UserId { get; set; }
    public string Version { get; set; } = "";
    public string TextHash { get; set; } = "";
    public DateTime AcceptedAt { get; set; }
}

// Empty by default: approved research instruments must be supplied separately.
public class QuestionnaireDefinition
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Code { get; set; } = "";
    public string Version { get; set; } = "";
    public string Phase { get; set; } = "";
    public string Title { get; set; } = "";
    public string Instructions { get; set; } = "";
    public bool Required { get; set; }
    public bool Approved { get; set; }
    public string ItemsJson { get; set; } = "[]";
}

public class QuestionnaireAdministration
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid InterviewSessionId { get; set; }
    public int UserId { get; set; }
    public string Condition { get; set; } = "";
    public string Code { get; set; } = "";
    public string Version { get; set; } = "";
    public string Phase { get; set; } = "";
    public string Title { get; set; } = "";
    public string Instructions { get; set; } = "";
    public bool Required { get; set; }
    public string ItemsSnapshotJson { get; set; } = "[]";
    public DateTime? CompletedAt { get; set; }
    public Guid? SubmissionId { get; set; }
    public string? SubmissionHash { get; set; }
}

public class QuestionnaireResponse
{
    public int Id { get; set; }
    public Guid AdministrationId { get; set; }
    public string ItemCode { get; set; } = "";
    public double Value { get; set; }
    public DateTime SubmittedAt { get; set; }
}
