using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace PocoUnlockAssistant.Infrastructure.Logging;

/// <summary>Append-only daily log file; one of the execution-log requirements of the spec.</summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly string _directory;
    private readonly ConcurrentDictionary<string, FileLogger> _loggers = new();
    private readonly object _writeGate = new();

    public FileLoggerProvider(string directory)
    {
        _directory = directory;
        Directory.CreateDirectory(directory);
    }

    public ILogger CreateLogger(string categoryName) =>
        _loggers.GetOrAdd(categoryName, name => new FileLogger(name, this));

    public void Dispose() => _loggers.Clear();

    internal void Write(string line)
    {
        lock (_writeGate)
        {
            try
            {
                var path = Path.Combine(_directory, $"assistant-{DateTimeOffset.Now:yyyyMMdd}.log");
                File.AppendAllText(path, line + Environment.NewLine);
            }
            catch (IOException)
            {
                // Logging must never take the application down.
            }
        }
    }

    private sealed class FileLogger(string category, FileLoggerProvider provider) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            var message = formatter(state, exception);
            var suffix = exception is null ? string.Empty : $" | {exception.GetType().Name}: {exception.Message}";
            provider.Write($"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff} [{logLevel}] {category}: {message}{suffix}");
        }
    }
}
