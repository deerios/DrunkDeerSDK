using DrunkDeer.FeatureTests.Fakes;
using DrunkDeer.Protocol;
using NUnit.Framework;

namespace DrunkDeer.FeatureTests.Features;

/// <summary>
/// POLL-1 regression coverage: on a dropped high-precision poll frame, stale packets left over
/// from the abandoned request must not bleed into - and permanently misalign - later frames.
/// </summary>
[TestFixture]
public class HighPrecisionPollingTests
{
	private static readonly int[] HpSectionBase = [0, 30, 60, 90, 120];

	/// <summary>Builds a 64-byte 0xFD 0x06 high-precision travel packet for one section (30 keys, u16le).</summary>
	private static byte[] BuildHpPacket(ushort firstKeyRaw)
	{
		var buf = new byte[64];
		buf[0] = 0xFD;
		buf[1] = 0x06;
		// Only the section's first key carries a non-zero (and non-noise, >=40) value; the rest
		// stay at 0, which DispatchFrameHighPrecision clamps to 0 and never fires an event for.
		buf[2] = (byte)(firstKeyRaw & 0xFF);
		buf[3] = (byte)(firstKeyRaw >> 8);
		return buf;
	}

	[Test]
	public void DroppedFrame_DoesNotMisassignSubsequentFrame()
	{
		var fake = new FakeKeyboardConnection(ModelRegistry.GetInfo(ModelSlugs.A75Ultra));
		using var session = new KeyboardSession(fake);

		var events = new List<(int Key, short Height)>();
		session.KeyHeightChanged += (_, e) => { lock (events) events.Add((e.Index, e.Height)); };

		static void WaitFor(Func<bool> condition, string failureMessage)
		{
			var deadline = DateTime.UtcNow.AddSeconds(5);
			while (!condition() && DateTime.UtcNow < deadline)
				Thread.Sleep(1);
			Assert.That(condition(), Is.True, failureMessage);
		}

		// PollLoop flushes once unconditionally at startup (unrelated to POLL-1) before it ever
		// sends a request. Pause there first so frame 1's packets can be staged without racing
		// that startup flush, which would otherwise clear them before they're ever read.
		fake.PauseAfterNextFlush();
		session.StartPolling();
		try
		{
			WaitFor(() => fake.FlushCount >= 1, "PollLoop never reached its startup flush.");

			// Still paused inside the startup flush - safe to arm the *next* flush (the
			// dropped-frame one under test) and stage frame 1 before resuming.
			fake.PauseAfterNextFlush();

			// Frame 1: only sections 0-2 of 5 arrive, then the connection goes quiet long enough
			// for every retry to time out - a dropped frame. Sections 3 and 4 (poisoned with a
			// value that must never appear in the results) are already sitting in the driver's
			// read buffer at that point, exactly as they would be if the real hardware's replies
			// to the abandoned request simply arrived late.
			fake.EnqueueResponse(BuildHpPacket(1000));
			fake.EnqueueResponse(BuildHpPacket(1001));
			fake.EnqueueResponse(BuildHpPacket(1002));
			for (int i = 0; i < 10; i++)
				fake.EnqueueTimeout();
			fake.EnqueueResponse(BuildHpPacket(9999)); // stale section 3 - must be discarded
			fake.EnqueueResponse(BuildHpPacket(9998)); // stale section 4 - must be discarded
			fake.ResumeAfterFlush();

			WaitFor(() => fake.FlushCount >= 2, "PollLoop never flushed the read buffer after a dropped frame.");

			// The poll thread is now blocked inside the dropped-frame FlushReadBuffer call, past
			// the point where it cleared the stale sections 3/4. Stage frame 2 - a full,
			// correctly-ordered set of 5 sections - then let the poll thread consume it.
			fake.EnqueueResponse(BuildHpPacket(2000));
			fake.EnqueueResponse(BuildHpPacket(2001));
			fake.EnqueueResponse(BuildHpPacket(2002));
			fake.EnqueueResponse(BuildHpPacket(2003));
			fake.EnqueueResponse(BuildHpPacket(2004));
			fake.ResumeAfterFlush();

			WaitFor(() =>
			{
				lock (events) return events.Any(e => e.Height == 2004);
			}, "Frame 2's last section was never dispatched.");
		}
		finally
		{
			session.StopPolling();
		}

		lock (events)
		{
			Assert.That(events.Any(e => e.Height == 9999 || e.Height == 9998), Is.False,
				"Stale packets from the dropped frame leaked into a later frame.");

			for (int section = 0; section < 5; section++)
			{
				short expected = (short)(2000 + section);
				Assert.That(events.Any(e => e.Key == HpSectionBase[section] && e.Height == expected), Is.True,
					$"Section {section}'s value was not assigned to key {HpSectionBase[section]}.");
			}
		}
	}
}
