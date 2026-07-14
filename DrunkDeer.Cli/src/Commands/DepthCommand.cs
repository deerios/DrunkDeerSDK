using System.CommandLine;
using DrunkDeer.Cli.Infrastructure;
using DrunkDeer.Protocol;
using Spectre.Console;

namespace DrunkDeer.Cli.Commands;

/// <summary>
/// Builds the <c>actuation</c>, <c>downstroke</c>, and <c>upstroke</c> command groups — they share
/// one shape (<c>get</c> / <c>set</c>) differing only in which session methods they call.
/// </summary>
public static class DepthCommand
{
	private delegate void SetUniform(KeyboardSession s, float mm);
	private delegate void SetProfile(KeyboardSession s, KeyDepthProfile p);
	private delegate IReadOnlyDictionary<DDKey, float> GetProfile(KeyboardSession s);

	public static Command BuildActuation(GlobalOptions g) => Build(g, "actuation",
		"Actuation point (the depth at which a key registers).",
		(s, mm) => s.SetActuationPoint(mm), (s, p) => s.SetActuationPoints(p), s => s.GetActuationProfile());

	public static Command BuildDownstroke(GlobalOptions g) => Build(g, "downstroke",
		"Rapid Trigger downstroke sensitivity.",
		(s, mm) => s.SetDownstrokePoint(mm), (s, p) => s.SetDownstrokePoints(p), s => s.GetDownstrokeProfile());

	public static Command BuildUpstroke(GlobalOptions g) => Build(g, "upstroke",
		"Rapid Trigger upstroke sensitivity.",
		(s, mm) => s.SetUpstrokePoint(mm), (s, p) => s.SetUpstrokePoints(p), s => s.GetUpstrokeProfile());

	private static Command Build(GlobalOptions g, string name, string desc,
		SetUniform setUniform, SetProfile setProfile, GetProfile getProfile)
	{
		var group = new Command(name, desc);

		// get
		var get = new Command("get", $"Show the current {name} profile (last written by this tool).");
		get.SetAction(parse => CommandRunner.Execute(g, parse, ctx => RunGet(ctx, name, getProfile)));
		group.Subcommands.Add(get);

		// set
		var depthArg = new Argument<float>("depth") { Description = "Target depth in millimetres." };
		var keysOpt = new Option<string?>("--keys", "-k") { Description = "Apply only to these keys (e.g. wasd, F1-F12, Esc,Space)." };
		var allOthersOpt = new Option<float?>("--all-others") { Description = "Baseline depth (mm) for keys not in --keys. Required for per-key writes (see notes)." };
		var set = new Command("set", $"Set the {name} depth.") { depthArg, keysOpt, allOthersOpt };
		set.SetAction(parse => CommandRunner.Execute(g, parse, ctx =>
			RunSet(ctx, name, setUniform, setProfile,
				parse.GetValue(depthArg), parse.GetValue(keysOpt), parse.GetValue(allOthersOpt))));
		group.Subcommands.Add(set);

		return group;
	}

	private static int RunGet(CliContext ctx, string name, GetProfile getProfile)
	{
		using var session = ctx.OpenSession();
		var profile = getProfile(session)
			.OrderBy(kv => (int)kv.Key)
			.ToDictionary(kv => kv.Key.ToString(), kv => kv.Value);

		ctx.Output.Emit(new { kind = name, unit = "mm", keys = profile }, () =>
		{
			ctx.Output.Note($"Values reflect what this tool last wrote this session (no hardware read-back yet).");
			var table = new Table().Border(TableBorder.Rounded).AddColumn("Key").AddColumn("mm");
			foreach (var (key, mm) in profile)
				table.AddRow(key, $"{mm:0.###}");
			ctx.Output.Console.Write(table);
		});
		return ExitCode.Ok;
	}

	private static int RunSet(CliContext ctx, string name, SetUniform setUniform, SetProfile setProfile,
		float depth, string? keysSpec, float? allOthers)
	{
		using var session = ctx.OpenSession();
		ValidateDepth(session, depth, name);

		// Uniform write: no per-key spec.
		if (string.IsNullOrWhiteSpace(keysSpec) && allOthers is null)
		{
			ctx.Confirm.Require($"Set {name} to {depth:0.###} mm on all keys");
			setUniform(session, depth);
			ProfileStore.PersistIfRequested(ctx, ProfileFor(name, new KeyDepthProfileBuilder().Default(depth).Build()));
			return Report(ctx, name, depth, keys: null, allOthers: null);
		}

		if (string.IsNullOrWhiteSpace(keysSpec))
			throw CliException.Usage("--all-others requires --keys (it sets the baseline for the keys you are NOT targeting).");

		var keys = KeyArgParser.Parse(keysSpec);
		var present = keys.Where(session.IsKeyPresent).ToArray();
		if (present.Length == 0)
			throw CliException.Usage($"None of the requested keys exist on this {session.Model.Slug}.");

		// Per-key writes rewrite the entire profile; unlisted keys would reset to the firmware default.
		// The SDK has no hardware read-back for depths, so we require an explicit baseline rather than
		// silently clobbering every other key. (A per-keyboard state cache is planned; until then this
		// is the safe contract.)
		if (allOthers is null)
			throw CliException.Usage(
				$"A per-key {name} write rewrites the whole keyboard; keys outside --keys would reset to the firmware default. " +
				$"Pass --all-others <mm> to set their baseline explicitly (e.g. --all-others 2.0), or run 'deerkb {name} set <mm>' once to set a uniform value first.");

		ValidateDepth(session, allOthers.Value, name);
		ctx.Confirm.Require($"Set {name}: {present.Length} key(s) to {depth:0.###} mm, all others to {allOthers.Value:0.###} mm");

		var depthProfile = new KeyDepthProfileBuilder().Default(allOthers.Value).Keys(present, depth).Build();
		setProfile(session, depthProfile);
		ProfileStore.PersistIfRequested(ctx, ProfileFor(name, depthProfile));
		return Report(ctx, name, depth, present, allOthers);
	}

	private static KeyboardProfile ProfileFor(string name, KeyDepthProfile depthProfile) => name switch
	{
		"actuation"  => new KeyboardProfile { Actuation = depthProfile },
		"downstroke" => new KeyboardProfile { Downstroke = depthProfile },
		"upstroke"   => new KeyboardProfile { Upstroke = depthProfile },
		_ => new KeyboardProfile(),
	};

	private static void ValidateDepth(KeyboardSession session, float mm, string name)
	{
		if (mm < session.MinDepthMm || mm > session.MaxDepthMm)
			throw CliException.Usage(
				$"{mm:0.###} mm is out of range for {name} on this model ({session.MinDepthMm:0.##}–{session.MaxDepthMm:0.##} mm).");
	}

	private static int Report(CliContext ctx, string name, float depth, DDKey[]? keys, float? allOthers)
	{
		ctx.Output.Emit(
			new
			{
				kind = name,
				applied = true,
				depthMm = depth,
				keys = keys?.Select(k => k.ToString()).ToArray(),
				allOthersMm = allOthers,
			},
			() =>
			{
				if (keys is null)
					ctx.Output.Line($"Set {name} to {depth:0.###} mm on all keys.");
				else
					ctx.Output.Line($"Set {name} to {depth:0.###} mm on {keys.Length} key(s); all others {allOthers:0.###} mm.");
			});
		return ExitCode.Ok;
	}
}
