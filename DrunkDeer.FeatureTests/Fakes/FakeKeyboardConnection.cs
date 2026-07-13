using DrunkDeer.Protocol;

namespace DrunkDeer.FeatureTests.Fakes;

/// <summary>
/// Test double for <see cref="IKeyboardConnection"/>.
/// Pre-queue responses via <see cref="EnqueueResponse"/> or the gateway helpers;
/// inspect what was transmitted via <see cref="SentPackets"/>.
/// </summary>
internal sealed class FakeKeyboardConnection : IKeyboardConnection
{
	private readonly Queue<byte[]> _responses = new();

	/// <summary>All packets passed to <see cref="Send"/> or <see cref="SendAndReceive"/>, in order.</summary>
	public List<byte[]> SentPackets { get; } = [];

	// ── IKeyboardConnection identity properties ───────────────────────────────

	public ModelInfo Model { get; }
	public string Variant { get; }
	public byte FirmwareVersion { get; }
	public bool HasDataStream { get; } = false;
	public byte InitialTurboValue { get; } = 0;
	public byte InitialRapidTriggerEnabled { get; } = 0;
	public byte InitialLastWinValue { get; } = 0;
	public byte InitialRapidTriggerAutoMatch { get; } = 0;

	/// <param name="model">Model to advertise; defaults to A75 (standard precision).</param>
	/// <param name="firmwareVersion">
	/// Firmware version to advertise; defaults to 1. Bumping this to/above a model's
	/// <see cref="ModelInfo.KunPrecisionMinFirmware"/> (e.g. 35 for the base A75) upgrades
	/// an otherwise-Standard-precision fake to Kun precision, mirroring real hardware.
	/// </param>
	public FakeKeyboardConnection(ModelInfo? model = null, byte firmwareVersion = 1)
	{
		Model           = model ?? ModelRegistry.GetInfo(ModelSlugs.A75)!;
		Variant         = "ansi";
		FirmwareVersion = firmwareVersion;
	}

	// ── Response queue helpers ────────────────────────────────────────────────

	/// <summary>Enqueues a raw response returned by the next <see cref="SendAndReceive"/> or <see cref="ReceiveCommand"/> call.</summary>
	public void EnqueueResponse(byte[] response) => _responses.Enqueue(response);

	/// <summary>Enqueues a minimal 64-byte packet whose first byte is <paramref name="firstByte"/>.</summary>
	public void EnqueueAck(byte firstByte)
	{
		var ack = new byte[64];
		ack[0] = firstByte;
		_responses.Enqueue(ack);
	}

	/// <summary>
	/// Enqueues the correct number of ACK responses for three standard-precision key-point
	/// write packets (actuation, downstroke, or upstroke). Each ACK has first byte 0xB6.
	/// </summary>
	public void EnqueueStandardKeyPointAcks() =>
		EnqueueAcks(0xB6, count: 3);

	/// <summary>Enqueues <paramref name="count"/> ACKs with <paramref name="firstByte"/>.</summary>
	public void EnqueueAcks(byte firstByte, int count)
	{
		for (int i = 0; i < count; i++)
			EnqueueAck(firstByte);
	}

	/// <summary>
	/// Enqueues 0xAA gateway read responses chunked from <paramref name="data"/> (56 bytes per chunk).
	/// Call before any <see cref="KeyboardSession"/> method that uses <c>ReadExtendedGateway</c>.
	/// </summary>
	public void EnqueueGatewayRead(ReadOnlySpan<byte> data)
	{
		int offset = 0;
		while (offset < data.Length)
		{
			int len = Math.Min(56, data.Length - offset);
			var resp = new byte[64];
			resp[0] = 0xAA;
			data.Slice(offset, len).CopyTo(resp.AsSpan(8));
			_responses.Enqueue(resp);
			offset += len;
		}
	}

	/// <summary>
	/// Enqueues the correct number of 0xAA ACKs for a <c>WriteExtendedGateway</c> call
	/// that writes <paramref name="totalBytes"/> bytes (56 bytes per chunk).
	/// </summary>
	public void EnqueueGatewayWriteAcks(int totalBytes)
	{
		int chunks = (totalBytes + 55) / 56;
		EnqueueAcks(0xAA, chunks);
	}

	/// <summary>
	/// Enqueues responses for a complete FuncBlock read-modify-write cycle:
	/// one read (64 bytes -> 2 chunks) followed by one write (64 bytes -> 2 chunks).
	/// The read returns <paramref name="existingBlock"/> (defaults to all zeros).
	/// </summary>
	public void EnqueueFuncBlockCycle(byte[]? existingBlock = null)
	{
		EnqueueGatewayRead(existingBlock ?? new byte[64]);
		EnqueueGatewayWriteAcks(64);
	}

	// ── IKeyboardConnection transport ─────────────────────────────────────────

	public byte[]? Receive(int timeoutMs = 1000) =>
		_responses.Count > 0 ? _responses.Dequeue() : null;

	public void Send(byte[] packet) => SentPackets.Add(packet);

	public byte[]? SendAndReceive(byte[] packet, int timeoutMs = 1000)
	{
		SentPackets.Add(packet);
		return _responses.Count > 0 ? _responses.Dequeue() : null;
	}

	public byte[]? ReceiveCommand(int timeoutMs = 1000) =>
		_responses.Count > 0 ? _responses.Dequeue() : null;

	public void FlushReadBuffer() { }

	public void Dispose() { }

	// ── Packet inspection helpers ─────────────────────────────────────────────

	/// <summary>Returns the last packet sent (most recent <see cref="Send"/> or <see cref="SendAndReceive"/> call).</summary>
	public byte[] LastSent => SentPackets[^1];

	/// <summary>
	/// Returns all 0x55 write gateway packets (subcommand at byte[1]) in order.
	/// Useful for inspecting data written via <c>WriteExtendedGateway</c>.
	/// </summary>
	public List<byte[]> GatewayWritePackets(byte subCmd) =>
		SentPackets.Where(p => p.Length > 1 && p[0] == 0x55 && p[1] == subCmd).ToList();

	/// <summary>
	/// Reassembles the data bytes sent across all gateway write chunks for a given subcommand.
	/// Each chunk carries up to 56 bytes at buf[8..].
	/// </summary>
	public byte[] ReassembleGatewayWriteData(byte subCmd)
	{
		var chunks = GatewayWritePackets(subCmd);
		var result = new List<byte>();
		foreach (var chunk in chunks)
		{
			int len = chunk[4]; // length field
			result.AddRange(chunk.Skip(8).Take(len));
		}
		return [.. result];
	}
}
