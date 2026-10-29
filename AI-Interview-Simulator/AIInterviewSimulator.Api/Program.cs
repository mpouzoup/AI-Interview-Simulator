using AIInterviewSimulator.Api;
using AIInterviewSimulator.Data.Context;
using AIInterviewSimulator.Api.Security;
using AIInterviewSimulator.Engine.Engines;
using AIInterviewSimulator.Engine.Feedback;
using AIInterviewSimulator.Engine.Managers;
using AIInterviewSimulator.Engine.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
var localTest = LocalTestMode.Configure(builder);

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
        if (builder.Environment.IsDevelopment()) policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader();
        else
        {
            var origins = builder.Configuration.GetSection("Security:AllowedOrigins").Get<string[]>() ?? Array.Empty<string>();
            if (origins.Length > 0) policy.WithOrigins(origins).AllowAnyMethod().AllowAnyHeader();
        }
    });
});

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddScoped<ScriptBasedEngine>();
builder.Services.AddScoped<InterviewManager>();
builder.Services.AddScoped<ScriptFeedbackEngine>();
builder.Services.AddScoped<LlmFeedbackEngine>();
builder.Services.AddScoped<FeedbackManager>();
var geminiClient = builder.Services.AddHttpClient<GeminiService>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
});

if (localTest) geminiClient.ConfigurePrimaryHttpMessageHandler(() => new LocalTestMode.StubHandler());

builder.Services.AddParticipantSecurity();
builder.Services.AddControllers(o => o.Filters.Add<ParticipantAccessFilter>());
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

if (localTest)
{
    app.Use(async (context, next) =>
    {
        if (!LocalTestMode.IsLoopback(context)) { context.Response.StatusCode = 403; return; }
        await next();
    });
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.MigrateAsync();
    app.Logger.LogWarning("LOCAL TEST ONLY: isolated SQLite; synthetic consent; Gemini STUB; no research collection. API http://127.0.0.1:15055");
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors("AllowAll");

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();
