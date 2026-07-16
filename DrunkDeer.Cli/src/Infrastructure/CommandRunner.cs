using System.CommandLine;

namespace DrunkDeer.Cli.Infrastructure;

/// <summary>
/// Shared entry point for command actions: builds the <see cref="CliContext"/>, runs the body, and
/// converts any exception into a stable exit code with output routed through <see cref="OutputWriter"/>.
/// Keeps every handler free of boilerplate error handling.
/// </summary>
public static class CommandRunner
{
	public static int Execute(GlobalOptions globals, ParseResult parse, Func<CliContext, int> body)
	{
		using var ctx = globals.BuildContext(parse);
		try
		{
			return body(ctx);
		}
		catch (CliException ex)
		{
			ctx.Output.Error(ex.Message, ex.Code);
			return ex.Code;
		}
		catch (Exception ex)
		{
			ctx.Output.Error(ex.Message, ExitCode.RuntimeFailure);
			if (ctx.Options.Verbosity > 0)
				Console.Error.WriteLine(ex);
			return ExitCode.RuntimeFailure;
		}
	}

	public static Task<int> ExecuteAsync(GlobalOptions globals, ParseResult parse, Func<CliContext, CancellationToken, Task<int>> body, CancellationToken ct)
	{
		return ExecuteAsyncCore(globals, parse, body, ct);
	}

	private static async Task<int> ExecuteAsyncCore(GlobalOptions globals, ParseResult parse, Func<CliContext, CancellationToken, Task<int>> body, CancellationToken ct)
	{
		using var ctx = globals.BuildContext(parse);
		try
		{
			return await body(ctx, ct);
		}
		catch (OperationCanceledException)
		{
			return ExitCode.Ok;
		}
		catch (CliException ex)
		{
			ctx.Output.Error(ex.Message, ex.Code);
			return ex.Code;
		}
		catch (Exception ex)
		{
			ctx.Output.Error(ex.Message, ExitCode.RuntimeFailure);
			if (ctx.Options.Verbosity > 0)
				Console.Error.WriteLine(ex);
			return ExitCode.RuntimeFailure;
		}
	}
}
