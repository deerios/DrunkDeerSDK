using DrunkDeer.FeatureTests.Fakes;
using DrunkDeer.Protocol;
using NUnit.Framework;

namespace DrunkDeer.FeatureTests.Features;

/// <summary>
/// TRN-4 regression coverage: WriteKeyPointAcknowledgeHighPrecision.Matches accepts any 0xFD
/// packet, which collides with 0xFD 0x06 travel frames - so a straggler travel packet still
/// sitting in the read buffer right after polling stops could be mistaken for a write's ACK.
/// The fix flushes the read buffer before the first send of a high-precision key-point write.
/// </summary>
[TestFixture]
public class HighPrecisionKeyPointWriteTests
{
	[Test]
	public void SetActuationPoint_Uniform_HpModel_FlushesBeforeWriting()
	{
		var fake = new FakeKeyboardConnection(ModelRegistry.GetInfo(ModelSlugs.A75Ultra));
		using var session = new KeyboardSession(fake);

		// Simulate a straggler travel packet sitting in the buffer from just before the write -
		// if WriteKeyPointHighPrecision didn't flush first, this would be consumed as section 0's
		// ACK instead of the real one below.
		fake.EnqueueResponse(BuildTravelPacket());
		fake.OnFlush = () => fake.EnqueueAcks(0xFD, 5);

		session.SetActuationPoint(2.0f);

		Assert.That(fake.FlushCount, Is.GreaterThanOrEqualTo(1));
		Assert.That(fake.SentPackets, Has.Count.EqualTo(5));
	}

	private static byte[] BuildTravelPacket()
	{
		var buf = new byte[64];
		buf[0] = 0xFD;
		buf[1] = 0x06;
		return buf;
	}
}
