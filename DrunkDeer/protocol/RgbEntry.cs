namespace DrunkDeer.Protocol;

public readonly partial struct RgbEntry
{
	/// <summary>
	/// Creates an entry for the key at <paramref name="layoutIndex"/> with the given colour.
	/// Prefer this over the raw constructor - it encodes the required 0x80 flag automatically.
	/// </summary>
	public static RgbEntry Create(int layoutIndex, byte r, byte g, byte b) =>
		new((byte)(layoutIndex | 0x80), r, g, b);
}
