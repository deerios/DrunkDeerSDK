using System.CommandLine;
using DrunkDeer.Cli.Infrastructure;
using DrunkDeer.Protocol;

namespace DrunkDeer.Cli.Commands;

/// <summary><c>deerkb profile capture|apply|show</c> — JSON profile files in and out.</summary>
public static class ProfileCommand
{
	public static Command Build(GlobalOptions g)
	{
		var group = new Command("profile", "Capture, apply, and inspect keyboard profiles (JSON).");
		group.Subcommands.Add(BuildCapture(g));
		group.Subcommands.Add(BuildApply(g));
		group.Subcommands.Add(BuildShow(g));
		return group;
	}

	private static Command BuildCapture(GlobalOptions g)
	{
		var outOpt = new Option<string?>("--out", "-o") { Description = "File to write (default: stdout)." };
		var capture = new Command("capture", "Capture the current profile to JSON.") { outOpt };
		capture.SetAction(parse => CommandRunner.Execute(g, parse, ctx =>
		{
			using var session = ctx.OpenSession();

			// Only Rapid Trigger and Turbo are read from the keyboard during the handshake; depth
			// profiles and lighting have no hardware read-back, so we deliberately leave them null
			// rather than serialise session defaults that would overwrite real settings on restore.
			var profile = new KeyboardProfile
			{
				RapidTrigger = session.RapidTriggerEnabled,
				TurboMode = session.TurboEnabled,
			};

			var path = parse.GetValue(outOpt);
			if (path is not null)
				profile.SaveToFile(path);

			ctx.Output.Note("Captured Rapid Trigger and Turbo state (the only settings the keyboard reports). " +
				"Depth profiles and lighting are omitted — the firmware offers no read-back for them yet.");

			ctx.Output.Emit(
				new { captured = new { rapidTrigger = profile.RapidTrigger, turboMode = profile.TurboMode }, file = path },
				() =>
				{
					if (path is not null) ctx.Output.Line($"Wrote {path}.");
					else ctx.Output.Line(profile.ToJson());
				});
			return ExitCode.Ok;
		}));
		return capture;
	}

	private static Command BuildApply(GlobalOptions g)
	{
		var fileArg = new Argument<string>("file") { Description = "Profile JSON file to apply." };
		var apply = new Command("apply", "Apply a profile JSON file to the keyboard.") { fileArg };
		apply.SetAction(parse => CommandRunner.Execute(g, parse, ctx =>
		{
			var path = parse.GetValue(fileArg)!;
			if (!File.Exists(path))
				throw CliException.Usage($"File not found: {path}");

			KeyboardProfile profile;
			try { profile = KeyboardProfile.FromFile(path); }
			catch (Exception ex) { throw CliException.Usage($"Could not read profile '{path}': {ex.Message}"); }

			using var session = ctx.OpenSession();
			ctx.Confirm.Require(profile.IsThemeOnly
				? $"Apply lighting from {Path.GetFileName(path)}"
				: $"Apply profile {Path.GetFileName(path)} (actuation/trigger/lighting)");

			session.ApplyProfile(profile);
			ctx.Output.Emit(new { applied = true, file = path, themeOnly = profile.IsThemeOnly },
				() => ctx.Output.Line($"Applied {(profile.IsThemeOnly ? "lighting from" : "profile")} {path}."));
			return ExitCode.Ok;
		}));
		return apply;
	}

	private static Command BuildShow(GlobalOptions g)
	{
		var fileArg = new Argument<string>("file") { Description = "Profile JSON file to display." };
		var show = new Command("show", "Print and validate a profile JSON file (no device needed).") { fileArg };
		show.SetAction(parse => CommandRunner.Execute(g, parse, ctx =>
		{
			var path = parse.GetValue(fileArg)!;
			if (!File.Exists(path))
				throw CliException.Usage($"File not found: {path}");

			KeyboardProfile profile;
			try { profile = KeyboardProfile.FromFile(path); }
			catch (Exception ex) { throw CliException.Usage($"Invalid profile '{path}': {ex.Message}"); }

			ctx.Output.Emit(
				new
				{
					file = path,
					themeOnly = profile.IsThemeOnly,
					rapidTrigger = profile.RapidTrigger,
					rapidTriggerAutoMatch = profile.RapidTriggerAutoMatch,
					turboMode = profile.TurboMode,
					hasActuation = profile.Actuation is not null,
					hasDownstroke = profile.Downstroke is not null,
					hasUpstroke = profile.Upstroke is not null,
					hasTheme = profile.Theme is not null,
				},
				() => ctx.Output.Line(profile.ToJson()));
			return ExitCode.Ok;
		}));
		return show;
	}
}
