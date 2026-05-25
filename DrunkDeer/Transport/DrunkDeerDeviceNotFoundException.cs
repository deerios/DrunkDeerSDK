namespace DrunkDeer.Protocol;

/// <summary>
/// Thrown by <see cref="KeyboardDiscoverer"/> when no DrunkDeer keyboard is detected.
/// Check that the keyboard is plugged in and that its VID/PID is in the supported list.
/// </summary>
public class DrunkDeerDeviceNotFoundException : Exception
{
	public DrunkDeerDeviceNotFoundException() { }

	public DrunkDeerDeviceNotFoundException(string? message) : base(message) { }

	public DrunkDeerDeviceNotFoundException(string? message, Exception? innerException)
		: base(message, innerException) { }
}
