using DrunkDeer.FeatureTests.Fakes;
using DrunkDeer.Protocol;
using NUnit.Framework;

namespace DrunkDeer.FeatureTests.Features;

[TestFixture]
public class ActuationPointTests
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

	// ── SetActuationPoint (uniform) ───────────────────────────────────────────

	[Test]
	public void SetActuationPoint_Uniform_SendsThreePackets()
	{
		_fake.EnqueueStandardKeyPointAcks();
		_session.SetActuationPoint(2.0f);
		Assert.That(_fake.SentPackets, Has.Count.EqualTo(3));
	}

	[Test]
	public void SetActuationPoint_Uniform_PacketsHaveCorrectSubCommand()
	{
		_fake.EnqueueStandardKeyPointAcks();
		_session.SetActuationPoint(2.0f);
		foreach (var pkt in _fake.SentPackets)
		{
			Assert.That(pkt[0], Is.EqualTo(0xB6)); // WriteActuationPointStandard
			Assert.That(pkt[1], Is.EqualTo(0x01));
		}
	}

	[Test]
	public void SetActuationPoint_Uniform_AllKeyValuesMatchDepth()
	{
		_fake.EnqueueStandardKeyPointAcks();
		_session.SetActuationPoint(2.0f); // 2.0 mm × 10 = 20 = 0x14
		byte expected = 20;
		// Packet 0 carries keys 0–58 (59 keys) at buf[4..62]
		var pkt0 = _fake.SentPackets[0];
		Assert.That(pkt0[3], Is.EqualTo(0)); // packet index 0
		for (int i = 0; i < 59; i++)
			Assert.That(pkt0[4 + i], Is.EqualTo(expected), $"key {i} in packet 0");
	}

	[Test]
	public void SetActuationPoint_Uniform_Packet1CarriesKeys59To117()
	{
		_fake.EnqueueStandardKeyPointAcks();
		_session.SetActuationPoint(1.0f); // raw = 10
		var pkt1 = _fake.SentPackets[1];
		Assert.That(pkt1[3], Is.EqualTo(1)); // packet index 1
		for (int i = 0; i < 59; i++)
			Assert.That(pkt1[4 + i], Is.EqualTo(10), $"key {59 + i} in packet 1");
	}

	[Test]
	public void SetActuationPoint_Uniform_Packet2CarriesKeys118To126()
	{
		_fake.EnqueueStandardKeyPointAcks();
		_session.SetActuationPoint(3.8f); // 3.8 × 10 = 38
		var pkt2 = _fake.SentPackets[2];
		Assert.That(pkt2[3], Is.EqualTo(2)); // packet index 2
		for (int i = 0; i < 9; i++)
			Assert.That(pkt2[4 + i], Is.EqualTo(38), $"key {118 + i} in packet 2");
	}

	// ── SetActuationPoints (per-key profile) ─────────────────────────────────

	[Test]
	public void SetActuationPoints_PerKey_FirstKeyEncodedInPacket0()
	{
		_fake.EnqueueStandardKeyPointAcks();
		var profile = new KeyDepthProfileBuilder().Default(2.0f).Key(DDKey.Escape, 1.0f).Build();
		_session.SetActuationPoints(profile);
		int escIdx = _session.GetKeyIndex(DDKey.Escape); // 0 on A75
		Assert.That(_fake.SentPackets[0][4 + escIdx], Is.EqualTo(10));
	}

	[Test]
	public void SetActuationPoints_PerKey_LastKeyInPacket1EncodedCorrectly()
	{
		_fake.EnqueueStandardKeyPointAcks();
		var profile = new KeyDepthProfileBuilder().Default(2.0f).Key(DDKey.Menu, 0.5f).Build();
		_session.SetActuationPoints(profile);
		int menuIdx    = _session.GetKeyIndex(DDKey.Menu); // 117 on A75
		int offsetInP1 = menuIdx - 59;
		Assert.That(_fake.SentPackets[1][4 + offsetInP1], Is.EqualTo(5));
	}

	[Test]
	public void SetActuationPoint_DepthBelowMin_Throws()
	{
		Assert.Throws<ArgumentOutOfRangeException>(() => _session.SetActuationPoint(0.0f));
	}

	[Test]
	public void SetActuationPoint_DepthAboveMax_Throws()
	{
		Assert.Throws<ArgumentOutOfRangeException>(() => _session.SetActuationPoint(100f));
	}

	// ── SetDownstrokePoint ────────────────────────────────────────────────────

	[Test]
	public void SetDownstrokePoint_Uniform_SendsThreePackets()
	{
		_fake.EnqueueStandardKeyPointAcks();
		_session.SetDownstrokePoint(1.5f);
		Assert.That(_fake.SentPackets, Has.Count.EqualTo(3));
	}

	[Test]
	public void SetDownstrokePoint_Uniform_UsesSubCommand0x04()
	{
		_fake.EnqueueStandardKeyPointAcks();
		_session.SetDownstrokePoint(1.5f);
		foreach (var pkt in _fake.SentPackets)
			Assert.That(pkt[1], Is.EqualTo(0x04));
	}

	[Test]
	public void SetDownstrokePoint_Uniform_ValuesEncodedCorrectly()
	{
		_fake.EnqueueStandardKeyPointAcks();
		_session.SetDownstrokePoint(1.5f); // 1.5 × 10 = 15
		Assert.That(_fake.SentPackets[0][4], Is.EqualTo(15));
	}

	// ── SetUpstrokePoint ──────────────────────────────────────────────────────

	[Test]
	public void SetUpstrokePoint_Uniform_SendsThreePackets()
	{
		_fake.EnqueueStandardKeyPointAcks();
		_session.SetUpstrokePoint(0.5f);
		Assert.That(_fake.SentPackets, Has.Count.EqualTo(3));
	}

	[Test]
	public void SetUpstrokePoint_Uniform_UsesSubCommand0x05()
	{
		_fake.EnqueueStandardKeyPointAcks();
		_session.SetUpstrokePoint(0.5f);
		foreach (var pkt in _fake.SentPackets)
			Assert.That(pkt[1], Is.EqualTo(0x05));
	}

	// ── Per-key DDKey overloads ───────────────────────────────────────────────

	[Test]
	public void SetActuationPoint_DDKey_UpdatesOnlyNamedKeys()
	{
		// Two calls — first to establish a baseline, then per-key
		_fake.EnqueueStandardKeyPointAcks();
		_session.SetActuationPoint(2.0f); // baseline: all keys at raw 20
		_fake.SentPackets.Clear();

		_fake.EnqueueStandardKeyPointAcks();
		_session.SetActuationPoint(1.0f, DDKey.A); // only A changes -> raw 10

		int aIdx = _session.GetKeyIndex(DDKey.A); // resolve actual layout index at runtime
												  // Packet 1 covers keys 59–117; A lives somewhere in that range.
		int aOffsetInPkt1 = aIdx - 59;

		var pkt0 = _fake.SentPackets[0];
		var pkt1 = _fake.SentPackets[1];
		Assert.That(pkt0[4], Is.EqualTo(20));               // key 0 unchanged
		Assert.That(pkt1[4 + aOffsetInPkt1], Is.EqualTo(10)); // A updated to 1.0 mm = raw 10
	}
}
