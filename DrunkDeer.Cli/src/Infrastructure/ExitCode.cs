namespace DrunkDeer.Cli.Infrastructure;

/// <summary>
/// Stable, documented process exit codes. Scripts depend on these — do not renumber.
/// </summary>
public static class ExitCode
{
	public const int Ok = 0;
	public const int RuntimeFailure = 1;
	public const int UsageError = 2;
	public const int NoDevice = 3;
	public const int CapabilityUnsupported = 4;
	public const int Aborted = 5;
	public const int DeviceBusy = 6;
}

/// <summary>
/// Thrown by command handlers to abort with a specific exit code and a user-facing message.
/// The root command's error boundary renders it (respecting --json / --quiet) and returns the code.
/// </summary>
public sealed class CliException(int exitCode, string message) : Exception(message)
{
	public int Code { get; } = exitCode;

	public static CliException NoDevice(string message) => new(ExitCode.NoDevice, message);
	public static CliException Unsupported(string message) => new(ExitCode.CapabilityUnsupported, message);
	public static CliException Usage(string message) => new(ExitCode.UsageError, message);
	public static CliException Aborted(string message = "Aborted.") => new(ExitCode.Aborted, message);
	public static CliException Busy(string message) => new(ExitCode.DeviceBusy, message);
}
