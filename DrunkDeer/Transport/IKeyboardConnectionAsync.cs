namespace DrunkDeer.Protocol;

/// <summary>
/// Identity metadata resolved during the connection handshake. Shared by the
/// synchronous <see cref="IKeyboardConnection"/> and the asynchronous
/// <see cref="IKeyboardConnectionAsync"/> so a connection can implement both without
/// re-declaring these members, and callers can read identity regardless of which
/// transport surface they hold.
/// </summary>
public interface IKeyboardConnectionInfo : IDisposable
{
	/// <summary>Model metadata resolved during the identity handshake.</summary>
	ModelInfo Model { get; }
	/// <summary>Variant string (e.g. "ansi", "iso") resolved during the identity handshake.</summary>
	string Variant { get; }
	/// <summary>Firmware version byte returned by the keyboard during the handshake.</summary>
	byte FirmwareVersion { get; }
	/// <summary><see langword="true"/> if a separate read-only data stream was found for this keyboard.</summary>
	bool HasDataStream { get; }

	/// <summary>Turbo mode state at connection time (0 = off, 1 = on).</summary>
	byte InitialTurboValue { get; }
	/// <summary>Rapid Trigger enabled state at connection time (0 = off, 1 = on).</summary>
	byte InitialRapidTriggerEnabled { get; }
	/// <summary>Last Win / Rapid Trigger combined mode at connection time.</summary>
	byte InitialLastWinValue { get; }
	/// <summary>Rapid Trigger Auto Match state at connection time (0 = off, 1 = on).</summary>
	byte InitialRapidTriggerAutoMatch { get; }
}

/// <summary>
/// Asynchronous, non-blocking transport to a DrunkDeer keyboard. This is the seam the
/// session needs to run on single-threaded hosts (Blazor WebAssembly), where the blocking
/// reads of <see cref="IKeyboardConnection"/> would freeze the only thread.
/// </summary>
/// <remarks>
/// Desktop connections implement both this and <see cref="IKeyboardConnection"/>; a
/// browser (WebHID) connection implements only this one. All methods are cancellation-first:
/// a cancelled token faults the returned task with <see cref="OperationCanceledException"/>.
/// </remarks>
public interface IKeyboardConnectionAsync : IKeyboardConnectionInfo
{
	/// <summary>Sends a pre-built packet with no response expected.</summary>
	ValueTask SendAsync(byte[] packet, CancellationToken cancellationToken = default);

	/// <summary>Sends a packet and awaits the next command-stream response (<see langword="null"/> on timeout).</summary>
	ValueTask<byte[]?> SendAndReceiveAsync(byte[] packet, int timeoutMs = 1000, CancellationToken cancellationToken = default);

	/// <summary>Awaits the next command-stream response (<see langword="null"/> on timeout).</summary>
	ValueTask<byte[]?> ReceiveCommandAsync(int timeoutMs = 1000, CancellationToken cancellationToken = default);

	/// <summary>Drains any buffered input without blocking on new data.</summary>
	ValueTask FlushReadBufferAsync(CancellationToken cancellationToken = default);
}
