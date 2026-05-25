namespace DrunkDeer.Protocol;

/// <summary>
/// Raised whenever a key's travel depth changes between two consecutive polls.
/// </summary>
public sealed class KeyHeightChangedEventArgs : EventArgs
{
	/// <summary>Zero-based layout index of the key that generated this event.</summary>
	public int Index { get; }

	/// <summary>
	/// Travel depth measured in the previous poll.
	/// Standard-precision models: 1 unit = 0.1 mm. High-precision: 1 unit = 0.005 mm.
	/// </summary>
	public short PreviousHeight { get; }

	/// <summary>
	/// Travel depth measured in the current poll.
	/// Standard-precision models: 1 unit = 0.1 mm. High-precision: 1 unit = 0.005 mm.
	/// </summary>
	public short Height { get; }

	internal KeyHeightChangedEventArgs(int index, short previousHeight, short height)
	{
		Index          = index;
		PreviousHeight = previousHeight;
		Height         = height;
	}
}
