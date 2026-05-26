namespace DrunkDeer.Protocol;

/// <summary>
/// Layer indices for <see cref="KeyRemapTable"/> and <see cref="KeyboardSession.SetRemap"/>.
/// </summary>
public static class RemapLayer
{
	/// <summary>Default layer — active when no Fn key is held.</summary>
	public const int Default = 0;
	/// <summary>Fn1 layer — active while the first Fn key is held.</summary>
	public const int Fn1 = 1;
	/// <summary>Fn2 layer — active while the second Fn key is held.</summary>
	public const int Fn2 = 2;
}

/// <summary>
/// A 126-slot × 3-layer key remap table sent to the keyboard via
/// <see cref="KeyboardSession.SetRemap"/>.
/// <para>
/// Each firmware slot corresponds to one physical key position (0–125).
/// Use <see cref="KeyboardSession.GetKeyIndex(Keys.DDKey)"/> to map a
/// named key to its slot number.
/// </para>
/// <para>
/// Slots left unset are zero-filled on the wire. The firmware interprets a
/// zero entry as "no key at this position", which suppresses HID output for
/// that key. Always populate all slots you want to emit HID codes — call
/// <see cref="SetHidKey"/> for every physical key that should remain active.
/// </para>
/// </summary>
public sealed class KeyRemapTable
{
	internal const int SlotCount  = 126; // 14 chunks × 9 entries per chunk
	internal const int LayerCount = 3;

	// [layer, slot] → raw 4 data bytes (null = empty/unset → sends all-zero entry)
	private readonly (byte cmd, byte d0, byte d1, byte d2, byte d3)?[,] _entries =
		new (byte cmd, byte d0, byte d1, byte d2, byte d3)?[LayerCount, SlotCount];

	/// <summary>
	/// Assigns a standard HID keyboard key code to a firmware slot.
	/// </summary>
	/// <param name="slot">Firmware slot index 0–125. Obtain via <see cref="KeyboardSession.GetKeyIndex"/>.</param>
	/// <param name="hidCode">HID Keyboard Usage ID (e.g. 0x04 = A, 0x28 = Enter, 0x29 = Escape).</param>
	/// <param name="layer">Layer to modify. Use <see cref="RemapLayer"/> constants.</param>
	public void SetHidKey(int slot, byte hidCode, int layer = RemapLayer.Default)
	{
		ValidateSlot(slot);
		ValidateLayer(layer);
		_entries[layer, slot] = (0xFC, 0x00, hidCode, 0x00, 0x00);
	}

	/// <summary>
	/// Assigns a modifier key to a firmware slot using an HID modifier bitmask.
	/// </summary>
	/// <param name="slot">Firmware slot index 0–125.</param>
	/// <param name="modifierMask">
	/// HID modifier byte bitmask:
	/// 0x01 LCtrl · 0x02 LShift · 0x04 LAlt · 0x08 LMeta ·
	/// 0x10 RCtrl · 0x20 RShift · 0x40 RAlt · 0x80 RMeta.
	/// </param>
	/// <param name="layer">Layer to modify.</param>
	public void SetModifierKey(int slot, byte modifierMask, int layer = RemapLayer.Default)
	{
		ValidateSlot(slot);
		ValidateLayer(layer);
		_entries[layer, slot] = (0xFC, modifierMask, 0x00, 0x00, 0x00);
	}

	/// <summary>
	/// Clears the remap entry for a slot. The slot will be zero-filled on the wire,
	/// which the firmware treats as "no key".
	/// </summary>
	public void Clear(int slot, int layer = RemapLayer.Default)
	{
		ValidateSlot(slot);
		ValidateLayer(layer);
		_entries[layer, slot] = null;
	}

	/// <summary>Clears all entries across every layer.</summary>
	public void ClearAll()
	{
		for (int l = 0; l < LayerCount; l++)
			for (int s = 0; s < SlotCount; s++)
				_entries[l, s] = null;
	}

	internal KeyRemapEntry GetEntry(int layer, int slot)
	{
		var e = _entries[layer, slot];
		if (e is null)
			return new KeyRemapEntry(0x00, 0x00, [0x00, 0x00, 0x00, 0x00]);

		var (cmd, d0, d1, d2, d3) = e.Value;
		return new KeyRemapEntry((byte)slot, cmd, [d0, d1, d2, d3]);
	}

	static void ValidateSlot(int slot)
	{
		if ((uint)slot >= SlotCount)
			throw new ArgumentOutOfRangeException(nameof(slot),
				$"Slot must be 0–{SlotCount - 1}.");
	}

	static void ValidateLayer(int layer)
	{
		if ((uint)layer >= LayerCount)
			throw new ArgumentOutOfRangeException(nameof(layer),
				$"Layer must be 0–{LayerCount - 1}. Use RemapLayer constants.");
	}
}
