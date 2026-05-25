namespace DrunkDeer.Protocol;

/// <summary>
/// Raised when a key is pressed past <see cref="KeyboardSession.PressThresholdMm"/>
/// or released below <see cref="KeyboardSession.ReleaseThresholdMm"/>.
/// </summary>
public sealed class KeyEventArgs : EventArgs
{
	/// <summary>Zero-based layout index of the key that generated this event.</summary>
	public int Index { get; }

	/// <summary>
	/// Raw travel depth at the moment this event fired.
	/// Standard-precision models: 1 unit = 0.1 mm (signed i8 range).
	/// High-precision models: 1 unit = 0.005 mm (u16 range, dead-zone below 40).
	/// </summary>
	public short Height { get; }

	internal KeyEventArgs(int index, short height) { Index = index; Height = height; }
}
