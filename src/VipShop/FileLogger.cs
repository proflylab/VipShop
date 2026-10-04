using System.Collections.Concurrent;
using System.Text;

namespace VipShop;

/// <summary>One log file per day next to the exe (logs/vipshop-YYYYMMDD.log). Never given passwords or tokens.</summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly string _dir;
    private readonly BlockingCollection<string> _queue = new(4096);
    private readonly Thread _writer;

    public FileLoggerProvider(string dir)
    {
        _dir = dir;
        Directory.CreateDirectory(dir);
        _writer = new Thread(Write) { IsBackground = true, Name = "VipShop log" };
        _writer.Start();
    }

    public ILogger CreateLogger(string category) => new FileLogger(this, category);

    internal void Add(string line) => _queue.TryAdd(line);

    private void Write()
    {
        foreach (var line in _queue.GetConsumingEnumerable())
        {
            try
            {
                File.AppendAllText(Path.Combine(_dir, $"vipshop-{DateTime.Now:yyyyMMdd}.log"), line, Encoding.UTF8);
            }
            catch (IOException)
            {
                // the log is best effort; the console still has the line
            }
        }
    }

    public void Dispose()
    {
        _queue.CompleteAdding();
        _writer.Join(2000);
    }

    private sealed class FileLogger(FileLoggerProvider owner, string category) : ILogger
    {
        private readonly string _short = category.Contains('.') ? category[(category.LastIndexOf('.') + 1)..] : category;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel level) => level >= LogLevel.Information &&
            (level >= LogLevel.Warning || !category.StartsWith("Microsoft", StringComparison.Ordinal));

        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? e, Func<TState, Exception?, string> fmt)
        {
            if (!IsEnabled(level)) return;
            var sb = new StringBuilder().Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")).Append(' ')
                .Append(level.ToString().ToUpperInvariant()[..4]).Append(' ').Append(_short).Append(": ")
                .Append(fmt(state, e));
            if (e is not null) sb.Append(" | ").Append(e.GetType().Name).Append(": ").Append(e.Message);
            owner.Add(sb.Append(Environment.NewLine).ToString());
        }
    }
}
