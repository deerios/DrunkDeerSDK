using DrunkDeer.Protocol;
using HidSharp;

namespace DrunkDeer.Cli.Infrastructure;

/// <summary>
/// Turns global options into an open <see cref="KeyboardSession"/>. Three paths:
/// an injected connection (demo mode / tests), a specific <c>--device</c> target, or the first
/// discovered keyboard. All failures surface as <see cref="CliException"/> with a stable exit code.
/// </summary>
public static class SessionFactory
{
	public static KeyboardSession Open(CliContext ctx)
	{
		if (ctx.Options.Demo && ctx.ConnectionOverride is null)
			return OpenDemo(ctx);

		if (ctx.ConnectionOverride is not null)
			return KeyboardSession.Open(ctx.ConnectionOverride(), ctx.LoggerFactory);

		if (!string.IsNullOrWhiteSpace(ctx.Options.Device))
			return OpenTargeted(ctx, ctx.Options.Device);

		return OpenFirst(ctx);
	}

	private static KeyboardSession OpenDemo(CliContext ctx)
	{
		var slug = ctx.Options.DemoModel ?? ModelSlugs.A75Ultra;
		var model = ModelRegistry.GetInfo(slug)
			?? throw CliException.Usage($"Unknown demo model '{slug}'. Try one of: {string.Join(", ", KnownSlugs())}.");
		return KeyboardSession.Open(new Simulation.SimulatedKeyboardConnection(model) { IdleJitter = true }, ctx.LoggerFactory);
	}

	private static KeyboardSession OpenFirst(CliContext ctx)
	{
		try
		{
			return KeyboardSession.OpenFirst(ctx.LoggerFactory);
		}
		catch (InvalidOperationException ex)
		{
			// KeyboardDiscoverer throws InvalidOperationException when no device handshakes.
			throw CliException.NoDevice(
				$"No DrunkDeer keyboard found. {ex.Message} (Try 'deerkb devices' to list HID candidates, or '--demo' to explore without hardware.)");
		}
	}

	private static KeyboardSession OpenTargeted(CliContext ctx, string target)
	{
		// Device identity isn't exposed on an opened KeyboardConnection yet, so match at the HidDevice
		// level (serial or device path) and try to open each matching interface — the command interface
		// is the one that completes the handshake; the rest fail and are skipped.
		var matches = DeviceInventory.FindAll()
			.Where(d => d.Matches(target))
			.ToList();

		if (matches.Count == 0)
			throw CliException.NoDevice($"No DrunkDeer keyboard matched --device '{target}'. Run 'deerkb devices' to see identifiers.");

		foreach (var match in matches)
		{
			try
			{
				var connection = KeyboardConnection.Open(match.Device, ctx.LoggerFactory);
				return KeyboardSession.Open(connection, ctx.LoggerFactory);
			}
			catch
			{
				// Not the command interface (or busy) — try the next matching interface.
			}
		}

		throw CliException.Busy(
			$"Found a device matching '{target}' but could not open its command interface (in use by another process, or not a DrunkDeer command interface).");
	}

	private static IEnumerable<string> KnownSlugs() =>
		ModelRegistry.DiscoveryPairs.Length == 0 ? [] : [ModelSlugs.A75, ModelSlugs.A75Ultra, ModelSlugs.A75Master];
}
