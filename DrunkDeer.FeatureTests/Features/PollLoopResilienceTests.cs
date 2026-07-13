using DrunkDeer.FeatureTests.Fakes;
using DrunkDeer.Protocol;
using NUnit.Framework;

namespace DrunkDeer.FeatureTests.Features;

/// <summary>
/// POLL-2 regression coverage: an exception thrown by a user event handler on the poll thread
/// must not silently kill polling.
/// </summary>
[TestFixture]
public class PollLoopResilienceTests
{
	/// <summary>Builds a 64-byte standard-precision 0xB7 travel packet for one of the 3 packets in a frame.</summary>
	private static byte[] BuildB7Packet(byte packetIndex, byte firstKeyValue)
	{
		var buf = new byte[64];
		buf[0] = 0xB7;
		buf[3] = packetIndex;
		buf[4] = firstKeyValue;
		return buf;
	}

	[Test]
	public void ThrowingEventHandler_DoesNotKillPollLoop()
	{
		var fake = new FakeKeyboardConnection(); // default A75, standard precision, 3-packet frames
		using var session = new KeyboardSession(fake);

		int framesDispatched = 0;
		session.KeyHeightChanged += (_, _) =>
		{
			Interlocked.Increment(ref framesDispatched);
			throw new InvalidOperationException("boom - simulated buggy subscriber");
		};

		void EnqueueFrame(byte value)
		{
			fake.EnqueueResponse(BuildB7Packet(0, value));
			fake.EnqueueResponse(BuildB7Packet(1, value));
			fake.EnqueueResponse(BuildB7Packet(2, value));
		}

		// PollLoop flushes the read buffer once, unconditionally, at startup - before frames can
		// be staged, since FlushReadBuffer now genuinely discards whatever's queued. Pause there
		// so the two frames below aren't wiped out before they're ever read.
		fake.PauseAfterNextFlush();
		session.StartPolling();
		try
		{
			var startDeadline = DateTime.UtcNow.AddSeconds(5);
			while (fake.FlushCount < 1 && DateTime.UtcNow < startDeadline)
				Thread.Sleep(1);
			Assert.That(fake.FlushCount, Is.GreaterThanOrEqualTo(1), "PollLoop never reached its startup flush.");

			// Two frames, each with a different first-key value so KeyHeightChanged fires (and
			// throws) on both - proving the loop survives past the first throw, not just once.
			EnqueueFrame(10);
			EnqueueFrame(20);
			fake.ResumeAfterFlush();

			var deadline = DateTime.UtcNow.AddSeconds(5);
			while (Volatile.Read(ref framesDispatched) < 2 && DateTime.UtcNow < deadline)
				Thread.Sleep(1);
			Assert.That(Volatile.Read(ref framesDispatched), Is.EqualTo(2),
				"PollLoop stopped dispatching frames after the first handler exception.");
			Assert.That(session.IsPolling, Is.True,
				"PollLoop exited after a handler exception instead of continuing.");
		}
		finally
		{
			// Must not throw or hang: a faulted poll task previously surfaced its exception here,
			// from _pollTask.Wait(), as an unrelated-looking AggregateException.
			Assert.DoesNotThrow(() => session.StopPolling());
		}
	}
}
