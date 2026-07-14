using DrunkDeer.FeatureTests.Fakes;
using DrunkDeer.Protocol;
using NUnit.Framework;

namespace DrunkDeer.FeatureTests.Features;

/// <summary>
/// Coverage for the public shadow-state read-back getters (PLAN-GAP-8): a consumer (CLI/UI)
/// must be able to read back the colours and depth profiles this session last wrote / seeded,
/// without tracking every write itself.
/// </summary>
[TestFixture]
public class ShadowStateReadbackTests
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

	private void EnqueueRgbAcks() =>
		_fake.EnqueueAcks(0xAE, _session.LightingKeyCount / 13 + (_session.LightingKeyCount % 13 > 0 ? 1 : 0));

	[Test]
	public void GetKeyColor_ReflectsLastSetKeyColor()
	{
		EnqueueRgbAcks();
		_session.SetKeyColor(DDKey.Escape, 0x12, 0x34, 0x56);
		Assert.That(_session.GetKeyColor(DDKey.Escape), Is.EqualTo(((byte)0x12, (byte)0x34, (byte)0x56)));
	}

	[Test]
	public void GetKeyColor_UnsetKey_IsBlack()
	{
		Assert.That(_session.GetKeyColor(DDKey.Escape), Is.EqualTo(((byte)0, (byte)0, (byte)0)));
	}

	[Test]
	public void DepthProfiles_FreshSession_ReturnSeededDefaults()
	{
		var actuation = _session.GetActuationProfile();
		var downstroke = _session.GetDownstrokeProfile();
		var upstroke = _session.GetUpstrokeProfile();

		// The ctor seeds actuation at 2.0 mm and downstroke/upstroke at 0.25 mm for every key.
		Assert.That(actuation.Values, Is.All.EqualTo(2.0f));
		Assert.That(downstroke.Values, Is.All.EqualTo(0.25f));
		Assert.That(upstroke.Values, Is.All.EqualTo(0.25f));
		Assert.That(actuation, Does.ContainKey(DDKey.W));
	}

	[Test]
	public void GetActuationProfile_ReflectsUniformWrite()
	{
		_fake.EnqueueStandardKeyPointAcks();
		_session.SetActuationPoint(1.5f);

		Assert.That(_session.GetActuationProfile().Values, Is.All.EqualTo(1.5f));
	}

	[Test]
	public void GetDownstrokeProfile_ReflectsPerKeyWrite()
	{
		_fake.EnqueueStandardKeyPointAcks();
		_session.SetDownstrokePoint(0.3f, DDKey.W);

		var profile = _session.GetDownstrokeProfile();
		Assert.That(profile[DDKey.W], Is.EqualTo(0.3f));
		// Other keys keep the seeded 0.25 mm default.
		Assert.That(profile[DDKey.A], Is.EqualTo(0.25f));
	}
}
