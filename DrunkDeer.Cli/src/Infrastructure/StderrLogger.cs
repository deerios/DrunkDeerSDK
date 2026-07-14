using Microsoft.Extensions.Logging;

namespace DrunkDeer.Cli.Infrastructure;

/// <summary>
/// Minimal logger provider that writes SDK diagnostics to stderr, keeping stdout clean for
/// command output (crucial for <c>--json</c> piping). Enabled by <c>-v</c> / <c>-vv</c>.
/// </summary>
public sealed class StderrLoggerProvider(LogLevel minLevel) : ILoggerProvider
{
	private readonly TextWriter _err = Console.Error;

	public ILogger CreateLogger(string categoryName) => new StderrLogger(categoryName, minLevel, _err);
	public void Dispose() { }

	private sealed class StderrLogger(string category, LogLevel minLevel, TextWriter err) : ILogger
	{
		public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
		public bool IsEnabled(LogLevel logLevel) => logLevel >= minLevel && logLevel != LogLevel.None;

		public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
			Func<TState, Exception?, string> formatter)
		{
			if (!IsEnabled(logLevel))
				return;
			var shortCat = category.Contains('.') ? category[(category.LastIndexOf('.') + 1)..] : category;
			err.WriteLine($"{logLevel.ToString().ToLowerInvariant()}: {shortCat}: {formatter(state, exception)}");
			if (exception is not null)
				err.WriteLine(exception);
		}
	}

	/// <summary>Builds a factory for the given verbosity (0 = off, 1 = Debug, 2+ = Trace), or null when off.</summary>
	public static ILoggerFactory? ForVerbosity(int verbosity) => verbosity switch
	{
		<= 0 => null,
		1 => LoggerFactory.Create(b => b.ClearProviders().AddProvider(new StderrLoggerProvider(LogLevel.Debug))),
		_ => LoggerFactory.Create(b => b.ClearProviders().AddProvider(new StderrLoggerProvider(LogLevel.Trace))),
	};
}
