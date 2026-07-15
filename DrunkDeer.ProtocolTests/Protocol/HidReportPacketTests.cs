using DrunkDeer.Protocol;
using NUnit.Framework;

namespace DrunkDeer.ProtocolTests.Protocol;

/// <summary>
/// Pins the report capacity rule that both transports depend on. The A75's report 4 holds 63
/// bytes, one less than this SDK's uniform 64-byte packet, so every packet sent to it is one
/// byte too long. Truncating blindly is what breaks the identity handshake; refusing to truncate
/// at all is what stops it from working at all. This is the rule that threads that needle, and it
/// is now the only copy of it - the desktop transport and the browser transport both call it.
/// </summary>
[TestFixture]
public class HidReportPacketTests
{
	[Test]
	public void FitToCapacity_ShorterThanCapacity_SendsWholePacket()
	{
		var packet = new byte[10];
		Assert.That(HidReportPacket.FitToCapacity(packet, 63), Is.EqualTo(10));
	}

	[Test]
	public void FitToCapacity_ExactlyCapacity_SendsWholePacket()
	{
		var packet = new byte[63];
		packet[62] = 0xFF; // real payload in the last byte: still fits, so nothing is dropped
		Assert.That(HidReportPacket.FitToCapacity(packet, 63), Is.EqualTo(63));
	}

	/// <summary>
	/// The case that matters: a 64-byte codegen packet whose 64th byte is padding, on a 63-byte
	/// report. Dropping that byte is safe because nothing reads it back.
	/// </summary>
	[Test]
	public void FitToCapacity_TrailingPaddingOnly_TruncatesToCapacity()
	{
		var packet = new byte[64];
		packet[0] = 0xA0; // IdentityRequest-shaped: header, then zero padding to 64
		packet[1] = 0x02;

		Assert.That(HidReportPacket.FitToCapacity(packet, 63), Is.EqualTo(63));
	}

	/// <summary>
	/// The trap the rule exists to catch: if the overflowing byte is real data, truncating would
	/// silently corrupt the message, so it throws instead.
	/// </summary>
	[Test]
	public void FitToCapacity_NonZeroPastCapacity_Throws()
	{
		var packet = new byte[64];
		packet[63] = 0x01;

		var ex = Assert.Throws<ArgumentException>(() => HidReportPacket.FitToCapacity(packet, 63));
		Assert.That(ex!.Message, Does.Contain("byte 63"));
	}

	[Test]
	public void FitToCapacity_RealIdentityRequest_FitsA75Report()
	{
		// The regression this guards: IdentityRequest.Build() is 64 bytes of which only the first
		// seven are the header, so it must fit a 63-byte report without complaint. Losing this
		// means the keyboard never identifies itself and nothing else works.
		Assert.That(
			HidReportPacket.FitToCapacity(IdentityHandshake.BuildRequest(), HidReportPacket.MinCommandCapacity),
			Is.EqualTo(63));
	}

	[Test]
	public void FitToCapacity_ZeroCapacity_Throws()
	{
		Assert.Throws<ArgumentOutOfRangeException>(() => HidReportPacket.FitToCapacity(new byte[1], 0));
	}
}
