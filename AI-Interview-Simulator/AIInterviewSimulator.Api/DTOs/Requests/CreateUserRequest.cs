namespace AIInterviewSimulator.Api.DTOs.Requests;

public class CreateUserRequest
{
    public string Nickname { get; set; } = string.Empty;
    public int Age { get; set; }
    public string Gender { get; set; } = string.Empty;
    public string AiFamiliarityLevel { get; set; } = string.Empty;
}