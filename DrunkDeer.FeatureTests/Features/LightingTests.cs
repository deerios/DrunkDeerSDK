using DrunkDeer.FeatureTests.Fakes;
using DrunkDeer.Protocol;
using NUnit.Framework;

namespace DrunkDeer.FeatureTests.Features;

[TestFixture]
public class LightingTests
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

	private void EnqueueRgbAcks()
	{
		// A75 has 7 RGB packets (91 LEDs / 13 per packet)
		_fake.EnqueueAcks(0xAE, _session.LightingKeyCount / 13 + (_session.LightingKeyCount % 13 > 0 ? 1 : 0));
	}

	// ── SetUniformLighting ────────────────────────────────────────────────────

	[Test]
	public void SetUniformLighting_SendsRgbPacketsWithCorrectFirstByte()
	{
		EnqueueRgbAcks();
		_session.SetUniformLighting(255, 0, 0);
		Assert.That(_fake.SentPackets.All(p => p[0] == 0xAE), Is.True);
	}

	[Test]
	public void SetUniformLighting_BrightnessEncodedAtByte6()
	{
		EnqueueRgbAcks();
		_session.SetUniformLighting(0, 255, 0, brightness: 5);
		// All RGB packets carry brightness at buf[6]
		foreach (var pkt in _fake.SentPackets)
			Assert.That(pkt[6], Is.EqualTo(5));
	}

	[Test]
	public void SetUniformLighting_FirstEntry_HasCorrectRgbAtOffset8()
	{
		EnqueueRgbAcks();
		_session.SetUniformLighting(0xAA, 0xBB, 0xCC);
		// First RGB packet; entry 0 at offset 8: [IndexFlag, R, G, B]
		var pkt = _fake.SentPackets[0];
		Assert.Multiple(() =>
		{
			Assert.That(pkt[9], Is.EqualTo(0xAA)); // R
			Assert.That(pkt[10], Is.EqualTo(0xBB)); // G
			Assert.That(pkt[11], Is.EqualTo(0xCC)); // B
		});
	}

	[Test]
	public void SetUniformLighting_SentinelByte_PlacedAfterLastEntry()
	{
		EnqueueRgbAcks();
		_session.SetUniformLighting(255, 255, 255);
		// Last packet has fewer than 13 entries; sentinel 0xFF written after them
		var lastPkt = _fake.SentPackets[^1];
		int remainder = _session.LightingKeyCount % 13;
		int sentinelOffset = 8 + (remainder > 0 ? remainder : 13) * RgbEntry.ByteSize;
		Assert.That(lastPkt[sentinelOffset], Is.EqualTo(0xFF));
	}

	// ── DisableLighting ───────────────────────────────────────────────────────

	[Test]
	public void DisableLighting_SendsSingleSetLightingOffPacket()
	{
		_fake.EnqueueAck(0xAE);
		_session.DisableLighting();
		Assert.That(_fake.SentPackets, Has.Count.EqualTo(1));
	}

	[Test]
	public void DisableLighting_PacketHasCorrectHeader()
	{
		_fake.EnqueueAck(0xAE);
		_session.DisableLighting();
		var pkt = _fake.SentPackets[0];
		Assert.Multiple(() =>
		{
			Assert.That(pkt[0], Is.EqualTo(0xAE));
			Assert.That(pkt[1], Is.EqualTo(0x01));
			Assert.That(pkt[2], Is.EqualTo(0x00));
			Assert.That(pkt[4], Is.EqualTo(0x05));
			Assert.That(pkt[5], Is.EqualTo(0x09));
		});
	}

	// ── SetKeyColor ───────────────────────────────────────────────────────────

	[Test]
	public void SetKeyColor_DDKey_UpdatesIndexFlagInPacket()
	{
		EnqueueRgbAcks();
		_session.SetKeyColor(DDKey.A, 0xFF, 0x00, 0x00);
		int aIdx = _session.GetKeyIndex(DDKey.A);
		// Find the RGB packet that contains key A
		bool found = false;
		foreach (var pkt in _fake.SentPackets)
		{
			for (int e = 0; e < 13; e++)
			{
				int offset = 8 + e * 4;
				if (offset + 3 >= 64) break;
				byte flag = pkt[offset];
				if ((flag & 0xFF) == 0xFF) break; // sentinel
				if ((flag & 0x7F) == aIdx)
				{
					Assert.That(pkt[offset + 1], Is.EqualTo(0xFF)); // R
					Assert.That(pkt[offset + 2], Is.EqualTo(0x00)); // G
					Assert.That(pkt[offset + 3], Is.EqualTo(0x00)); // B
					found = true;
					break;
				}
			}
			if (found) break;
		}
		Assert.That(found, Is.True, "Key A not found in any RGB packet");
	}
}
