using Spectre.Console;

namespace DrunkDeer.Cli.Infrastructure;

/// <summary>
/// Gate for actions that write keyboard flash. Honours <c>--yes</c>, and in a non-interactive
/// context (piped stdin, --json) refuses unless <c>--yes</c> was given — a script must opt in
/// explicitly rather than hang on a prompt it can never answer.
/// </summary>
public sealed class Confirmation(OutputWriter output, bool assumeYes, bool interactive)
{
	/// <summary>
	/// Confirms a flash write. Throws <see cref="CliException"/> with <see cref="ExitCode.Aborted"/>
	/// if the user declines or if confirmation is impossible without <c>--yes</c>.
	/// </summary>
	public void Require(string what)
	{
		if (assumeYes)
			return;

		if (!interactive || output.Json)
			throw CliException.Aborted(
				$"{what} writes to keyboard flash and needs confirmation. Re-run with --yes to proceed non-interactively.");

		var ok = output.Console.Confirm($"[yellow]{Markup.Escape(what)}[/] will write to keyboard flash. Continue?", false);
		if (!ok)
			throw CliException.Aborted();
	}
}
