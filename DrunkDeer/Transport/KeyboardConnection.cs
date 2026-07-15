using HidSharp;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DrunkDeer.Protocol;

/// <summary>
/// An open connection to a DrunkDeer keyboard. Performs the identity handshake on
/// construction and exposes typed send/receive over the underlying HID interface.
/// </summary>
public sealed class KeyboardConnection : IKeyboardConnection, IKeyboardConnectionAsync
{
	private readonly ILogger _log;

	private readonly HidTransport _transport;

	public ModelInfo Model { get; }
	public string Variant { get; }
	public byte FirmwareVersion { get; }

	/// <summary><see langword="true"/> if a separate read-only data stream was found for this keyboard.</summary>
	public bool HasDataStream => _transport.HasDataStream;

	/// <summary>Turbo mode state reported by the keyboard at connection time (0 = off, 1 = on).</summary>
	public byte InitialTurboValue { get; }
	/// <summary>Rapid Trigger enabled state reported at connection time (0 = off, 1 = on).</summary>
	public byte InitialRapidTriggerEnabled { get; }
	/// <summary>Last Win / Rapid Trigger mode reported at connection time (see <see cref="LastWinRapidTriggerMode"/>).</summary>
	public byte InitialLastWinValue { get; }
	/// <summary>Rapid Trigger Auto Match state reported at connection time (0 = off, 1 = on).</summary>
	public byte InitialRapidTriggerAutoMatch { get; }

	private KeyboardConnection(HidTransport transport, ModelInfo model, string variant, byte fw,
		byte initialTurboValue, byte initialRapidTriggerEnabled, byte initialLastWinValue, byte initialRapidTriggerAutoMatch,
		ILogger log)
	{
		_transport                    = transport;
		Model                         = model;
		Variant                       = variant;
		FirmwareVersion               = fw;
		InitialTurboValue             = initialTurboValue;
		InitialRapidTriggerEnabled    = initialRapidTriggerEnabled;
		InitialLastWinValue           = initialLastWinValue;
		InitialRapidTriggerAutoMatch  = initialRapidTriggerAutoMatch;
		_log                          = log;
	}

	/// <summary>
	/// Opens <paramref name="commandDevice"/> (the bidirectional command interface),
	/// also tries to open a read-only data stream on the same VID/PID,
	/// then performs the identity handshake to resolve the keyboard model.
	/// </summary>
	public static KeyboardConnection Open(HidDevice commandDevice, ILoggerFactory? loggerFactory = null)
	{
		var log = (ILogger?)loggerFactory?.CreateLogger<KeyboardConnection>() ?? NullLogger.Instance;

		var options = new OpenConfiguration();
		options.SetOption(OpenOption.Interruptible, true);

		log.LogDebug("Opening command interface: {Path}", commandDevice.DevicePath);

		if (!commandDevice.TryOpen(options, out var commandStream))
			throw new InvalidOperationException($"Cannot open HID command interface: {commandDevice.DevicePath}");

		commandStream.WriteTimeout = 5000;
		commandStream.ReadTimeout  = 5000;
		log.LogDebug("Command interface opened (MaxOut={Out} MaxIn={In})",
			commandDevice.GetMaxOutputReportLength(), commandDevice.GetMaxInputReportLength());

		// Try to open the read-only data stream (Out=0, In>=64) on the same physical device as
		// commandDevice. The keyboard streams unsolicited 0xB7 key-travel packets on this
		// interface. A same-VID/PID match alone isn't enough to identify "same physical device" -
		// with two identical keyboards attached, that would risk opening the *other* unit's data
		// interface and interleaving its packets into this session. Constrain to devices that
		// also share a serial number; if the serial number isn't available (not all platforms/
		// devices expose one), don't guess - fall back to command-stream-only polling instead of
		// risking a cross-device bind.
		HidStream? dataStream = null;
		string? commandSerial = TryGetSerialNumber(commandDevice);
		if (commandSerial is null)
		{
			log.LogDebug("Command device has no serial number; skipping data-stream search to avoid binding another physical device's interface.");
		}
		else
		{
			foreach (var device in DeviceList.Local.GetHidDevices())
			{
				if (device.VendorID != commandDevice.VendorID || device.ProductID != commandDevice.ProductID) continue;
				if (device.DevicePath == commandDevice.DevicePath) continue;
				if (TryGetSerialNumber(device) != commandSerial) continue;

				int outLen, inLen;
				try
				{
					outLen = device.GetMaxOutputReportLength();
					inLen  = device.GetMaxInputReportLength();
				}
				catch { continue; }

				log.LogDebug("Data-stream candidate: {Path}  Out={Out} In={In}", device.DevicePath, outLen, inLen);
				if (outLen != 0 || inLen < 64) continue;

				if (device.TryOpen(options, out dataStream))
				{
					dataStream.ReadTimeout = 200;
					log.LogDebug("Data stream opened: {Path}", device.DevicePath);
					break;
				}
			}
		}

		if (dataStream is null)
			log.LogWarning("No data stream found - falling back to command-stream polling.");

		var transport = new HidTransport(commandDevice, commandStream, dataStream, loggerFactory);

		try
		{
			byte[]? resp = null;
			byte[]? lastReceived = null;

			for (int attempt = 0; attempt < IdentityHandshake.Attempts; attempt++)
			{
				// drain anything buffered from before this session.
				transport.FlushReadBuffer();

				log.LogDebug("Sending IdentityRequest…");
				transport.Send(IdentityHandshake.BuildRequest());

				resp = transport.ReceiveCommand(IdentityHandshake.AttemptTimeoutMs);
				if (resp is not null)
				{
					log.LogDebug("Handshake attempt {N}: received {Len} bytes, matches={M}",
						attempt, resp.Length, IdentityResponse.Matches(resp));
					lastReceived = resp;
					if (IdentityHandshake.IsComplete(resp))
						break;
				}
				else
				{
					log.LogDebug("Handshake attempt {N}: timeout", attempt);
				}
				resp = null;
			}

			if (resp is null)
				throw new InvalidOperationException(
					$"No identity response received from device ({IdentityHandshake.DescribeFailure(lastReceived)}).");

			var identity = IdentityHandshake.Interpret(resp);
			log.LogInformation("Handshake complete: {Name} ({Variant}) fw={Fw}",
				identity.Model.Name, identity.Variant, identity.FirmwareVersion);
			return new KeyboardConnection(transport, identity.Model, identity.Variant, identity.FirmwareVersion,
				identity.InitialTurboValue,
				identity.InitialRapidTriggerEnabled,
				identity.InitialLastWinValue,
				identity.InitialRapidTriggerAutoMatch,
				log);
		}
		catch
		{
			transport.Dispose();
			throw;
		}
	}

	/// <summary>Returns the device's serial number, or <see langword="null"/> if unavailable.</summary>
	private static string? TryGetSerialNumber(HidDevice device)
	{
		try
		{
			var serial = device.GetSerialNumber();
			return string.IsNullOrEmpty(serial) ? null : serial;
		}
		catch
		{
			return null;
		}
	}

	/// <summary>Sends a pre-built packet with no response expected.</summary>
	public void Send(byte[] packet) => _transport.Send(packet);

	/// <summary>Reads from the data stream (if open) or command stream.</summary>
	public byte[]? Receive(int timeoutMs = 1000) => _transport.Receive(timeoutMs);

	/// <summary>Reads only from the command stream (request-response pattern).</summary>
	public byte[]? ReceiveCommand(int timeoutMs = 1000) => _transport.ReceiveCommand(timeoutMs);

	/// <summary>Sends a packet and waits for the next command-stream response.</summary>
	public byte[]? SendAndReceive(byte[] packet, int timeoutMs = 1000)
	{
		_transport.Send(packet);
		return _transport.ReceiveCommand(timeoutMs);
	}

	/// <summary>Drains any buffered input on both streams without blocking.</summary>
	public void FlushReadBuffer() => _transport.FlushReadBuffer();

	// ── Async transport (IKeyboardConnectionAsync) ────────────────────────────
	// HidSharp's stream is blocking-only, so the async surface offloads the blocking
	// read/write to the thread pool. That's acceptable on desktop (where threads are
	// plentiful); the point of the async seam is single-threaded hosts (WASM), which
	// use a native-async connection like WebHID instead of this one. The token cancels
	// the wait to *enqueue*/observe the work, not an in-flight native HID read.

	/// <summary>Sends a pre-built packet with no response expected.</summary>
	public ValueTask SendAsync(byte[] packet, CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		_transport.Send(packet);
		return ValueTask.CompletedTask;
	}

	/// <summary>Sends a packet and awaits the next command-stream response.</summary>
	public ValueTask<byte[]?> SendAndReceiveAsync(byte[] packet, int timeoutMs = 1000, CancellationToken cancellationToken = default) =>
		new(Task.Run(() =>
		{
			_transport.Send(packet);
			return _transport.ReceiveCommand(timeoutMs);
		}, cancellationToken));

	/// <summary>Awaits the next command-stream response.</summary>
	public ValueTask<byte[]?> ReceiveCommandAsync(int timeoutMs = 1000, CancellationToken cancellationToken = default) =>
		new(Task.Run(() => _transport.ReceiveCommand(timeoutMs), cancellationToken));

	/// <summary>Drains any buffered input without blocking on new data.</summary>
	public ValueTask FlushReadBufferAsync(CancellationToken cancellationToken = default) =>
		new(Task.Run(() => _transport.FlushReadBuffer(), cancellationToken));

	public void Dispose() => _transport.Dispose();
}
