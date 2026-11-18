using System.Net;
using FlowBlox.Core.Constants;
using FlowBlox.Core.Util;
using FlowBlox.Core.Validations;
using FlowBlox.WebServer.Components;
using FlowBlox.WebServer.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using System.Threading.RateLimiting;

var dataArgument = Array.IndexOf(args, "--data-directory");
if (dataArgument >= 0 && dataArgument + 1 < args.Length)
    GlobalPaths.UseUserDataDirectory(args[dataArgument + 1]);

using var instance = new Mutex(true, @"Global\FlowBlox.WebServer." + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(GlobalPaths.UserDataDirectory.ToUpperInvariant()))), out var firstInstance);
if (!firstInstance) return;
try
{
    var configuration = WebServerConfigurationStore.Load();
    var errors = FlowBloxComponentValidator.Validate(configuration);
    if (errors.Count > 0)
        throw new InvalidOperationException(string.Join(Environment.NewLine, errors));

    var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = args, ContentRootPath = AppContext.BaseDirectory });
    builder.Logging.ClearProviders();
    builder.Logging.AddProvider(new ServerFileLoggerProvider());
    builder.WebHost.UseStaticWebAssets();
    builder.WebHost.ConfigureKestrel(options =>
    {
        options.AddServerHeader = false;
        options.Limits.MaxRequestBodySize = 1024 * 1024;
        options.Listen(IPAddress.Parse(configuration.BindAddress), configuration.Port, endpoint =>
        {
            if (!string.IsNullOrWhiteSpace(configuration.CertificateFile))
                endpoint.UseHttps(configuration.CertificateFile, configuration.CertificatePassword);
        });
    });
    builder.Services.AddSingleton(configuration);
    builder.Services.AddSingleton<DashboardService>();
    builder.Services.AddSingleton<WebSessions>();
    builder.Services.AddScoped<Microsoft.AspNetCore.Components.Authorization.AuthenticationStateProvider, WebAuthenticationStateProvider>();
    builder.Services.AddHostedService<DashboardRefreshService>();
    builder.Services.AddLocalization(options => options.ResourcesPath = "Resources");
    builder.Services.AddRazorComponents().AddInteractiveServerComponents();
    builder.Services.AddCascadingAuthenticationState();
    builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(options =>
    {
        options.LoginPath = "/login";
        options.Events.OnValidatePrincipal = context =>
        {
            if (!context.HttpContext.RequestServices.GetRequiredService<WebSessions>().IsValid(context.Principal!)) context.RejectPrincipal();
            return Task.CompletedTask;
        };
        options.Cookie.Name = "FlowBlox.WebServer";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = false;
    });
    builder.Services.AddAuthorization();
    builder.Services.AddDataProtection().SetApplicationName("FlowBlox.WebServer")
        .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(GlobalPaths.UserDataDirectory, "webserver", "keys")))
        .ProtectKeysWithDpapi();
    builder.Services.AddRateLimiter(options =>
    {
        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        options.AddPolicy("login", context => RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "local",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    });

    var app = builder.Build();
    app.UseExceptionHandler("/error");
    app.UseRequestLocalization(new RequestLocalizationOptions().SetDefaultCulture("en").AddSupportedCultures("en", "de")
        .AddSupportedUICultures("en", "de"));
    app.Use(async (context, next) =>
    {
        context.Response.Headers.XContentTypeOptions = "nosniff";
        context.Response.Headers.XFrameOptions = "DENY";
        context.Response.Headers["Referrer-Policy"] = "no-referrer";
        context.Response.Headers.CacheControl = "no-cache, no-store";
        await next();
    });
    app.Use(async (context, next) =>
    {
        if (string.IsNullOrEmpty(configuration.AppPassword))
            context.User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(
                [new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Name, "FlowBlox")], "FlowBlox.Local"));
        await next();
    });
    app.UseAuthentication();
    app.UseAuthorization();
    app.UseRateLimiter();
    app.UseAntiforgery();
    app.MapStaticAssets();
    app.MapDashboardEndpoints();
    app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
    app.Lifetime.ApplicationStarted.Register(WebServerProcess.RecordCurrentProcess);
    app.Run();
}
catch (Exception exception)
{
    var directory = Path.Combine(GlobalPaths.UserDataDirectory, "webserver");
    Directory.CreateDirectory(directory);
    File.AppendAllText(Path.Combine(directory, "startup.log"), DateTime.UtcNow.ToString("O") + " " + exception + Environment.NewLine);
    throw;
}
