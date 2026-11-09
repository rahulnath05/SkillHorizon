using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace SkillHorizon.Framework.Tests;

public sealed class LogCapture
{
    public ConcurrentQueue<string> Messages { get; } = new();
}

public sealed class CaptureLoggerProvider : ILoggerProvider
{
    private readonly LogCapture _capture;

    public CaptureLoggerProvider(LogCapture capture)
    {
        _capture = capture;
    }

    public ILogger CreateLogger(string categoryName) => new CaptureLogger(_capture);

    public void Dispose()
    {
    }

    private sealed class CaptureLogger : ILogger
    {
        private readonly LogCapture _capture;

        public CaptureLogger(LogCapture capture)
        {
            _capture = capture;
        }

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
                return;

            _capture.Messages.Enqueue(formatter(state, exception));
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
