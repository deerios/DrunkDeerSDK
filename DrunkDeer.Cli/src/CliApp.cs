using System.CommandLine;
using DrunkDeer.Cli.Commands;
using DrunkDeer.Cli.Infrastructure;

namespace DrunkDeer.Cli;

/// <summary>Builds and invokes the root command. Kept separate from the entry point so tests can drive it.</summary>
public static class CliApp
{
	public static RootCommand BuildRoot()
	{
		var globals = new GlobalOptions();

		var root = new RootCommand(
			"deerkb - a command line tool for configuring DrunkDeer keyboards.")
		{
			DevicesCommand.Build(globals),
			InfoCommand.Build(globals),
			WatchCommand.Build(globals),
			DepthCommand.BuildActuation(globals),
			DepthCommand.BuildDownstroke(globals),
			DepthCommand.BuildUpstroke(globals),
			TriggerCommands.BuildRapidTrigger(globals),
			TriggerCommands.BuildTurbo(globals),
			LightCommand.Build(globals),
			ProfileCommand.Build(globals),
		};

		globals.AddTo(root);

		// Running bare (no subcommand) behaves like `info`.
		root.SetAction(parse => CommandRunner.Execute(globals, parse, InfoCommand.Run));

		return root;
	}

	public static Task<int> InvokeAsync(string[] args) => BuildRoot().Parse(args).InvokeAsync();
}
