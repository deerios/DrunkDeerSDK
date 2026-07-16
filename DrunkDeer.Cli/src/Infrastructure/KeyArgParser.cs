using DrunkDeer.Protocol;

namespace DrunkDeer.Cli.Infrastructure;

/// <summary>
/// Turns a human key spec (from <c>--keys</c>) into a list of <see cref="DDKey"/>. Accepts
/// comma-separated tokens where each token is one of:
/// <list type="bullet">
///   <item>a <see cref="DDKey"/> name (case-insensitive): <c>Escape</c>, <c>LeftShift</c>, <c>ArrowUp</c></item>
///   <item>a single letter or digit shorthand: <c>w</c> → W, <c>5</c> → D5</item>
///   <item>a named group: <c>wasd</c>, <c>arrows</c>, <c>fnrow</c>, <c>letters</c>, <c>numrow</c>, <c>modifiers</c></item>
///   <item>an inclusive range in physical key order: <c>F1-F12</c>, <c>1-5</c></item>
/// </list>
/// </summary>
public static class KeyArgParser
{
	private static readonly DDKey[] AllKeys = Enum.GetValues<DDKey>();

	private static readonly Dictionary<string, DDKey> ByName =
		AllKeys.ToDictionary(k => k.ToString(), k => k, StringComparer.OrdinalIgnoreCase);

	private static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
	{
		["esc"] = "Escape", ["ctrl"] = "LeftCtrl", ["lctrl"] = "LeftCtrl", ["rctrl"] = "RightCtrl",
		["alt"] = "LeftAlt", ["lalt"] = "LeftAlt", ["ralt"] = "RightAlt",
		["shift"] = "LeftShift", ["lshift"] = "LeftShift", ["rshift"] = "RightShift",
		["win"] = "LeftWin", ["super"] = "LeftWin", ["cmd"] = "LeftWin",
		["caps"] = "CapsLock", ["space"] = "Space", ["ret"] = "Enter", ["return"] = "Enter",
		["bksp"] = "Backspace", ["del"] = "Delete", ["ins"] = "Insert",
		["pgup"] = "PageUp", ["pgdn"] = "PageDown", ["pgdown"] = "PageDown",
		["up"] = "ArrowUp", ["down"] = "ArrowDown", ["left"] = "ArrowLeft", ["right"] = "ArrowRight",
		["tilde"] = "Backtick", ["grave"] = "Backtick",
	};

	private static readonly Dictionary<string, DDKey[]> Groups = new(StringComparer.OrdinalIgnoreCase)
	{
		["wasd"] = [DDKey.W, DDKey.A, DDKey.S, DDKey.D],
		["arrows"] = [DDKey.ArrowUp, DDKey.ArrowDown, DDKey.ArrowLeft, DDKey.ArrowRight],
		["fnrow"] = Range(DDKey.F1, DDKey.F12),
		["fn"] = Range(DDKey.F1, DDKey.F12),
		["numrow"] = [DDKey.D1, DDKey.D2, DDKey.D3, DDKey.D4, DDKey.D5, DDKey.D6, DDKey.D7, DDKey.D8, DDKey.D9, DDKey.D0],
		["letters"] = AllKeys.Where(IsLetter).ToArray(),
		["modifiers"] = [DDKey.LeftCtrl, DDKey.LeftWin, DDKey.LeftAlt, DDKey.RightAlt, DDKey.RightCtrl, DDKey.LeftShift, DDKey.RightShift],
	};

	/// <summary>Parses a spec into distinct keys, preserving first-seen order. Throws a usage error on any unknown token.</summary>
	public static DDKey[] Parse(string spec)
	{
		if (string.IsNullOrWhiteSpace(spec))
			throw CliException.Usage("No keys given.");

		var result = new List<DDKey>();
		var seen = new HashSet<DDKey>();

		foreach (var raw in spec.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
		{
			foreach (var key in ResolveToken(raw))
				if (seen.Add(key))
					result.Add(key);
		}

		if (result.Count == 0)
			throw CliException.Usage($"No keys resolved from '{spec}'.");

		return [.. result];
	}

	private static IEnumerable<DDKey> ResolveToken(string token)
	{
		int dash = token.IndexOf('-');
		if (dash > 0 && dash < token.Length - 1)
		{
			var lo = ResolveSingle(token[..dash]);
			var hi = ResolveSingle(token[(dash + 1)..]);
			return Range(lo, hi);
		}

		if (Groups.TryGetValue(token, out var group))
			return group;

		return [ResolveSingle(token)];
	}

	private static DDKey ResolveSingle(string token)
	{
		if (Aliases.TryGetValue(token, out var canonical))
			token = canonical;

		if (ByName.TryGetValue(token, out var key))
			return key;

		if (token.Length == 1)
		{
			char c = char.ToUpperInvariant(token[0]);
			if (c is >= 'A' and <= 'Z' && ByName.TryGetValue(c.ToString(), out var letter))
				return letter;
			if (c is >= '0' and <= '9' && ByName.TryGetValue("D" + c, out var digit))
				return digit;
		}

		throw CliException.Usage($"Unknown key '{token}'.");
	}

	private static DDKey[] Range(DDKey lo, DDKey hi)
	{
		int a = (int)lo, b = (int)hi;
		if (a > b)
			(a, b) = (b, a);
		return AllKeys.Where(k => (int)k >= a && (int)k <= b).ToArray();
	}

	private static bool IsLetter(DDKey k)
	{
		var s = k.ToString();
		return s.Length == 1 && s[0] is >= 'A' and <= 'Z';
	}
}
