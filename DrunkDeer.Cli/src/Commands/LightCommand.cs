using System.CommandLine;
using DrunkDeer.Cli.Infrastructure;
using DrunkDeer.Protocol;

namespace DrunkDeer.Cli.Commands;

/// <summary><c>deerkb light set|mode|off</c> — live RGB control.</summary>
public static class LightCommand
{
	public static Command Build(GlobalOptions g)
	{
		var group = new Command("light", "Control RGB lighting (live).");
		group.Subcommands.Add(BuildSet(g));
		group.Subcommands.Add(BuildMode(g));
		group.Subcommands.Add(BuildOff(g));
		return group;
	}

	private static Command BuildSet(GlobalOptions g)
	{
		var colorArg = new Argument<string>("color") { Description = "Colour as #RRGGBB or a name (red, orange, …)." };
		var keysOpt = new Option<string?>("--keys", "-k") { Description = "Apply only to these keys (e.g. wasd, Esc)." };
		var baseOpt = new Option<string?>("--base") { Description = "Base colour for keys not in --keys (default: keeps board off elsewhere)." };
		var brightnessOpt = new Option<int>("--brightness", "-b") { Description = "Brightness 0–9 (default 9).", DefaultValueFactory = _ => 9 };
		var baseBrightnessOpt = new Option<int?>("--base-brightness") { Description = "Dim --base independently of --brightness (0–9). The firmware has one brightness per frame, so this scales --base's RGB values in software instead." };

		var set = new Command("set", "Set a solid colour, optionally per key.") { colorArg, keysOpt, baseOpt, brightnessOpt, baseBrightnessOpt };
		set.SetAction(parse => CommandRunner.Execute(g, parse, ctx =>
		{
			using var session = ctx.OpenSession();
			byte brightness = Brightness(parse.GetValue(brightnessOpt));
			var color = ColorParser.Parse(parse.GetValue(colorArg)!);
			var keysSpec = parse.GetValue(keysOpt);
			byte? baseBrightness = parse.GetValue(baseBrightnessOpt) is { } bb0 ? Brightness(bb0) : null;

			if (string.IsNullOrWhiteSpace(keysSpec))
			{
				if (baseBrightness is not null)
					throw CliException.Usage("--base-brightness needs --keys (there's no separate base color without it).");

				session.SetUniformLighting(color, brightness);
				ProfileStore.PersistIfRequested(ctx, new KeyboardProfile
				{
					Theme = new KeyboardThemeBuilder().Base(color).Brightness(brightness).Build(),
				});
				ctx.Output.Emit(new { applied = true, color = Hex(color), brightness, keys = (string[]?)null },
					() => ctx.Output.Line($"Set all keys to {Hex(color)} at brightness {brightness}."));
				return ExitCode.Ok;
			}

			var keys = KeyArgParser.Parse(keysSpec).Where(session.IsKeyPresent).ToArray();
			if (keys.Length == 0)
				throw CliException.Usage($"None of the requested keys exist on this {session.Model.Slug}.");

			// Per-key colour writes the whole RGB frame; unlisted keys take --base (default off/black).
			var baseColor = parse.GetValue(baseOpt) is { } b ? ColorParser.Parse(b) : new RgbColor(0, 0, 0);
			var effectiveBase = baseBrightness is { } bb1 ? baseColor.Scale(bb1) : baseColor;
			session.SetUniformLighting(effectiveBase, brightness);
			session.SetKeyColor(color, brightness, keys);

			var themeBuilder = new KeyboardThemeBuilder().Base(baseColor).Brightness(brightness).Keys(keys, color.R, color.G, color.B);
			if (baseBrightness is { } bbTheme)
				themeBuilder.BaseBrightness(bbTheme);
			ProfileStore.PersistIfRequested(ctx, new KeyboardProfile { Theme = themeBuilder.Build() });

			ctx.Output.Emit(
				new { applied = true, color = Hex(color), baseColor = Hex(baseColor), brightness, baseBrightness, keys = keys.Select(k => k.ToString()).ToArray() },
				() => ctx.Output.Line($"Set {keys.Length} key(s) to {Hex(color)}, others to {Hex(effectiveBase)}" +
					(baseBrightness is { } bbv ? $" (base brightness {bbv})" : "") + $", brightness {brightness}."));
			return ExitCode.Ok;
		}));
		return set;
	}

	private static Command BuildMode(GlobalOptions g)
	{
		var modeArg = new Argument<string>("mode") { Description = "Lighting effect (e.g. breath, spectrum, ripple, alwayslight)." };
		var brightnessOpt = new Option<int>("--brightness", "-b") { Description = "Brightness 0–9 (default 9).", DefaultValueFactory = _ => 9 };
		var speedOpt = new Option<int>("--speed", "-s") { Description = "Animation speed 0–9 (default 5).", DefaultValueFactory = _ => 5 };

		var mode = new Command("mode", "Set a built-in lighting animation.") { modeArg, brightnessOpt, speedOpt };
		mode.SetAction(parse => CommandRunner.Execute(g, parse, ctx =>
		{
			using var session = ctx.OpenSession();
			var lm = ParseMode(parse.GetValue(modeArg)!);
			byte brightness = Brightness(parse.GetValue(brightnessOpt));
			byte speed = Speed(parse.GetValue(speedOpt));
			session.SetLightingMode(lm, brightness, speed);
			if (ctx.Options.Persist is not null)
				ctx.Output.Note("Lighting animations aren't part of the saved profile schema (only static colors persist); skipping --persist.");
			ctx.Output.Emit(new { applied = true, mode = lm.ToString(), brightness, speed },
				() => ctx.Output.Line($"Lighting mode set to {lm} (brightness {brightness}, speed {speed})."));
			return ExitCode.Ok;
		}));
		return mode;
	}

	private static Command BuildOff(GlobalOptions g)
	{
		var off = new Command("off", "Turn off all lighting.");
		off.SetAction(parse => CommandRunner.Execute(g, parse, ctx =>
		{
			using var session = ctx.OpenSession();
			session.DisableLighting();
			ProfileStore.PersistIfRequested(ctx, new KeyboardProfile
			{
				Theme = new KeyboardThemeBuilder().Base(0, 0, 0).Brightness(0).Build(),
			});
			ctx.Output.Emit(new { applied = true, lighting = "off" }, () => ctx.Output.Line("Lighting off."));
			return ExitCode.Ok;
		}));
		return off;
	}

	private static LightingMode ParseMode(string spec)
	{
		var t = spec.Replace("-", "").Replace("_", "").Trim();
		t = t.ToLowerInvariant() switch
		{
			"solid" or "static" or "on" => nameof(LightingMode.AlwaysLight),
			"breathing" => nameof(LightingMode.Breath),
			"wave" => nameof(LightingMode.WaveSpectrum),
			_ => t,
		};
		if (Enum.TryParse<LightingMode>(t, ignoreCase: true, out var mode))
			return mode;
		var names = string.Join(", ", Enum.GetNames<LightingMode>());
		throw CliException.Usage($"Unknown lighting mode '{spec}'. Options: {names}.");
	}

	private static byte Brightness(int v) =>
		v is >= 0 and <= 9 ? (byte)v : throw CliException.Usage("--brightness must be 0–9.");

	private static byte Speed(int v) =>
		v is >= 0 and <= 9 ? (byte)v : throw CliException.Usage("--speed must be 0–9.");

	private static string Hex(RgbColor c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";
}
