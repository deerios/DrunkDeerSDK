using System.CommandLine;

namespace DrunkDeer.Cli.Infrastructure;

/// <summary>
/// The set of recursive global options, plus a factory that reads them off a parse result into a
/// ready-to-use <see cref="CliContext"/>. Handlers receive the context; they never re-read options.
/// </summary>
public sealed class GlobalOptions
{
	public Option<string?> Device { get; } = new("--device") { Description = "Target a specific keyboard by serial or device path (default: first found).", Recursive = true };
	public Option<bool> Json { get; } = new("--json") { Description = "Emit machine-readable JSON instead of human output.", Recursive = true };
	public Option<bool> Yes { get; } = new("--yes", "-y") { Description = "Skip confirmation prompts for flash writes.", Recursive = true };
	public Option<bool> Quiet { get; } = new("--quiet", "-q") { Description = "Suppress notices; errors only.", Recursive = true };
	public Option<bool> Verbose { get; } = new("--verbose", "-v") { Description = "Log SDK diagnostics to stderr at Debug level.", Recursive = true };
	public Option<bool> Trace { get; } = new("--trace") { Description = "Log SDK diagnostics to stderr at Trace level (includes TX/RX).", Recursive = true };
	public Option<int?> Timeout { get; } = new("--timeout") { Description = "HID receive timeout override, in milliseconds.", Recursive = true };
	public Option<bool> Demo { get; } = new("--demo") { Description = "Use a simulated keyboard (no hardware required).", Recursive = true };
	public Option<string?> DemoModel { get; } = new("--demo-model") { Description = "Model slug to simulate in --demo mode (default: a75_ultra).", Recursive = true };
	public Option<string?> Persist { get; } = new("--persist") { Description = "Save applied settings into a named profile (see 'profile save'), merging with any existing profile of that name.", Recursive = true };
	public Option<string?> LoadProfile { get; } = new("--load-profile") { Description = "Apply a saved profile (see 'profile save') before running the command.", Recursive = true };

	public void AddTo(RootCommand root)
	{
		root.Options.Add(Device);
		root.Options.Add(Json);
		root.Options.Add(Yes);
		root.Options.Add(Quiet);
		root.Options.Add(Verbose);
		root.Options.Add(Trace);
		root.Options.Add(Timeout);
		root.Options.Add(Demo);
		root.Options.Add(DemoModel);
		root.Options.Add(Persist);
		root.Options.Add(LoadProfile);
	}

	public CliContext BuildContext(ParseResult parse)
	{
		var options = new CliOptions
		{
			Device = parse.GetValue(Device),
			Json = parse.GetValue(Json),
			AssumeYes = parse.GetValue(Yes),
			Quiet = parse.GetValue(Quiet),
			Verbosity = parse.GetValue(Trace) ? 2 : parse.GetValue(Verbose) ? 1 : 0,
			TimeoutMs = parse.GetValue(Timeout),
			Demo = parse.GetValue(Demo),
			DemoModel = parse.GetValue(DemoModel),
			Persist = parse.GetValue(Persist),
			LoadProfile = parse.GetValue(LoadProfile),
		};

		bool noColor = Environment.GetEnvironmentVariable("NO_COLOR") is not null;
		var output = new OutputWriter(Console.Out, Console.Error, options.Json, options.Quiet, noColor);
		bool interactive = !Console.IsInputRedirected && !Console.IsOutputRedirected;
		var confirm = new Confirmation(output, options.AssumeYes, interactive);
		var loggerFactory = StderrLoggerProvider.ForVerbosity(options.Verbosity);

		return new CliContext(options, output, confirm, loggerFactory);
	}
}
