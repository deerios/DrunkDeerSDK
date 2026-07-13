using DrunkDeer.FeatureTests.Fakes;
using DrunkDeer.Protocol;
using NUnit.Framework;

namespace DrunkDeer.FeatureTests.Features;

[TestFixture]
public class CommonConfigTests
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

	// ── SetCommonConfig ───────────────────────────────────────────────────────

	[Test]
	public void SetCommonConfig_SendsB5Packet()
	{
		_fake.EnqueueAck(0xB5);
		_session.SetCommonConfig(false, false, LastWinRapidTriggerMode.Disabled, false);
		Assert.That(_fake.SentPackets[0][0], Is.EqualTo(0xB5));
	}

	[Test]
	public void SetCommonConfig_EncodesTurboModeAtByte7()
	{
		_fake.EnqueueAck(0xB5);
		_session.SetCommonConfig(turboMode: true, rapidTriggerMode: false, lastWinRapidTriggerMode: LastWinRapidTriggerMode.Disabled, rapidTriggerAutoMatch: false);
		Assert.That(_fake.SentPackets[0][7], Is.EqualTo(1));
	}

	[Test]
	public void SetCommonConfig_EncodesRapidTriggerAtByte8()
	{
		_fake.EnqueueAck(0xB5);
		_session.SetCommonConfig(turboMode: false, rapidTriggerMode: true, lastWinRapidTriggerMode: LastWinRapidTriggerMode.Disabled, rapidTriggerAutoMatch: false);
		Assert.That(_fake.SentPackets[0][8], Is.EqualTo(1));
	}

	[Test]
	public void SetCommonConfig_EncodesLastWinRtModeAtByte10()
	{
		_fake.EnqueueAck(0xB5);
		_session.SetCommonConfig(turboMode: false, rapidTriggerMode: false, lastWinRapidTriggerMode: LastWinRapidTriggerMode.Both, rapidTriggerAutoMatch: false);
		Assert.That(_fake.SentPackets[0][10], Is.EqualTo(3));
	}

	[Test]
	public void SetCommonConfig_EncodesAutoMatchAtByte11()
	{
		_fake.EnqueueAck(0xB5);
		_session.SetCommonConfig(turboMode: false, rapidTriggerMode: false, lastWinRapidTriggerMode: LastWinRapidTriggerMode.Disabled, rapidTriggerAutoMatch: true);
		Assert.That(_fake.SentPackets[0][11], Is.EqualTo(1));
	}

	// ── EnableRapidTrigger / DisableRapidTrigger ──────────────────────────────

	[Test]
	public void EnableRapidTrigger_SetsRapidTriggerByteToOne()
	{
		_fake.EnqueueAck(0xB5);
		_session.EnableRapidTrigger();
		Assert.That(_fake.SentPackets[0][8], Is.EqualTo(1));
	}

	[Test]
	public void EnableRapidTrigger_WithAutoMatch_SetsAutoMatchByteToOne()
	{
		_fake.EnqueueAck(0xB5);
		_session.EnableRapidTrigger(autoMatch: true);
		Assert.That(_fake.SentPackets[0][11], Is.EqualTo(1));
	}

	[Test]
	public void DisableRapidTrigger_SetsRapidTriggerByteToZero()
	{
		// Enable first
		_fake.EnqueueAck(0xB5);
		_session.EnableRapidTrigger();
		_fake.SentPackets.Clear();

		_fake.EnqueueAck(0xB5);
		_session.DisableRapidTrigger();
		Assert.That(_fake.SentPackets[0][8], Is.EqualTo(0));
	}

	// ── EnableAutoMatch / DisableAutoMatch ────────────────────────────────────
	// API-7: these send 0xFD 0x0C directly and must also update the _rapidTriggerAutoMatch
	// mirror, or the next SendCommonConfig() call (from any Enable/Disable RT/Turbo call)
	// rebuilds its B5 packet from the stale mirror and reverts auto-match on the keyboard.

	[Test]
	public void EnableAutoMatch_ThenDisableRapidTrigger_KeepsAutoMatchByteSet()
	{
		_session.EnableAutoMatch();
		_fake.SentPackets.Clear();

		_fake.EnqueueAck(0xB5);
		_session.DisableRapidTrigger();
		Assert.That(_fake.SentPackets[0][11], Is.EqualTo(1));
	}

	[Test]
	public void DisableAutoMatch_ThenEnableRapidTrigger_KeepsAutoMatchByteClear()
	{
		_session.EnableAutoMatch();
		_session.DisableAutoMatch();
		_fake.SentPackets.Clear();

		_fake.EnqueueAck(0xB5);
		_session.EnableRapidTrigger();
		Assert.That(_fake.SentPackets[0][11], Is.EqualTo(0));
	}

	// ── EnableTurboMode / DisableTurboMode ────────────────────────────────────

	[Test]
	public void EnableTurboMode_SetsTurboByteToOne()
	{
		_fake.EnqueueAck(0xB5);
		_session.EnableTurboMode();
		Assert.That(_fake.SentPackets[0][7], Is.EqualTo(1));
	}

	[Test]
	public void DisableTurboMode_SetsTurboByteToZero()
	{
		_fake.EnqueueAck(0xB5);
		_session.EnableTurboMode();
		_fake.SentPackets.Clear();

		_fake.EnqueueAck(0xB5);
		_session.DisableTurboMode();
		Assert.That(_fake.SentPackets[0][7], Is.EqualTo(0));
	}

	// ── SetLastWinRapidTriggerMode ────────────────────────────────────────────

	[Test]
	public void SetLastWinRapidTriggerMode_SendsFC0APacket()
	{
		_session.SetLastWinRapidTriggerMode(LastWinRapidTriggerMode.Both);
		var pkt = _fake.SentPackets[0];
		Assert.Multiple(() =>
		{
			Assert.That(pkt[0], Is.EqualTo(0xFC));
			Assert.That(pkt[1], Is.EqualTo(0x0A));
			Assert.That(pkt[2], Is.EqualTo((byte)LastWinRapidTriggerMode.Both));
		});
	}

	// ── ConfigureLastWinReplace ───────────────────────────────────────────────

	[Test]
	public void ConfigureLastWinReplace_Enabled_SendsFC0B01()
	{
		_session.ConfigureLastWinReplace(true);
		var pkt = _fake.SentPackets[0];
		Assert.Multiple(() =>
		{
			Assert.That(pkt[0], Is.EqualTo(0xFC));
			Assert.That(pkt[1], Is.EqualTo(0x0B));
			Assert.That(pkt[2], Is.EqualTo(1));
		});
	}

	[Test]
	public void ConfigureLastWinReplace_Disabled_SendsFC0B00()
	{
		_session.ConfigureLastWinReplace(false);
		Assert.That(_fake.SentPackets[0][2], Is.EqualTo(0));
	}

	// ── ConfigureLastWinPairs ─────────────────────────────────────────────────

	[Test]
	public void ConfigureLastWinPairs_Raw_SendsFC01Packet()
	{
		_session.ConfigureLastWinPairs((16, 26));
		var pkt = _fake.SentPackets[0];
		Assert.Multiple(() =>
		{
			Assert.That(pkt[0], Is.EqualTo(0xFC));
			Assert.That(pkt[1], Is.EqualTo(0x01));
			Assert.That(pkt[2], Is.EqualTo(0x00));
			Assert.That(pkt[3], Is.EqualTo(1)); // one pair
		});
	}

	[Test]
	public void ConfigureLastWinPairs_Raw_EncodesFirstPairAtOffset4()
	{
		_session.ConfigureLastWinPairs((16, 26));
		var pkt = _fake.SentPackets[0];
		Assert.Multiple(() =>
		{
			Assert.That(pkt[4], Is.EqualTo(16)); // keyA
			Assert.That(pkt[5], Is.EqualTo(26)); // keyB
		});
	}

	[Test]
	public void ConfigureLastWinPairs_TooManyPairs_Throws()
	{
		var pairs = Enumerable.Range(0, 15).Select(i => (i, i + 1)).ToArray();
		Assert.Throws<ArgumentException>(() => _session.ConfigureLastWinPairs(pairs));
	}

	[Test]
	public void ConfigureLastWinPairs_DDKey_ResolvesIndicesCorrectly()
	{
		int idxA = _session.GetKeyIndex(DDKey.A);
		int idxD = _session.GetKeyIndex(DDKey.D);

		_session.ConfigureLastWinPairs((DDKey.A, DDKey.D));
		var pkt = _fake.SentPackets[0];
		Assert.Multiple(() =>
		{
			Assert.That(pkt[4], Is.EqualTo((byte)idxA));
			Assert.That(pkt[5], Is.EqualTo((byte)idxD));
		});
	}
}
