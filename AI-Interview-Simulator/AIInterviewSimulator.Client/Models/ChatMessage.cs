namespace AIInterviewSimulator.Client.Models;

public class ChatMessage
{
    public string Text { get; set; } = string.Empty;

    public bool IsUser { get; set; }
}