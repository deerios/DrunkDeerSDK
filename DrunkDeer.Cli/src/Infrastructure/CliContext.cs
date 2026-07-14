using DrunkDeer.Protocol;
using Microsoft.Extensions.Logging;

namespace DrunkDeer.Cli.Infrastructure;

/// <summary>Parsed global options, shared by every command handler.</summary>
public sealed class CliOptions
{
	public string? Device { get; init; }
	public bool Json { get; init; }
	public bool AssumeYes { get; init; }
	public bool Quiet { get; init; }
	public int Verbosity { get; init; }
	public int? TimeoutMs { get; init; }
	public bool Demo { get; init; }
	public string? DemoModel { get; init; }
}

/// <summary>
/// Everything a command handler needs: parsed options plus the shared services (output, confirmation,
/// session factory). Built once by the root command; passed into each handler. A test can construct
/// one directly with a stub connection factory to drive handlers end-to-end without hardware.
/// </summary>
public sealed class CliContext : IDisposable
{
	public CliOptions Options { get; }
	public OutputWriter Output { get; }
	public Confirmation Confirm { get; }
	public ILoggerFactory? LoggerFactory { get; }

	/// <summary>
	/// Optional connection override. When set (demo mode, tests) the session factory builds a session
	/// over this connection instead of discovering hardware. The returned connection is owned by the
	/// session and disposed with it.
	/// </summary>
	public Func<IKeyboardConnection>? ConnectionOverride { get; init; }

	public CliContext(CliOptions options, OutputWriter output, Confirmation confirm, ILoggerFactory? loggerFactory)
	{
		Options = options;
		Output = output;
		Confirm = confirm;
		LoggerFactory = loggerFactory;
	}

	/// <summary>Opens a session (real hardware, demo, or injected). Throws <see cref="CliException"/> on failure.</summary>
	public KeyboardSession OpenSession() => SessionFactory.Open(this);

	public void Dispose() => LoggerFactory?.Dispose();
}
