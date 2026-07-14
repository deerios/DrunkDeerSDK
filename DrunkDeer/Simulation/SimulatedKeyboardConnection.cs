using System.Buffers.Binary;
using DrunkDeer.Protocol;

namespace DrunkDeer.Simulation;

/// <summary>
/// A hardware-free <see cref="IKeyboardConnection"/> that behaves like a real keyboard well enough
/// to develop and demo against without a device: it synthesises travel frames for the poll loop
/// (from an in-memory per-key travel model you drive with <see cref="SetKeyTravelMm"/> /
/// <see cref="ReleaseAll"/>, optionally plus idle jitter) and acknowledges every configuration
/// write so nothing throws.
/// </summary>
/// <remarks>
/// This is a <em>simulator</em>, distinct from the test-only <c>FakeKeyboardConnection</c> (a strict
/// response queue). It answers command-stream reads only (<see cref="HasDataStream"/> is
/// <see langword="false"/>), matching the WebHID transport's command-stream-only reality. Config
/// reads (e.g. profile capture) return zero-filled data - a blank keyboard - which is plausible and
/// keeps those paths from failing. Only the A75 is verified hardware, so frame values here are
/// synthetic, not a capture.
/// </remarks>
public sealed class SimulatedKeyboardConnection : IKeyboardConnection, IKeyboardConnectionAsync
{
	// Mirror of KeyboardSession's layout constants (private there): 3 standard B7 packets vs
	// 5 high-precision 0xFD/0x06 sections, and their per-packet key ranges.
	private static readonly int[] StdPacketBase  = [0, 59, 118];
	private static readonly int[] StdPacketCount = [59, 59, 9];
	private static readonly int[] HpSectionBase  = [0, 30, 60, 90, 120];
	private static readonly int[] HpSectionSizes = [30, 30, 30, 30, 6];

	private readonly Lock _gate = new();
	private readonly PrecisionMode _precision;
	private readonly int _keyCount;
	private readonly int[] _travelRaw;         // per-slot raw travel, in this model's units
	private readonly Queue<byte[]> _pending = new();
	private readonly Random _rng = new();

	/// <summary>Model metadata this connection reports (defaults to the A75).</summary>
	public ModelInfo Model { get; }
	/// <summary>Variant string this connection reports (defaults to "ansi").</summary>
	public string Variant { get; }
	/// <summary>Firmware version this connection reports.</summary>
	public byte FirmwareVersion { get; }
	/// <summary>Always <see langword="false"/>: the simulator serves the command stream only.</summary>
	public bool HasDataStream => false;

	/// <summary>Turbo state reported at connect time (default 0 = off).</summary>
	public byte InitialTurboValue { get; init; }
	/// <summary>Rapid Trigger state reported at connect time (default 0 = off).</summary>
	public byte InitialRapidTriggerEnabled { get; init; }
	/// <summary>Last Win / Rapid Trigger mode reported at connect time (default 0).</summary>
	public byte InitialLastWinValue { get; init; }
	/// <summary>Rapid Trigger Auto Match state reported at connect time (default 0 = off).</summary>
	public byte InitialRapidTriggerAutoMatch { get; init; }

	/// <summary>
	/// When <see langword="true"/>, each synthesised frame adds a little random travel to a few keys
	/// so a demo display looks alive at rest. Off by default so tests stay deterministic.
	/// </summary>
	public bool IdleJitter { get; set; }

	/// <param name="model">Model to advertise; defaults to the A75.</param>
	/// <param name="firmwareVersion">Firmware version to advertise (affects Kun-precision upgrade).</param>
	/// <param name="variant">Variant to advertise (e.g. "ansi", "iso").</param>
	public SimulatedKeyboardConnection(ModelInfo? model = null, byte firmwareVersion = 1, string variant = "ansi")
	{
		Model           = model ?? ModelRegistry.GetInfo(ModelSlugs.A75)
						  ?? throw new InvalidOperationException("A75 model info is missing from the registry.");
		FirmwareVersion = firmwareVersion;
		Variant         = variant;
		_precision      = KeyboardSession.DeterminePrecisionMode(Model, firmwareVersion);
		// HighPrecision boards address 126 key slots; everything else 127 (matches KeyboardSession).
		_keyCount       = _precision == PrecisionMode.HighPrecision ? 126 : 127;
		_travelRaw      = new int[_keyCount];
	}

	/// <summary>Number of addressable key slots for the simulated model (126 HP, 127 otherwise).</summary>
	public int KeyCount => _keyCount;

	/// <summary>mm-to-raw scale for the travel stream: Standard = 10, Kun = 100, HighPrecision = 200.</summary>
	private float HeightScale => _precision switch
	{
		PrecisionMode.HighPrecision => 200f,
		PrecisionMode.Kun => 100f,
		_ => 10f,
	};

	/// <summary>Standard/Kun frames store one byte per key; HighPrecision stores a u16.</summary>
	private int MaxRaw => _precision == PrecisionMode.HighPrecision ? ushort.MaxValue : byte.MaxValue;

	/// <summary>
	/// Sets the simulated travel depth for a raw key slot in millimetres. Reflected on the next poll.
	/// </summary>
	public void SetKeyTravelMm(int slot, float mm) =>
		SetKeyTravelRaw(slot, (int)MathF.Round(mm * HeightScale));

	/// <summary>Sets the simulated travel for a raw key slot in this model's raw units.</summary>
	public void SetKeyTravelRaw(int slot, int raw)
	{
		if ((uint)slot >= (uint)_keyCount)
			throw new ArgumentOutOfRangeException(nameof(slot), $"Slot {slot} is out of range [0, {_keyCount - 1}].");
		lock (_gate)
			_travelRaw[slot] = Math.Clamp(raw, 0, MaxRaw);
	}

	/// <summary>Returns every simulated key to rest (0 travel).</summary>
	public void ReleaseAll()
	{
		lock (_gate)
			Array.Clear(_travelRaw);
	}

	// ── IKeyboardConnection transport ─────────────────────────────────────────

	public void Send(byte[] packet)
	{
		// The poll loop's travel request ([0xB6, 0x03, 0x01]) is the one command sent without
		// awaiting a response; answer it by staging a full frame for the following ReceiveCommand
		// calls. Any other fire-and-forget send is simply accepted.
		if (IsTravelRequest(packet))
			StageFrame();
	}

	public byte[]? ReceiveCommand(int timeoutMs = 1000)
	{
		lock (_gate)
			return _pending.Count > 0 ? _pending.Dequeue() : null;
	}

	public byte[]? Receive(int timeoutMs = 1000) => ReceiveCommand(timeoutMs);

	public byte[]? SendAndReceive(byte[] packet, int timeoutMs = 1000) => SynthesizeAck(packet);

	public void FlushReadBuffer()
	{
		lock (_gate)
			_pending.Clear();
	}

	// ── Async transport (IKeyboardConnectionAsync) ────────────────────────────
	// Mirrors the sync surface. Sends stage/ack synchronously (cheap, no I/O); a receive
	// on an empty queue waits out the timeout honouring cancellation, matching a real
	// keyboard's blocking read so the async poll loop paces itself the same way.

	public ValueTask SendAsync(byte[] packet, CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		Send(packet);
		return ValueTask.CompletedTask;
	}

	public ValueTask<byte[]?> SendAndReceiveAsync(byte[] packet, int timeoutMs = 1000, CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		return new(SendAndReceive(packet, timeoutMs));
	}

	public async ValueTask<byte[]?> ReceiveCommandAsync(int timeoutMs = 1000, CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		lock (_gate)
		{
			if (_pending.Count > 0)
				return _pending.Dequeue();
		}
		if (timeoutMs > 0)
			await Task.Delay(timeoutMs, cancellationToken).ConfigureAwait(false);
		return null;
	}

	public ValueTask FlushReadBufferAsync(CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		FlushReadBuffer();
		return ValueTask.CompletedTask;
	}

	public void Dispose() { }

	// ── Frame synthesis ───────────────────────────────────────────────────────

	private static bool IsTravelRequest(byte[] p) =>
		p.Length >= 3 && p[0] == 0xB6 && p[1] == 0x03 && p[2] == 0x01;

	private void StageFrame()
	{
		lock (_gate)
		{
			var travel = (int[])_travelRaw.Clone();
			if (IdleJitter)
				ApplyJitter(travel);

			if (_precision == PrecisionMode.HighPrecision)
				StageHighPrecisionFrame(travel);
			else
				StageStandardFrame(travel);
		}
	}

	private void StageStandardFrame(int[] travel)
	{
		for (int pkt = 0; pkt < StdPacketBase.Length; pkt++)
		{
			var buf = new byte[64];
			buf[0] = 0xB7;
			buf[3] = (byte)pkt;
			int baseIdx = StdPacketBase[pkt];
			int count   = StdPacketCount[pkt];
			for (int x = 0; x < count; x++)
				buf[4 + x] = (byte)Math.Clamp(travel[baseIdx + x], 0, byte.MaxValue);
			_pending.Enqueue(buf);
		}
	}

	private void StageHighPrecisionFrame(int[] travel)
	{
		for (int sec = 0; sec < HpSectionBase.Length; sec++)
		{
			var buf = new byte[64];
			buf[0] = 0xFD;
			buf[1] = 0x06;
			int baseKey = HpSectionBase[sec];
			int count   = HpSectionSizes[sec];
			for (int x = 0; x < count; x++)
				BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(2 + x * 2), (ushort)travel[baseKey + x]);
			_pending.Enqueue(buf);
		}
	}

	private void ApplyJitter(int[] travel)
	{
		// Nudge a handful of resting keys by up to ~0.1 mm so an idle demo display shimmers.
		int max = (int)MathF.Round(0.1f * HeightScale);
		if (max <= 0) return;
		for (int i = 0; i < 4; i++)
		{
			int slot = _rng.Next(travel.Length);
			if (travel[slot] == 0)
				travel[slot] = _rng.Next(max + 1);
		}
	}

	// ── Config acknowledgement ────────────────────────────────────────────────

	// Every configuration path expects a response whose leading byte identifies the ACK: key-point
	// writes 0xB6 (standard) / 0xFD (high precision), lighting 0xAE, common config 0xB5, and the
	// FuncBlock gateway 0xAA (both reads and writes). Reads reassemble the zero-filled payload.
	private static byte[] SynthesizeAck(byte[] request)
	{
		byte ackHeader = request.Length == 0 ? (byte)0 : request[0] switch
		{
			0x55 => (byte)0xAA,   // extended gateway read/write -> 0xAA response
			_    => request[0],
		};
		var resp = new byte[64];
		resp[0] = ackHeader;
		return resp;
	}
}
