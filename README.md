# Interview Simulator

A web-based interview simulation system developed as part of a university thesis project.

## Overview

The application is designed to support a comparative study of two feedback approaches in a simulated job interview environment.

Both experimental conditions use the same predefined interview questions in order to maintain a controlled and comparable interview structure. The main difference between the conditions is the way feedback is generated.

### Experimental Conditions

- **Condition A – Script-Based Feedback:**  
  Participants receive predefined feedback after each interview response.

- **Condition B – LLM-Based Feedback:**  
  Participants receive dynamically generated and personalized feedback based on their response, using a Large Language Model (LLM).

After receiving feedback, participants can either continue to the next interview question or revise their previous answer.

## Interview Structure

The interview consists of seven predefined stages covering:

1. Self-introduction
2. Motivation for the position and company
3. Self-assessment of skills
4. Teamwork and collaboration
5. Handling difficult or stressful situations
6. Failure and adaptability
7. Professional goals and career development

## Technologies

- .NET 8
- ASP.NET Core Web API
- Blazor
- MudBlazor
- Entity Framework Core
- SQLite
- Google Gemini API

## Project Structure

- `AIInterviewSimulator.Api` – REST API and interview endpoints
- `AIInterviewSimulator.Client` – Blazor user interface
- `AIInterviewSimulator.Data` – Entity Framework Core entities, database context and migrations
- `AIInterviewSimulator.Engine` – Interview logic, feedback engines and Gemini integration

## Data Collection

The system records interaction data required for the experimental study, including interview responses, response timing, feedback, revision decisions and revised responses.

## Status

The application is currently under development and testing as part of the thesis research.

## How to Run

### Prerequisites

Make sure the following are installed:

- .NET 8 SDK or newer
- Git
- A Google Gemini API key for Condition B

### 1. Clone the repository

```bash
git clone https://github.com/mpouzoup/AI-Interview-Simulator.git
cd AI-Interview-Simulator/AI-Interview-Simulator
## How to Run

### Prerequisites

Make sure the following are installed:

- .NET 8 SDK or newer
- Git
- A Google Gemini API key for Condition B

### 1. Clone the repository

```bash
git clone https://github.com/mpouzoup/AI-Interview-Simulator.git
cd AI-Interview-Simulator/AI-Interview-Simulator
```

### 2. Configure the Gemini API key

The Gemini API key is stored using .NET User Secrets and is not included in the repository.

From the project root, run:

```bash
dotnet user-secrets set "GeminiApiKey" "YOUR_API_KEY" --project AIInterviewSimulator.Api/AIInterviewSimulator.Api.csproj
```

Condition A can run without a Gemini API key.

Condition B requires a valid Gemini API key in order to generate personalized feedback.

### 3. Set up the database

If the Entity Framework Core CLI tools are not installed, install them with:

```bash
dotnet tool install --global dotnet-ef
```

Then apply the database migrations:

```bash
dotnet ef database update --project AIInterviewSimulator.Data/AIInterviewSimulator.Data.csproj --startup-project AIInterviewSimulator.Api/AIInterviewSimulator.Api.csproj
```

### 4. Run the API

From the project root, run:

```bash
dotnet run --project AIInterviewSimulator.Api/AIInterviewSimulator.Api.csproj
```

The API will run at:

```text
http://localhost:5055
```

### 5. Run the Client

Open a second terminal in the project root and run:

```bash
dotnet run --project AIInterviewSimulator.Client/AIInterviewSimulator.Client.csproj
```

The application will run at:

```text
http://localhost:5265
```

Open `http://localhost:5265` in a web browser to use the interview simulator.