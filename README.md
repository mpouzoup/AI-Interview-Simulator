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