using System.Text.Json;
using System.Text.Json.Serialization;
using Spectre.Console;

namespace DrunkDeer.Cli.Infrastructure;

/// <summary>
/// The single sink for all command output. Handlers never touch <see cref="Console"/> directly so
/// that <c>--json</c> is total (no stray human text leaks into a machine-readable stream) and so
/// tests can capture everything through the injected writers.
/// </summary>
public sealed class OutputWriter
{
	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		WriteIndented = true,
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
		Converters = { new JsonStringEnumConverter() },
	};

	private readonly TextWriter _out;
	private readonly TextWriter _err;

	public bool Json { get; }
	public bool Quiet { get; }

	/// <summary>Spectre console bound to the same stdout stream, respecting NO_COLOR / --json.</summary>
	public IAnsiConsole Console { get; }

	public OutputWriter(TextWriter stdout, TextWriter stderr, bool json, bool quiet, bool noColor)
	{
		_out = stdout;
		_err = stderr;
		Json = json;
		Quiet = quiet;

		Console = AnsiConsole.Create(new AnsiConsoleSettings
		{
			Ansi = noColor || json ? AnsiSupport.No : AnsiSupport.Detect,
			ColorSystem = noColor || json ? ColorSystemSupport.NoColors : ColorSystemSupport.Detect,
			Out = new AnsiConsoleOutput(stdout),
		});
	}

	/// <summary>
	/// Emits a result. In <c>--json</c> mode the data object is serialised; otherwise the supplied
	/// human renderer runs. Every command result therefore has one JSON shape and one human shape.
	/// </summary>
	public void Emit(object data, Action human)
	{
		if (Json)
			_out.WriteLine(JsonSerializer.Serialize(data, JsonOptions));
		else
			human();
	}

	/// <summary>Writes one line of plain human text (suppressed in --json and --quiet).</summary>
	public void Line(string text = "")
	{
		if (!Json && !Quiet)
			_out.WriteLine(text);
	}

	/// <summary>Writes a status/notice line to stderr (suppressed in --quiet and --json).</summary>
	public void Note(string text)
	{
		if (!Quiet && !Json)
			_err.WriteLine(text);
	}

	/// <summary>Renders an error. JSON mode emits a stable error object to stdout; human mode a red line to stderr.</summary>
	public void Error(string message, int exitCode)
	{
		if (Json)
			_out.WriteLine(JsonSerializer.Serialize(new { error = message, exitCode }, JsonOptions));
		else
			_err.WriteLine($"error: {message}");
	}
}
