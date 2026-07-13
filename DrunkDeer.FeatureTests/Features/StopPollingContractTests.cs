using DrunkDeer.FeatureTests.Fakes;
using DrunkDeer.Protocol;
using NUnit.Framework;

namespace DrunkDeer.FeatureTests.Features;

/// <summary>
/// API-9 regression coverage: StopPolling's contract must be explicit (return whether it
/// actually stopped) rather than always logging "stopped" and leaving IsPolling ambiguous.
/// </summary>
[TestFixture]
public class StopPollingContractTests
{
	[Test]
	public void StopPolling_CleanStop_ReturnsTrueAndClearsIsPolling()
	{
		var fake = new FakeKeyboardConnection();
		using var session = new KeyboardSession(fake);

		session.StartPolling();
		bool stopped = session.StopPolling();

		Assert.That(stopped, Is.True);
		Assert.That(session.IsPolling, Is.False);
	}

	[Test]
	public void StartStop_RepeatedCycles_DoNotThrow()
	{
		// Regression for CTS leak: StopPolling now disposes and nulls _pollCts/_pollTask on a
		// clean stop, so a fresh CancellationTokenSource is created each StartPolling call
		// instead of linking against an already-disposed one.
		var fake = new FakeKeyboardConnection();
		using var session = new KeyboardSession(fake);

		for (int i = 0; i < 5; i++)
		{
			session.StartPolling();
			Assert.That(session.StopPolling(), Is.True);
		}
	}
}
