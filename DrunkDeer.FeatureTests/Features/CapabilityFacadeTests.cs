using System.Reflection;
using DrunkDeer.FeatureTests.Fakes;
using DrunkDeer.Protocol;
using NUnit.Framework;

namespace DrunkDeer.FeatureTests.Features;

/// <summary>
/// Drift guard between the SDK's two capability gates.
/// <para><see cref="KeyboardSession{TModel}"/> gates the programmable API at compile time through
/// the generated model marker interfaces; the untyped <see cref="KeyboardSession"/> gates the same
/// API at runtime through <see cref="KeyboardSession.TryGetFeatures{TFeatures}"/>. Nothing in the
/// compiler ties the two together — the markers are generated from models.yaml, the facades are
/// hand-written in SupportsFeatures — so adding a model or changing a capability can silently make
/// them answer differently for the same keyboard. These tests fail when that happens.</para>
/// <para>Every gate here is a fixed property of the model, so the two answers must match exactly
/// for every model at every firmware.</para>
/// </summary>
[TestFixture]
public class CapabilityFacadeTests
{
	/// <summary>Every generated model marker type (A75, A75Ultra, G65Lite, ...).</summary>
	private static Type[] MarkerTypes { get; } = typeof(IModelMarker).Assembly.GetTypes()
		.Where(t => t is { IsClass: true, IsAbstract: false } && typeof(IModelMarker).IsAssignableFrom(t))
		.OrderBy(t => t.Name)
		.ToArray();

	private static IEnumerable<TestCaseData> AllModels() =>
		MarkerTypes.Select(t => new TestCaseData(t).SetArgDisplayNames(t.Name));

	private static string SlugOf(Type markerType) =>
		(string)markerType.GetProperty("Slug", BindingFlags.Public | BindingFlags.Static)!.GetValue(null)!;

	// Firmware 255 puts every model in the best precision mode its hardware allows. No gate below
	// depends on firmware any more, but opening at the top of the range means a gate that regressed
	// into reading the precision mode again would still show up here.
	private static KeyboardSession OpenAtNewestFirmware(Type markerType) =>
		OpenAt(markerType, 255);

	private static KeyboardSession OpenAt(Type markerType, byte firmwareVersion) =>
		new(new FakeKeyboardConnection(ModelRegistry.GetInfo(SlugOf(markerType)), firmwareVersion));

	// TryGetFeatures is generic over the facade; the facade set under test is data here.
	private static bool TryGetFeatures(KeyboardSession session, Type facade)
	{
		var method = typeof(KeyboardSession).GetMethod(nameof(KeyboardSession.TryGetFeatures))!
			.MakeGenericMethod(facade);
		object?[] args = [null];
		return (bool)method.Invoke(session, args)!;
	}

	// Every facade, paired with the marker interface that makes the same claim at compile time.
	// The programmable facade belongs here like the rest: the FuncBlock gateway is a property of
	// the model, not of the firmware it happens to be running.
	private static readonly (Type Marker, Type Facade)[] StaticGates =
	[
		(typeof(IHasFuncBlock),     typeof(IProgrammableKeyboardFeatures)),
		(typeof(IHasHighPrecision), typeof(IHighPrecisionFeatures)),
		(typeof(IHasLogoLight),     typeof(ILogoLightFeatures)),
		(typeof(IHasSideLight),     typeof(ISideLightFeatures)),
	];

	[TestCaseSource(nameof(AllModels))]
	public void StaticFacades_AgreeWithTheMarkerInterfaces(Type markerType)
	{
		using var session = OpenAtNewestFirmware(markerType);

		foreach (var (marker, facade) in StaticGates)
		{
			bool claimed = marker.IsAssignableFrom(markerType);
			bool granted = TryGetFeatures(session, facade);
			Assert.That(granted, Is.EqualTo(claimed),
				$"{markerType.Name} implements {marker.Name} = {claimed}, but the runtime facade " +
				$"{facade.Name} = {granted} for the same keyboard. The generated marker and " +
				$"SupportsFeatures disagree — fix whichever is wrong about the hardware.");
		}
	}

	[TestCaseSource(nameof(AllModels))]
	public void Supports_AgreesWithTheHighPrecisionFacade(Type markerType)
	{
		using var session = OpenAtNewestFirmware(markerType);

		// HighPrecision is the one facade a caller can also test for with a capability flag; the
		// two public routes must not answer differently.
		Assert.That(session.TryGetFeatures<IHighPrecisionFeatures>(out _),
			Is.EqualTo(session.Supports(Capabilities.HighPrecision)),
			$"{markerType.Name}: Supports(HighPrecision) and the IHighPrecisionFeatures facade disagree.");
	}

	[Test]
	public void SomeModel_ClaimsTheFuncBlockGateway()
	{
		// Guards the StaticGates row above: if codegen stopped emitting IHasFuncBlock entirely, the
		// programmable half of the drift test would pass by agreeing that nothing supports anything.
		Assert.That(MarkerTypes.Where(t => typeof(IHasFuncBlock).IsAssignableFrom(t)), Is.Not.Empty,
			"No model marker implements IHasFuncBlock — codegen is broken.");
	}

	[TestCaseSource(nameof(AllModels))]
	public void ProgrammableFacade_IgnoresFirmware(Type markerType)
	{
		// The gateway is model hardware, so no firmware may change the answer. This is the test that
		// would have caught the old rule, which derived the gateway from the precision mode and so
		// handed a base A75 a programmable surface the moment its firmware crossed the Kun threshold.
		bool atOldest;
		using (var oldest = OpenAt(markerType, 0))
			atOldest = oldest.TryGetFeatures<IProgrammableKeyboardFeatures>(out _);

		for (int fw = 1; fw <= 255; fw++)
		{
			using var session = OpenAt(markerType, (byte)fw);
			Assert.That(session.TryGetFeatures<IProgrammableKeyboardFeatures>(out _), Is.EqualTo(atOldest),
				$"{markerType.Name} answers differently for the programmable facade on firmware {fw} " +
				$"than on firmware 0.");
		}
	}

	[Test]
	public void BaseA75_HasNoGateway_EvenAboveItsKunFirmwareThreshold()
	{
		// No released A75 firmware answers the gateway sub-commands (0x55/0x05-0x06) — reported by an
		// A75 owner, and the reason the gateway no longer follows the precision mode. Kun precision
		// itself stays firmware-gated: crossing the threshold changes the depth resolution and
		// nothing else. This test pins the two apart, which is the whole point of the split.
		var a75 = ModelRegistry.GetInfo(ModelSlugs.A75)!;
		byte kunThreshold = a75.KunPrecisionMinFirmware!.Value;

		using var session = new KeyboardSession(new FakeKeyboardConnection(a75, firmwareVersion: 255));

		Assert.Multiple(() =>
		{
			Assert.That(session.PrecisionMode, Is.EqualTo(PrecisionMode.Kun),
				$"An A75 above firmware {kunThreshold} is still expected to run Kun precision.");
			Assert.That(session.TryGetFeatures<IProgrammableKeyboardFeatures>(out _), Is.False,
				"Kun precision must not imply the FuncBlock gateway on the base A75.");
			Assert.That(typeof(IHasFuncBlock).IsAssignableFrom(typeof(A75)), Is.False,
				"KeyboardSession<A75> must not offer the programmable API at compile time either.");
		});
	}

	[Test]
	public void AlwaysKunModels_HaveTheGateway()
	{
		// The other side of the split: G65 m1/m2/m3 and G60 v600 declare kun_precision as a
		// capability rather than earning it from firmware, and they do have the gateway. Before the
		// split these reached IHasFuncBlock only by way of IHasTurboMode extending it, which was an
		// accident — turbo rides on CommonConfig, not the gateway.
		foreach (var markerType in new[] { typeof(G65M1), typeof(G65M2), typeof(G65M3), typeof(G60V600) })
		{
			Assert.That(typeof(IHasFuncBlock).IsAssignableFrom(markerType), Is.True,
				$"{markerType.Name} is always Kun-precision and must claim the gateway at compile time.");

			using var session = OpenAt(markerType, 1);
			Assert.That(session.TryGetFeatures<IProgrammableKeyboardFeatures>(out _), Is.True,
				$"{markerType.Name} must offer the programmable facade regardless of firmware.");
		}
	}

	[Test]
	public void TurboMarker_DoesNotImplyTheGateway()
	{
		// Turbo is sent with the CommonConfig packet (0xB5); only its persistence into the stored
		// function block needs the gateway, and that step is skipped on boards without one. Wiring
		// IHasTurboMode back under IHasFuncBlock would silently re-grant every A75 a programmable
		// API its firmware refuses.
		Assert.That(typeof(IHasFuncBlock).IsAssignableFrom(typeof(IHasTurboMode)), Is.False,
			"IHasTurboMode must not extend IHasFuncBlock.");
	}

	[Test]
	public void EveryModelSlug_HasExactlyOneMarkerType()
	{
		var slugsInRegistry = typeof(ModelSlugs)
			.GetFields(BindingFlags.Public | BindingFlags.Static)
			.Where(f => f.IsLiteral && f.FieldType == typeof(string))
			.Select(f => (string)f.GetRawConstantValue()!)
			.ToArray();
		var slugsWithMarkers = MarkerTypes.Select(SlugOf).ToArray();

		Assert.That(slugsWithMarkers, Is.EquivalentTo(slugsInRegistry),
			"Every model slug needs a marker type and vice versa, or the facade drift tests above " +
			"silently stop covering a model.");
	}

	[Test]
	public void TryGetFeatures_ForATypeThatIsNotAFacade_Throws()
	{
		using var session = new KeyboardSession(new FakeKeyboardConnection());

		// A typo'd or invented type argument must fail loudly rather than report "unsupported",
		// which would read as a hardware limitation.
		Assert.Throws<ArgumentException>(() => session.TryGetFeatures<IDisposable>(out _));
		Assert.Throws<ArgumentException>(() => session.GetFeatures<IDisposable>());
	}

	[Test]
	public void GetFeatures_WhenUnsupported_IdentifiesTheKeyboardItRefusedFor()
	{
		using var session = new KeyboardSession(
			new FakeKeyboardConnection(ModelRegistry.GetInfo(ModelSlugs.A75), firmwareVersion: 1));

		var ex = Assert.Throws<DrunkDeerCapabilityException>(
			() => session.GetFeatures<IProgrammableKeyboardFeatures>());

		// The model is the reason for the refusal; the firmware and variant are there so a bug report
		// pasting this message describes the whole keyboard rather than just its name.
		Assert.That(ex!.Message, Does.Contain("A75"));
		Assert.That(ex.Message, Does.Contain("fw 1"));
		Assert.That(ex.Message, Does.Contain(nameof(IProgrammableKeyboardFeatures)));
	}

	[Test]
	public void CastingDirectlyToAFacade_DoesNotBypassTheGate()
	{
		// The session implements every facade explicitly, so this cast always succeeds — the gate
		// is not the cast, it is the check inside each method.
		using var session = new KeyboardSession(new FakeKeyboardConnection());
		var logo = (ILogoLightFeatures)session;

		Assert.That(session.TryGetFeatures<ILogoLightFeatures>(out _), Is.False, "A75 has no logo zone.");
		Assert.Throws<NotSupportedException>(() => logo.SetLogoLightOff());
	}

	[Test]
	public void Facades_AddNothingToTheSessionsOwnSurface()
	{
		// The facades are implemented explicitly on purpose: KeyboardSession's public surface must
		// not grow a SetKeyMap/SetLogoLightColor that skips the capability question entirely.
		var publicNames = typeof(KeyboardSession)
			.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
			.Select(m => m.Name)
			.ToHashSet();

		foreach (var (_, facade) in StaticGates.Append((typeof(IHasFuncBlock), typeof(IProgrammableKeyboardFeatures))))
			foreach (var member in facade.GetMethods())
				Assert.That(publicNames, Does.Not.Contain(member.Name),
					$"{facade.Name}.{member.Name} is reachable on KeyboardSession without going " +
					$"through TryGetFeatures/GetFeatures, so the runtime capability gate can be skipped.");
	}
}
