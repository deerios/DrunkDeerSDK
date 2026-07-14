using System.CommandLine;
using DrunkDeer.Cli.Infrastructure;
using DrunkDeer.Protocol;
using Spectre.Console;

namespace DrunkDeer.Cli.Commands;

/// <summary>
/// <c>deerkb info</c> — opens the first (or targeted) keyboard and reports model, firmware,
/// precision mode, variant, capabilities, and depth range. This is the root default.
/// </summary>
public static class InfoCommand
{
	public static Command Build(GlobalOptions g)
	{
		var cmd = new Command("info", "Show the connected keyboard's model, firmware, and capabilities.");
		cmd.SetAction(parse => CommandRunner.Execute(g, parse, Run));
		return cmd;
	}

	public static int Run(CliContext ctx)
	{
		using var session = ctx.OpenSession();

		var caps = Enum.GetValues<DrunkDeer.Protocol.Capabilities>()
			.Where(c => c != DrunkDeer.Protocol.Capabilities.None && session.Model.Capabilities.HasFlag(c))
			.Select(c => c.ToString())
			.ToArray();

		var data = new
		{
			model = session.Model.Slug,
			name = session.Model.Name,
			variant = session.Variant,
			firmware = (int)session.FirmwareVersion,
			precision = session.PrecisionMode.ToString(),
			highPrecision = session.IsHighPrecision,
			capabilities = caps,
			keyCount = session.TotalKeyCount,
			lightingKeyCount = session.LightingKeyCount,
			depthMinMm = session.MinDepthMm,
			depthMaxMm = session.MaxDepthMm,
			rapidTrigger = session.RapidTriggerEnabled,
			turbo = session.TurboEnabled,
		};

		ctx.Output.Emit(data, () =>
		{
			var grid = new Grid().AddColumn().AddColumn();
			void Row(string k, string v) => grid.AddRow($"[grey]{k}[/]", v);
			Row("Model", $"[bold]{session.Model.Name}[/] ([green]{session.Model.Slug}[/])");
			Row("Variant", session.Variant);
			Row("Firmware", session.FirmwareVersion.ToString());
			Row("Precision", session.PrecisionMode.ToString());
			Row("Keys", $"{session.TotalKeyCount} ({session.LightingKeyCount} lit)");
			Row("Depth range", $"{session.MinDepthMm:0.##}–{session.MaxDepthMm:0.##} mm");
			Row("Rapid Trigger", session.RapidTriggerEnabled ? "[green]on[/]" : "off");
			Row("Turbo", session.TurboEnabled ? "[green]on[/]" : "off");
			Row("Capabilities", caps.Length == 0 ? "[dim]none[/]" : string.Join(", ", caps));
			ctx.Output.Console.Write(grid);
		});

		return ExitCode.Ok;
	}
}
