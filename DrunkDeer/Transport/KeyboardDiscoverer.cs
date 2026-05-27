using HidSharp;
using Microsoft.Extensions.Logging;

namespace DrunkDeer.Protocol;

/// <summary>
/// Scans the system for connected DrunkDeer keyboards using the VID/PID table
/// from protocol/models.yaml (discovery section).
/// </summary>
public static class KeyboardDiscoverer
{
	// Kept in sync with protocol/models.yaml discovery section.
	private static readonly int[] KnownVids = [0x352D, 0x05AC, 0x04D9, 0x1A85];
	private static readonly int[] KnownPids = [0x2382, 0x2383, 0x2384, 0x2386, 0x2387, 0x2391,
												0x024F, 0x2A08, 0xFC4F];

	/// <summary>
	/// Returns all HID command interfaces that match a known DrunkDeer VID/PID.
	/// The command interface has both In and Out report lengths of at least 64 bytes.
	/// </summary>
	public static IReadOnlyList<HidDevice> FindAll()
	{
		var matches = DeviceList.Local
			.GetHidDevices()
			.Where(d => KnownVids.Contains(d.VendorID) && KnownPids.Contains(d.ProductID))
			.Where(IsCommandInterface)
			.ToList();

		if (matches.Count == 0)
			throw new DrunkDeerDeviceNotFoundException(
				"No DrunkDeer keyboard command interface found. Is your keyboard connected?");

		return matches;
	}

	/// <summary>
	/// Opens a <see cref="KeyboardConnection"/> on the first matching device.
	/// </summary>
	public static KeyboardConnection OpenFirst(ILoggerFactory? loggerFactory = null) =>
		KeyboardConnection.Open(FindAll()[0], loggerFactory);

	// Command interface: bidirectional, Out >= 64 and In >= 64.
	// (The read-only data interface has Out == 0 and is found separately in KeyboardConnection.Open.)
	private static bool IsCommandInterface(HidDevice d)
	{
		try { return d.GetMaxOutputReportLength() >= 64 && d.GetMaxInputReportLength() >= 64; }
		catch { return false; }
	}
}
