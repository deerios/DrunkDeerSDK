using System.Buffers.Binary;
using Microsoft.Extensions.Logging;

namespace DrunkDeer.Protocol;

/// <summary>
/// Async twins of the FuncBlock gateway surface (keymaps, macros, DKS, multi-tap, toggle keys,
/// per-key triggers, profile slots, and the firmware-level settings that live in the function
/// block). Everything here goes through the same 0x55 extended gateway as the synchronous half
/// and produces byte-identical traffic; only the waiting differs.
/// </summary>
/// <remarks>
/// <para>Each public method here is one logical command and takes the wire gate for its whole
/// multi-chunk exchange, so an async poll frame can never land between a request and its
/// response. Composite operations (<see cref="PullFullProfileAsync"/>, <see cref="SetMacroSlotAsync"/>)
/// deliberately do <em>not</em> hold the gate across their sub-commands: the gate is not
/// reentrant, and a full profile pull would otherwise stall the poll loop for thousands of
/// chunks.</para>
/// <para>These require the FuncBlock gateway, which depends on the connected keyboard's
/// precision mode and therefore its firmware version — not on the model alone. Reach them
/// through <see cref="GetFeatures{TFeatures}"/> / <see cref="TryGetFeatures{TFeatures}"/>
/// rather than assuming a model supports them.</para>
/// </remarks>
public partial class KeyboardSession
{
	// ── Gateway primitives (assume the wire gate is held) ─────────────────────

	// Async twin of ReadExtendedGateway. Same chunking, same checksum, same 56-byte payload
	// window; see that method for the response-echo limitation this shares.
	private async Task<byte[]> ReadExtendedGatewayAsync(IKeyboardConnectionAsync conn,
		byte subCmd, int baseAddr, int totalBytes, CancellationToken ct)
	{
		EnsureHasFuncBlock();

		var result = new byte[totalBytes];
		int offset = 0, chunk = 0;
		while (offset < totalBytes)
		{
			int len = Math.Min(56, totalBytes - offset);
			ushort addr = checked((ushort)(baseAddr + offset));
			byte cs = (byte)((addr & 0xFF) + (addr >> 8) + len);
			var req = new byte[64];
			req[0] = 0x55; req[1] = subCmd; req[2] = 0x00;
			req[3] = cs; req[4] = (byte)len;
			BinaryPrimitives.WriteUInt16LittleEndian(req.AsSpan(5), addr);
			var resp = await conn.SendAndReceiveAsync(req, 1000, ct).ConfigureAwait(false);
			if (resp is null || !ExtendedGatewayResponse.Matches(resp))
				throw new InvalidOperationException(
					$"No response for 0x55/0x{subCmd:X2} chunk {chunk} (addr=0x{addr:X4}).");
			ExtendedGatewayResponse.GetData(resp)[..len].CopyTo(result.AsSpan(offset));
			offset += len;
			chunk++;
		}
		return result;
	}

	// Async twin of WriteExtendedGateway. Takes an array rather than a span because a span
	// cannot cross an await.
	private async Task WriteExtendedGatewayAsync(IKeyboardConnectionAsync conn,
		byte subCmd, int baseAddr, byte[] data, CancellationToken ct)
	{
		EnsureHasFuncBlock();

		int offset = 0, chunk = 0;
		while (offset < data.Length)
		{
			int len = Math.Min(56, data.Length - offset);
			ushort addr = checked((ushort)(baseAddr + offset));
			byte isLast = (offset + len >= data.Length) ? (byte)1 : (byte)0;
			var req = new byte[64];
			req[0] = 0x55; req[1] = subCmd; req[2] = 0x00;
			req[3] = (byte)(len + (addr & 0xFF) + (addr >> 8) + isLast + SumBytes(data.AsSpan(offset, len)));
			req[4] = (byte)len;
			BinaryPrimitives.WriteUInt16LittleEndian(req.AsSpan(5), addr);
			req[7] = isLast;
			data.AsSpan(offset, len).CopyTo(req.AsSpan(8));
			var resp = await conn.SendAndReceiveAsync(req, 1000, ct).ConfigureAwait(false);
			if (resp is null || !ExtendedGatewayResponse.Matches(resp))
				throw new InvalidOperationException(
					$"No ACK for 0x55/0x{subCmd:X2} chunk {chunk} (addr=0x{addr:X4}).");
			offset += len;
			chunk++;
		}
	}

	// Reads the function block, applies a change, and writes it back while holding the gate for
	// the whole cycle, so no other command can land between the read and the write. Most of the
	// firmware-level settings below are exactly this shape.
	private Task MutateFuncBlockAsync(Action<KeyboardFuncBlock> mutate, int profileIndex, CancellationToken ct)
	{
		ValidateProfileIndex(profileIndex);
		return RunWireCommandAsync(async (conn, token) =>
		{
			var block = new KeyboardFuncBlock();
			(await ReadExtendedGatewayAsync(conn, 0x05, 64 * profileIndex, 64, token).ConfigureAwait(false))
				.CopyTo(block.RawBytes, 0);
			mutate(block);
			await WriteExtendedGatewayAsync(conn, 0x06, 64 * profileIndex, block.RawBytes, token).ConfigureAwait(false);
		}, ct);
	}

	private Task<byte[]> ReadGatewayAsync(byte subCmd, int baseAddr, int totalBytes, CancellationToken ct) =>
		RunWireCommandAsync((conn, token) => ReadExtendedGatewayAsync(conn, subCmd, baseAddr, totalBytes, token), ct);

	private Task WriteGatewayAsync(byte subCmd, int baseAddr, byte[] data, CancellationToken ct) =>
		RunWireCommandAsync((conn, token) => WriteExtendedGatewayAsync(conn, subCmd, baseAddr, data, token), ct);

	// ── Function block ────────────────────────────────────────────────────────

	/// <summary>Async twin of <see cref="ReadFuncBlock"/>.</summary>
	internal async Task<KeyboardFuncBlock> ReadFuncBlockAsync(int profileIndex = 0, CancellationToken ct = default)
	{
		ValidateProfileIndex(profileIndex);
		var block = new KeyboardFuncBlock();
		(await ReadGatewayAsync(0x05, 64 * profileIndex, 64, ct).ConfigureAwait(false))
			.CopyTo(block.RawBytes, 0);
		return block;
	}

	/// <summary>Async twin of <see cref="WriteFuncBlock"/>.</summary>
	internal Task WriteFuncBlockAsync(KeyboardFuncBlock block, int profileIndex = 0, CancellationToken ct = default)
	{
		ValidateProfileIndex(profileIndex);
		return WriteGatewayAsync(0x06, 64 * profileIndex, block.RawBytes, ct);
	}

	// ── Firmware-level settings ───────────────────────────────────────────────

	/// <summary>Async twin of <see cref="SetKeyboardMode"/>.</summary>
	internal Task SetKeyboardModeAsync(KeyboardMode mode, CancellationToken ct = default) =>
		MutateFuncBlockAsync(b => b.MacMode = (byte)mode, 0, ct);

	/// <summary>Async twin of <see cref="SetReportRate"/>.</summary>
	internal Task SetReportRateAsync(ReportRate rate, CancellationToken ct = default) =>
		MutateFuncBlockAsync(b => b.ReportRate = rate, 0, ct);

	/// <summary>Async twin of <see cref="SetDebounce"/>.</summary>
	internal Task SetDebounceAsync(byte level, CancellationToken ct = default)
	{
		if (level > 7)
			throw new ArgumentOutOfRangeException(nameof(level), "Debounce level must be 0-7.");
		return MutateFuncBlockAsync(b => b.Debounce = level, 0, ct);
	}

	/// <summary>Async twin of <see cref="SetStabilityMode"/>.</summary>
	internal Task SetStabilityModeAsync(byte level, CancellationToken ct = default)
	{
		if (level > 3)
			throw new ArgumentOutOfRangeException(nameof(level), "Stability mode must be 0-3.");
		return MutateFuncBlockAsync(b => b.StabilityMode = level, 0, ct);
	}

	/// <summary>Async twin of <see cref="SetTickRate"/>.</summary>
	internal Task SetTickRateAsync(byte rate, CancellationToken ct = default)
	{
		if (rate > 15)
			throw new ArgumentOutOfRangeException(nameof(rate), "Tick rate must be 0-15.");
		return MutateFuncBlockAsync(b => b.TickRate = rate, 0, ct);
	}

	/// <summary>Async twin of <see cref="ConfigureKeyLocks"/>.</summary>
	internal Task ConfigureKeyLocksAsync(bool? winLock = null, bool? altTabLock = null,
		bool? altF4Lock = null, CancellationToken ct = default) =>
		MutateFuncBlockAsync(b =>
		{
			if (winLock.HasValue)    b.WinLock    = winLock.Value;
			if (altTabLock.HasValue) b.AltTabLock = altTabLock.Value;
			if (altF4Lock.HasValue)  b.AltF4Lock  = altF4Lock.Value;
		}, 0, ct);

	// ── Preset lighting ───────────────────────────────────────────────────────

	/// <summary>Async twin of <see cref="SetLightPreset"/>.</summary>
	internal Task SetLightPresetAsync(LightPreset effect, byte brightness = 9, byte speed = 5,
		CancellationToken ct = default)
	{
		ValidateBrightness(brightness);
		ValidateSpeed(speed);
		return MutateFuncBlockAsync(b =>
		{
			b.LightEffect     = (byte)effect;
			b.LightBrightness = brightness;
			b.LightSpeed      = speed;
		}, 0, ct);
	}

	/// <summary>Async twin of <see cref="SetLightCustom"/>.</summary>
	internal Task SetLightCustomAsync(CancellationToken ct = default) =>
		MutateFuncBlockAsync(b => b.LightEffect = 0, 0, ct);

	/// <summary>Async twin of <see cref="SetLightPresetColor(byte, byte, byte)"/>.</summary>
	internal Task SetLightPresetColorAsync(byte r, byte g, byte b, CancellationToken ct = default) =>
		MutateFuncBlockAsync(fb =>
		{
			fb.LightSingleColor = true;
			fb.LightColorR = r;
			fb.LightColorG = g;
			fb.LightColorB = b;
		}, 0, ct);

	/// <inheritdoc cref="SetLightPresetColorAsync(byte, byte, byte, CancellationToken)"/>
	internal Task SetLightPresetColorAsync(RgbColor color, CancellationToken ct = default) =>
		SetLightPresetColorAsync(color.R, color.G, color.B, ct);

	// ── Logo light zone ───────────────────────────────────────────────────────

	/// <summary>Async twin of <see cref="SetLogoLightPreset"/>.</summary>
	internal Task SetLogoLightPresetAsync(LightPreset effect, byte brightness = 9, byte speed = 5,
		CancellationToken ct = default)
	{
		EnsureHasLogoLight();
		ValidateBrightness(brightness);
		ValidateSpeed(speed);
		return MutateFuncBlockAsync(b =>
		{
			b.LogoLightEffect     = (byte)effect;
			b.LogoLightBrightness = brightness;
			b.LogoLightSpeed      = speed;
		}, 0, ct);
	}

	/// <summary>Async twin of <see cref="SetLogoLightOff"/>.</summary>
	internal Task SetLogoLightOffAsync(CancellationToken ct = default)
	{
		EnsureHasLogoLight();
		return MutateFuncBlockAsync(b => b.LogoLightEffect = 0, 0, ct);
	}

	/// <summary>Async twin of <see cref="SetLogoLightColor(byte, byte, byte)"/>.</summary>
	internal Task SetLogoLightColorAsync(byte r, byte g, byte b, CancellationToken ct = default)
	{
		EnsureHasLogoLight();
		return MutateFuncBlockAsync(fb =>
		{
			fb.LogoLightSingleColor = true;
			fb.LogoLightColorR = r;
			fb.LogoLightColorG = g;
			fb.LogoLightColorB = b;
		}, 0, ct);
	}

	/// <inheritdoc cref="SetLogoLightColorAsync(byte, byte, byte, CancellationToken)"/>
	internal Task SetLogoLightColorAsync(RgbColor color, CancellationToken ct = default) =>
		SetLogoLightColorAsync(color.R, color.G, color.B, ct);

	// ── Side light zone ───────────────────────────────────────────────────────

	/// <summary>Async twin of <see cref="SetSideLightPreset"/>.</summary>
	internal Task SetSideLightPresetAsync(LightPreset effect, byte brightness = 9, byte speed = 5,
		CancellationToken ct = default)
	{
		EnsureHasSideLight();
		ValidateBrightness(brightness);
		ValidateSpeed(speed);
		return MutateFuncBlockAsync(b =>
		{
			b.SideLightEffect     = (byte)effect;
			b.SideLightBrightness = brightness;
			b.SideLightSpeed      = speed;
		}, 0, ct);
	}

	/// <summary>Async twin of <see cref="SetSideLightOff"/>.</summary>
	internal Task SetSideLightOffAsync(CancellationToken ct = default)
	{
		EnsureHasSideLight();
		return MutateFuncBlockAsync(b => b.SideLightEffect = 0, 0, ct);
	}

	/// <summary>Async twin of <see cref="SetSideLightColor(byte, byte, byte)"/>.</summary>
	internal Task SetSideLightColorAsync(byte r, byte g, byte b, CancellationToken ct = default)
	{
		EnsureHasSideLight();
		return MutateFuncBlockAsync(fb =>
		{
			fb.SideLightSingleColor = true;
			fb.SideLightColorR = r;
			fb.SideLightColorG = g;
			fb.SideLightColorB = b;
		}, 0, ct);
	}

	/// <inheritdoc cref="SetSideLightColorAsync(byte, byte, byte, CancellationToken)"/>
	internal Task SetSideLightColorAsync(RgbColor color, CancellationToken ct = default) =>
		SetSideLightColorAsync(color.R, color.G, color.B, ct);

	// ── Per-key triggers ──────────────────────────────────────────────────────

	/// <summary>Async twin of <see cref="ReadKeyTriggers"/>.</summary>
	internal async Task<KeyTriggerConfig[]> ReadKeyTriggersAsync(int profileIndex = 0, CancellationToken ct = default)
	{
		ValidateProfileIndex(profileIndex);
		var raw = await ReadGatewayAsync(0xA0, KeyTriggerStride * profileIndex, KeyTriggerStride, ct)
			.ConfigureAwait(false);
		var result = new KeyTriggerConfig[128];
		for (int i = 0; i < 128; i++)
			result[i] = KeyTriggerConfig.Decode(raw.AsSpan(i * 8));
		return result;
	}

	/// <summary>Async twin of <see cref="WriteKeyTriggers"/>.</summary>
	internal Task WriteKeyTriggersAsync(KeyTriggerConfig[] configs, int profileIndex = 0, CancellationToken ct = default)
	{
		ValidateProfileIndex(profileIndex);
		if (configs.Length != 128)
			throw new ArgumentException(
				$"Expected 128 key trigger configs, got {configs.Length}.", nameof(configs));
		var raw = new byte[KeyTriggerStride];
		for (int i = 0; i < 128; i++)
			KeyTriggerConfig.Encode(configs[i], raw.AsSpan(i * 8));
		return WriteGatewayAsync(0xA1, KeyTriggerStride * profileIndex, raw, ct);
	}

	/// <summary>Async twin of <see cref="SetKeyTrigger(int, KeyTriggerConfig, int)"/>.</summary>
	internal Task SetKeyTriggerAsync(int keyIndex, KeyTriggerConfig config, int profileIndex = 0,
		CancellationToken ct = default)
	{
		ValidateProfileIndex(profileIndex);
		if ((uint)keyIndex >= 128)
			throw new ArgumentOutOfRangeException(nameof(keyIndex),
				$"Key trigger index {keyIndex} must be in [0, 127].");
		var entry = new byte[8];
		KeyTriggerConfig.Encode(config, entry);
		return WriteGatewayAsync(0xA1, KeyTriggerStride * profileIndex + 8 * keyIndex, entry, ct);
	}

	/// <summary>Async twin of <see cref="SetKeyTrigger(DDKey, KeyTriggerConfig, int)"/>.</summary>
	internal Task SetKeyTriggerAsync(DDKey key, KeyTriggerConfig config, int profileIndex = 0,
		CancellationToken ct = default) =>
		SetKeyTriggerAsync(GetKeyIndex(key), config, profileIndex, ct);

	// ── Key map ───────────────────────────────────────────────────────────────

	/// <summary>Async twin of <see cref="ReadKeyMap"/>.</summary>
	internal async Task<UserKey[]> ReadKeyMapAsync(int layerIndex = 0, int profileIndex = 0,
		CancellationToken ct = default)
	{
		ValidateLayer(layerIndex);
		var raw = await ReadGatewayAsync(0x07, KeyMapAddr(profileIndex, layerIndex), KeyMapKeyCount * 3, ct)
			.ConfigureAwait(false);
		return DecodeKeyMap(raw);
	}

	/// <summary>Async twin of <see cref="ReadDefaultKeyMap"/>.</summary>
	internal async Task<UserKey[]> ReadDefaultKeyMapAsync(int layerIndex = 0, int profileIndex = 0,
		CancellationToken ct = default)
	{
		ValidateLayer(layerIndex);
		var raw = await ReadGatewayAsync(0x08, KeyMapAddr(profileIndex, layerIndex), KeyMapKeyCount * 3, ct)
			.ConfigureAwait(false);
		return DecodeKeyMap(raw);
	}

	/// <summary>Async twin of <see cref="WriteKeyMap"/>.</summary>
	internal Task WriteKeyMapAsync(UserKey[] keys, int layerIndex = 0, int profileIndex = 0,
		CancellationToken ct = default)
	{
		ValidateLayer(layerIndex);
		if (keys.Length != KeyMapKeyCount)
			throw new ArgumentException(
				$"Expected {KeyMapKeyCount} key entries, got {keys.Length}.", nameof(keys));
		var raw = new byte[KeyMapKeyCount * 3];
		EncodeUserKeyArray(keys, raw, KeyMapKeyCount);
		return WriteGatewayAsync(0x09, KeyMapAddr(profileIndex, layerIndex), raw, ct);
	}

	/// <summary>Async twin of <see cref="SetKey(int, UserKey, int, int)"/>.</summary>
	internal Task SetKeyAsync(int keyIndex, UserKey key, int layerIndex = 0, int profileIndex = 0,
		CancellationToken ct = default)
	{
		ValidateLayer(layerIndex);
		if ((uint)keyIndex >= KeyMapKeyCount)
			throw new ArgumentOutOfRangeException(nameof(keyIndex),
				$"Key index {keyIndex} must be in [0, {KeyMapKeyCount - 1}].");
		return WriteGatewayAsync(0x09, KeyMapAddr(profileIndex, layerIndex, keyIndex),
			[key.Type, key.Param1, key.Param2], ct);
	}

	/// <summary>Async twin of <see cref="SetKey(DDKey, UserKey, int, int)"/>.</summary>
	internal Task SetKeyAsync(DDKey key, UserKey value, int layerIndex = 0, int profileIndex = 0,
		CancellationToken ct = default) =>
		SetKeyAsync(GetKeyIndex(key), value, layerIndex, profileIndex, ct);

	// ── Dynamic Keystroke ─────────────────────────────────────────────────────

	/// <summary>Async twin of <see cref="ReadDynamicKeystrokeEntries"/>.</summary>
	internal async Task<DynamicKeystrokeEntry[]> ReadDynamicKeystrokeEntriesAsync(int profileIndex = 0,
		CancellationToken ct = default)
	{
		ValidateProfileIndex(profileIndex);
		var raw = await ReadGatewayAsync(0xA2, DksStride * profileIndex, DksStride, ct).ConfigureAwait(false);
		var result = new DynamicKeystrokeEntry[DynamicKeystrokeEntry.SlotCount];
		for (int i = 0; i < DynamicKeystrokeEntry.SlotCount; i++)
			result[i] = DynamicKeystrokeEntry.Decode(raw.AsSpan(i * DynamicKeystrokeEntry.ByteSize));
		return result;
	}

	/// <summary>Async twin of <see cref="WriteDynamicKeystrokeEntries"/>.</summary>
	internal Task WriteDynamicKeystrokeEntriesAsync(DynamicKeystrokeEntry[] entries, int profileIndex = 0,
		CancellationToken ct = default)
	{
		ValidateProfileIndex(profileIndex);
		if (entries.Length != DynamicKeystrokeEntry.SlotCount)
			throw new ArgumentException(
				$"Expected {DynamicKeystrokeEntry.SlotCount} DKS entries, got {entries.Length}.", nameof(entries));
		var raw = new byte[DksStride];
		for (int i = 0; i < DynamicKeystrokeEntry.SlotCount; i++)
			DynamicKeystrokeEntry.Encode(entries[i], raw.AsSpan(i * DynamicKeystrokeEntry.ByteSize));
		return WriteGatewayAsync(0xA3, DksStride * profileIndex, raw, ct);
	}

	/// <summary>Async twin of <see cref="SetDynamicKeystrokeEntry"/>.</summary>
	internal Task SetDynamicKeystrokeEntryAsync(int slotIndex, DynamicKeystrokeEntry entry,
		int profileIndex = 0, CancellationToken ct = default)
	{
		ValidateProfileIndex(profileIndex);
		if ((uint)slotIndex >= DynamicKeystrokeEntry.SlotCount)
			throw new ArgumentOutOfRangeException(nameof(slotIndex),
				$"DKS slot index {slotIndex} must be in [0, {DynamicKeystrokeEntry.SlotCount - 1}].");
		var raw = new byte[DynamicKeystrokeEntry.ByteSize];
		DynamicKeystrokeEntry.Encode(entry, raw);
		return WriteGatewayAsync(0xA3,
			DksStride * profileIndex + DynamicKeystrokeEntry.ByteSize * slotIndex, raw, ct);
	}

	// ── Multi-Tap ─────────────────────────────────────────────────────────────

	/// <summary>Async twin of <see cref="ReadMultiTapEntries"/>.</summary>
	internal async Task<MultiTapEntry[]> ReadMultiTapEntriesAsync(int profileIndex = 0,
		CancellationToken ct = default)
	{
		ValidateProfileIndex(profileIndex);
		var raw = await ReadGatewayAsync(0xA4, MtStride * profileIndex,
			MultiTapEntry.SlotCount * MultiTapEntry.ByteSize, ct).ConfigureAwait(false);
		var result = new MultiTapEntry[MultiTapEntry.SlotCount];
		for (int i = 0; i < MultiTapEntry.SlotCount; i++)
			result[i] = MultiTapEntry.Decode(raw.AsSpan(i * MultiTapEntry.ByteSize));
		return result;
	}

	/// <summary>Async twin of <see cref="WriteMultiTapEntries"/>.</summary>
	internal Task WriteMultiTapEntriesAsync(MultiTapEntry[] entries, int profileIndex = 0,
		CancellationToken ct = default)
	{
		ValidateProfileIndex(profileIndex);
		if (entries.Length != MultiTapEntry.SlotCount)
			throw new ArgumentException(
				$"Expected {MultiTapEntry.SlotCount} MT entries, got {entries.Length}.", nameof(entries));
		var raw = new byte[MultiTapEntry.SlotCount * MultiTapEntry.ByteSize];
		for (int i = 0; i < MultiTapEntry.SlotCount; i++)
			MultiTapEntry.Encode(entries[i], raw.AsSpan(i * MultiTapEntry.ByteSize));
		return WriteGatewayAsync(0xA5, MtStride * profileIndex, raw, ct);
	}

	/// <summary>Async twin of <see cref="SetMultiTapEntry"/>.</summary>
	internal Task SetMultiTapEntryAsync(int slotIndex, MultiTapEntry entry, int profileIndex = 0,
		CancellationToken ct = default)
	{
		ValidateProfileIndex(profileIndex);
		if ((uint)slotIndex >= MultiTapEntry.SlotCount)
			throw new ArgumentOutOfRangeException(nameof(slotIndex),
				$"MT slot index {slotIndex} must be in [0, {MultiTapEntry.SlotCount - 1}].");
		var raw = new byte[MultiTapEntry.ByteSize];
		MultiTapEntry.Encode(entry, raw);
		return WriteGatewayAsync(0xA5,
			MtStride * profileIndex + MultiTapEntry.ByteSize * slotIndex, raw, ct);
	}

	// ── Toggle keys ───────────────────────────────────────────────────────────

	/// <summary>Async twin of <see cref="ReadToggleKeyEntries"/>.</summary>
	internal async Task<UserKey[]> ReadToggleKeyEntriesAsync(int profileIndex = 0, CancellationToken ct = default)
	{
		ValidateProfileIndex(profileIndex);
		var raw = await ReadGatewayAsync(0xA6, TglStride * profileIndex, TglSlotCount * TglSlotSize, ct)
			.ConfigureAwait(false);
		return DecodeUserKeyArray(raw, TglSlotCount);
	}

	/// <summary>Async twin of <see cref="WriteToggleKeyEntries"/>.</summary>
	internal Task WriteToggleKeyEntriesAsync(UserKey[] entries, int profileIndex = 0, CancellationToken ct = default)
	{
		ValidateProfileIndex(profileIndex);
		if (entries.Length != TglSlotCount)
			throw new ArgumentException(
				$"Expected {TglSlotCount} Toggle entries, got {entries.Length}.", nameof(entries));
		var raw = new byte[TglSlotCount * TglSlotSize];
		EncodeUserKeyArray(entries, raw, TglSlotCount);
		return WriteGatewayAsync(0xA7, TglStride * profileIndex, raw, ct);
	}

	/// <summary>Async twin of <see cref="SetToggleKeyEntry"/>.</summary>
	internal Task SetToggleKeyEntryAsync(int slotIndex, UserKey entry, int profileIndex = 0,
		CancellationToken ct = default)
	{
		ValidateProfileIndex(profileIndex);
		if ((uint)slotIndex >= TglSlotCount)
			throw new ArgumentOutOfRangeException(nameof(slotIndex),
				$"Toggle slot index {slotIndex} must be in [0, {TglSlotCount - 1}].");
		return WriteGatewayAsync(0xA7, TglStride * profileIndex + TglSlotSize * slotIndex,
			[entry.Type, entry.Param1, entry.Param2], ct);
	}

	// ── Macros ────────────────────────────────────────────────────────────────

	/// <summary>Async twin of <see cref="ReadMacroSlots"/>.</summary>
	internal async Task<MacroAction[][]> ReadMacroSlotsAsync(int profileIndex = 0, CancellationToken ct = default)
	{
		ValidateProfileIndex(profileIndex);
		var raw = await ReadGatewayAsync(0x0C, MacroStride * profileIndex, MacroStride, ct).ConfigureAwait(false);
		return MacroAction.DecodeBlock(raw);
	}

	/// <summary>Async twin of <see cref="WriteMacroSlots"/>.</summary>
	internal Task WriteMacroSlotsAsync(MacroAction[]?[] slots, int profileIndex = 0, CancellationToken ct = default)
	{
		ValidateProfileIndex(profileIndex);
		if (slots.Length != MacroAction.SlotCount)
			throw new ArgumentException(
				$"Expected {MacroAction.SlotCount} macro slots, got {slots.Length}.", nameof(slots));
		return WriteGatewayAsync(0x0D, MacroStride * profileIndex, MacroAction.EncodeBlock(slots), ct);
	}

	/// <summary>Async twin of <see cref="SetMacroSlot"/>.</summary>
	internal async Task SetMacroSlotAsync(int slotIndex, MacroAction[] actions, int profileIndex = 0,
		CancellationToken ct = default)
	{
		if ((uint)slotIndex >= MacroAction.SlotCount)
			throw new ArgumentOutOfRangeException(nameof(slotIndex),
				$"Macro slot index {slotIndex} must be in [0, {MacroAction.SlotCount - 1}].");
		var slots = await ReadMacroSlotsAsync(profileIndex, ct).ConfigureAwait(false);
		slots[slotIndex] = actions;
		await WriteMacroSlotsAsync(slots, profileIndex, ct).ConfigureAwait(false);
	}

	// ── Profile slots ─────────────────────────────────────────────────────────

	/// <summary>Async twin of <see cref="SwitchProfile"/>.</summary>
	internal Task SwitchProfileAsync(int profileIndex, CancellationToken ct = default)
	{
		ValidateProfileIndex(profileIndex);
		return WriteGatewayAsync(0x0E, 0, [(byte)profileIndex], ct);
	}

	/// <summary>Async twin of <see cref="GetCurrentProfile"/>.</summary>
	internal async Task<int> GetCurrentProfileAsync(CancellationToken ct = default) =>
		(await ReadGatewayAsync(0x04, 0, BaseBlockSize, ct).ConfigureAwait(false))[0];

	/// <summary>Async twin of <see cref="PullFullProfile"/>.</summary>
	internal async Task<FullProfileData> PullFullProfileAsync(int profileIndex = 0, CancellationToken ct = default)
	{
		ValidateProfileIndex(profileIndex);
		EnsureHasFuncBlock();

		var funcBlock = await ReadFuncBlockAsync(profileIndex, ct).ConfigureAwait(false);
		var triggers  = await ReadKeyTriggersAsync(profileIndex, ct).ConfigureAwait(false);

		var layers = new UserKey[KeyMapLayerCount][];
		for (int layer = 0; layer < KeyMapLayerCount; layer++)
			layers[layer] = await ReadKeyMapAsync(layer, profileIndex, ct).ConfigureAwait(false);

		return new FullProfileData
		{
			FuncBlock               = funcBlock,
			KeyTriggers             = triggers,
			KeyMapLayers            = layers,
			DynamicKeystrokeEntries = await ReadDynamicKeystrokeEntriesAsync(profileIndex, ct).ConfigureAwait(false),
			MultiTapEntries         = await ReadMultiTapEntriesAsync(profileIndex, ct).ConfigureAwait(false),
			ToggleKeyEntries        = await ReadToggleKeyEntriesAsync(profileIndex, ct).ConfigureAwait(false),
			MacroSlots              = await ReadMacroSlotsAsync(profileIndex, ct).ConfigureAwait(false),
		};
	}

	/// <summary>Async twin of <see cref="PushFullProfile"/>.</summary>
	internal async Task PushFullProfileAsync(FullProfileData data, int profileIndex = 0,
		CancellationToken ct = default)
	{
		ValidateProfileIndex(profileIndex);

		if (data.FuncBlock != null)
			await WriteFuncBlockAsync(data.FuncBlock, profileIndex, ct).ConfigureAwait(false);

		if (data.KeyTriggers != null)
			await WriteKeyTriggersAsync(data.KeyTriggers, profileIndex, ct).ConfigureAwait(false);

		if (data.KeyMapLayers != null)
		{
			for (int layer = 0; layer < KeyMapLayerCount; layer++)
			{
				if (data.KeyMapLayers.Length > layer && data.KeyMapLayers[layer] != null)
					await WriteKeyMapAsync(data.KeyMapLayers[layer]!, layer, profileIndex, ct).ConfigureAwait(false);
			}
		}

		if (data.DynamicKeystrokeEntries != null)
			await WriteDynamicKeystrokeEntriesAsync(data.DynamicKeystrokeEntries, profileIndex, ct).ConfigureAwait(false);

		if (data.MultiTapEntries != null)
			await WriteMultiTapEntriesAsync(data.MultiTapEntries, profileIndex, ct).ConfigureAwait(false);

		if (data.ToggleKeyEntries != null)
			await WriteToggleKeyEntriesAsync(data.ToggleKeyEntries, profileIndex, ct).ConfigureAwait(false);

		if (data.MacroSlots != null)
			await WriteMacroSlotsAsync(data.MacroSlots, profileIndex, ct).ConfigureAwait(false);
	}

	/// <summary>Async twin of <see cref="CopyProfile"/>.</summary>
	internal async Task CopyProfileAsync(int fromSlot, int toSlot, CancellationToken ct = default)
	{
		ValidateProfileIndex(fromSlot);
		ValidateProfileIndex(toSlot);
		var data = await PullFullProfileAsync(fromSlot, ct).ConfigureAwait(false);
		await PushFullProfileAsync(data, toSlot, ct).ConfigureAwait(false);
	}

	// ── Stored lighting ───────────────────────────────────────────────────────

	/// <summary>Async twin of <see cref="ReadStoredColors"/>.</summary>
	internal async Task<(byte R, byte G, byte B)[]> ReadStoredColorsAsync(int profileIndex = 0,
		CancellationToken ct = default)
	{
		ValidateProfileIndex(profileIndex);
		var raw = await ReadGatewayAsync(0x0A, StoredColorStride * profileIndex, StoredColorByteCount, ct)
			.ConfigureAwait(false);
		var result = new (byte, byte, byte)[StoredColorKeyCount];
		for (int i = 0; i < StoredColorKeyCount; i++)
			result[i] = (raw[i * 3], raw[i * 3 + 1], raw[i * 3 + 2]);
		return result;
	}

	/// <summary>Async twin of <see cref="WriteStoredColors"/>.</summary>
	internal Task WriteStoredColorsAsync((byte R, byte G, byte B)[] colors, int profileIndex = 0,
		CancellationToken ct = default)
	{
		ValidateProfileIndex(profileIndex);
		if (colors.Length != StoredColorKeyCount)
			throw new ArgumentException(
				$"Expected {StoredColorKeyCount} color entries, got {colors.Length}.", nameof(colors));
		return WriteGatewayAsync(0x0B, StoredColorStride * profileIndex, PackColors(colors), ct);
	}

	/// <summary>Async twin of <see cref="SaveLightingToProfile"/>.</summary>
	internal Task SaveLightingToProfileAsync(int profileIndex = 0, CancellationToken ct = default)
	{
		ValidateProfileIndex(profileIndex);
		return WriteGatewayAsync(0x0B, StoredColorStride * profileIndex, PackColors(_rgbProfile), ct);
	}

	/// <summary>Async twin of <see cref="LoadLightingFromProfile"/>.</summary>
	internal async Task LoadLightingFromProfileAsync(int profileIndex = 0, byte brightness = 9,
		CancellationToken ct = default)
	{
		ValidateProfileIndex(profileIndex);
		ValidateBrightness(brightness);
		var raw = await ReadGatewayAsync(0x0A, StoredColorStride * profileIndex, StoredColorByteCount, ct)
			.ConfigureAwait(false);
		for (int i = 0; i < StoredColorKeyCount; i++)
			_rgbProfile[i] = (raw[i * 3], raw[i * 3 + 1], raw[i * 3 + 2]);
		await RunWireCommandAsync((conn, token) =>
			SendLightingPacketsAsync(conn, BuildEntriesFromProfile(), brightness, token), ct).ConfigureAwait(false);
	}

	/// <summary>Async twin of <see cref="ReadLiveColors"/>.</summary>
	internal async Task<(byte R, byte G, byte B)[]> ReadLiveColorsAsync(CancellationToken ct = default)
	{
		var raw = await ReadGatewayAsync(0xDE, 0, StoredColorByteCount, ct).ConfigureAwait(false);
		var result = new (byte, byte, byte)[StoredColorKeyCount];
		for (int i = 0; i < StoredColorKeyCount; i++)
			result[i] = (raw[i * 3], raw[i * 3 + 1], raw[i * 3 + 2]);
		return result;
	}

	// Flattens 128 RGB triples into the 384-byte stored-colour payload.
	private static byte[] PackColors((byte R, byte G, byte B)[] colors)
	{
		var raw = new byte[StoredColorByteCount];
		for (int i = 0; i < StoredColorKeyCount; i++)
		{
			raw[i * 3]     = colors[i].R;
			raw[i * 3 + 1] = colors[i].G;
			raw[i * 3 + 2] = colors[i].B;
		}
		return raw;
	}

	// ── Maintenance ───────────────────────────────────────────────────────────
	//
	// These are bare 0x55 sub-commands, not extended-gateway transfers: a short packet with no
	// address, no chunking, and no response to check. Mirrors the synchronous half exactly.

	// Sends one fire-and-forget packet under the wire gate, so it can't land inside a poll frame.
	private Task SendCommandAsync(byte[] packet, CancellationToken ct)
	{
		EnsureHasFuncBlock();
		return RunWireCommandAsync(async (conn, token) =>
			await conn.SendAsync(packet, token).ConfigureAwait(false), ct);
	}

	/// <summary>Async twin of <see cref="StartFastTransferMode"/>.</summary>
	internal async Task StartFastTransferModeAsync(CancellationToken ct = default)
	{
		await SendCommandAsync([0x55, 0x01], ct).ConfigureAwait(false);
		_inFastMode = true;
	}

	/// <summary>Async twin of <see cref="StopFastTransferMode"/>.</summary>
	internal async Task StopFastTransferModeAsync(CancellationToken ct = default)
	{
		EnsureHasFuncBlock();
		if (!_inFastMode)
		{
			_log.LogWarning("{Method} called without a preceding {Prereq}; nothing sent",
				nameof(StopFastTransferModeAsync), nameof(StartFastTransferModeAsync));
			return;
		}
		await SendCommandAsync([0x55, 0x02], ct).ConfigureAwait(false);
		_inFastMode = false;
	}

	/// <summary>Async twin of <see cref="StartCalibration"/>.</summary>
	internal Task StartCalibrationAsync(CancellationToken ct = default) =>
		SendCommandAsync([0x55, 0xA8], ct);

	/// <summary>Async twin of <see cref="EndCalibration"/>.</summary>
	internal Task EndCalibrationAsync(CancellationToken ct = default) =>
		SendCommandAsync([0x55, 0xA9], ct);

	/// <summary>Async twin of <see cref="Reset"/>.</summary>
	internal Task ResetAsync(CancellationToken ct = default) =>
		SendCommandAsync([0x55, 0xEE, 0, 0, 1, 0, 0, 0, 0xFF], ct);
}
