using System.Reflection;
using DrunkDeer.FeatureTests.Fakes;
using DrunkDeer.Protocol;
using NUnit.Framework;

namespace DrunkDeer.FeatureTests.Features;

/// <summary>
/// Regression coverage for the session lifecycle fixes: a constructor that throws after taking
/// the connection must dispose it (PLAN-BUG-1), and StartPolling must not leak the previous
/// CancellationTokenSource when the prior loop exited on its own (PLAN-BUG-3).
/// </summary>
[TestFixture]
public class SessionLifecycleFixTests
{
	[Test]
	public void Ctor_UnknownModelSlug_DisposesConnection()
	{
		// A model whose slug has no KeyLayout case makes the base ctor throw NotSupportedException
		// after it has already taken the connection. Without the fix the just-opened streams leak.
		var bogus = new ModelInfo { Slug = "not-a-real-model", Name = "Bogus" };
		var fake  = new FakeKeyboardConnection(bogus);

		Assert.Throws<NotSupportedException>(() => new KeyboardSession(fake));
		Assert.That(fake.DisposeCount, Is.EqualTo(1),
			"A ctor that threw after taking the connection must dispose it.");
	}

	[Test]
	public void StartPolling_AfterLoopSelfExit_DisposesStaleCts()
	{
		var fake = new FakeKeyboardConnection();
		using var session = new KeyboardSession(fake);

		var pollCtsField = typeof(KeyboardSession)
			.GetField("_pollCts", BindingFlags.NonPublic | BindingFlags.Instance)!;

		// Start polling under an external token, then cancel it so the loop exits on its own -
		// StopPolling never runs, so the CTS is neither disposed nor cleared (the leak scenario).
		using var externalCts = new CancellationTokenSource();
		session.StartPolling(externalCts.Token);
		var staleCts = (CancellationTokenSource)pollCtsField.GetValue(session)!;

		externalCts.Cancel();
		var deadline = DateTime.UtcNow.AddSeconds(5);
		while (session.IsPolling && DateTime.UtcNow < deadline)
			Thread.Sleep(1);
		Assert.That(session.IsPolling, Is.False, "Poll loop did not exit after cancellation.");

		// Restarting must dispose the stale CTS before overwriting it.
		session.StartPolling();
		try
		{
			Assert.Throws<ObjectDisposedException>(() => _ = staleCts.Token,
				"StartPolling leaked the previous CancellationTokenSource instead of disposing it.");
		}
		finally
		{
			session.StopPolling();
		}
	}
}
