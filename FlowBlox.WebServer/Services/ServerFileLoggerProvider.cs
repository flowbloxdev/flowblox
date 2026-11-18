using FlowBlox.Core.Constants;

namespace FlowBlox.WebServer.Services;

public sealed class ServerFileLoggerProvider : ILoggerProvider
{
    internal object Sync { get; } = new();
    internal string Path { get; } = System.IO.Path.Combine(GlobalPaths.UserDataDirectory, "webserver", "server.log");
    public ILogger CreateLogger(string categoryName) => new ServerFileLogger(this, categoryName);
    public void Dispose() { }
}
