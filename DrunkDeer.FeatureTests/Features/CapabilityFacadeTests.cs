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
/// <para>The two gates are deliberately not identical for the programmable facade: see
/// <see cref="ProgrammableFacade_FollowsFirmware_NotTheModelMarker"/>.</para>
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

	// Firmware 255 puts every model in the best precision mode its hardware allows, so the model's
	// static marker and the session's runtime answer are being asked the same question.
	private static KeyboardSession OpenAtNewestFirmware(Type markerType) =>
		new(new FakeKeyboardConnection(ModelRegistry.GetInfo(SlugOf(markerType)), firmwareVersion: 255));

	// TryGetFeatures is generic over the facade; the facade set under test is data here.
	private static bool TryGetFeatures(KeyboardSession session, Type facade)
	{
		var method = typeof(KeyboardSession).GetMethod(nameof(KeyboardSession.TryGetFeatures))!
			.MakeGenericMethod(facade);
		object?[] args = [null];
		return (bool)method.Invoke(session, args)!;
	}

	// The facades whose availability is a fixed property of the model, each paired with the marker
	// interface that makes the same claim at compile time. IProgrammableKeyboardFeatures is absent
	// on purpose: it depends on firmware, so no marker can settle it.
	private static readonly (Type Marker, Type Facade)[] StaticGates =
	[
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
	public void ProgrammableFacade_IsGrantedOnEveryModelWhoseMarkerClaimsFuncBlock()
	{
		var claiming = MarkerTypes.Where(t => typeof(IHasFuncBlock).IsAssignableFrom(t)).ToArray();

		// Guard against the loop below passing because it never ran.
		Assert.That(claiming, Is.Not.Empty, "No model marker implements IHasFuncBlock — codegen is broken.");

		foreach (var markerType in claiming)
		{
			using var session = OpenAtNewestFirmware(markerType);
			Assert.That(session.TryGetFeatures<IProgrammableKeyboardFeatures>(out _), Is.True,
				$"KeyboardSession<{markerType.Name}> exposes the programmable API at compile time " +
				$"(its marker implements IHasFuncBlock), but the runtime facade refuses it even at " +
				$"firmware 255. The compile-time gate is promising hardware that cannot deliver.");
		}
	}

	[Test]
	public void ProgrammableFacade_FollowsFirmware_NotTheModelMarker()
	{
		// The base A75 acquires Kun precision — and with it the FuncBlock gateway — at firmware 35.
		// Same model, same marker type, different answer. This is why the facade is the only honest
		// way to ask, and why there is no Capabilities flag for FuncBlock.
		var a75 = ModelRegistry.GetInfo(ModelSlugs.A75)!;
		Assert.That(a75.KunPrecisionMinFirmware, Is.EqualTo((byte)35),
			"This test is pinned to the A75's Kun firmware threshold.");

		using (var oldFirmware = new KeyboardSession(new FakeKeyboardConnection(a75, firmwareVersion: 34)))
			Assert.That(oldFirmware.TryGetFeatures<IProgrammableKeyboardFeatures>(out _), Is.False,
				"An A75 on firmware 34 runs in Standard precision and has no FuncBlock gateway.");

		using (var newFirmware = new KeyboardSession(new FakeKeyboardConnection(a75, firmwareVersion: 35)))
			Assert.That(newFirmware.TryGetFeatures<IProgrammableKeyboardFeatures>(out _), Is.True,
				"An A75 on firmware 35 runs in Kun precision and does have the FuncBlock gateway.");
	}

	[Test]
	public void ProgrammableFacade_GrantsMoreThanTheMarkers_OnFirmwareUpgradedModels()
	{
		// The G75 carries no capability flags, so its marker type implements nothing and
		// KeyboardSession<G75> offers no programmable API at all. The same board on firmware 13+
		// runs in Kun mode, and the runtime facade correctly offers it. The runtime gate being
		// strictly more permissive than the compile-time one here is the point of the facades, not
		// drift — don't "fix" the tests above into a strict equality.
		Assert.That(typeof(IHasFuncBlock).IsAssignableFrom(typeof(G75)), Is.False);

		using var session = new KeyboardSession(
			new FakeKeyboardConnection(ModelRegistry.GetInfo(ModelSlugs.G75), firmwareVersion: 13));
		Assert.That(session.TryGetFeatures<IProgrammableKeyboardFeatures>(out _), Is.True);
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
	public void GetFeatures_WhenUnsupported_ExplainsWhyWithModelAndFirmware()
	{
		// A75 on firmware 1: Standard precision, no gateway.
		using var session = new KeyboardSession(
			new FakeKeyboardConnection(ModelRegistry.GetInfo(ModelSlugs.A75), firmwareVersion: 1));

		var ex = Assert.Throws<DrunkDeerCapabilityException>(
			() => session.GetFeatures<IProgrammableKeyboardFeatures>());

		// The firmware matters as much as the model: this same board is programmable on fw 35.
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
