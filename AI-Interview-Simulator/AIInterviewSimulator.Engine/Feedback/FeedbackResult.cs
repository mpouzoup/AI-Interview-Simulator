namespace AIInterviewSimulator.Engine.Feedback;

public enum FeedbackStatus { Success, Unavailable, InvalidResult }

public sealed record FeedbackResult(
    FeedbackStatus Status,
    string? Text = null,
    string? ErrorCode = null,
    string? Model = null,
    int Attempts = 0)
{
    public static bool IsUsable(string? text) =>
        !string.IsNullOrWhiteSpace(text) &&
        text.Trim() != "Η ανατροφοδότηση δεν ήταν διαθέσιμη αυτή τη στιγμή." &&
        text.Trim() != "Δεν παράχθηκε απάντηση.";

    public static FeedbackResult FromText(string? text) => IsUsable(text)
        ? new(FeedbackStatus.Success, text)
        : new(FeedbackStatus.InvalidResult, ErrorCode: "empty_or_unavailable_text");
}
