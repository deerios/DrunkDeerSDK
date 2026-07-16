using System.CommandLine;
using System.Text.Json;
using DrunkDeer.Cli.Infrastructure;
using DrunkDeer.Protocol;
using DrunkDeer.Simulation;
using Spectre.Console;

namespace DrunkDeer.Cli.Commands;

/// <summary>
/// <c>deerkb watch</c> — live per-key travel display. Runs until Ctrl+C. A hardware diagnostic and
/// the CLI's showpiece. <c>--raw</c> streams JSON-lines for piping into other tools.
/// </summary>
public static class WatchCommand
{
	public static Command Build(GlobalOptions g)
	{
		var rawOpt = new Option<bool>("--raw") { Description = "Stream JSON-lines of pressed keys instead of a live table." };
		var thresholdOpt = new Option<float>("--threshold") { Description = "Minimum travel (mm) to show a key as active (default 0.2).", DefaultValueFactory = _ => 0.2f };
		var cmd = new Command("watch", "Live per-key travel display (Ctrl+C to stop).") { rawOpt, thresholdOpt };

		cmd.SetAction((parse, ct) => CommandRunner.ExecuteAsync(g, parse,
			(ctx, token) => Run(ctx, parse.GetValue(rawOpt), parse.GetValue(thresholdOpt), token), ct));
		return cmd;
	}

	private static async Task<int> Run(CliContext ctx, bool raw, float threshold, CancellationToken ct)
	{
		var (session, animator) = OpenForWatch(ctx);
		using var animatorCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
		Task? animatorTask = animator is null ? null : Animate(animator, animatorCts.Token);

		try
		{
			session.StartPolling(ct);

			if (raw || ctx.Output.Json)
				await RunRaw(ctx, session, threshold, ct);
			else
				await RunLive(ctx, session, threshold, ct);

			return ExitCode.Ok;
		}
		finally
		{
			session.StopPolling();
			animatorCts.Cancel();
			if (animatorTask is not null)
				try { await animatorTask; } catch (OperationCanceledException) { }
			session.Dispose();
		}
	}

	private static (KeyboardSession session, SimulatedKeyboardConnection? animator) OpenForWatch(CliContext ctx)
	{
		if (ctx.Options.Demo && ctx.ConnectionOverride is null)
		{
			var model = ModelRegistry.GetInfo(ctx.Options.DemoModel ?? ModelSlugs.A75Ultra)
				?? throw CliException.Usage($"Unknown demo model '{ctx.Options.DemoModel}'.");
			var sim = new SimulatedKeyboardConnection(model) { IdleJitter = true };
			return (KeyboardSession.Open(sim, ctx.LoggerFactory), sim);
		}
		return (ctx.OpenSession(), null);
	}

	private static async Task RunLive(CliContext ctx, KeyboardSession session, float threshold, CancellationToken ct)
	{
		int polls = 0;
		session.Polled += (_, _) => Interlocked.Increment(ref polls);
		var started = DateTime.UtcNow;

		await ctx.Output.Console.Live(new Table())
			.StartAsync(async live =>
			{
				while (!ct.IsCancellationRequested)
				{
					var active = session.GetAllKeyHeightsMmByKey()
						.Where(kv => kv.Value >= threshold)
						.OrderByDescending(kv => kv.Value)
						.ToList();

					var table = new Table().Border(TableBorder.Rounded).Expand();
					table.Title = new TableTitle($"{session.Model.Name}  ·  {active.Count} keys down  ·  {Rate(polls, started):0} Hz poll");
					table.AddColumn("Key");
					table.AddColumn(new TableColumn("Travel").Width(40));
					table.AddColumn("mm");

					foreach (var (key, mm) in active.Take(20))
						table.AddRow(key.ToString(), Bar(mm, session.MaxDepthMm), $"{mm:0.00}");

					if (active.Count == 0)
						table.AddRow("[dim]—[/]", "[dim](press keys)[/]", "");

					live.UpdateTarget(table);
					try { await Task.Delay(33, ct); } catch (OperationCanceledException) { break; }
				}
			});
	}

	private static async Task RunRaw(CliContext ctx, KeyboardSession session, float threshold, CancellationToken ct)
	{
		while (!ct.IsCancellationRequested)
		{
			var active = session.GetAllKeyHeightsMmByKey()
				.Where(kv => kv.Value >= threshold)
				.ToDictionary(kv => kv.Key.ToString(), kv => MathF.Round(kv.Value, 2));

			var line = JsonSerializer.Serialize(new { t = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), keys = active });
			Console.Out.WriteLine(line);
			try { await Task.Delay(50, ct); } catch (OperationCanceledException) { break; }
		}
	}

	private static string Bar(float mm, float max)
	{
		int width = 38;
		int filled = Math.Clamp((int)MathF.Round(mm / MathF.Max(max, 0.01f) * width), 0, width);
		var color = mm >= max * 0.66f ? "red" : mm >= max * 0.33f ? "yellow" : "green";
		return $"[{color}]{new string('█', filled)}[/]{new string('·', width - filled)}";
	}

	private static double Rate(int polls, DateTime started)
	{
		var secs = (DateTime.UtcNow - started).TotalSeconds;
		return secs < 0.5 ? 0 : polls / secs;
	}

	// Demo animator: sweeps a rolling set of key slots so `--demo watch` is visibly alive.
	private static async Task Animate(SimulatedKeyboardConnection sim, CancellationToken ct)
	{
		var rng = new Random();
		try
		{
			while (!ct.IsCancellationRequested)
			{
				int slot = rng.Next(0, sim.KeyCount);
				for (float mm = 0; mm <= 3.4f; mm += 0.6f)
				{
					sim.SetKeyTravelMm(slot, mm);
					await Task.Delay(20, ct);
				}
				await Task.Delay(40, ct);
				sim.SetKeyTravelMm(slot, 0);
			}
		}
		catch (OperationCanceledException) { }
	}
}
