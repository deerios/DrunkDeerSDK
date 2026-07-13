using HidSharp;
using Microsoft.Extensions.Logging;

namespace DrunkDeer.Protocol;

/// <summary>
/// Scans the system for connected DrunkDeer keyboards using the VID/PID table
/// from protocol/models.yaml (discovery section).
/// </summary>
public static class KeyboardDiscoverer
{
	// Kept in sync with protocol/models.yaml discovery section. Each entry is a specific
	// (vid, pid) pair, not a cross-product of independent VID and PID lists - matching the
	// cross-product would accept e.g. Apple's VID (0x05AC) with an unrelated PID, or Holtek's
	// VID (0x04D9, used by hundreds of third-party keyboards) with an unrelated PID.
	private static readonly (int Vid, int Pid)[] KnownIdentifiers =
	[
		(0x352D, 0x2382), (0x352D, 0x2383), (0x352D, 0x2384),
		(0x352D, 0x2386), (0x352D, 0x2387), (0x352D, 0x2391),
		(0x05AC, 0x024F),
		(0x04D9, 0x2A08),
		(0x1A85, 0xFC4F),
	];

	/// <summary>
	/// Returns all HID command interfaces that match a known DrunkDeer (VID, PID) pair.
	/// The command interface has both In and Out report lengths of at least 64 bytes.
	/// Returns an empty list if none are found - "find all" returning nothing found is a normal
	/// result, not an error; <see cref="OpenFirst"/> is where "no keyboard" becomes exceptional.
	/// </summary>
	public static IReadOnlyList<HidDevice> FindAll() =>
		DeviceList.Local
			.GetHidDevices()
			.Where(d => KnownIdentifiers.Contains((d.VendorID, d.ProductID)))
			.Where(IsCommandInterface)
			.ToList();

	/// <summary>
	/// Opens a <see cref="KeyboardConnection"/> on the first candidate device that completes the
	/// identity handshake. A (vid, pid) match is necessary but not sufficient - e.g. a
	/// third-party keyboard could still share a known pair - so a candidate that fails the
	/// handshake is skipped rather than aborting the whole scan.
	/// </summary>
	public static KeyboardConnection OpenFirst(ILoggerFactory? loggerFactory = null)
	{
		var candidates = FindAll();
		if (candidates.Count == 0)
			throw new DrunkDeerDeviceNotFoundException(
				"No DrunkDeer keyboard command interface found. Is your keyboard connected?");

		Exception? lastError = null;

		foreach (var device in candidates)
		{
			try
			{
				return KeyboardConnection.Open(device, loggerFactory);
			}
			catch (Exception ex)
			{
				lastError = ex;
			}
		}

		throw new DrunkDeerDeviceNotFoundException(
			$"Found {candidates.Count} candidate device(s) matching a known VID/PID, but none " +
			"completed the identity handshake. Is your keyboard connected and not in use by " +
			"another process?", lastError);
	}

	// Command interface: bidirectional, Out >= 64 and In >= 64.
	// (The read-only data interface has Out == 0 and is found separately in KeyboardConnection.Open.)
	private static bool IsCommandInterface(HidDevice d)
	{
		try { return d.GetMaxOutputReportLength() >= 64 && d.GetMaxInputReportLength() >= 64; }
		catch { return false; }
	}
}
