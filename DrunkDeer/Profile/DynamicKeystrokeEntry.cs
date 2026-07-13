namespace DrunkDeer.Protocol;

/// <summary>
/// The fire-event pattern for one of the four action slots in a <see cref="DynamicKeystrokeEntry"/>.
/// Each depth field encodes a level (0-3) at which the action fires using a thermometer
/// code: 0 = never, 1 = at the shallow threshold, 2 = at the mid threshold, 3 = at the
/// deep threshold.
/// </summary>
public readonly record struct DynamicKeystrokeAction
{
	/// <summary>The key (or action) that fires at this slot.</summary>
	public UserKey Key { get; init; }

	/// <summary>Depth level at which the key fires when pressing down starts (0-3).</summary>
	public byte DownStart { get; init; }

	/// <summary>Depth level at which the key fires when pressing down ends (0-3).</summary>
	public byte DownEnd { get; init; }

	/// <summary>Depth level at which the key fires when releasing starts (0-3).</summary>
	public byte UpStart { get; init; }

	/// <summary>Whether the key fires when releasing ends.</summary>
	public bool UpEnd { get; init; }
}

/// <summary>
/// One Dynamic Keystroke (DKS) slot. A DKS key maps four depth thresholds to four
/// independent key actions that fire at configurable press and release events.
/// </summary>
/// <remarks>
/// Up to 32 DKS slots can be configured per profile. A key is assigned to a DKS slot
/// by setting its <see cref="UserKey.Type"/> to <see cref="UserKeyType.DynamicKeystroke"/> and its
/// <c>Param1</c> field to the zero-based slot index.
/// </remarks>
public sealed class DynamicKeystrokeEntry
{
	/// <summary>
	/// Four depth thresholds in firmware units (1 unit = 0.1 mm). Default: [10, 30, 30, 10].
	/// Use <see cref="WithPointsMm"/> to set these in mm instead.
	/// </summary>
	public byte[] Points { get; init; } = [10, 30, 30, 10];

	/// <summary>Four key actions, one per depth zone.</summary>
	public DynamicKeystrokeAction[] Actions { get; init; } = new DynamicKeystrokeAction[4];

	internal const int ByteSize = 24;
	internal const int SlotCount = 32;

	private static byte MmToUnit(float mm) =>
		(byte)Math.Clamp((int)Math.Round(mm * 10, MidpointRounding.AwayFromZero), 0, 255);

	/// <summary>
	/// Returns a copy of this entry with the four depth thresholds set from mm values.
	/// 1 firmware unit = 0.1 mm; values are rounded to the nearest unit and clamped to [0, 255].
	/// </summary>
	/// <param name="point1Mm">Shallow threshold in mm (typically the first press zone).</param>
	/// <param name="point2Mm">Mid-upper threshold in mm.</param>
	/// <param name="point3Mm">Mid-lower threshold in mm.</param>
	/// <param name="point4Mm">Deep threshold in mm (typically the bottom zone).</param>
	/// <example>
	/// <code>
	/// var dks = new DynamicKeystrokeEntry { Actions = [...] }.WithPointsMm(1.0f, 2.5f, 2.5f, 1.0f);
	/// </code>
	/// </example>
	public DynamicKeystrokeEntry WithPointsMm(float point1Mm, float point2Mm, float point3Mm, float point4Mm) =>
		new DynamicKeystrokeEntry
		{
			Points  = [MmToUnit(point1Mm), MmToUnit(point2Mm), MmToUnit(point3Mm), MmToUnit(point4Mm)],
			Actions = Actions,
		};

	// The fire-event fields (DownStart, DownEnd, UpStart) use 3-bit thermometer
	// encoding: 0->0, 1->1, 2->3, 3->7.
	// UpEnd is a single bit: false (0) = no event, true (1) = fires on release-end.

	private static byte Encode3(byte level) => level switch
	{
		0 => 0,
		1 => 1,
		2 => 3,
		_ => 7,
	};

	private static byte Decode3(int bits) => (byte)((bits & 0x07) switch
	{
		0 => 0,
		1 => 1,
		3 => 2,
		_ => 3,
	});

	internal static DynamicKeystrokeEntry Decode(ReadOnlySpan<byte> data)
	{
		var points = new byte[] { data[0], data[1], data[2], data[3] };
		var actions = new DynamicKeystrokeAction[4];
		for (int k = 0; k < 4; k++)
		{
			int b = 5 * k + 4;
			int fp = data[b + 3] | (data[b + 4] << 8); // fire-pattern LE16
			actions[k] = new DynamicKeystrokeAction
			{
				Key       = new UserKey { Type = data[b], Param1 = data[b + 1], Param2 = data[b + 2] },
				DownStart = Decode3(fp),
				DownEnd   = Decode3(fp >> 3),
				UpStart   = Decode3(fp >> 6),
				UpEnd     = ((fp >> 9) & 0x01) != 0,
			};
		}
		return new DynamicKeystrokeEntry { Points = points, Actions = actions };
	}

	internal static void Encode(DynamicKeystrokeEntry entry, Span<byte> data)
	{
		data[0] = entry.Points[0];
		data[1] = entry.Points[1];
		data[2] = entry.Points[2];
		data[3] = entry.Points[3];
		for (int k = 0; k < 4; k++)
		{
			int b = 5 * k + 4;
			var act = k < entry.Actions.Length ? entry.Actions[k] : default;
			int fp = (Encode3(act.DownStart) & 0x07)
				   | ((Encode3(act.DownEnd) & 0x07) << 3)
				   | ((Encode3(act.UpStart) & 0x07) << 6)
				   | ((act.UpEnd ? 1 : 0) << 9);
			data[b]     = act.Key.Type;
			data[b + 1] = act.Key.Param1;
			data[b + 2] = act.Key.Param2;
			data[b + 3] = (byte)(fp & 0xFF);
			data[b + 4] = (byte)(fp >> 8);
		}
	}
}
