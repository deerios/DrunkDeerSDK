namespace DrunkDeer.Protocol;

/// <summary>
/// Abstraction over the physical HID connection to a DrunkDeer keyboard.
/// Implement this interface to supply a test double to <see cref="KeyboardSession"/>.
/// </summary>
public interface IKeyboardConnection : IDisposable
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

	/// <summary>Reads from the data stream when available (unsolicited), otherwise from the command stream. Returns <see langword="null"/> on timeout.</summary>
	byte[]? Receive(int timeoutMs = 1000);

	/// <summary>Sends a pre-built packet with no response expected.</summary>
	void Send(byte[] packet);

	/// <summary>Sends a packet and waits for the next command-stream response.</summary>
	byte[]? SendAndReceive(byte[] packet, int timeoutMs = 1000);

	/// <summary>Reads only from the command stream (request-response pattern).</summary>
	byte[]? ReceiveCommand(int timeoutMs = 1000);

	/// <summary>Drains any buffered input without blocking.</summary>
	void FlushReadBuffer();
}
