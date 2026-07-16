using System.CommandLine;
using DrunkDeer.Cli.Infrastructure;
using DrunkDeer.Protocol;

namespace DrunkDeer.Cli.Commands;

/// <summary>
/// <c>deerkb profile capture|apply|show|save|load|list</c> — JSON profile files in and out, plus
/// named profiles kept in this tool's own config directory (see <see cref="ProfileStore"/>).
/// </summary>
public static class ProfileCommand
{
	public static Command Build(GlobalOptions g)
	{
		var group = new Command("profile", "Capture, apply, and inspect keyboard profiles (JSON).");
		group.Subcommands.Add(BuildCapture(g));
		group.Subcommands.Add(BuildApply(g));
		group.Subcommands.Add(BuildShow(g));
		group.Subcommands.Add(BuildSave(g));
		group.Subcommands.Add(BuildLoad(g));
		group.Subcommands.Add(BuildList(g));
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

			ProfileStore.PersistIfRequested(ctx, profile);

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
			ProfileStore.PersistIfRequested(ctx, profile);
			ctx.Output.Emit(new { applied = true, file = path, themeOnly = profile.IsThemeOnly },
				() => ctx.Output.Line($"Applied {(profile.IsThemeOnly ? "lighting from" : "profile")} {path}."));
			return ExitCode.Ok;
		}));
		return apply;
	}

	private static Command BuildShow(GlobalOptions g)
	{
		var fileArg = new Argument<string?>("file") { Description = "Profile JSON file to display (omit if using --name)." };
		fileArg.Arity = ArgumentArity.ZeroOrOne;
		var nameOpt = new Option<string?>("--name", "-n") { Description = "Show a saved named profile instead of a file (see 'profile save')." };
		var show = new Command("show", "Print and validate a profile — either a file or a saved name.") { fileArg, nameOpt };
		show.SetAction(parse => CommandRunner.Execute(g, parse, ctx =>
		{
			var name = parse.GetValue(nameOpt);
			var path = parse.GetValue(fileArg);

			string source;
			KeyboardProfile profile;
			if (name is not null)
			{
				profile = ProfileStore.LoadNamed(name);
				source = $"profile '{name}'";
			}
			else if (path is not null)
			{
				if (!File.Exists(path))
					throw CliException.Usage($"File not found: {path}");
				try { profile = KeyboardProfile.FromFile(path); }
				catch (Exception ex) { throw CliException.Usage($"Invalid profile '{path}': {ex.Message}"); }
				source = path;
			}
			else
			{
				throw CliException.Usage("Pass a file or --name <profile>. Run 'deerkb profile list' to see saved profiles.");
			}

			ctx.Output.Emit(
				new
				{
					source,
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

	private static Command BuildSave(GlobalOptions g)
	{
		var nameArg = new Argument<string>("name") { Description = "Name to save the captured profile under (letters, digits, '-', '_')." };
		var save = new Command("save", "Capture the current profile and save it under a name for later use with 'profile load'.") { nameArg };
		save.SetAction(parse => CommandRunner.Execute(g, parse, ctx =>
		{
			var name = parse.GetValue(nameArg)!;
			ProfileStore.ValidateName(name);

			using var session = ctx.OpenSession();

			// Same hardware read-back limitation as 'profile capture': only Rapid Trigger and Turbo
			// are reported by the keyboard, so that's all a saved profile can reliably capture.
			var profile = new KeyboardProfile
			{
				RapidTrigger = session.RapidTriggerEnabled,
				TurboMode = session.TurboEnabled,
			};

			var path = ProfileStore.SaveNamed(name, profile);
			ctx.Output.Note("Captured Rapid Trigger and Turbo state (the only settings the keyboard reports). " +
				"Depth profiles and lighting are omitted — the firmware offers no read-back for them yet.");
			ctx.Output.Emit(
				new { saved = name, file = path, rapidTrigger = profile.RapidTrigger, turboMode = profile.TurboMode },
				() => ctx.Output.Line($"Saved profile '{name}' to {path}."));
			return ExitCode.Ok;
		}));
		return save;
	}

	private static Command BuildLoad(GlobalOptions g)
	{
		var nameArg = new Argument<string>("name") { Description = "Saved profile name to apply (see 'profile save')." };
		var load = new Command("load", "Apply a saved named profile to the keyboard.") { nameArg };
		load.SetAction(parse => CommandRunner.Execute(g, parse, ctx =>
		{
			var name = parse.GetValue(nameArg)!;
			var profile = ProfileStore.LoadNamed(name);

			using var session = ctx.OpenSession();
			ctx.Confirm.Require(profile.IsThemeOnly
				? $"Apply lighting from profile '{name}'"
				: $"Apply profile '{name}' (actuation/trigger/lighting)");

			session.ApplyProfile(profile);
			ProfileStore.PersistIfRequested(ctx, profile);
			ctx.Output.Emit(new { applied = true, profile = name, themeOnly = profile.IsThemeOnly },
				() => ctx.Output.Line($"Applied {(profile.IsThemeOnly ? "lighting from" : "profile")} '{name}'."));
			return ExitCode.Ok;
		}));
		return load;
	}

	private static Command BuildList(GlobalOptions g)
	{
		var list = new Command("list", "List profiles saved with 'profile save'.");
		list.SetAction(parse => CommandRunner.Execute(g, parse, ctx =>
		{
			var names = ProfileStore.ListNamed();
			ctx.Output.Emit(new { profiles = names }, () =>
			{
				if (names.Count == 0)
					ctx.Output.Line("No saved profiles. Use 'deerkb profile save <name>' to create one.");
				else
					foreach (var name in names)
						ctx.Output.Line(name);
			});
			return ExitCode.Ok;
		}));
		return list;
	}
}
