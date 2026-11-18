using System.Security.Claims;
using FlowBlox.Core.Authentication;
using FlowBlox.Core.Models.WebServer;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace FlowBlox.WebServer.Services;

public static class DashboardEndpoints
{

    public static void MapDashboardEndpoints(this WebApplication app)
    {
        app.MapPost("/session/login", LoginAsync).RequireRateLimiting("login");
        app.MapPost("/session/logout", async (HttpContext context, IAntiforgery antiforgery, WebSessions sessions) =>
        {
            if (!await ValidateAntiforgeryAsync(context, antiforgery)) return Results.BadRequest();
            sessions.Remove(context.User);
            await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.LocalRedirect("/login");
        }).RequireAuthorization();
        app.MapGet("/download/{projectId}", DownloadAsync).RequireAuthorization();
        app.MapGet("/logs/{executionId:guid}", (Guid executionId, DashboardService dashboard) =>
        {
            var entry = dashboard.GetExecution(executionId);
            var path = entry.Response?.LogfilePath;
            return !string.IsNullOrWhiteSpace(path) && File.Exists(path)
                ? Results.File(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite), "text/plain", Path.GetFileName(path))
                : Results.NotFound();
        }).RequireAuthorization();
    }

    private static async Task<IResult> LoginAsync(HttpContext context, IAntiforgery antiforgery, WebSessions sessions)
    {
        if (!await ValidateAntiforgeryAsync(context, antiforgery)) return Results.BadRequest();
        var form = await context.Request.ReadFormAsync();
        var password = form["password"].ToString();
        if (!sessions.VerifyPassword(password))
            return Results.LocalRedirect("/login?invalid=true");
        var expires = DateTimeOffset.UtcNow.AddHours(8);
        var identity = new ClaimsIdentity([new Claim(ClaimTypes.Name, "FlowBlox"), new Claim(WebSessions.ClaimName, sessions.Create(expires))], CookieAuthenticationDefaults.AuthenticationScheme);
        await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity), new AuthenticationProperties
        {
            IsPersistent = false,
            ExpiresUtc = expires
        });
        return Results.LocalRedirect("/");
    }

    private static async Task<IResult> DownloadAsync(string projectId, string path, Guid? executionId, DashboardService dashboard)
    {
        try
        {
            var artifacts = await dashboard.ArtifactsAsync(projectId, executionId);
            return Results.File(artifacts.OpenRead(path), "application/octet-stream", Path.GetFileName(path), enableRangeProcessing: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or KeyNotFoundException)
        {
            return Results.NotFound();
        }
    }

    private static async Task<bool> ValidateAntiforgeryAsync(HttpContext context, IAntiforgery antiforgery)
    {
        try { await antiforgery.ValidateRequestAsync(context); return true; }
        catch (AntiforgeryValidationException) { return false; }
    }
}
