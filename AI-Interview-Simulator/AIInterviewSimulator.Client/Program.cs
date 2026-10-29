using AI_Interview_Simulator;
using AIInterviewSimulator.Client;
using MudBlazor.Services;

var builder = WebApplication.CreateBuilder(args);
var localTest = ParticipantClientSecurity.ConfigureLocalTestClient(builder);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddMudServices();

builder.Services.AddParticipantClientSecurity(builder.Configuration, builder.Environment);

var app = builder.Build();

if (localTest)
{
    app.Use(async (context, next) =>
    {
        if (context.Connection.RemoteIpAddress is not { } address || !System.Net.IPAddress.IsLoopback(address))
        { context.Response.StatusCode = 403; return; }
        await next();
    });
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();
app.MapParticipantGateway();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
