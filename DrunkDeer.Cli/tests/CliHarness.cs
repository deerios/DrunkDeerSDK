using System.Text.Json;
using DrunkDeer.Cli;

namespace DrunkDeer.Cli.Tests;

/// <summary>
/// Drives the full command tree against the built-in simulator (via <c>--demo</c>), capturing stdout
/// and the exit code. Console redirection is process-global, so these tests must run non-parallel
/// (the default for this assembly).
/// </summary>
public sealed record CliResult(int ExitCode, string Stdout, string Stderr)
{
	public JsonElement Json => JsonDocument.Parse(Stdout).RootElement;
}

public static class CliHarness
{
	public static CliResult Run(params string[] args)
	{
		var prevOut = Console.Out;
		var prevErr = Console.Error;
		var prevNoColor = Environment.GetEnvironmentVariable("NO_COLOR");
		Environment.SetEnvironmentVariable("NO_COLOR", "1");

		var outWriter = new StringWriter();
		var errWriter = new StringWriter();
		Console.SetOut(outWriter);
		Console.SetError(errWriter);
		try
		{
			int code = CliApp.InvokeAsync(args).GetAwaiter().GetResult();
			return new CliResult(code, outWriter.ToString(), errWriter.ToString());
		}
		finally
		{
			Console.SetOut(prevOut);
			Console.SetError(prevErr);
			Environment.SetEnvironmentVariable("NO_COLOR", prevNoColor);
		}
	}
}
