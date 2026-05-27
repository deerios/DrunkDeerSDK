using HidSharp;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DrunkDeer.Protocol;

/// <summary>
/// An open connection to a DrunkDeer keyboard. Performs the identity handshake on
/// construction and exposes typed send/receive over the underlying HID interface.
/// </summary>
public sealed class KeyboardConnection : IKeyboardConnection
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

		// Try to open the read-only data stream (Out=0, In>=64) on the same VID/PID.
		// The keyboard streams unsolicited 0xB7 key-travel packets on this interface.
		HidStream? dataStream = null;
		foreach (var device in DeviceList.Local.GetHidDevices())
		{
			if (device.VendorID != commandDevice.VendorID || device.ProductID != commandDevice.ProductID) continue;
			if (device.DevicePath == commandDevice.DevicePath) continue;
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

		if (dataStream is null)
			log.LogWarning("No data stream found - falling back to command-stream polling.");

		var transport = new HidTransport(commandDevice, commandStream, dataStream, loggerFactory);

		try
		{
			log.LogDebug("Sending IdentityRequest…");
			transport.Send(IdentityRequest.Build());

			byte[]? resp = null;
			byte[]? lastReceived = null;
			for (int attempt = 0; attempt < 20; attempt++)
			{
				resp = transport.ReceiveCommand(500);
				if (resp is not null)
				{
					log.LogDebug("Handshake attempt {N}: received {Len} bytes, matches={M}",
						attempt, resp.Length, IdentityResponse.Matches(resp));
					lastReceived = resp;
					if (IdentityResponse.Matches(resp))
						break;
				}
				else
				{
					log.LogDebug("Handshake attempt {N}: timeout", attempt);
				}
				resp = null;
			}

			if (resp is null)
			{
				string hint = lastReceived is null
					? "no packets received (timeout)"
					: $"last packet: [{string.Join(", ", lastReceived.Take(8).Select(b => $"0x{b:X2}"))}]";
				throw new InvalidOperationException($"No identity response received from device ({hint}).");
			}

			var id = IdentityResponse.GetModel(resp);
			log.LogDebug("Model bytes: {A:X2} {B:X2} {C:X2}", id[0], id[1], id[2]);

			var resolved = ModelRegistry.Resolve(id[0], id[1], id[2])
				?? throw new InvalidOperationException(
					$"Unknown model identity: 0x{id[0]:X2} 0x{id[1]:X2} 0x{id[2]:X2}");

			var info = ModelRegistry.GetInfo(resolved.Slug)
				?? throw new InvalidOperationException($"No ModelInfo for slug '{resolved.Slug}'.");

			byte fw = IdentityResponse.GetFirmwareVersion(resp);
			log.LogInformation("Handshake complete: {Name} ({Variant}) fw={Fw}", info.Name, resolved.Variant, fw);
			return new KeyboardConnection(transport, info, resolved.Variant, fw,
				IdentityResponse.GetTurboValue(resp),
				IdentityResponse.GetRapidTriggerEnabled(resp),
				IdentityResponse.GetLastWinValue(resp),
				IdentityResponse.GetRapidTriggerAutoMatch(resp),
				log);
		}
		catch
		{
			transport.Dispose();
			throw;
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

	public void Dispose() => _transport.Dispose();
}
