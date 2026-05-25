namespace DrunkDeer.Protocol;

/// <summary>
/// One Multi-Tap (MT) slot. An MT key fires different actions depending on whether it is
/// clicked (quick tap) or held down.
/// </summary>
/// <remarks>
/// Up to 32 MT slots can be configured per profile. A key is assigned to an MT slot by
/// setting its <see cref="UserKey.Type"/> to <see cref="UserKeyType.MultiTap"/> and its
/// <c>Param1</c> field to the zero-based slot index.
/// </remarks>
public readonly record struct MultiTapEntry
{
	/// <summary>Key (or action) fired on a quick tap (click).</summary>
	public UserKey ClickKey { get; init; }

	/// <summary>Key (or action) fired while held down.</summary>
	public UserKey DownKey { get; init; }

	internal const int ByteSize = 6;
	internal const int SlotCount = 32;

	internal static MultiTapEntry Decode(ReadOnlySpan<byte> data) => new MultiTapEntry
	{
		ClickKey = new UserKey { Type = data[0], Param1 = data[1], Param2 = data[2] },
		DownKey  = new UserKey { Type = data[3], Param1 = data[4], Param2 = data[5] },
	};

	internal static void Encode(in MultiTapEntry entry, Span<byte> data)
	{
		data[0] = entry.ClickKey.Type;
		data[1] = entry.ClickKey.Param1;
		data[2] = entry.ClickKey.Param2;
		data[3] = entry.DownKey.Type;
		data[4] = entry.DownKey.Param1;
		data[5] = entry.DownKey.Param2;
	}
}
