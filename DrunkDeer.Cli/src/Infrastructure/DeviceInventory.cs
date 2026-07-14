using DrunkDeer.Protocol;
using HidSharp;

namespace DrunkDeer.Cli.Infrastructure;

/// <summary>A single HID interface that matches a known DrunkDeer VID/PID.</summary>
public sealed class DeviceDescriptor(HidDevice device)
{
	public HidDevice Device { get; } = device;
	public int VendorId => Device.VendorID;
	public int ProductId => Device.ProductID;
	public string Path => Device.DevicePath;

	public string? Serial
	{
		get
		{
			try { return Device.GetSerialNumber(); }
			catch { return null; }
		}
	}

	public string? ProductName
	{
		get
		{
			try { return Device.GetProductName(); }
			catch { return null; }
		}
	}

	/// <summary>True when <paramref name="target"/> equals this interface's serial or device path (case-insensitive).</summary>
	public bool Matches(string target) =>
		string.Equals(Serial, target, StringComparison.OrdinalIgnoreCase) ||
		string.Equals(Path, target, StringComparison.OrdinalIgnoreCase);
}

/// <summary>Enumerates HID interfaces whose VID/PID is a known DrunkDeer pair (no handshake).</summary>
public static class DeviceInventory
{
	public static IReadOnlyList<DeviceDescriptor> FindAll() =>
		KeyboardDiscoverer.FindAll().Select(d => new DeviceDescriptor(d)).ToList();
}
