using System.CommandLine;
using DrunkDeer.Cli.Infrastructure;
using DrunkDeer.Protocol;
using Spectre.Console;

namespace DrunkDeer.Cli.Commands;

/// <summary>
/// <c>deerkb devices</c> — lists HID interfaces whose VID/PID match a known DrunkDeer model.
/// With <c>--probe</c> it opens each and reports the handshaked model and firmware.
/// </summary>
public static class DevicesCommand
{
	public static Command Build(GlobalOptions g)
	{
		var probe = new Option<bool>("--probe") { Description = "Open each candidate and report its handshaked model + firmware." };
		var cmd = new Command("devices", "List connected DrunkDeer keyboards (HID candidates).") { probe };

		cmd.SetAction(parse => CommandRunner.Execute(g, parse, ctx => Run(ctx, parse.GetValue(probe))));
		return cmd;
	}

	private sealed record DeviceRow(string Path, string? Serial, string Vid, string Pid, string? Product, string? Model, int? Firmware);

	private static int Run(CliContext ctx, bool probe)
	{
		var rows = new List<DeviceRow>();
		foreach (var d in DeviceInventory.FindAll())
		{
			string? model = null;
			int? fw = null;
			if (probe)
			{
				try
				{
					using var conn = KeyboardConnection.Open(d.Device, ctx.LoggerFactory);
					model = conn.Model.Slug;
					fw = conn.FirmwareVersion;
				}
				catch { /* not the command interface, or busy */ }
			}
			rows.Add(new DeviceRow(d.Path, d.Serial, $"0x{d.VendorId:X4}", $"0x{d.ProductId:X4}", d.ProductName, model, fw));
		}

		ctx.Output.Emit(new { count = rows.Count, devices = rows }, () =>
		{
			if (rows.Count == 0)
			{
				ctx.Output.Line("No DrunkDeer keyboards found.");
				return;
			}

			var table = new Table().Border(TableBorder.Rounded);
			table.AddColumn("VID"); table.AddColumn("PID"); table.AddColumn("Serial"); table.AddColumn("Product");
			if (probe) { table.AddColumn("Model"); table.AddColumn("FW"); }
			table.AddColumn("Path");

			foreach (var r in rows)
			{
				var cells = new List<string> { r.Vid, r.Pid, r.Serial ?? "-", r.Product ?? "-" };
				if (probe) { cells.Add(r.Model ?? "[dim]no handshake[/]"); cells.Add(r.Firmware?.ToString() ?? "-"); }
				cells.Add($"[dim]{Markup.Escape(r.Path)}[/]");
				table.AddRow(cells.ToArray());
			}
			ctx.Output.Console.Write(table);
		});

		return ExitCode.Ok;
	}
}
