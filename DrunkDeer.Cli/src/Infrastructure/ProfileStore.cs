using System.Text.RegularExpressions;
using DrunkDeer.Protocol;

namespace DrunkDeer.Cli.Infrastructure;

/// <summary>
/// Reads and writes the CLI's default persisted profile: an OS-appropriate JSON file that
/// accumulates the actuation/trigger/lighting settings this tool has applied, so a user's
/// configuration survives across invocations without passing an explicit file path. Only used
/// when the <c>--persist</c> global option is set.
/// </summary>
public static class ProfileStore
{
	/// <summary>
	/// The OS-appropriate config directory for this tool: <c>%APPDATA%\deerkb</c> on Windows,
	/// <c>~/Library/Application Support/deerkb</c> on macOS, and <c>$XDG_CONFIG_HOME/deerkb</c>
	/// (falling back to <c>~/.config/deerkb</c>) on Linux and other Unix platforms.
	/// </summary>
	public static string DefaultDirectory()
	{
		if (OperatingSystem.IsWindows())
			return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "deerkb");

		if (OperatingSystem.IsMacOS())
			return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
				"Library", "Application Support", "deerkb");

		var xdgConfigHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
		var configHome = string.IsNullOrEmpty(xdgConfigHome)
			? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config")
			: xdgConfigHome;
		return Path.Combine(configHome, "deerkb");
	}

	/// <summary>
	/// Merges <paramref name="changes"/> onto the profile already saved under <paramref name="name"/>
	/// (non-null fields in <paramref name="changes"/> overwrite the stored value, null fields leave it
	/// as-is), and writes the result back. Creates the profile if it doesn't exist yet.
	/// </summary>
	public static string PersistNamed(string name, KeyboardProfile changes)
	{
		var path = NamedPath(name);
		var existing = File.Exists(path) ? KeyboardProfile.FromFile(path) : new KeyboardProfile();
		var merged = new KeyboardProfile
		{
			Actuation             = changes.Actuation             ?? existing.Actuation,
			Downstroke            = changes.Downstroke            ?? existing.Downstroke,
			Upstroke              = changes.Upstroke              ?? existing.Upstroke,
			RapidTrigger          = changes.RapidTrigger          ?? existing.RapidTrigger,
			RapidTriggerAutoMatch = changes.RapidTriggerAutoMatch ?? existing.RapidTriggerAutoMatch,
			TurboMode             = changes.TurboMode             ?? existing.TurboMode,
			Theme                 = changes.Theme                 ?? existing.Theme,
		};

		return SaveNamed(name, merged);
	}

	/// <summary>
	/// Merges <paramref name="changes"/> into the named profile given by <c>--persist</c> and prints
	/// a note, but only when that option was passed. Call this from command handlers right after a
	/// successful mutation.
	/// </summary>
	public static void PersistIfRequested(CliContext ctx, KeyboardProfile changes)
	{
		if (ctx.Options.Persist is not { } name)
			return;

		var path = PersistNamed(name, changes);
		ctx.Output.Note($"Persisted to profile '{name}' ({path}).");
	}

	// Letters, digits, '-' and '_' only. No path separators or '.', so a name can never escape
	// ProfilesDirectory() (e.g. "..", "../x", an absolute path, or a hidden dotfile).
	private static readonly Regex NamePattern = new(@"^[A-Za-z0-9_-]{1,64}$", RegexOptions.Compiled);

	/// <summary>Throws <see cref="CliException"/> if <paramref name="name"/> isn't a safe profile name.</summary>
	public static void ValidateName(string name)
	{
		if (string.IsNullOrWhiteSpace(name) || !NamePattern.IsMatch(name))
			throw CliException.Usage(
				$"Invalid profile name '{name}'. Use 1-64 characters: letters, digits, '-' or '_' only.");
	}

	/// <summary>Directory holding named profiles saved with <see cref="SaveNamed"/>.</summary>
	public static string ProfilesDirectory() => Path.Combine(DefaultDirectory(), "profiles");

	/// <summary>Path a named profile is stored at. Validates <paramref name="name"/> first.</summary>
	public static string NamedPath(string name)
	{
		ValidateName(name);
		return Path.Combine(ProfilesDirectory(), name + ".json");
	}

	/// <summary>Saves <paramref name="profile"/> under <paramref name="name"/>, creating the directory if needed.</summary>
	public static string SaveNamed(string name, KeyboardProfile profile)
	{
		var path = NamedPath(name);
		Directory.CreateDirectory(ProfilesDirectory());
		profile.SaveToFile(path);
		return path;
	}

	/// <summary>Loads the profile saved under <paramref name="name"/>.</summary>
	/// <exception cref="CliException">Thrown when the name is invalid or no such profile exists.</exception>
	public static KeyboardProfile LoadNamed(string name)
	{
		var path = NamedPath(name);
		if (!File.Exists(path))
			throw CliException.Usage(
				$"No saved profile named '{name}'. Run 'deerkb profile save {name}' first, or 'deerkb profile list' to see what's available.");
		return KeyboardProfile.FromFile(path);
	}

	/// <summary>Names of all profiles saved with <see cref="SaveNamed"/>, sorted alphabetically.</summary>
	public static IReadOnlyList<string> ListNamed()
	{
		var dir = ProfilesDirectory();
		if (!Directory.Exists(dir))
			return [];

		return Directory.EnumerateFiles(dir, "*.json")
			.Select(Path.GetFileNameWithoutExtension)
			.Where(n => !string.IsNullOrEmpty(n))
			.Select(n => n!)
			.OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
			.ToList();
	}
}
