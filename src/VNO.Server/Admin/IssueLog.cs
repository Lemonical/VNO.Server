using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;

namespace VNO.Server.Admin;

/// <summary>
/// Logger provider that captures warning and error records for the console
/// </summary>
/// <remarks>
/// Registered next to the console logger in the logging builder. Keeps a bounded
/// list so a chatty failure cannot grow memory without limit
/// </remarks>
public sealed class IssueLog : IIssueLog, ILoggerProvider
{
    private const int MaxIssues = 200;

    private readonly object _gate = new();
    private readonly List<IssueEntry> _issues = new();

    /// <inheritdoc />
    public IReadOnlyList<IssueEntry> Issues
    {
        get
        {
            lock (_gate)
            {
                return _issues.ToArray();
            }
        }
    }

    /// <inheritdoc />
    public event EventHandler<IssueEntry>? IssueRaised;

    /// <inheritdoc />
    public ILogger CreateLogger(string categoryName) => new IssueLogger(this, categoryName);

    /// <inheritdoc />
    public void Dispose()
    {
    }

    private void Capture(IssueEntry entry)
    {
        lock (_gate)
        {
            _issues.Add(entry);
            while (_issues.Count > MaxIssues)
            {
                _issues.RemoveAt(0);
            }
        }
        IssueRaised?.Invoke(this, entry);
    }

    private sealed class IssueLogger : ILogger
    {
        private readonly IssueLog _owner;
        private readonly string _category;

        public IssueLogger(IssueLog owner, string category)
        {
            _owner = owner;
            _category = category;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;

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

            var level = logLevel >= LogLevel.Error ? IssueLevel.Error : IssueLevel.Warning;
            var detail = exception is null
                ? _category
                : $"{_category}{Environment.NewLine}{exception.GetType().Name}: {exception.Message}";
            _owner.Capture(new IssueEntry(
                DateTimeOffset.Now, level, formatter(state, exception), detail));
        }
    }
}
