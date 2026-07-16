using System.Buffers.Binary;
using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace DrunkDeer.Protocol;

/// <summary>
/// Async, non-blocking half of <see cref="KeyboardSession"/>. This is what runs on
/// single-threaded hosts (Blazor WebAssembly): the poll loop and configuration commands
/// await their I/O instead of blocking a thread.
/// </summary>
/// <remarks>
/// <para><b>One pump, one wire.</b> A single <see cref="SemaphoreSlim"/> (<c>_wireGate</c>)
/// serialises all wire access on the async path. The async poll loop takes the gate to collect
/// one frame, then releases it before dispatching events, so a queued configuration command gets
/// its turn <em>between</em> frames. Unlike the sync path there is no <c>EnsureNotPolling</c>
/// requirement — a live actuation/lighting write interleaves with a running heatmap.</para>
/// <para><b>Don't mix paths.</b> Use either the sync API (desktop) or the async API (WASM)
/// for a given session, not both. The sync poll loop does not take the gate, so issuing async
/// commands while a synchronous poll loop runs is unsupported and throws.</para>
/// </remarks>
public partial class KeyboardSession
{
	// Serialises wire access on the async path: the poll loop holds it per-frame, config
	// commands hold it for the whole (possibly multi-packet) command so a poll frame can't
	// interleave between a write's packets and be mistaken for its ACK.
	private readonly SemaphoreSlim _wireGate = new(1, 1);

	// True only while PollLoopAsync is running. Lets async config methods tell "an async poll
	// loop is running (fine, the gate coordinates us)" from "a *sync* poll loop is running
	// (unsupported — it bypasses the gate)".
	private volatile bool _asyncPollActive;

	/// <summary>
	/// Builds a session over a caller-supplied <see cref="IKeyboardConnectionAsync"/> — the
	/// browser (WebHID) case, where no blocking transport exists. The synchronous API throws on
	/// such a session; use the <c>*Async</c> methods and <see cref="StartPollingAsync"/>. The
	/// session takes ownership: disposing it disposes the connection.
	/// </summary>
	/// <remarks>
	/// A connection that implements <em>both</em> transport interfaces (the desktop connection,
	/// the simulator, the fake) should be opened with <see cref="Open(IKeyboardConnection, ILoggerFactory?)"/>
	/// instead — it gains the async surface automatically. This factory is for connections that
	/// implement <em>only</em> the async interface, so its distinct name avoids an ambiguous
	/// overload against the sync <c>Open</c> for dual-interface types.
	/// </remarks>
	public static KeyboardSession OpenAsyncConnection(IKeyboardConnectionAsync connection, ILoggerFactory? loggerFactory = null) =>
		new(connection, loggerFactory);

	// Chains through the sync constructor with a shim so all the layout/shadow-state setup runs
	// unchanged, then swaps in the real async connection (the shim can't serve async I/O). The
	// shim's blocking methods throw a clear "async-only session" error if the sync API is used.
	// Private (not internal) so it stays out of overload resolution in the test assembly, where a
	// dual-interface fake would otherwise make new KeyboardSession(fake) ambiguous.
	private KeyboardSession(IKeyboardConnectionAsync connection, ILoggerFactory? loggerFactory = null)
		: this(new AsyncConnectionSyncShim(connection), loggerFactory)
	{
		_asyncConnection = connection;
	}

	private IKeyboardConnectionAsync AsyncConnectionOrThrow =>
		_asyncConnection ?? throw new InvalidOperationException(
			"This session's connection does not implement IKeyboardConnectionAsync. " +
			"Use the synchronous methods, or open the session over an IKeyboardConnectionAsync (e.g. WebHID).");

	/// <summary><see langword="true"/> if this session can drive the async (non-blocking) API.</summary>
	public bool SupportsAsync => _asyncConnection is not null;

	private void EnsureNotSyncPolling()
	{
		if (_pollTask is { IsCompleted: false } && !_asyncPollActive)
			throw new InvalidOperationException(
				"A synchronous poll loop is running. Stop it with StopPolling() before issuing async " +
				"commands, or start polling with StartPollingAsync() so commands can interleave.");
	}

	// ── Async poll loop ───────────────────────────────────────────────────────

	/// <summary>
	/// Starts the non-blocking async poll loop. Safe to await, but completes as soon as the loop
	/// is scheduled (it runs in the background). Calling while already polling is a no-op.
	/// </summary>
	public Task StartPollingAsync(CancellationToken cancellationToken = default)
	{
		if (_pollTask is { IsCompleted: false })
		{
			_log.LogWarning("StartPollingAsync called while already polling");
			return Task.CompletedTask;
		}

		_ = AsyncConnectionOrThrow; // fail fast if this session is sync-only

		_log.LogInformation("Starting async poll loop (PressThresholdMm={P}, ReleaseThresholdMm={R}, PrecisionMode={PM})",
			PressThresholdMm, ReleaseThresholdMm, _precisionMode);
		// Mirror StartPolling: a loop that exited on its own (disconnect) left its CTS behind.
		_pollCts?.Dispose();
		_pollCts  = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		// Claimed here rather than inside the loop, which cannot set it until after its first
		// await: until this is true, EnsureNotSyncPolling sees a running _pollTask with no async
		// loop behind it and mistakes this for a sync one. A caller that issues a config command
		// straight after awaiting this method would get that error, on a session polling async.
		// PollLoopAsync's finally clears it however the loop ends.
		_asyncPollActive = true;
		_pollTask = PollLoopAsync(_pollCts.Token);
		return Task.CompletedTask;
	}

	/// <summary>
	/// Signals the async poll loop to stop and awaits its exit (up to two seconds).
	/// </summary>
	/// <returns><see langword="true"/> if the loop stopped within the timeout; otherwise <see langword="false"/>.</returns>
	public async Task<bool> StopPollingAsync()
	{
		_log.LogInformation("Stopping async poll loop…");
		_pollCts?.Cancel();

		var task = _pollTask;
		bool completed = true;
		if (task is not null)
		{
			try
			{
				await task.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
			}
			catch (TimeoutException)
			{
				completed = false;
			}
			catch (OperationCanceledException)
			{
				// Expected: the loop observed the cancellation and unwound. Treat as stopped.
			}
			catch (Exception ex)
			{
				_log.LogError(ex, "Async poll loop faulted while stopping.");
			}
		}

		if (completed)
		{
			_pollCts?.Dispose();
			_pollCts  = null;
			_pollTask = null;
			_log.LogInformation("Async poll loop stopped.");
		}
		else
		{
			_log.LogWarning("Async poll loop did not stop within the 2s timeout; it may still be running.");
		}

		return completed;
	}

	// Faithful async port of PollLoop: same frame collection, resync-on-desync, and dropped-frame
	// flush. The only differences are awaited I/O and the per-frame wire gate that lets async
	// config commands run between frames. Dispatch happens outside the gate (it invokes user
	// handlers and touches no wire), so a queued command isn't blocked by event processing.
	private async Task PollLoopAsync(CancellationToken ct)
	{
		await Task.Yield(); // return to the caller before touching the wire
		var conn = AsyncConnectionOrThrow;
		_log.LogInformation("PollLoopAsync started (HasDataStream={DS}, PrecisionMode={PM}).",
			conn.HasDataStream, _precisionMode);

		var request = TravelRequest.Build();
		int frameCount = _precisionMode == PrecisionMode.HighPrecision ? 5 : 3;
		var packets = new byte[frameCount][];
		var gotPkt = new bool[frameCount];
		long lastTicks = Stopwatch.GetTimestamp();
		long totalFrames = 0;
		long droppedFrames = 0;

		try
		{
			try { await conn.FlushReadBufferAsync(ct).ConfigureAwait(false); }
			catch (OperationCanceledException) { return; }

			while (!ct.IsCancellationRequested)
			{
				bool frameComplete = false;
				try
				{
					await _wireGate.WaitAsync(ct).ConfigureAwait(false);
					try
					{
						try { await conn.SendAsync(request, ct).ConfigureAwait(false); }
						catch (OperationCanceledException) { break; }
						catch (Exception ex)
						{
							// Same rationale as PollLoop: a send failure after Dispose yanked the
							// streams is expected teardown, not a real disconnect.
							if (!IsDisposed)
							{
								_log.LogWarning(ex, "PollLoopAsync: send failed - keyboard disconnected.");
								Disconnected?.Invoke(this, EventArgs.Empty);
							}
							return;
						}

						Array.Clear(gotPkt, 0, frameCount);
						int retries = 0;

						while (!AllReceived(gotPkt) && retries < 10 && !ct.IsCancellationRequested)
						{
							var buf = await conn.ReceiveCommandAsync(200, ct).ConfigureAwait(false);
							if (buf is null) { retries++; continue; }

							if (_precisionMode == PrecisionMode.HighPrecision)
							{
								if (!KeyTravelHighPrecision.Matches(buf)) { retries++; continue; }
								int slot = FirstEmpty(gotPkt);
								if (slot < 0)
								{
									_log.LogDebug("PollLoopAsync: unexpected extra HP packet, resyncing.");
									Array.Clear(gotPkt, 0, frameCount);
									await conn.FlushReadBufferAsync(ct).ConfigureAwait(false);
									retries++;
									continue;
								}
								packets[slot] = buf;
								gotPkt[slot]  = true;
							}
							else
							{
								if (!TravelResponse.Matches(buf))
								{
									_log.LogDebug("PollLoopAsync: non-B7 during frame collection (cmd=0x{C:X2}), retry {R}",
										buf[0], retries);
									retries++;
									continue;
								}
								int idx = TravelResponse.GetPacketIndex(buf);
								if ((uint)idx > 2)
								{
									_log.LogDebug("PollLoopAsync: B7 bad index {Idx}, retry {R}", idx, retries);
									retries++;
									continue;
								}
								packets[idx] = buf;
								gotPkt[idx]  = true;
							}
						}

						frameComplete = AllReceived(gotPkt);
						if (!frameComplete)
						{
							droppedFrames++;
							_log.LogDebug("PollLoopAsync: dropped frame #{F} (retries={R})",
								totalFrames + droppedFrames, retries);
							// Abandon stragglers so they don't get misassigned on the next request.
							await conn.FlushReadBufferAsync(ct).ConfigureAwait(false);
						}
					}
					finally
					{
						_wireGate.Release();
					}
				}
				catch (OperationCanceledException)
				{
					break;
				}

				if (!frameComplete) continue;

				long now = Stopwatch.GetTimestamp();
				var elapsed = TimeSpan.FromSeconds((double)(now - lastTicks) / Stopwatch.Frequency);
				lastTicks = now;
				totalFrames++;

				_log.LogTrace("PollLoopAsync: frame #{F} elapsed={Ms:F2}ms (dropped={D})",
					totalFrames, elapsed.TotalMilliseconds, droppedFrames);

				try
				{
					if (_precisionMode == PrecisionMode.HighPrecision)
						DispatchFrameHighPrecision(packets, elapsed);
					else
						DispatchFrame(packets, elapsed);
				}
				catch (Exception ex)
				{
					// A user event handler must not fault the loop (see PollLoop).
					_log.LogError(ex, "PollLoopAsync: unhandled exception from an event handler; continuing to poll.");
				}
			}
		}
		finally
		{
			_asyncPollActive = false;
			_log.LogInformation("PollLoopAsync exiting. frames={F} dropped={D}", totalFrames, droppedFrames);
		}
	}

	// ── Wire gating for config commands ───────────────────────────────────────

	// Runs one logical command holding the wire gate for its entirety, so it either runs between
	// two poll frames or blocks the loop's next frame - never interleaves with one.
	private async Task RunWireCommandAsync(Func<IKeyboardConnectionAsync, CancellationToken, Task> command, CancellationToken ct)
	{
		EnsureNotSyncPolling();
		var conn = AsyncConnectionOrThrow;
		await _wireGate.WaitAsync(ct).ConfigureAwait(false);
		try { await command(conn, ct).ConfigureAwait(false); }
		finally { _wireGate.Release(); }
	}

	// As above, for commands that read a value back. The gate covers the whole read, so a
	// multi-chunk gateway read can't have a poll frame land between two of its chunks.
	private async Task<T> RunWireCommandAsync<T>(Func<IKeyboardConnectionAsync, CancellationToken, Task<T>> command, CancellationToken ct)
	{
		EnsureNotSyncPolling();
		var conn = AsyncConnectionOrThrow;
		await _wireGate.WaitAsync(ct).ConfigureAwait(false);
		try { return await command(conn, ct).ConfigureAwait(false); }
		finally { _wireGate.Release(); }
	}

	// ── Async key-point writers (assume the gate is held) ─────────────────────

	private async Task WriteKeyPointStandardAsync(IKeyboardConnectionAsync conn, byte[] values,
		Func<byte, ReadOnlySpan<byte>, byte[]> build, CancellationToken ct)
	{
		var normalized = new byte[KeyCount];
		values.AsSpan(0, Math.Min(values.Length, KeyCount)).CopyTo(normalized);

		for (int pkt = 0; pkt < 3; pkt++)
		{
			// build consumes the span synchronously and returns a complete packet before any await.
			var req = build((byte)pkt, normalized.AsSpan(PacketBase[pkt], PacketCount[pkt]));
			var resp = await conn.SendAndReceiveAsync(req, 1000, ct).ConfigureAwait(false);
			if (resp is null || !WriteKeyPointAcknowledgeStandard.Matches(resp))
				throw new InvalidOperationException($"No ACK for standard key-point write packet {pkt}.");
		}
	}

	private async Task WriteKeyPointHighPrecisionAsync(IKeyboardConnectionAsync conn, ushort[] values,
		Func<int, ReadOnlySpan<byte>, byte[]> build, CancellationToken ct)
	{
		var normalized = new ushort[HpKeyCount];
		values.AsSpan(0, Math.Min(values.Length, HpKeyCount)).CopyTo(normalized);
		var sectionBytes = new byte[60];

		// See WriteKeyPointHighPrecision: flush first so a straggler travel frame isn't mistaken
		// for the first section's ACK (both are 0xFD).
		await conn.FlushReadBufferAsync(ct).ConfigureAwait(false);

		for (int sec = 0; sec < 5; sec++)
		{
			int baseKey = HpSectionBase[sec];
			int count = HpSectionSizes[sec];
			Array.Clear(sectionBytes, 0, sectionBytes.Length);
			for (int i = 0; i < count; i++)
				BinaryPrimitives.WriteUInt16LittleEndian(sectionBytes.AsSpan(i * 2), normalized[baseKey + i]);

			var req = build(sec, sectionBytes);
			var resp = await conn.SendAndReceiveAsync(req, 1000, ct).ConfigureAwait(false);
			if (resp is null || !WriteKeyPointAcknowledgeHighPrecision.Matches(resp))
				throw new InvalidOperationException($"No ACK for high-precision key-point write section {sec}.");
		}
	}

	private Task SetKeyPointUniformAsync(IKeyboardConnectionAsync conn, float depthMm,
		Func<byte, ReadOnlySpan<byte>, byte[]> buildStandard,
		Func<int, ReadOnlySpan<byte>, byte[]> buildHighPrecision, CancellationToken ct)
	{
		if (_precisionMode == PrecisionMode.HighPrecision)
		{
			var raw = new ushort[HpKeyCount];
			Array.Fill(raw, MmToHighPrecisionUnit(depthMm));
			return WriteKeyPointHighPrecisionAsync(conn, raw, buildHighPrecision, ct);
		}
		else
		{
			var raw = new byte[KeyCount];
			Array.Fill(raw, MmToB6Unit(depthMm));
			return WriteKeyPointStandardAsync(conn, raw, buildStandard, ct);
		}
	}

	private Task SetKeyPointPerKeyAsync(IKeyboardConnectionAsync conn, float[] depthsMm,
		Func<byte, ReadOnlySpan<byte>, byte[]> buildStandard,
		Func<int, ReadOnlySpan<byte>, byte[]> buildHighPrecision, CancellationToken ct)
	{
		int expectedCount = _precisionMode == PrecisionMode.HighPrecision ? HpKeyCount : KeyCount;
		if (depthsMm.Length != expectedCount)
			throw new ArgumentException(
				$"Expected {expectedCount} depth values for this model, got {depthsMm.Length}.",
				nameof(depthsMm));

		for (int i = 0; i < depthsMm.Length; i++)
			ValidateDepthMm(depthsMm[i], i);

		if (_precisionMode == PrecisionMode.HighPrecision)
		{
			var raw = new ushort[HpKeyCount];
			for (int i = 0; i < HpKeyCount; i++)
				raw[i] = MmToHighPrecisionUnit(depthsMm[i]);
			return WriteKeyPointHighPrecisionAsync(conn, raw, buildHighPrecision, ct);
		}
		else
		{
			var raw = new byte[KeyCount];
			for (int i = 0; i < KeyCount; i++)
				raw[i] = MmToB6Unit(depthsMm[i]);
			return WriteKeyPointStandardAsync(conn, raw, buildStandard, ct);
		}
	}

	// ── Async actuation / downstroke / upstroke ───────────────────────────────

	/// <summary>Async twin of <see cref="SetActuationPoint(float)"/>.</summary>
	public Task SetActuationPointAsync(float depthMm, CancellationToken ct = default)
	{
		ValidateDepthMm(depthMm);
		Array.Fill(_actuationProfile, depthMm);
		return RunWireCommandAsync((conn, token) => SetKeyPointUniformAsync(conn, depthMm,
			(idx, vals) => WriteActuationPointStandard.Build(idx, vals),
			(sec, data) => WriteActuationPointHighPrecision.Build((byte)sec, data), token), ct);
	}

	internal Task SetActuationPointAsync(float[] depthsMm, CancellationToken ct = default)
	{
		var snapshot = (float[])depthsMm.Clone();
		return RunWireCommandAsync(async (conn, token) =>
		{
			await SetKeyPointPerKeyAsync(conn, snapshot,
				(idx, vals) => WriteActuationPointStandard.Build(idx, vals),
				(sec, data) => WriteActuationPointHighPrecision.Build((byte)sec, data), token).ConfigureAwait(false);
			snapshot.CopyTo(_actuationProfile, 0);
		}, ct);
	}

	/// <summary>Async twin of <see cref="SetActuationPoint(float, DDKey[])"/>.</summary>
	public Task SetActuationPointAsync(float depthMm, DDKey[] keys, CancellationToken ct = default)
	{
		if (keys.Length == 0)
			throw new ArgumentException("At least one key must be specified.", nameof(keys));
		foreach (var key in keys)
		{
			if (!TryGetKeyIndex(key, out int idx))
			{
				_log.LogWarning("SetActuationPointAsync: key {Key} not present on {Model}; skipped.", key, Model.Name);
				continue;
			}
			ValidateDepthMm(depthMm, idx);
			_actuationProfile[idx] = depthMm;
		}
		return RunWireCommandAsync((conn, token) => SetKeyPointPerKeyAsync(conn, _actuationProfile,
			(idx, vals) => WriteActuationPointStandard.Build(idx, vals),
			(sec, data) => WriteActuationPointHighPrecision.Build((byte)sec, data), token), ct);
	}

	/// <summary>Async twin of <see cref="SetDownstrokePoint(float)"/>.</summary>
	public Task SetDownstrokePointAsync(float depthMm, CancellationToken ct = default)
	{
		ValidateDepthMm(depthMm);
		Array.Fill(_downstrokeProfile, depthMm);
		return RunWireCommandAsync((conn, token) => SetKeyPointUniformAsync(conn, depthMm,
			(idx, vals) => WriteDownstrokePointStandard.Build(idx, vals),
			(sec, data) => WriteDownstrokePointHighPrecision.Build((byte)sec, data), token), ct);
	}

	/// <summary>Async twin of <see cref="SetDownstrokePoint(float, DDKey[])"/>.</summary>
	public Task SetDownstrokePointAsync(float depthMm, DDKey[] keys, CancellationToken ct = default)
	{
		if (keys.Length == 0)
			throw new ArgumentException("At least one key must be specified.", nameof(keys));
		foreach (var key in keys)
		{
			if (!TryGetKeyIndex(key, out int idx))
			{
				_log.LogWarning("SetDownstrokePointAsync: key {Key} not present on {Model}; skipped.", key, Model.Name);
				continue;
			}
			ValidateDepthMm(depthMm, idx);
			_downstrokeProfile[idx] = depthMm;
		}
		return RunWireCommandAsync((conn, token) => SetKeyPointPerKeyAsync(conn, _downstrokeProfile,
			(idx, vals) => WriteDownstrokePointStandard.Build(idx, vals),
			(sec, data) => WriteDownstrokePointHighPrecision.Build((byte)sec, data), token), ct);
	}

	internal Task SetDownstrokePointAsync(float[] depthsMm, CancellationToken ct = default)
	{
		var snapshot = (float[])depthsMm.Clone();
		return RunWireCommandAsync(async (conn, token) =>
		{
			await SetKeyPointPerKeyAsync(conn, snapshot,
				(idx, vals) => WriteDownstrokePointStandard.Build(idx, vals),
				(sec, data) => WriteDownstrokePointHighPrecision.Build((byte)sec, data), token).ConfigureAwait(false);
			snapshot.CopyTo(_downstrokeProfile, 0);
		}, ct);
	}

	/// <summary>Async twin of <see cref="SetUpstrokePoint(float)"/>.</summary>
	public Task SetUpstrokePointAsync(float depthMm, CancellationToken ct = default)
	{
		ValidateDepthMm(depthMm);
		Array.Fill(_upstrokeProfile, depthMm);
		return RunWireCommandAsync((conn, token) => SetKeyPointUniformAsync(conn, depthMm,
			(idx, vals) => WriteUpstrokePointStandard.Build(idx, vals),
			(sec, data) => WriteUpstrokePointHighPrecision.Build((byte)sec, data), token), ct);
	}

	/// <summary>Async twin of <see cref="SetUpstrokePoint(float, DDKey[])"/>.</summary>
	public Task SetUpstrokePointAsync(float depthMm, DDKey[] keys, CancellationToken ct = default)
	{
		if (keys.Length == 0)
			throw new ArgumentException("At least one key must be specified.", nameof(keys));
		foreach (var key in keys)
		{
			if (!TryGetKeyIndex(key, out int idx))
			{
				_log.LogWarning("SetUpstrokePointAsync: key {Key} not present on {Model}; skipped.", key, Model.Name);
				continue;
			}
			ValidateDepthMm(depthMm, idx);
			_upstrokeProfile[idx] = depthMm;
		}
		return RunWireCommandAsync((conn, token) => SetKeyPointPerKeyAsync(conn, _upstrokeProfile,
			(idx, vals) => WriteUpstrokePointStandard.Build(idx, vals),
			(sec, data) => WriteUpstrokePointHighPrecision.Build((byte)sec, data), token), ct);
	}

	internal Task SetUpstrokePointAsync(float[] depthsMm, CancellationToken ct = default)
	{
		var snapshot = (float[])depthsMm.Clone();
		return RunWireCommandAsync(async (conn, token) =>
		{
			await SetKeyPointPerKeyAsync(conn, snapshot,
				(idx, vals) => WriteUpstrokePointStandard.Build(idx, vals),
				(sec, data) => WriteUpstrokePointHighPrecision.Build((byte)sec, data), token).ConfigureAwait(false);
			snapshot.CopyTo(_upstrokeProfile, 0);
		}, ct);
	}

	// ── Async lighting ────────────────────────────────────────────────────────

	private async Task SendLightingPacketsAsync(IKeyboardConnectionAsync conn, RgbEntry[] entries, byte brightness, CancellationToken ct)
	{
		ValidateBrightness(brightness);
		const int EntriesPerPacket = 13;
		int packetCount = (entries.Length + EntriesPerPacket - 1) / EntriesPerPacket;

		for (int pkt = 0; pkt < packetCount; pkt++)
		{
			var buf = RgbKeyDataPacket.Build(isTurbo: 0, modeIndex: 0x13, brightness);
			int entryBase = pkt * EntriesPerPacket;
			int writeOffset = 8;

			for (int i = 0; i < EntriesPerPacket && entryBase + i < entries.Length; i++)
			{
				entries[entryBase + i].Write(buf.AsSpan(writeOffset));
				writeOffset += RgbEntry.ByteSize;
			}

			if (writeOffset < 64)
				buf[writeOffset] = 0xFF;

			var resp = await conn.SendAndReceiveAsync(buf, 1000, ct).ConfigureAwait(false);
			if (resp is null || !RgbAcknowledge.Matches(resp))
				throw new InvalidOperationException($"No ACK for RGB packet {pkt}.");
		}
	}

	/// <summary>Async twin of <see cref="SetLighting"/>.</summary>
	public Task SetLightingAsync(Func<int, (byte R, byte G, byte B)> colorForKey, byte brightness = 9, CancellationToken ct = default)
	{
		for (int i = 0; i < _rgbIndices.Length; i++)
		{
			int gridPos = _rgbIndices[i];
			var (r, g, b) = colorForKey(gridPos);
			_rgbProfile[gridPos] = (r, g, b);
		}
		return RunWireCommandAsync((conn, token) => SendLightingPacketsAsync(conn, BuildEntriesFromProfile(), brightness, token), ct);
	}

	/// <summary>Async twin of <see cref="SetUniformLighting(byte, byte, byte, byte)"/>.</summary>
	public Task SetUniformLightingAsync(byte r, byte g, byte b, byte brightness = 9, CancellationToken ct = default) =>
		SetLightingAsync(_ => (r, g, b), brightness, ct);

	/// <summary>Async twin of <see cref="SetKeyColor(DDKey, byte, byte, byte, byte)"/>.</summary>
	public Task SetKeyColorAsync(DDKey key, byte r, byte g, byte b, byte brightness = 9, CancellationToken ct = default)
	{
		int gridIdx = GetKeyIndex(key);
		_rgbProfile[gridIdx] = (r, g, b);
		return RunWireCommandAsync((conn, token) => SendLightingPacketsAsync(conn, BuildEntriesFromProfile(), brightness, token), ct);
	}

	/// <summary>Async twin of <see cref="SetKeyColor(byte, byte, byte, byte, DDKey[])"/>.</summary>
	public Task SetKeyColorAsync(byte r, byte g, byte b, DDKey[] keys, byte brightness = 9, CancellationToken ct = default)
	{
		if (keys.Length == 0)
			throw new ArgumentException("At least one key must be specified.", nameof(keys));
		foreach (var key in keys)
		{
			if (!TryGetKeyIndex(key, out int gridIdx))
			{
				_log.LogWarning("SetKeyColorAsync: key {Key} not present on {Model}; skipped.", key, Model.Name);
				continue;
			}
			_rgbProfile[gridIdx] = (r, g, b);
		}
		return RunWireCommandAsync((conn, token) => SendLightingPacketsAsync(conn, BuildEntriesFromProfile(), brightness, token), ct);
	}

	/// <summary>Async twin of <see cref="SetLightingMode"/>.</summary>
	public Task SetLightingModeAsync(LightingMode modeCode, byte brightness = 9, byte speed = 5, CancellationToken ct = default)
	{
		ValidateBrightness(brightness);
		ValidateSpeed(speed);
		return RunWireCommandAsync(async (conn, token) =>
		{
			var resp = await conn.SendAndReceiveAsync(Protocol.SetLightingMode.Build(
				slot: 0, (byte)modeCode, brightness, speed, tail: 0), 1000, token).ConfigureAwait(false);
			if (resp is null || !RgbAcknowledge.Matches(resp))
				throw new InvalidOperationException("No ACK for SetLightingMode.");
		}, ct);
	}

	/// <summary>Async twin of <see cref="DisableLighting"/>.</summary>
	public Task DisableLightingAsync(CancellationToken ct = default) =>
		RunWireCommandAsync(async (conn, token) =>
		{
			var resp = await conn.SendAndReceiveAsync(SetLightingOff.Build(), 1000, token).ConfigureAwait(false);
			if (resp is null || !RgbAcknowledge.Matches(resp))
				throw new InvalidOperationException("No ACK for SetLightingOff.");
		}, ct);

	// ── Async common config (Rapid Trigger / Turbo / Last Win) ────────────────

	// Async port of SendCommonConfig: commit the mirrors only after the ACK. Assumes the gate is held.
	private async Task SendCommonConfigAsync(IKeyboardConnectionAsync conn, bool turboMode, bool rapidTriggerMode,
		LastWinRapidTriggerMode lastWinRapidTriggerMode, bool rapidTriggerAutoMatch, CancellationToken ct)
	{
		var resp = await conn.SendAndReceiveAsync(CommonConfig.Build(
			turboMode: turboMode ? (byte)1 : (byte)0,
			rapidTriggerMode: rapidTriggerMode ? (byte)1 : (byte)0,
			lastWinRapidTriggerMode: (byte)lastWinRapidTriggerMode,
			rapidTriggerAutoMatch: rapidTriggerAutoMatch ? (byte)1 : (byte)0), 1000, ct).ConfigureAwait(false);
		if (resp is null || !CommonConfigAcknowledge.Matches(resp))
			throw new InvalidOperationException("No ACK for CommonConfig.");

		_turboEnabled          = turboMode;
		_rapidTriggerEnabled   = rapidTriggerMode;
		_lastWinRtMode         = lastWinRapidTriggerMode;
		_rapidTriggerAutoMatch = rapidTriggerAutoMatch;
	}

	/// <summary>Async twin of <see cref="SetCommonConfig"/>.</summary>
	public Task SetCommonConfigAsync(bool turboMode, bool rapidTriggerMode, LastWinRapidTriggerMode lastWinRapidTriggerMode, bool rapidTriggerAutoMatch, CancellationToken ct = default) =>
		RunWireCommandAsync((conn, token) => SendCommonConfigAsync(conn, turboMode, rapidTriggerMode, lastWinRapidTriggerMode, rapidTriggerAutoMatch, token), ct);

	/// <summary>Async twin of <see cref="EnableRapidTrigger"/>.</summary>
	public Task EnableRapidTriggerAsync(bool autoMatch = false, CancellationToken ct = default) =>
		RunWireCommandAsync((conn, token) => SendCommonConfigAsync(conn, _turboEnabled, true, _lastWinRtMode, autoMatch, token), ct);

	/// <summary>Async twin of <see cref="DisableRapidTrigger"/>.</summary>
	public Task DisableRapidTriggerAsync(CancellationToken ct = default) =>
		RunWireCommandAsync((conn, token) => SendCommonConfigAsync(conn, _turboEnabled, false, _lastWinRtMode, _rapidTriggerAutoMatch, token), ct);

	// ── Async profile application ─────────────────────────────────────────────

	/// <summary>
	/// Async twin of <see cref="ApplyProfile"/>. Holds the wire gate for the whole profile so the
	/// keyboard is configured atomically with respect to a running async poll loop.
	/// </summary>
	/// <remarks>Note: unlike the sync path, this does not persist Turbo mode to a FuncBlock (no
	/// programmable model is verified over the async/WebHID transport yet); it writes the same
	/// CommonConfig the sync path sends.</remarks>
	public Task ApplyProfileAsync(KeyboardProfile profile, CancellationToken ct = default) =>
		RunWireCommandAsync(async (conn, token) =>
		{
			if (!profile.IsThemeOnly)
			{
				if (profile.Actuation != null)
				{
					var depths = BuildDepthArray(profile.Actuation, _actuationProfile);
					await SetKeyPointPerKeyAsync(conn, depths,
						(idx, vals) => WriteActuationPointStandard.Build(idx, vals),
						(sec, data) => WriteActuationPointHighPrecision.Build((byte)sec, data), token).ConfigureAwait(false);
					depths.CopyTo(_actuationProfile, 0);
				}

				if (profile.Downstroke != null)
				{
					var depths = BuildDepthArray(profile.Downstroke, _downstrokeProfile);
					await SetKeyPointPerKeyAsync(conn, depths,
						(idx, vals) => WriteDownstrokePointStandard.Build(idx, vals),
						(sec, data) => WriteDownstrokePointHighPrecision.Build((byte)sec, data), token).ConfigureAwait(false);
					depths.CopyTo(_downstrokeProfile, 0);
				}

				if (profile.Upstroke != null)
				{
					var depths = BuildDepthArray(profile.Upstroke, _upstrokeProfile);
					await SetKeyPointPerKeyAsync(conn, depths,
						(idx, vals) => WriteUpstrokePointStandard.Build(idx, vals),
						(sec, data) => WriteUpstrokePointHighPrecision.Build((byte)sec, data), token).ConfigureAwait(false);
					depths.CopyTo(_upstrokeProfile, 0);
				}

				if (profile.RapidTrigger.HasValue)
					await SendCommonConfigAsync(conn, _turboEnabled, profile.RapidTrigger.Value, _lastWinRtMode,
						profile.RapidTriggerAutoMatch ?? _rapidTriggerAutoMatch, token).ConfigureAwait(false);
				else if (profile.RapidTriggerAutoMatch.HasValue)
					await SendCommonConfigAsync(conn, _turboEnabled, _rapidTriggerEnabled, _lastWinRtMode,
						profile.RapidTriggerAutoMatch.Value, token).ConfigureAwait(false);

				if (profile.TurboMode.HasValue)
					await SendCommonConfigAsync(conn, profile.TurboMode.Value, _rapidTriggerEnabled, _lastWinRtMode,
						_rapidTriggerAutoMatch, token).ConfigureAwait(false);
			}

			if (profile.Theme != null)
				await ApplyThemeAsync(conn, profile.Theme, token).ConfigureAwait(false);
		}, ct);

	private Task ApplyThemeAsync(IKeyboardConnectionAsync conn, KeyboardTheme theme, CancellationToken ct)
	{
		var baseColor = theme.BaseBrightness is { } level ? theme.BaseColor.Scale(level) : theme.BaseColor;
		var (br, bg, bb) = baseColor;
		for (int i = 0; i < _rgbIndices.Length; i++)
			_rgbProfile[_rgbIndices[i]] = (br, bg, bb);

		if (theme.Keys != null)
		{
			foreach (var (name, keyColor) in theme.Keys)
			{
				if (!Enum.TryParse<DDKey>(name, ignoreCase: true, out var key) || !TryGetKeyIndex(key, out int gridIdx))
				{
					_log.LogWarning("ApplyThemeAsync: unknown key '{Key}'; skipped.", name);
					continue;
				}
				_rgbProfile[gridIdx] = (keyColor.R, keyColor.G, keyColor.B);
			}
		}

		return SendLightingPacketsAsync(conn, BuildEntriesFromProfile(), theme.Brightness, ct);
	}

	// ── Async disposal ────────────────────────────────────────────────────────

	/// <summary>
	/// Async counterpart to <see cref="Dispose"/> for hosts that can't block (WASM): stops the
	/// poll loop without a blocking wait, then disposes the connection.
	/// </summary>
	public async ValueTask DisposeAsync()
	{
		if (Interlocked.CompareExchange(ref _disposedFlag, 1, 0) != 0) return;

		try { await StopPollingAsync().ConfigureAwait(false); }
		catch (Exception ex) { _log.LogWarning(ex, "DisposeAsync: StopPollingAsync faulted."); }

		if (_inFastMode && _asyncConnection is not null)
		{
			try
			{
				await _asyncConnection.SendAsync([0x55, 0x02]).ConfigureAwait(false);
				_inFastMode = false;
			}
			catch (Exception ex) { _log.LogWarning(ex, "DisposeAsync: EndFastMode send failed (keyboard may be disconnected)."); }
		}

		_connection.Dispose();
		_wireGate.Dispose();
	}

	// ── Sync shim for an async-only connection ────────────────────────────────

	// Lets an async-only connection (WebHID) satisfy the sync constructor's identity/shadow-state
	// setup. Its blocking transport methods throw: an async-only session must use the *Async API.
	private sealed class AsyncConnectionSyncShim : IKeyboardConnection
	{
		private readonly IKeyboardConnectionAsync _inner;
		public AsyncConnectionSyncShim(IKeyboardConnectionAsync inner) => _inner = inner;

		public ModelInfo Model => _inner.Model;
		public string Variant => _inner.Variant;
		public byte FirmwareVersion => _inner.FirmwareVersion;
		public bool HasDataStream => _inner.HasDataStream;
		public byte InitialTurboValue => _inner.InitialTurboValue;
		public byte InitialRapidTriggerEnabled => _inner.InitialRapidTriggerEnabled;
		public byte InitialLastWinValue => _inner.InitialLastWinValue;
		public byte InitialRapidTriggerAutoMatch => _inner.InitialRapidTriggerAutoMatch;

		private static InvalidOperationException BlockingUnsupported() => new(
			"This session was opened over an async-only connection (e.g. WebHID). Use the *Async " +
			"methods and StartPollingAsync instead of the blocking synchronous API.");

		public byte[]? Receive(int timeoutMs = 1000) => throw BlockingUnsupported();
		public void Send(byte[] packet) => throw BlockingUnsupported();
		public byte[]? SendAndReceive(byte[] packet, int timeoutMs = 1000) => throw BlockingUnsupported();
		public byte[]? ReceiveCommand(int timeoutMs = 1000) => throw BlockingUnsupported();
		public void FlushReadBuffer() => throw BlockingUnsupported();
		public void Dispose() => _inner.Dispose();
	}
}
