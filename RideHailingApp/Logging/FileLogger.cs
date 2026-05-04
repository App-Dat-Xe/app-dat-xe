using Microsoft.Extensions.Logging;
using System.IO;

namespace RideHailingApp.Logging;

public class FileLogger : ILogger
{
    private readonly string _categoryName;
    private readonly string _filePath;

    public FileLogger(string categoryName, string filePath)
    {
        _categoryName = categoryName;
        _filePath = filePath;
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

    public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Debug;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        try
        {
            var line = $"{DateTime.Now:O} [{logLevel}] {_categoryName}: {formatter(state, exception)}";
            if (exception != null) line += "\n" + exception;
            Directory.CreateDirectory(Path.GetDirectoryName(_filePath) ?? "");
            File.AppendAllText(_filePath, line + "\n");
        }
        catch { }
    }

    private class NullScope : IDisposable
    {
        public static NullScope Instance { get; } = new NullScope();
        public void Dispose() { }
    }
}
