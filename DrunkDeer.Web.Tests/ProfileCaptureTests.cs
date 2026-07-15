using DrunkDeer;
using DrunkDeer.Protocol;
using DrunkDeer.Web.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;
using NUnit.Framework;

namespace DrunkDeer.Web.Tests;

/// <summary>
/// Pins what <see cref="KeyboardService.CaptureProfile"/> is allowed to claim it knows, and that
/// what it captures survives a round trip back onto the board.
/// </summary>
/// <remarks>
/// The A75 cannot report its settings back, so the session starts with the SDK's invented seed:
/// 2.0 mm on every key, and black. Capturing that as though the user had chosen it is the bug this
/// fixture exists to prevent — the same family as the baselines pinned by ActuationBaselineTests
/// and LightingBaselineTests in the SDK's own suite.
/// </remarks>
[TestFixture]
public class ProfileCaptureTests
{
	private KeyboardService _service = null!;

	[SetUp]
	public async Task SetUp()
	{
		_service = new KeyboardService(
			new KeyboardStore(), new StubJsRuntime(), new DiagnosticsLog(), NullLoggerFactory.Instance);
		await _service.ConnectDemoAsync();
	}

	[TearDown]
	public async Task TearDown() => await _service.DisposeAsync();

	private static DDKey[] Wasd => [DDKey.W, DDKey.A, DDKey.S, DDKey.D];

	// ── What a fresh session is allowed to claim ─────────────────────────────

	[Test]
	public void FreshSession_CapturesNeitherDepthsNorColors()
	{
		var profile = _service.CaptureProfile();

		Assert.Multiple(() =>
		{
			// Both would otherwise serialise the SDK's seed as fact.
			Assert.That(profile.Actuation, Is.Null, "a fresh session has never written a depth");
			Assert.That(profile.Theme, Is.Null, "a fresh session has never written a colour");
		});
	}

	[Test]
	public void FreshSession_StillCapturesRapidTrigger()
	{
		// Unlike depths and colours, this one the board actually reports, in the identity handshake.
		Assert.That(_service.CaptureProfile().RapidTrigger, Is.Not.Null);
	}

	[Test]
	public async Task AfterAnActuationWrite_CapturesDepthsButStillNotColors()
	{
		await _service.ApplyActuationAsync(1.2f, Wasd, baselineMm: 2.0f);

		var profile = _service.CaptureProfile();
		Assert.Multiple(() =>
		{
			Assert.That(profile.Actuation, Is.Not.Null, "the session has now written every depth");
			Assert.That(profile.Theme, Is.Null, "but it still has never written a colour");
		});
	}

	[Test]
	public async Task AfterALightingWrite_CapturesColors()
	{
		await _service.ApplyLightingAsync(
			new RgbColor(255, 0, 0), Wasd, new RgbColor(0, 0, 40),
			backgroundBrightness: 9, brightness: 7);

		Assert.That(_service.CaptureProfile().Theme, Is.Not.Null);
	}

	// ── The captured shape ───────────────────────────────────────────────────

	[Test]
	public async Task CapturedDepths_UseARealDefault_NotZero()
	{
		await _service.ApplyActuationAsync(1.2f, Wasd, baselineMm: 2.0f);

		var actuation = _service.CaptureProfile().Actuation!;
		Assert.Multiple(() =>
		{
			// A zero default means "leave the keys I didn't list alone", which would make this
			// profile land differently depending on what the session it's applied to had written.
			Assert.That(actuation.Default, Is.Not.Zero);
			Assert.That(actuation.Default, Is.EqualTo(2.0f).Within(0.001f), "the value most keys share");
			// Only the exceptions are listed, not all 82 keys.
			Assert.That(actuation.Keys, Has.Count.EqualTo(Wasd.Length));
		});
	}

	[Test]
	public async Task CapturedDepths_ListTheKeysThatDiffer()
	{
		await _service.ApplyActuationAsync(1.2f, Wasd, baselineMm: 2.0f);

		var keys = _service.CaptureProfile().Actuation!.Keys!;
		foreach (var key in Wasd)
			Assert.That(keys[key.ToString()], Is.EqualTo(1.2f).Within(0.001f), $"{key} was set individually");
	}

	[Test]
	public async Task CapturedDepths_UniformBoard_ListsNoExceptions()
	{
		var all = _service.Session!.Layout.Select(k => k.Key).ToArray();
		await _service.ApplyActuationAsync(1.5f, all, baselineMm: 1.5f);

		var actuation = _service.CaptureProfile().Actuation!;
		Assert.Multiple(() =>
		{
			Assert.That(actuation.Default, Is.EqualTo(1.5f).Within(0.001f));
			Assert.That(actuation.Keys, Is.Null.Or.Empty, "nothing differs from the default");
		});
	}

	[Test]
	public async Task CapturedTheme_DoesNotReapplyBackgroundBrightness()
	{
		// The SDK scales BaseBrightness into the colours it stores, so a capture that set it again
		// would dim the background a second time on every save/apply round trip.
		await _service.ApplyLightingAsync(
			new RgbColor(255, 0, 0), Wasd, new RgbColor(90, 180, 270 % 256),
			backgroundBrightness: 3, brightness: 7);

		var theme = _service.CaptureProfile().Theme!;
		Assert.That(theme.BaseBrightness, Is.Null);
	}

	[Test]
	public async Task CapturedTheme_KeepsTheBrightnessThatWasSent()
	{
		await _service.ApplyLightingAsync(
			new RgbColor(255, 0, 0), Wasd, new RgbColor(0, 0, 40),
			backgroundBrightness: 9, brightness: 7);

		Assert.That(_service.CaptureProfile().Theme!.Brightness, Is.EqualTo(7));
	}

	[Test]
	public async Task CapturedTheme_SeparatesBackgroundFromTheKeysThatDiffer()
	{
		await _service.ApplyLightingAsync(
			new RgbColor(255, 0, 0), Wasd, new RgbColor(0, 0, 40),
			backgroundBrightness: 9, brightness: 7);

		var theme = _service.CaptureProfile().Theme!;
		Assert.Multiple(() =>
		{
			Assert.That((theme.BaseColor.R, theme.BaseColor.G, theme.BaseColor.B), Is.EqualTo(((byte)0, (byte)0, (byte)40)));
			Assert.That(theme.Keys, Has.Count.EqualTo(Wasd.Length));
			foreach (var key in Wasd)
			{
				var c = theme.Keys![key.ToString()];
				Assert.That((c.R, c.G, c.B), Is.EqualTo(((byte)255, (byte)0, (byte)0)));
			}
		});
	}

	// ── Round trip ───────────────────────────────────────────────────────────

	[Test]
	public async Task Capture_Serialise_Apply_RestoresTheSameDepths()
	{
		await _service.ApplyActuationAsync(1.2f, Wasd, baselineMm: 2.0f);
		var expected = _service.GetActuationProfile().ToDictionary(kv => kv.Key, kv => kv.Value);

		// Through JSON, because that is how a saved profile actually comes back.
		var json = _service.CaptureProfile().ToJson();
		await _service.DisconnectAsync();
		await _service.ConnectDemoAsync();

		await _service.ApplyProfileAsync(KeyboardProfile.FromJson(json));

		var restored = _service.GetActuationProfile();
		foreach (var (key, depthMm) in expected)
			Assert.That(restored[key], Is.EqualTo(depthMm).Within(0.001f), $"{key} came back at a different depth");
	}

	[Test]
	public async Task Capture_Serialise_Apply_RestoresTheSameColors()
	{
		await _service.ApplyLightingAsync(
			new RgbColor(255, 0, 0), Wasd, new RgbColor(0, 0, 40),
			backgroundBrightness: 9, brightness: 7);
		var expected = _service.SnapshotColors().ToDictionary(c => c.Slot, c => (c.R, c.G, c.B));

		var json = _service.CaptureProfile().ToJson();
		await _service.DisconnectAsync();
		await _service.ConnectDemoAsync();

		await _service.ApplyProfileAsync(KeyboardProfile.FromJson(json));

		foreach (var (slot, r, g, b) in _service.SnapshotColors())
			Assert.That((r, g, b), Is.EqualTo(expected[slot]), $"slot {slot} came back a different colour");
	}

	[Test]
	public async Task ApplyingACapturedProfile_MakesTheSessionsPictureTrue()
	{
		await _service.ApplyActuationAsync(1.2f, Wasd, baselineMm: 2.0f);
		await _service.ApplyLightingAsync(
			new RgbColor(255, 0, 0), Wasd, new RgbColor(0, 0, 40),
			backgroundBrightness: 9, brightness: 7);
		var json = _service.CaptureProfile().ToJson();

		await _service.DisconnectAsync();
		await _service.ConnectDemoAsync();
		Assert.Multiple(() =>
		{
			Assert.That(_service.DepthsAreKnown, Is.False, "a new session knows nothing");
			Assert.That(_service.ColorsAreKnown, Is.False);
		});

		await _service.ApplyProfileAsync(KeyboardProfile.FromJson(json));

		Assert.Multiple(() =>
		{
			// A captured profile writes every key, which is exactly what earns these flags: the
			// session's picture is no longer inherited from the SDK's seed.
			Assert.That(_service.DepthsAreKnown, Is.True);
			Assert.That(_service.ColorsAreKnown, Is.True);
		});
	}

	[Test]
	public async Task ApplyingADepthProfileThatPreserves_DoesNotClaimTheDepthsAreKnown()
	{
		// The trap: a zero default writes only the listed keys and leaves the rest at whatever the
		// session held — which on a fresh session is the invented seed, not the board. Nothing was
		// learned, so nothing may be claimed.
		var preserving = new KeyDepthProfileBuilder().Default(0f).Keys(Wasd, 1.2f).Build();

		await _service.ApplyProfileAsync(new KeyboardProfile { Actuation = preserving });

		Assert.That(_service.DepthsAreKnown, Is.False);
	}

	/// <summary>Stands in for the browser. Capture and apply never reach JS, so nothing should call this.</summary>
	private sealed class StubJsRuntime : IJSRuntime
	{
		public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
			throw new NotSupportedException($"The test reached JS: {identifier}");

		public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken ct, object?[]? args) =>
			throw new NotSupportedException($"The test reached JS: {identifier}");
	}
}
