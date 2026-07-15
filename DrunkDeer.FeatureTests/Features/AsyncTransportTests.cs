using DrunkDeer.FeatureTests.Fakes;
using DrunkDeer.Protocol;
using DrunkDeer.Simulation;
using NUnit.Framework;

namespace DrunkDeer.FeatureTests.Features;

/// <summary>
/// Coverage for the async transport/session surface (Phase 0 §3.1): the non-blocking poll loop,
/// configuration commands interleaving between poll frames, and cancellation of a pending receive.
/// </summary>
[TestFixture]
public class AsyncTransportTests
{
	/// <summary>Builds a 64-byte standard-precision 0xB7 travel packet for one packet of a frame.</summary>
	private static byte[] BuildB7Packet(byte packetIndex, byte firstKeyValue)
	{
		var buf = new byte[64];
		buf[0] = 0xB7;
		buf[3] = packetIndex;
		buf[4] = firstKeyValue;
		return buf;
	}

	[Test]
	public async Task AsyncPollLoop_DispatchesFramesFromFake()
	{
		var fake = new FakeKeyboardConnection(); // A75, standard precision, 3-packet frames
		await using var session = KeyboardSession.OpenAsyncConnection(fake);

		int framesDispatched = 0;
		session.Polled += (_, _) => Interlocked.Increment(ref framesDispatched);

		void EnqueueFrame(byte value)
		{
			fake.EnqueueResponse(BuildB7Packet(0, value));
			fake.EnqueueResponse(BuildB7Packet(1, value));
			fake.EnqueueResponse(BuildB7Packet(2, value));
		}

		// The loop flushes once at startup before frames can be staged; pause there so the frames
		// below survive (same technique the sync PollLoop tests use).
		fake.PauseAfterNextFlush();
		await session.StartPollingAsync();
		try
		{
			var startDeadline = DateTime.UtcNow.AddSeconds(5);
			while (fake.FlushCount < 1 && DateTime.UtcNow < startDeadline)
				await Task.Delay(1);
			Assert.That(fake.FlushCount, Is.GreaterThanOrEqualTo(1), "Async poll loop never reached its startup flush.");

			EnqueueFrame(10);
			EnqueueFrame(20);
			fake.ResumeAfterFlush();

			var deadline = DateTime.UtcNow.AddSeconds(5);
			while (Volatile.Read(ref framesDispatched) < 2 && DateTime.UtcNow < deadline)
				await Task.Delay(1);

			Assert.That(Volatile.Read(ref framesDispatched), Is.GreaterThanOrEqualTo(2),
				"Async poll loop did not dispatch the staged frames.");
			Assert.That(session.IsPolling, Is.True, "Async poll loop exited unexpectedly.");
		}
		finally
		{
			Assert.That(await session.StopPollingAsync(), Is.True, "Async poll loop did not stop cleanly.");
			Assert.That(session.IsPolling, Is.False);
		}
	}

	[Test]
	public async Task ConfigCommand_InterleavesWithRunningAsyncPollLoop()
	{
		// The simulator stages travel frames on the poll request and answers config writes with a
		// separate synthesized ACK, so a config command can complete while the poll loop runs
		// without the two contending for one response queue.
		var sim = new SimulatedKeyboardConnection(); // A75, standard precision
		await using var session = KeyboardSession.OpenAsyncConnection(sim);

		int keyDowns = 0;
		session.KeyDown += (_, _) => Interlocked.Increment(ref keyDowns);

		await session.StartPollingAsync();
		try
		{
			// Press a key: the next synthesized frame should raise KeyDown while the loop runs.
			sim.SetKeyTravelMm(0, 2.0f); // 2.0 mm > default 1.0 mm press threshold
			var pressDeadline = DateTime.UtcNow.AddSeconds(5);
			while (Volatile.Read(ref keyDowns) == 0 && DateTime.UtcNow < pressDeadline)
				await Task.Delay(1);
			Assert.That(Volatile.Read(ref keyDowns), Is.GreaterThanOrEqualTo(1),
				"Async poll loop never raised KeyDown for the pressed key.");

			// Now issue a configuration write WITHOUT stopping the loop. It must complete (the wire
			// gate lets it run between frames) and not throw.
			Assert.That(session.IsPolling, Is.True, "Loop should still be polling before the config write.");
			await session.SetActuationPointAsync(0.5f);
			Assert.That(session.IsPolling, Is.True, "Async poll loop stopped when a config command interleaved.");
		}
		finally
		{
			Assert.That(await session.StopPollingAsync(), Is.True);
		}
	}

	[Test]
	public void ReceiveCommandAsync_AlreadyCancelledToken_Throws()
	{
		var fake = new FakeKeyboardConnection();
		using var cts = new CancellationTokenSource();
		cts.Cancel();

		Assert.ThrowsAsync<OperationCanceledException>(async () =>
			await fake.ReceiveCommandAsync(1000, cts.Token));
	}

	[Test]
	public async Task ReceiveCommandAsync_CancelledWhilePending_Throws()
	{
		var fake = new FakeKeyboardConnection(); // empty queue → the receive waits out its timeout
		using var cts = new CancellationTokenSource();

		var pending = fake.ReceiveCommandAsync(5000, cts.Token);
		await Task.Delay(20); // let the receive start waiting
		cts.Cancel();

		// CatchAsync (not ThrowsAsync) so the derived TaskCanceledException from Task.Delay counts.
		Assert.CatchAsync<OperationCanceledException>(async () => await pending);
	}

	[Test]
	public async Task ConfigCommand_RunsImmediatelyAfterStartPollingAsync()
	{
		// The async loop cannot flag itself as running until after its first await, so there is a
		// window where the session holds a live poll task with no async loop behind it yet — which
		// EnsureNotSyncPolling reads as a *synchronous* loop and rejects, naming the wrong poll loop
		// entirely. Anything that starts polling and configures the board in the same breath hits it.
		//
		// The window only stays open where that continuation can't run: on the thread pool it
		// usually wins the race and hides the bug, so this pins the browser's single thread, where
		// nothing runs until the current call stack yields.
		var original = SynchronizationContext.Current;
		var ctx = new QueueingSyncContext();
		SynchronizationContext.SetSynchronizationContext(ctx);

		var sim = new SimulatedKeyboardConnection();
		var session = KeyboardSession.OpenAsyncConnection(sim);
		try
		{
			// Completes synchronously and leaves the loop's first continuation queued — precisely
			// the state the browser is in the instant after connecting.
			_ = session.StartPollingAsync();

			// Faults before its first await if the check rejects it, so the returned task already
			// carries the verdict; awaiting it would need the context to pump.
			var command = session.SetActuationPointAsync(1.5f, [DDKey.W]);

			Assert.That(command.IsFaulted, Is.False,
				$"A config command issued straight after StartPollingAsync was rejected: {command.Exception?.InnerException?.Message}");
		}
		finally
		{
			// Restore first so the poll loop's own continuations go to the pool, not back into the
			// queue, once it is finally allowed to start.
			SynchronizationContext.SetSynchronizationContext(original);
			ctx.Drain();
			await session.DisposeAsync();
		}
	}

	/// <summary>
	/// Queues continuations instead of running them, standing in for the browser's single thread:
	/// nothing posted here runs until <see cref="Drain"/> pumps it.
	/// </summary>
	private sealed class QueueingSyncContext : SynchronizationContext
	{
		private readonly Queue<(SendOrPostCallback Callback, object? State)> _queued = new();

		public override void Post(SendOrPostCallback d, object? state)
		{
			lock (_queued) _queued.Enqueue((d, state));
		}

		public void Drain()
		{
			while (true)
			{
				(SendOrPostCallback Callback, object? State) next;
				lock (_queued)
				{
					if (_queued.Count == 0) return;
					next = _queued.Dequeue();
				}
				next.Callback(next.State);
			}
		}
	}

	[Test]
	public async Task SimulatedConnection_PacesAsyncFrames()
	{
		// Without pacing the async poll loop spins flat-out (the simulator stages a frame on every
		// travel request and returns it instantly), pegging a core — fatal on the single-threaded
		// WASM UI. SendAsync must hold each travel request for at least FrameInterval since the last.
		var sim = new SimulatedKeyboardConnection { FrameInterval = TimeSpan.FromMilliseconds(50) };
		var request = TravelRequest.Build();

		var start = DateTime.UtcNow;
		for (int i = 0; i < 4; i++) // first send is immediate; the next 3 each wait ~one interval
			await sim.SendAsync(request);
		var elapsed = DateTime.UtcNow - start;

		// 3 paced gaps of 50 ms ≈ 150 ms; allow slack for timer coarseness but require real pacing.
		Assert.That(elapsed, Is.GreaterThan(TimeSpan.FromMilliseconds(120)),
			"Travel requests were not paced — the async poll loop would spin.");
	}

	[Test]
	public async Task SimulatedConnection_ZeroFrameInterval_DisablesPacing()
	{
		// Opt-out for tests that want the old instant behaviour on the async surface.
		var sim = new SimulatedKeyboardConnection { FrameInterval = TimeSpan.Zero };
		var request = TravelRequest.Build();

		var start = DateTime.UtcNow;
		for (int i = 0; i < 50; i++)
			await sim.SendAsync(request);
		var elapsed = DateTime.UtcNow - start;

		Assert.That(elapsed, Is.LessThan(TimeSpan.FromMilliseconds(200)),
			"Zero FrameInterval should not pace at all.");
	}

	[Test]
	public async Task AsyncOnlySession_RejectsBlockingApi()
	{
		// A session opened over an async-only connection (the WebHID case) must steer callers to
		// the *Async API rather than silently blocking.
		var asyncOnly = new AsyncOnlyConnection();
		await using var session = KeyboardSession.OpenAsyncConnection(asyncOnly);

		Assert.That(session.SupportsAsync, Is.True);
		// A synchronous config call hits the shim's blocking transport and is rejected outright.
		Assert.Throws<InvalidOperationException>(() => session.SetActuationPoint(0.5f));
		// The async surface, by contrast, works.
		await session.StartPollingAsync();
		Assert.That(await session.StopPollingAsync(), Is.True);
	}

	/// <summary>A connection that implements only <see cref="IKeyboardConnectionAsync"/> (no blocking transport), like WebHID.</summary>
	private sealed class AsyncOnlyConnection : IKeyboardConnectionAsync
	{
		public ModelInfo Model { get; } = ModelRegistry.GetInfo(ModelSlugs.A75)!;
		public string Variant => "ansi";
		public byte FirmwareVersion => 1;
		public bool HasDataStream => false;
		public byte InitialTurboValue => 0;
		public byte InitialRapidTriggerEnabled => 0;
		public byte InitialLastWinValue => 0;
		public byte InitialRapidTriggerAutoMatch => 0;

		public ValueTask SendAsync(byte[] packet, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
		public ValueTask<byte[]?> SendAndReceiveAsync(byte[] packet, int timeoutMs = 1000, CancellationToken cancellationToken = default) => new((byte[]?)null);
		public async ValueTask<byte[]?> ReceiveCommandAsync(int timeoutMs = 1000, CancellationToken cancellationToken = default)
		{
			if (timeoutMs > 0) await Task.Delay(timeoutMs, cancellationToken).ConfigureAwait(false);
			return null;
		}
		public ValueTask FlushReadBufferAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
		public void Dispose() { }
	}
}
