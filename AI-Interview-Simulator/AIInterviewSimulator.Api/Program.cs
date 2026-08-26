using AIInterviewSimulator.Data.Context;
using AIInterviewSimulator.Engine.Engines;
using AIInterviewSimulator.Engine.Feedback;
using AIInterviewSimulator.Engine.Managers;
using AIInterviewSimulator.Engine.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

const int minimumExportKeyLength = 32;
var exportKey = builder.Configuration["Export:Key"];

if (builder.Environment.IsProduction() &&
    (string.IsNullOrWhiteSpace(exportKey) ||
     exportKey.Length < minimumExportKeyLength))
{
    throw new InvalidOperationException(
        $"Export:Key must be configured and contain at least {minimumExportKeyLength} characters.");
}

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
            .AllowAnyMethod()
            .AllowAnyHeader();
    });
});

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddScoped<ScriptBasedEngine>();
builder.Services.AddScoped<InterviewManager>();
builder.Services.AddScoped<ScriptFeedbackEngine>();
builder.Services.AddScoped<LlmFeedbackEngine>();
builder.Services.AddScoped<FeedbackManager>();
builder.Services.AddHttpClient<GeminiService>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
});

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors("AllowAll");

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();

app.Run();
