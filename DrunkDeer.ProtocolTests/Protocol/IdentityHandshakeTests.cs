using DrunkDeer.Protocol;
using NUnit.Framework;

namespace DrunkDeer.ProtocolTests.Protocol;

/// <summary>
/// Pins what an identity response means, now that the desktop and browser transports share this
/// instead of each deciding for itself. The retry loops stay per-transport (one blocks, one
/// awaits), but "is this response usable" and "what does it say" are here, and drift between the
/// two would show up as a browser session trusting a report the desktop would have rejected.
/// </summary>
[TestFixture]
public class IdentityHandshakeTests
{
	// A75 ANSI: header 0xA0 0x02 0x00, identity bytes at 4..6, firmware at 7.
	private static byte[] BuildA75Response(int length = 64)
	{
		var buf = new byte[length];
		buf[0] = 0xA0; buf[1] = 0x02; buf[2] = 0x00;
		buf[4] = 0x0B; buf[5] = 0x04; buf[6] = 0x01; // A75 ANSI
		buf[7] = 0x2A;  // firmware
		buf[15] = 0x01; // turbo on
		buf[16] = 0x01; // rapid trigger on
		buf[19] = 0x02; // last win
		buf[30] = 0x01; // auto match on
		return buf;
	}

	[Test]
	public void Interpret_A75Response_ResolvesModelAndSettings()
	{
		var result = IdentityHandshake.Interpret(BuildA75Response());

		Assert.Multiple(() =>
		{
			Assert.That(result.Model.Slug, Is.EqualTo(ModelSlugs.A75));
			Assert.That(result.Variant, Is.EqualTo("ansi"));
			Assert.That(result.FirmwareVersion, Is.EqualTo(0x2A));
			Assert.That(result.InitialTurboValue, Is.EqualTo(1));
			Assert.That(result.InitialRapidTriggerEnabled, Is.EqualTo(1));
			Assert.That(result.InitialLastWinValue, Is.EqualTo(2));
			Assert.That(result.InitialRapidTriggerAutoMatch, Is.EqualTo(1));
		});
	}

	/// <summary>
	/// The trap this constant exists for: the header check passes on 3 bytes, but the accessors
	/// read out to byte 32. A short report has to be rejected and retried, not indexed past its
	/// end - which would throw IndexOutOfRange out of a connection attempt instead.
	/// </summary>
	[Test]
	public void IsComplete_HeaderMatchesButTruncated_IsNotComplete()
	{
		var truncated = BuildA75Response(IdentityHandshake.MinResponseLength - 1);

		Assert.Multiple(() =>
		{
			Assert.That(IdentityResponse.Matches(truncated), Is.True, "header alone still matches");
			Assert.That(IdentityHandshake.IsComplete(truncated), Is.False);
		});
	}

	[Test]
	public void IsComplete_AtMinimumLength_IsComplete()
	{
		Assert.That(IdentityHandshake.IsComplete(BuildA75Response(IdentityHandshake.MinResponseLength)), Is.True);
	}

	[Test]
	public void IsComplete_WrongHeader_IsNotComplete()
	{
		var travel = new byte[64];
		travel[0] = 0xB7; // an unsolicited travel packet, not an identity response

		Assert.That(IdentityHandshake.IsComplete(travel), Is.False);
	}

	[Test]
	public void Interpret_TruncatedResponse_ThrowsRatherThanReadingPastEnd()
	{
		var truncated = BuildA75Response(IdentityHandshake.MinResponseLength - 1);

		var ex = Assert.Throws<InvalidOperationException>(() => IdentityHandshake.Interpret(truncated));
		Assert.That(ex!.Message, Does.Contain("complete identity response"));
	}

	[Test]
	public void Interpret_UnknownIdentityBytes_ThrowsNamingTheBytes()
	{
		var unknown = BuildA75Response();
		unknown[4] = 0xEE; unknown[5] = 0xEE; unknown[6] = 0xEE;

		var ex = Assert.Throws<InvalidOperationException>(() => IdentityHandshake.Interpret(unknown));
		Assert.That(ex!.Message, Does.Contain("0xEE"));
	}

	[Test]
	public void BuildRequest_FitsTheNarrowestCommandReport()
	{
		// The browser transport sends this the moment a device is granted; if it can't fit the
		// report, no keyboard ever identifies itself.
		Assert.That(
			HidReportPacket.FitToCapacity(IdentityHandshake.BuildRequest(), HidReportPacket.MinCommandCapacity),
			Is.EqualTo(HidReportPacket.MinCommandCapacity));
	}

	[Test]
	public void DescribeFailure_NoResponse_SaysTimeout()
	{
		Assert.That(IdentityHandshake.DescribeFailure(null), Does.Contain("timeout"));
	}

	[Test]
	public void DescribeFailure_WrongResponse_ShowsWhatCameBack()
	{
		Assert.That(IdentityHandshake.DescribeFailure([0xB7, 0x01]), Does.Contain("0xB7"));
	}
}
