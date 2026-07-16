using DrunkDeer.Protocol;
using DrunkDeer.Simulation;
using NUnit.Framework;

namespace DrunkDeer.FeatureTests.Features;

/// <summary>
/// Coverage for what a per-key colour write does to the keys the caller did not list. Like actuation,
/// lighting has no "set just these keys" command — every write sends the whole board from the
/// session's in-memory profile. Unlike actuation, that profile is not seeded at connect, so it starts
/// black and the first write of a session turns every other key off. The web UI leans on this split:
/// an explicit background colour for the first write of a session, then per-key writes to preserve.
/// </summary>
[TestFixture]
public class LightingBaselineTests
{
	private static KeyboardSession OpenSimulatedA75() =>
		KeyboardSession.Open(new SimulatedKeyboardConnection());

	/// <summary>A key that exists on the A75 and is not one of the ones under test.</summary>
	private const DDKey Bystander = DDKey.K;

	/// <summary>
	/// The trap the explicit-background flow exists to avoid. A fresh session's colour profile is all
	/// zeroes rather than anything the board reported (the A75 can only report its live colours through
	/// an internal, blocking read that a WASM host cannot call), so colouring one key sends black to
	/// every other one.
	/// </summary>
	[Test]
	public async Task SetKeyColorAsync_OnAFreshSession_BlacksOutUnlistedKeys()
	{
		await using var session = OpenSimulatedA75();

		await session.SetKeyColorAsync(255, 0, 0, [DDKey.W]);

		Assert.Multiple(() =>
		{
			Assert.That(session.GetKeyColor(DDKey.W), Is.EqualTo(((byte)255, (byte)0, (byte)0)));
			Assert.That(session.GetKeyColor(Bystander), Is.EqualTo(((byte)0, (byte)0, (byte)0)),
				"an unlisted key goes black because the whole profile is sent and it was never seeded");
		});
	}

	[Test]
	public async Task ApplyProfileAsync_Theme_SetsUnlistedKeysToTheBaseColor()
	{
		await using var session = OpenSimulatedA75();

		await session.ApplyProfileAsync(new KeyboardProfile
		{
			Theme = new KeyboardThemeBuilder()
				.Base(20, 20, 40)
				.Keys([DDKey.W, DDKey.A, DDKey.S, DDKey.D], 255, 0, 0)
				.Build(),
		});

		Assert.Multiple(() =>
		{
			Assert.That(session.GetKeyColor(DDKey.W), Is.EqualTo(((byte)255, (byte)0, (byte)0)));
			Assert.That(session.GetKeyColor(DDKey.D), Is.EqualTo(((byte)255, (byte)0, (byte)0)));
			Assert.That(session.GetKeyColor(Bystander), Is.EqualTo(((byte)20, (byte)20, (byte)40)),
				"an unlisted key should take the theme's base colour");
		});
	}

	/// <summary>
	/// Once a theme has established every key, the session's profile is a true record of what it wrote,
	/// so a per-key write can preserve the rest. This is why the panel only asks for a background once.
	/// </summary>
	[Test]
	public async Task SetKeyColorAsync_AfterATheme_LeavesUnlistedKeysAtTheirColor()
	{
		await using var session = OpenSimulatedA75();

		await session.ApplyProfileAsync(new KeyboardProfile
		{
			Theme = new KeyboardThemeBuilder().Base(20, 20, 40).Build(),
		});

		await session.SetKeyColorAsync(255, 0, 0, [DDKey.W]);

		Assert.Multiple(() =>
		{
			Assert.That(session.GetKeyColor(DDKey.W), Is.EqualTo(((byte)255, (byte)0, (byte)0)));
			Assert.That(session.GetKeyColor(Bystander), Is.EqualTo(((byte)20, (byte)20, (byte)40)),
				"the background written by the theme must survive a later per-key write");
		});
	}

	/// <summary>
	/// BaseBrightness dims only the background, in software, because the firmware sends one brightness
	/// byte for the whole frame. The web UI offers it so a background can sit under brighter highlights.
	/// </summary>
	[Test]
	public async Task ApplyProfileAsync_Theme_BaseBrightnessScalesOnlyTheBaseColor()
	{
		await using var session = OpenSimulatedA75();

		await session.ApplyProfileAsync(new KeyboardProfile
		{
			Theme = new KeyboardThemeBuilder()
				.Base(200, 200, 200)
				.BaseBrightness(0)
				.Key(DDKey.W, 255, 0, 0)
				.Build(),
		});

		Assert.Multiple(() =>
		{
			Assert.That(session.GetKeyColor(Bystander), Is.Not.EqualTo(((byte)200, (byte)200, (byte)200)),
				"the base colour should be scaled down before being sent");
			Assert.That(session.GetKeyColor(DDKey.W), Is.EqualTo(((byte)255, (byte)0, (byte)0)),
				"a per-key override must not be scaled by BaseBrightness");
		});
	}
}
