using System.Buffers.Binary;
using System.Numerics;

namespace DrunkDeer.Protocol;

/// <summary>The kind of input event action <see cref="MacroAction"/> represents.</summary>
public enum MacroEventType : byte
{
	/// <summary>A keyboard key was pressed down.</summary>
	KeyDown,
	/// <summary>A keyboard key was released.</summary>
	KeyUp,
	/// <summary>A mouse button was pressed down.</summary>
	MouseDown,
	/// <summary>A mouse button was released.</summary>
	MouseUp,
}

/// <summary>
/// One step in action macro sequence. Each action describes an input event and the
/// delay to wait after it fires before the next action begins.
/// </summary>
/// <remarks>
/// For <see cref="MacroEventType.KeyDown"/> and <see cref="MacroEventType.KeyUp"/>,
/// <see cref="Code"/> is the HID keyboard usage code (e.g. 0x04 = A, 0xE0 = Left Ctrl).
/// Modifier keys use usage codes 0xE0-0xE7.
///
/// For <see cref="MacroEventType.MouseDown"/> and <see cref="MacroEventType.MouseUp"/>,
/// <see cref="Code"/> is the mouse button index: 0 = left, 1 = right, 2 = middle.
/// </remarks>
public readonly record struct MacroAction
{
	/// <summary>The type of input event.</summary>
	public MacroEventType EventType { get; init; }

	/// <summary>
	/// HID usage code for keyboard events, or mouse button index for mouse events.
	/// </summary>
	public byte Code { get; init; }

	/// <summary>
	/// Time in milliseconds to wait after this action fires before the next action
	/// begins. Ignored for the final action in a macro slot.
	/// </summary>
	public ushort DelayAfterMs { get; init; }

	/// <summary>Creates a key-down action for the given HID usage code.</summary>
	public static MacroAction KeyDown(byte hidCode, ushort delayAfterMs = 0) =>
		new() { EventType = MacroEventType.KeyDown, Code = hidCode, DelayAfterMs = delayAfterMs };

	/// <summary>Creates a key-up action for the given HID usage code.</summary>
	public static MacroAction KeyUp(byte hidCode, ushort delayAfterMs = 0) =>
		new() { EventType = MacroEventType.KeyUp, Code = hidCode, DelayAfterMs = delayAfterMs };

	/// <summary>Creates a mouse-button-down action for the given button index.</summary>
	public static MacroAction MouseDown(byte button, ushort delayAfterMs = 0) =>
		new() { EventType = MacroEventType.MouseDown, Code = button, DelayAfterMs = delayAfterMs };

	/// <summary>Creates a mouse-button-up action for the given button index.</summary>
	public static MacroAction MouseUp(byte button, ushort delayAfterMs = 0) =>
		new() { EventType = MacroEventType.MouseUp, Code = button, DelayAfterMs = delayAfterMs };

	// Wire layout (4 bytes):
	//   [0..1] LE16 delay  - delay BEFORE the next step (= this step's DelayAfterMs stored here)
	//   [2]    flags       - bits 0-5: key_type (1=mod, 2=key, 3=mouse), bit 6=isDown, bit 7=isLast
	//   [3]    code        - HID usage or bitmask for modifiers

	private const byte KeyTypeModifier = 1;
	private const byte KeyTypeKey = 2;
	private const byte KeyTypeMouse = 3;

	// HID modifier usage range (Left Ctrl … Right Meta)
	private const byte ModifierMin = 0xE0;
	private const byte ModifierMax = 0xE7;

	internal static void Encode(ReadOnlySpan<MacroAction> actions, Span<byte> dest, int offset)
	{
		for (int i = 0; i < actions.Length; i++)
		{
			var action = actions[i];
			bool isDown = action.EventType is MacroEventType.KeyDown or MacroEventType.MouseDown;
			bool isMouse = action.EventType is MacroEventType.MouseDown or MacroEventType.MouseUp;
			bool isModifier = !isMouse && action.Code >= ModifierMin && action.Code <= ModifierMax;
			bool isLast = i == actions.Length - 1;

			ushort delayToStore = actions[i].DelayAfterMs;

			byte keyType = isMouse ? KeyTypeMouse : (isModifier ? KeyTypeModifier : KeyTypeKey);
			byte flags = (byte)(keyType | (isDown ? 0x40 : 0) | (isLast ? 0x80 : 0));
			byte wireCode = isModifier
				? (byte)(1 << (action.Code - ModifierMin))
				: action.Code;

			BinaryPrimitives.WriteUInt16LittleEndian(dest.Slice(offset), delayToStore);
			dest[offset + 2] = flags;
			dest[offset + 3] = wireCode;
			offset += 4;
		}
	}

	internal static MacroAction[] Decode(ReadOnlySpan<byte> data, int offset)
	{
		var result = new List<MacroAction>();
		while (offset + 4 <= data.Length)
		{
			ushort delay = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(offset));
			byte flags = data[offset + 2];
			byte code = data[offset + 3];
			offset += 4;

			byte keyType = (byte)(flags & 0x3F);
			bool isDown = (flags & 0x40) != 0;
			bool isLast = (flags & 0x80) != 0;

			MacroEventType eventType = keyType switch
			{
				KeyTypeMouse => isDown ? MacroEventType.MouseDown : MacroEventType.MouseUp,
				_ => isDown ? MacroEventType.KeyDown : MacroEventType.KeyUp,
			};

			// Modifier bitmask -> HID usage code
			byte hidCode = keyType == KeyTypeModifier
				? (byte)(ModifierMin + BitOperations.TrailingZeroCount(code))
				: code;

			result.Add(new MacroAction
			{
				EventType    = eventType,
				Code         = hidCode,
				DelayAfterMs = delay,
			});

			if (isLast) break;
		}
		return [.. result];
	}

	/// <summary>Number of macro slots per profile.</summary>
	public const int SlotCount = 32;

	/// <summary>Total byte size of the macro region per profile.</summary>
	public const int BlockSize = 2048;

	private const int HeaderSize = SlotCount * 2; // 32 × LE16 = 64 bytes

	/// <summary>
	/// Encodes 32 macro slots into the 2048-byte wire block.
	/// Null or empty arrays produce empty slot entries in the header.
	/// </summary>
	public static byte[] EncodeBlock(MacroAction[]?[] slots)
	{
		if (slots.Length != SlotCount)
			throw new ArgumentException(
				$"Expected {SlotCount} macro slots, got {slots.Length}.", nameof(slots));

		// Pre-compute total size so an overflow is a clear, up-front ArgumentException instead of
		// an ArgumentOutOfRangeException thrown mid-encode from a span slice, after the header
		// (and any earlier slots' data) has already been written into `block`.
		int totalBytes = HeaderSize + 4;
		foreach (var actions in slots)
			if (actions is { Length: > 0 })
				totalBytes += actions.Length * 4;
		if (totalBytes > BlockSize)
			throw new ArgumentException(
				$"Macro slots require {totalBytes} bytes, but the wire block only holds " +
				$"{BlockSize} bytes ({(BlockSize - HeaderSize - 4) / 4} total actions across all " +
				"slots). Reduce the number of actions.", nameof(slots));

		var block = new byte[BlockSize];
		// dataOffset starts at 4 so the first non-empty slot's ptr = HeaderSize + 4 = 68,
		// ensuring it never collides with the empty sentinel 0x0040 (= HeaderSize = 64).
		int dataOffset = 4;

		for (int i = 0; i < SlotCount; i++)
		{
			var actions = slots[i];
			if (actions is not { Length: > 0 })
			{
				// Empty sentinel: pointer = 0x0040 (64)
				BinaryPrimitives.WriteUInt16LittleEndian(block.AsSpan(i * 2), 0x0040);
				continue;
			}

			// Pointer = absolute offset within block = HeaderSize + dataOffset
			ushort ptr = (ushort)(HeaderSize + dataOffset);
			BinaryPrimitives.WriteUInt16LittleEndian(block.AsSpan(i * 2), ptr);
			Encode(actions, block, ptr);
			dataOffset += actions.Length * 4;
		}

		return block;
	}

	/// <summary>
	/// Decodes the 2048-byte wire block into 32 macro slots.
	/// Empty slots are returned as empty arrays.
	/// </summary>
	public static MacroAction[][] DecodeBlock(ReadOnlySpan<byte> block)
	{
		if (block.Length < BlockSize)
			throw new ArgumentException(
				$"Macro block must be at least {BlockSize} bytes, got {block.Length}.", nameof(block));

		var result = new MacroAction[SlotCount][];
		for (int i = 0; i < SlotCount; i++)
		{
			ushort ptr = BinaryPrimitives.ReadUInt16LittleEndian(block.Slice(i * 2));
			if (ptr == 0 || ptr == 0x0040 || ptr >= BlockSize)
			{
				result[i] = [];
				continue;
			}
			result[i] = Decode(block, ptr);
		}
		return result;
	}
}
