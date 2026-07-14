using System.Globalization;
using DrunkDeer.Protocol;

namespace DrunkDeer.Cli.Infrastructure;

/// <summary>Parses a colour spec into an <see cref="RgbColor"/>: <c>#RRGGBB</c>, <c>RRGGBB</c>, or a common name.</summary>
public static class ColorParser
{
	private static readonly Dictionary<string, RgbColor> Named = new(StringComparer.OrdinalIgnoreCase)
	{
		["red"] = new(255, 0, 0), ["green"] = new(0, 255, 0), ["blue"] = new(0, 0, 255),
		["white"] = new(255, 255, 255), ["black"] = new(0, 0, 0), ["off"] = new(0, 0, 0),
		["yellow"] = new(255, 255, 0), ["cyan"] = new(0, 255, 255), ["magenta"] = new(255, 0, 255),
		["orange"] = new(255, 140, 0), ["purple"] = new(128, 0, 255), ["pink"] = new(255, 96, 160),
	};

	public static RgbColor Parse(string spec)
	{
		if (string.IsNullOrWhiteSpace(spec))
			throw CliException.Usage("No colour given.");

		var t = spec.Trim();
		if (Named.TryGetValue(t, out var named))
			return named;

		var hex = t.StartsWith('#') ? t[1..] : t;
		if (hex.Length == 6 &&
			byte.TryParse(hex.AsSpan(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var r) &&
			byte.TryParse(hex.AsSpan(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var green) &&
			byte.TryParse(hex.AsSpan(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b))
			return new RgbColor(r, green, b);

		throw CliException.Usage($"Unrecognised colour '{spec}'. Use #RRGGBB or a name (red, blue, orange, …).");
	}
}
