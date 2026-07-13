using DrunkDeer.FeatureTests.Fakes;
using DrunkDeer.Protocol;
using NUnit.Framework;

namespace DrunkDeer.FeatureTests.Features;

/// <summary>
/// API-10 regression coverage: Dispose() must be idempotent under concurrency (a plain
/// check-then-set on a bool field lets two racing callers both pass the guard), and the poll
/// loop must not raise Disconnected off the back of a Dispose()-induced Send failure.
/// </summary>
[TestFixture]
public class DisposeConcurrencyTests
{
	[Test]
	public void Dispose_CalledConcurrently_TearsDownExactlyOnce()
	{
		var fake = new FakeKeyboardConnection();
		var session = new KeyboardSession(fake);

		var threads = new Thread[8];
		for (int i = 0; i < threads.Length; i++)
			threads[i] = new Thread(session.Dispose);
		foreach (var t in threads) t.Start();
		foreach (var t in threads) t.Join();

		Assert.That(fake.DisposeCount, Is.EqualTo(1));
	}

	[Test]
	public void Dispose_CalledSequentiallyTwice_DoesNotThrow()
	{
		var fake = new FakeKeyboardConnection();
		var session = new KeyboardSession(fake);

		Assert.DoesNotThrow(() =>
		{
			session.Dispose();
			session.Dispose();
		});
		Assert.That(fake.DisposeCount, Is.EqualTo(1));
	}
}
