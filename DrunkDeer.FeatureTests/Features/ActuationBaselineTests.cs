using DrunkDeer.Protocol;
using DrunkDeer.Simulation;
using NUnit.Framework;

namespace DrunkDeer.FeatureTests.Features;

/// <summary>
/// Coverage for how <see cref="KeyboardProfile.Actuation"/>'s default depth decides what happens to
/// keys the caller did not list. The firmware has no "set just these keys" command, so every
/// actuation write sends the whole board and the default is the only thing standing between a
/// per-key edit and a silent reset of everything else. The web UI leans on exactly this split:
/// an explicit default for the first write of a session, then a zero default to preserve.
/// </summary>
[TestFixture]
public class ActuationBaselineTests
{
	private static KeyboardSession OpenSimulatedA75() =>
		KeyboardSession.Open(new SimulatedKeyboardConnection());

	/// <summary>A key that exists on the A75 and is not one of the ones under test.</summary>
	private const DDKey Bystander = DDKey.K;

	[Test]
	public async Task ApplyProfileAsync_NonZeroDefault_SetsUnlistedKeysToTheDefault()
	{
		await using var session = OpenSimulatedA75();

		await session.ApplyProfileAsync(new KeyboardProfile
		{
			Actuation = new KeyDepthProfileBuilder()
				.Default(1.5f)
				.Keys([DDKey.W, DDKey.A, DDKey.S, DDKey.D], 0.4f)
				.Build(),
		});

		var profile = session.GetActuationProfile();
		Assert.Multiple(() =>
		{
			Assert.That(profile[DDKey.W], Is.EqualTo(0.4f).Within(0.01f));
			Assert.That(profile[DDKey.D], Is.EqualTo(0.4f).Within(0.01f));
			Assert.That(profile[Bystander], Is.EqualTo(1.5f).Within(0.01f),
				"an unlisted key should take the profile default");
		});
	}

	[Test]
	public async Task ApplyProfileAsync_ZeroDefault_LeavesUnlistedKeysUnchanged()
	{
		await using var session = OpenSimulatedA75();

		// Establish a known board: everything at 1.5 mm.
		await session.ApplyProfileAsync(new KeyboardProfile
		{
			Actuation = new KeyDepthProfileBuilder().Default(1.5f).Build(),
		});

		// A zero default means "leave keys not listed at their current value".
		await session.ApplyProfileAsync(new KeyboardProfile
		{
			Actuation = new KeyDepthProfileBuilder()
				.Default(0f)
				.Keys([DDKey.W], 0.4f)
				.Build(),
		});

		var profile = session.GetActuationProfile();
		Assert.Multiple(() =>
		{
			Assert.That(profile[DDKey.W], Is.EqualTo(0.4f).Within(0.01f));
			Assert.That(profile[Bystander], Is.EqualTo(1.5f).Within(0.01f),
				"a zero default must preserve the previous write, not reset to the SDK's own default");
		});
	}

	/// <summary>
	/// The trap the explicit-baseline flow exists to avoid: a fresh session's shadow profile is an
	/// SDK-chosen 2.0 mm rather than anything the board reported, so a per-key edit that preserves
	/// "current" values on a board that can't be read back writes that guess to every other key.
	/// </summary>
	[Test]
	public async Task ApplyProfileAsync_ZeroDefaultOnAFreshSession_WritesTheSdkDefaultToUnlistedKeys()
	{
		await using var session = OpenSimulatedA75();

		await session.ApplyProfileAsync(new KeyboardProfile
		{
			Actuation = new KeyDepthProfileBuilder().Default(0f).Keys([DDKey.W], 0.4f).Build(),
		});

		var profile = session.GetActuationProfile();
		Assert.That(profile[Bystander], Is.EqualTo(2.0f).Within(0.01f));
	}
}
