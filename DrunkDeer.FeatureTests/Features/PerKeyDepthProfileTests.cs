using DrunkDeer.FeatureTests.Fakes;
using DrunkDeer.Protocol;
using NUnit.Framework;

namespace DrunkDeer.FeatureTests.Features;

/// <summary>
/// API-3 regression coverage: the first per-key downstroke/upstroke call on a fresh session
/// must not throw, and a CaptureProfile -> ApplyProfile round-trip must not throw for
/// non-uniform (per-key) depths.
/// </summary>
[TestFixture]
public class PerKeyDepthProfileTests
{
	private FakeKeyboardConnection _fake = null!;
	private KeyboardSession _session = null!;

	[SetUp]
	public void SetUp()
	{
		_fake    = new FakeKeyboardConnection();
		_session = new KeyboardSession(_fake);
	}

	[TearDown]
	public void TearDown() => _session.Dispose();

	[Test]
	public void SetDownstrokePoint_DDKey_FirstCallOnFreshSession_DoesNotThrow()
	{
		_fake.EnqueueStandardKeyPointAcks();
		Assert.DoesNotThrow(() => _session.SetDownstrokePoint(0.3f, DDKey.W));
	}

	[Test]
	public void SetUpstrokePoint_DDKey_FirstCallOnFreshSession_DoesNotThrow()
	{
		_fake.EnqueueStandardKeyPointAcks();
		Assert.DoesNotThrow(() => _session.SetUpstrokePoint(0.3f, DDKey.W));
	}

	[Test]
	public void CaptureProfile_ApplyProfile_NonUniformDepths_RoundTripsWithoutThrowing()
	{
		using var fake    = new FakeKeyboardConnection(ModelRegistry.GetInfo(ModelSlugs.G65M1));
		using var session = new KeyboardSession(fake);

		// Non-uniform trigger data: 128 keys x 8-byte KeyTriggerConfig, actuation raw = 20 + i
		// (so no two keys share a value -> CaptureProfile sees a non-uniform profile and emits
		// Default: 0 with a Keys map for actuation/downstroke/upstroke).
		var funcBlock = new byte[64];
		var triggers = new byte[1024];
		for (int i = 0; i < 128; i++)
		{
			var cfg = new KeyTriggerConfig
			{
				SwitchType = 0, KeyMode = 1, Priority = 0,
				Actuation = (byte)(20 + i % 50),
				RtPress   = 25,
				RtRelease = 25,
			};
			KeyTriggerConfig.Encode(cfg, triggers.AsSpan(i * 8));
		}

		fake.EnqueueGatewayRead(funcBlock);
		fake.EnqueueGatewayRead(triggers);
		var profile = session.CaptureProfile();

		Assert.That(profile.Actuation, Is.Not.Null);
		Assert.That(profile.Actuation!.Default, Is.EqualTo(0f),
			"Precondition: a non-uniform profile must capture with Default 0 to exercise the bug.");

		// ApplyProfile writes actuation, downstroke, and upstroke in turn (all three are
		// populated by CaptureProfile whenever HasFuncBlock is true); each is a per-key write
		// of 3 standard-precision packets needing 3 ACKs.
		fake.EnqueueStandardKeyPointAcks();
		fake.EnqueueStandardKeyPointAcks();
		fake.EnqueueStandardKeyPointAcks();
		// Captured RapidTrigger/AutoMatch and TurboMode flags each round-trip via their own
		// SendCommonConfig call.
		fake.EnqueueAck(0xB5);
		fake.EnqueueAck(0xB5);
		Assert.DoesNotThrow(() => session.ApplyProfile(profile));
	}
}
