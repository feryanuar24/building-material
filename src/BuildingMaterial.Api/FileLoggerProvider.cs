using System.Text;
using Microsoft.Extensions.Logging;

namespace BuildingMaterial.Api;

internal sealed class FileLoggerProvider(string filePath) : ILoggerProvider
{
    private readonly object syncRoot = new();

    public ILogger CreateLogger(string categoryName) =>
        new FileLogger(categoryName, filePath, syncRoot);

    public void Dispose()
    {
    }

    private sealed class FileLogger(
        string categoryName,
        string filePath,
        object syncRoot) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull =>
            NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
                return;

            var message = formatter(state, exception);
            var entry = new StringBuilder()
                .Append(DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss zzz"))
                .Append(" ")
                .Append(logLevel.ToString().ToUpperInvariant())
                .Append(" ")
                .Append(categoryName)
                .Append(": ")
                .Append(message);

            if (exception is not null && !message.Contains(exception.ToString(), StringComparison.Ordinal))
                entry.AppendLine().Append(exception);

            try
            {
                lock (syncRoot)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
                    File.AppendAllText(filePath, entry.AppendLine().ToString(), Encoding.UTF8);
                }
            }
            catch (IOException)
            {
                Console.Error.WriteLine($"Unable to write log entry to '{filePath}'.");
            }
            catch (UnauthorizedAccessException)
            {
                Console.Error.WriteLine($"Unable to write log entry to '{filePath}'.");
            }
        }
    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();

        public void Dispose()
        {
        }
    }
}
