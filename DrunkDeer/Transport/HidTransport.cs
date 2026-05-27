using HidSharp;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DrunkDeer.Protocol;

/// <summary>
/// Raw HID read/write over a command stream (bidirectional) and an optional
/// data stream (read-only, used for unsolicited 0xB7 key-travel packets).
/// Follows the same buffer-sizing strategy as DDSharp's HidInterface.
/// </summary>
internal sealed class HidTransport : IDisposable
{
	private readonly ILogger _log;

	private readonly HidDevice _device;
	private readonly HidStream _command;
	private readonly HidStream? _data;

	internal HidTransport(HidDevice device, HidStream command, HidStream? data, ILoggerFactory? loggerFactory = null)
	{
		_device  = device;
		_command = command;
		_data    = data;
		_log     = (ILogger?)loggerFactory?.CreateLogger<HidTransport>() ?? NullLogger.Instance;
	}

	public bool HasDataStream => _data is not null;

	/// <summary>
	/// Writes <paramref name="packet"/> to the command stream.
	/// DrunkDeer keyboards use HID output report ID 4; it is prepended here so
	/// callers work with plain protocol bytes and never touch the report ID.
	/// </summary>
	public void Send(byte[] packet)
	{
		int reportLen = _device.GetMaxOutputReportLength(); // includes the report-ID byte
		var buf = new byte[reportLen];
		buf[0] = 0x04; // HID output report ID
		Array.Copy(packet, 0, buf, 1, Math.Min(packet.Length, reportLen - 1));
		_log.LogTrace("TX  [{Hex}]", Hex(buf));
		_command.Write(buf);
	}

	/// <summary>
	/// Reads from the data stream when available (unsolicited), otherwise from the
	/// command stream. Returns <c>null</c> on timeout.
	/// </summary>
	public byte[]? Receive(int timeoutMs = 1000) =>
		ReadFrom(_data ?? _command, timeoutMs, _data is not null ? "DATA" : "CMD");

	/// <summary>Always reads from the command stream (request-response).</summary>
	public byte[]? ReceiveCommand(int timeoutMs = 1000) =>
		ReadFrom(_command, timeoutMs, "CMD");

	public void FlushReadBuffer()
	{
		foreach (var stream in new[] { _command, _data })
		{
			if (stream is null) continue;
			int saved = stream.ReadTimeout;
			stream.ReadTimeout = 1;
			try
			{
				var tmp = new byte[_device.GetMaxInputReportLength()];
				int attempts = 0;
				while (attempts++ < 32 && stream.Read(tmp, 0, tmp.Length) > 0) { }
			}
			catch { }
			finally { stream.ReadTimeout = saved; }
		}
		_log.LogTrace("FlushReadBuffer complete");
	}

	private byte[]? ReadFrom(HidStream stream, int timeoutMs, string label)
	{
		stream.ReadTimeout = timeoutMs;
		var buf = new byte[_device.GetMaxInputReportLength()]; // includes leading report-ID byte
		int read;
		try { read = stream.Read(buf, 0, buf.Length); }
		catch (TimeoutException)
		{
			_log.LogTrace("RX  {Label} timeout ({Ms}ms)", label, timeoutMs);
			return null;
		}
		catch (Exception ex)
		{
			_log.LogError(ex, "RX  {Label} read exception", label);
			return null;
		}
		if (read <= 1)
		{
			_log.LogTrace("RX  {Label} too-short ({N} bytes)", label, read);
			return null;
		}
		var result = buf[1..read]; // strip the report-ID byte; callers see protocol bytes at index 0
		_log.LogTrace("RX  {Label} [{Hex}]", label, Hex(result));
		return result;
	}

	private static string Hex(byte[] buf) =>
		string.Join(" ", buf.Select(b => $"{b:X2}"));

	public void Dispose()
	{
		_command.Dispose();
		_data?.Dispose();
	}
}
