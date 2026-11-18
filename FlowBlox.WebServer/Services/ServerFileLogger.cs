namespace FlowBlox.WebServer.Services;

public sealed class ServerFileLogger(ServerFileLoggerProvider provider, string category) : ILogger
{
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel level) => level >= (category.StartsWith("Microsoft.", StringComparison.Ordinal) ? LogLevel.Warning : LogLevel.Information);
    public void Log<TState>(LogLevel level, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(level)) return;
        lock (provider.Sync)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(provider.Path)!);
                if (File.Exists(provider.Path) && new FileInfo(provider.Path).Length > 2 * 1024 * 1024)
                    File.Move(provider.Path, provider.Path + ".previous", overwrite: true);
                File.AppendAllText(provider.Path, $"{DateTime.UtcNow:O} {level} {category}: {formatter(state, exception)} {exception}{Environment.NewLine}");
            }
            catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
            { System.Diagnostics.Debug.WriteLine("Web server file logging failed: " + failure.Message); }
        }
    }
}
