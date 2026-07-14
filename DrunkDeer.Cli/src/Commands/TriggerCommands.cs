using System.CommandLine;
using DrunkDeer.Cli.Infrastructure;
using DrunkDeer.Protocol;

namespace DrunkDeer.Cli.Commands;

/// <summary><c>deerkb rt on|off</c> and <c>deerkb turbo on|off</c>.</summary>
public static class TriggerCommands
{
	public static Command BuildRapidTrigger(GlobalOptions g)
	{
		var group = new Command("rt", "Rapid Trigger (analog reset-on-release).");

		var autoMatch = new Option<bool>("--auto-match") { Description = "Release threshold automatically mirrors the press threshold." };
		var on = new Command("on", "Enable Rapid Trigger.") { autoMatch };
		on.SetAction(parse => CommandRunner.Execute(g, parse, ctx => Set(ctx, "rapidTrigger", true, s =>
		{
			ctx.Confirm.Require("Enable Rapid Trigger");
			s.EnableRapidTrigger(parse.GetValue(autoMatch));
		})));
		group.Subcommands.Add(on);

		var off = new Command("off", "Disable Rapid Trigger.");
		off.SetAction(parse => CommandRunner.Execute(g, parse, ctx => Set(ctx, "rapidTrigger", false, s =>
		{
			ctx.Confirm.Require("Disable Rapid Trigger");
			s.DisableRapidTrigger();
		})));
		group.Subcommands.Add(off);

		return group;
	}

	public static Command BuildTurbo(GlobalOptions g)
	{
		var group = new Command("turbo", "Turbo mode (held keys re-fire at the polling rate).");

		var on = new Command("on", "Enable Turbo mode.");
		on.SetAction(parse => CommandRunner.Execute(g, parse, ctx => Set(ctx, "turbo", true, s =>
		{
			RequireTurbo(s);
			ctx.Confirm.Require("Enable Turbo mode");
			s.EnableTurboMode();
		})));
		group.Subcommands.Add(on);

		var off = new Command("off", "Disable Turbo mode.");
		off.SetAction(parse => CommandRunner.Execute(g, parse, ctx => Set(ctx, "turbo", false, s =>
		{
			RequireTurbo(s);
			ctx.Confirm.Require("Disable Turbo mode");
			s.DisableTurboMode();
		})));
		group.Subcommands.Add(off);

		return group;
	}

	private static void RequireTurbo(KeyboardSession s)
	{
		if (!s.Model.Capabilities.HasFlag(Capabilities.TurboMode))
			throw CliException.Unsupported($"Turbo mode is not supported on {s.Model.Slug} ({s.Model.Name}).");
	}

	private static int Set(CliContext ctx, string feature, bool enabled, Action<KeyboardSession> apply)
	{
		using var session = ctx.OpenSession();
		apply(session);
		ctx.Output.Emit(new { feature, enabled }, () =>
			ctx.Output.Line($"{feature} {(enabled ? "enabled" : "disabled")}."));
		return ExitCode.Ok;
	}
}
