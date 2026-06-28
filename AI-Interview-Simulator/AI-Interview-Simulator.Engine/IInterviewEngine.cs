namespace AI_Interview_Simulator.Engine;

public interface IInterviewEngine
{
    string GetCurrentPrompt();
    void ProcessUserResponse(string userResponse, double responseLatencySeconds);
    bool IsInterviewFinished();
}