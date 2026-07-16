namespace DrunkDeer.Protocol;

/// <summary>
/// Abstraction over the physical HID connection to a DrunkDeer keyboard.
/// Implement this interface to supply a test double to <see cref="KeyboardSession"/>.
/// </summary>
public interface IKeyboardConnection : IKeyboardConnectionInfo
{
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
