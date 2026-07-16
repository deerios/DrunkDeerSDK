using System.Text.Json;
using DrunkDeer.Cli.Infrastructure;
using NUnit.Framework;

namespace DrunkDeer.Cli.Tests;

[TestFixture]
[NonParallelizable]
public class CommandTests
{
	[Test]
	public void Info_Demo_ReportsModelAndExitsOk()
	{
		var r = CliHarness.Run("info", "--demo", "--json");
		Assert.That(r.ExitCode, Is.EqualTo(ExitCode.Ok));
		Assert.That(r.Json.GetProperty("model").GetString(), Is.EqualTo("a75_ultra"));
		Assert.That(r.Json.GetProperty("highPrecision").GetBoolean(), Is.True);
	}

	[Test]
	public void Info_BareRoot_DefaultsToInfo()
	{
		var r = CliHarness.Run("--demo", "--json");
		Assert.That(r.ExitCode, Is.EqualTo(ExitCode.Ok));
		Assert.That(r.Json.GetProperty("name").GetString(), Is.EqualTo("A75 Ultra"));
	}

	[Test]
	public void Actuation_Uniform_Applies()
	{
		var r = CliHarness.Run("actuation", "set", "1.5", "--demo", "--yes", "--json");
		Assert.That(r.ExitCode, Is.EqualTo(ExitCode.Ok));
		Assert.That(r.Json.GetProperty("applied").GetBoolean(), Is.True);
		Assert.That(r.Json.GetProperty("depthMm").GetSingle(), Is.EqualTo(1.5f));
	}

	[Test]
	public void Actuation_PerKey_WithoutBaseline_RefusesAsUsageError()
	{
		var r = CliHarness.Run("actuation", "set", "0.2", "--keys", "wasd", "--demo", "--yes", "--json");
		Assert.That(r.ExitCode, Is.EqualTo(ExitCode.UsageError));
		Assert.That(r.Json.GetProperty("error").GetString(), Does.Contain("--all-others"));
	}

	[Test]
	public void Actuation_PerKey_WithBaseline_Applies()
	{
		var r = CliHarness.Run("actuation", "set", "0.2", "--keys", "wasd", "--all-others", "2.0", "--demo", "--yes", "--json");
		Assert.That(r.ExitCode, Is.EqualTo(ExitCode.Ok));
		var keys = r.Json.GetProperty("keys").EnumerateArray().Select(e => e.GetString()).ToArray();
		Assert.That(keys, Is.EquivalentTo(new[] { "W", "A", "S", "D" }));
	}

	[Test]
	public void Actuation_OutOfRange_IsUsageError()
	{
		var r = CliHarness.Run("actuation", "set", "9.9", "--demo", "--yes", "--json");
		Assert.That(r.ExitCode, Is.EqualTo(ExitCode.UsageError));
	}

	[Test]
	public void RapidTrigger_On_Applies()
	{
		var r = CliHarness.Run("rt", "on", "--demo", "--yes", "--json");
		Assert.That(r.ExitCode, Is.EqualTo(ExitCode.Ok));
		Assert.That(r.Json.GetProperty("enabled").GetBoolean(), Is.True);
	}

	[Test]
	public void Turbo_On_SupportedModel_Applies()
	{
		var r = CliHarness.Run("turbo", "on", "--demo", "--yes", "--json");
		Assert.That(r.ExitCode, Is.EqualTo(ExitCode.Ok));
	}

	[Test]
	public void Turbo_On_UnsupportedModel_IsCapabilityError()
	{
		// The G75 lists no capabilities at all, so it is a standing example of a board without turbo.
		// This used to name the A75, which does have turbo — the model list says so, and the CLI has
		// been answering "enabled" for it ever since.
		var r = CliHarness.Run("turbo", "on", "--demo", "--demo-model", "g75", "--yes", "--json");
		Assert.That(r.ExitCode, Is.EqualTo(ExitCode.CapabilityUnsupported));
	}

	[Test]
	public void Light_SetUniform_Applies()
	{
		var r = CliHarness.Run("light", "set", "#0064FF", "--demo", "--json");
		Assert.That(r.ExitCode, Is.EqualTo(ExitCode.Ok));
		Assert.That(r.Json.GetProperty("color").GetString(), Is.EqualTo("#0064FF"));
	}

	[Test]
	public void Light_Mode_Applies()
	{
		var r = CliHarness.Run("light", "mode", "breath", "--demo", "--json");
		Assert.That(r.ExitCode, Is.EqualTo(ExitCode.Ok));
		Assert.That(r.Json.GetProperty("mode").GetString(), Is.EqualTo("Breath"));
	}

	[Test]
	public void Profile_ShowMissingFile_IsUsageError()
	{
		var r = CliHarness.Run("profile", "show", "/does/not/exist.json", "--json");
		Assert.That(r.ExitCode, Is.EqualTo(ExitCode.UsageError));
	}

	[Test]
	public void Profile_CaptureThenShow_RoundTrips()
	{
		var tmp = Path.Combine(Path.GetTempPath(), $"deerkb-test-{Guid.NewGuid():N}.json");
		try
		{
			var cap = CliHarness.Run("profile", "capture", "-o", tmp, "--demo", "--json");
			Assert.That(cap.ExitCode, Is.EqualTo(ExitCode.Ok));
			Assert.That(File.Exists(tmp), Is.True);

			var show = CliHarness.Run("profile", "show", tmp, "--json");
			Assert.That(show.ExitCode, Is.EqualTo(ExitCode.Ok));
		}
		finally
		{
			if (File.Exists(tmp)) File.Delete(tmp);
		}
	}

	[Test]
	public void Devices_Json_ListsWithoutError()
	{
		var r = CliHarness.Run("devices", "--json");
		Assert.That(r.ExitCode, Is.EqualTo(ExitCode.Ok));
		Assert.That(r.Json.TryGetProperty("count", out _), Is.True);
	}
}
