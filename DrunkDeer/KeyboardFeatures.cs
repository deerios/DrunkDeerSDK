namespace DrunkDeer.Protocol;

/// <summary>
/// Thrown when a feature facade is requested from a <see cref="KeyboardSession"/> whose connected
/// keyboard does not support it.
/// </summary>
public sealed class DrunkDeerCapabilityException : Exception
{
	public DrunkDeerCapabilityException() { }

	public DrunkDeerCapabilityException(string? message) : base(message) { }

	public DrunkDeerCapabilityException(string? message, Exception? innerException)
		: base(message, innerException) { }
}

/// <summary>
/// The programmable surface behind the keyboard's extended gateway (0x55/0x05-0x06): key maps,
/// macros, Dynamic Keystroke, Multi-Tap, toggle keys, per-key triggers, profile slots, stored
/// lighting, and firmware-level settings such as report rate and debounce.
/// </summary>
/// <remarks>
/// <para>Obtain this from <see cref="KeyboardSession.GetFeatures{TFeatures}"/> or
/// <see cref="KeyboardSession.TryGetFeatures{TFeatures}"/>; it is the runtime equivalent of the
/// compile-time <see cref="IHasFuncBlock"/> gate on <see cref="KeyboardSession{TModel}"/>.</para>
/// <para><b>Availability is a property of the model.</b> The gateway is present on HighPrecision
/// boards (A75 Ultra, A75 Master, X60 Future) and on boards that are always Kun-precision
/// (G65 m1/m2/m3, G60 v600). Everything else — including the base A75 and A75 Pro on every
/// released firmware — has no gateway at all, so this facade is simply absent there.</para>
/// <para>Every method has an async twin producing identical wire traffic. Use the sync half on
/// desktop and the async half on single-threaded hosts (WebAssembly); do not mix both on one
/// session.</para>
/// </remarks>
public interface IProgrammableKeyboardFeatures
{
	// ── Firmware-level settings ───────────────────────────────────────────────

	/// <inheritdoc cref="KeyboardSession.SetKeyboardMode"/>
	void SetKeyboardMode(KeyboardMode mode);
	/// <inheritdoc cref="KeyboardSession.SetKeyboardMode"/>
	Task SetKeyboardModeAsync(KeyboardMode mode, CancellationToken ct = default);

	/// <inheritdoc cref="KeyboardSession.SetReportRate"/>
	void SetReportRate(ReportRate rate);
	/// <inheritdoc cref="KeyboardSession.SetReportRate"/>
	Task SetReportRateAsync(ReportRate rate, CancellationToken ct = default);

	/// <inheritdoc cref="KeyboardSession.SetDebounce"/>
	void SetDebounce(byte level);
	/// <inheritdoc cref="KeyboardSession.SetDebounce"/>
	Task SetDebounceAsync(byte level, CancellationToken ct = default);

	/// <inheritdoc cref="KeyboardSession.SetStabilityMode"/>
	void SetStabilityMode(byte level);
	/// <inheritdoc cref="KeyboardSession.SetStabilityMode"/>
	Task SetStabilityModeAsync(byte level, CancellationToken ct = default);

	/// <inheritdoc cref="KeyboardSession.SetTickRate"/>
	void SetTickRate(byte rate);
	/// <inheritdoc cref="KeyboardSession.SetTickRate"/>
	Task SetTickRateAsync(byte rate, CancellationToken ct = default);

	/// <inheritdoc cref="KeyboardSession.ConfigureKeyLocks"/>
	void ConfigureKeyLocks(bool? winLock = null, bool? altTabLock = null, bool? altF4Lock = null);
	/// <inheritdoc cref="KeyboardSession.ConfigureKeyLocks"/>
	Task ConfigureKeyLocksAsync(bool? winLock = null, bool? altTabLock = null,
		bool? altF4Lock = null, CancellationToken ct = default);

	// ── Preset lighting ───────────────────────────────────────────────────────

	/// <inheritdoc cref="KeyboardSession.SetLightPreset"/>
	void SetLightPreset(LightPreset effect, byte brightness = 9, byte speed = 5);
	/// <inheritdoc cref="KeyboardSession.SetLightPreset"/>
	Task SetLightPresetAsync(LightPreset effect, byte brightness = 9, byte speed = 5,
		CancellationToken ct = default);

	/// <inheritdoc cref="KeyboardSession.SetLightCustom"/>
	void SetLightCustom();
	/// <inheritdoc cref="KeyboardSession.SetLightCustom"/>
	Task SetLightCustomAsync(CancellationToken ct = default);

	/// <inheritdoc cref="KeyboardSession.SetLightPresetColor(byte, byte, byte)"/>
	void SetLightPresetColor(byte r, byte g, byte b);
	/// <inheritdoc cref="KeyboardSession.SetLightPresetColor(RgbColor)"/>
	void SetLightPresetColor(RgbColor color);
	/// <inheritdoc cref="KeyboardSession.SetLightPresetColor(byte, byte, byte)"/>
	Task SetLightPresetColorAsync(byte r, byte g, byte b, CancellationToken ct = default);
	/// <inheritdoc cref="KeyboardSession.SetLightPresetColor(RgbColor)"/>
	Task SetLightPresetColorAsync(RgbColor color, CancellationToken ct = default);

	// ── Per-key triggers ──────────────────────────────────────────────────────

	/// <inheritdoc cref="KeyboardSession.ReadKeyTriggers"/>
	KeyTriggerConfig[] ReadKeyTriggers(int profileIndex = 0);
	/// <inheritdoc cref="KeyboardSession.ReadKeyTriggers"/>
	Task<KeyTriggerConfig[]> ReadKeyTriggersAsync(int profileIndex = 0, CancellationToken ct = default);

	/// <inheritdoc cref="KeyboardSession.WriteKeyTriggers"/>
	void WriteKeyTriggers(KeyTriggerConfig[] configs, int profileIndex = 0);
	/// <inheritdoc cref="KeyboardSession.WriteKeyTriggers"/>
	Task WriteKeyTriggersAsync(KeyTriggerConfig[] configs, int profileIndex = 0, CancellationToken ct = default);

	/// <inheritdoc cref="KeyboardSession.SetKeyTrigger(int, KeyTriggerConfig, int)"/>
	void SetKeyTrigger(int keyIndex, KeyTriggerConfig config, int profileIndex = 0);
	/// <inheritdoc cref="KeyboardSession.SetKeyTrigger(DDKey, KeyTriggerConfig, int)"/>
	void SetKeyTrigger(DDKey key, KeyTriggerConfig config, int profileIndex = 0);
	/// <inheritdoc cref="KeyboardSession.SetKeyTrigger(int, KeyTriggerConfig, int)"/>
	Task SetKeyTriggerAsync(int keyIndex, KeyTriggerConfig config, int profileIndex = 0,
		CancellationToken ct = default);
	/// <inheritdoc cref="KeyboardSession.SetKeyTrigger(DDKey, KeyTriggerConfig, int)"/>
	Task SetKeyTriggerAsync(DDKey key, KeyTriggerConfig config, int profileIndex = 0,
		CancellationToken ct = default);

	// ── Key map ───────────────────────────────────────────────────────────────

	/// <inheritdoc cref="KeyboardSession.ReadKeyMap"/>
	UserKey[] ReadKeyMap(int layerIndex = 0, int profileIndex = 0);
	/// <inheritdoc cref="KeyboardSession.ReadKeyMap"/>
	Task<UserKey[]> ReadKeyMapAsync(int layerIndex = 0, int profileIndex = 0, CancellationToken ct = default);

	/// <inheritdoc cref="KeyboardSession.ReadDefaultKeyMap"/>
	UserKey[] ReadDefaultKeyMap(int layerIndex = 0, int profileIndex = 0);
	/// <inheritdoc cref="KeyboardSession.ReadDefaultKeyMap"/>
	Task<UserKey[]> ReadDefaultKeyMapAsync(int layerIndex = 0, int profileIndex = 0,
		CancellationToken ct = default);

	/// <inheritdoc cref="KeyboardSession.WriteKeyMap"/>
	void WriteKeyMap(UserKey[] keys, int layerIndex = 0, int profileIndex = 0);
	/// <inheritdoc cref="KeyboardSession.WriteKeyMap"/>
	Task WriteKeyMapAsync(UserKey[] keys, int layerIndex = 0, int profileIndex = 0,
		CancellationToken ct = default);

	/// <inheritdoc cref="KeyboardSession.SetKey(int, UserKey, int, int)"/>
	void SetKey(int keyIndex, UserKey key, int layerIndex = 0, int profileIndex = 0);
	/// <inheritdoc cref="KeyboardSession.SetKey(DDKey, UserKey, int, int)"/>
	void SetKey(DDKey key, UserKey value, int layerIndex = 0, int profileIndex = 0);
	/// <inheritdoc cref="KeyboardSession.SetKey(int, UserKey, int, int)"/>
	Task SetKeyAsync(int keyIndex, UserKey key, int layerIndex = 0, int profileIndex = 0,
		CancellationToken ct = default);
	/// <inheritdoc cref="KeyboardSession.SetKey(DDKey, UserKey, int, int)"/>
	Task SetKeyAsync(DDKey key, UserKey value, int layerIndex = 0, int profileIndex = 0,
		CancellationToken ct = default);

	// ── Dynamic Keystroke ─────────────────────────────────────────────────────

	/// <inheritdoc cref="KeyboardSession.ReadDynamicKeystrokeEntries"/>
	DynamicKeystrokeEntry[] ReadDynamicKeystrokeEntries(int profileIndex = 0);
	/// <inheritdoc cref="KeyboardSession.ReadDynamicKeystrokeEntries"/>
	Task<DynamicKeystrokeEntry[]> ReadDynamicKeystrokeEntriesAsync(int profileIndex = 0,
		CancellationToken ct = default);

	/// <inheritdoc cref="KeyboardSession.WriteDynamicKeystrokeEntries"/>
	void WriteDynamicKeystrokeEntries(DynamicKeystrokeEntry[] entries, int profileIndex = 0);
	/// <inheritdoc cref="KeyboardSession.WriteDynamicKeystrokeEntries"/>
	Task WriteDynamicKeystrokeEntriesAsync(DynamicKeystrokeEntry[] entries, int profileIndex = 0,
		CancellationToken ct = default);

	/// <inheritdoc cref="KeyboardSession.SetDynamicKeystrokeEntry"/>
	void SetDynamicKeystrokeEntry(int slotIndex, DynamicKeystrokeEntry entry, int profileIndex = 0);
	/// <inheritdoc cref="KeyboardSession.SetDynamicKeystrokeEntry"/>
	Task SetDynamicKeystrokeEntryAsync(int slotIndex, DynamicKeystrokeEntry entry,
		int profileIndex = 0, CancellationToken ct = default);

	// ── Multi-Tap ─────────────────────────────────────────────────────────────

	/// <inheritdoc cref="KeyboardSession.ReadMultiTapEntries"/>
	MultiTapEntry[] ReadMultiTapEntries(int profileIndex = 0);
	/// <inheritdoc cref="KeyboardSession.ReadMultiTapEntries"/>
	Task<MultiTapEntry[]> ReadMultiTapEntriesAsync(int profileIndex = 0, CancellationToken ct = default);

	/// <inheritdoc cref="KeyboardSession.WriteMultiTapEntries"/>
	void WriteMultiTapEntries(MultiTapEntry[] entries, int profileIndex = 0);
	/// <inheritdoc cref="KeyboardSession.WriteMultiTapEntries"/>
	Task WriteMultiTapEntriesAsync(MultiTapEntry[] entries, int profileIndex = 0,
		CancellationToken ct = default);

	/// <inheritdoc cref="KeyboardSession.SetMultiTapEntry"/>
	void SetMultiTapEntry(int slotIndex, MultiTapEntry entry, int profileIndex = 0);
	/// <inheritdoc cref="KeyboardSession.SetMultiTapEntry"/>
	Task SetMultiTapEntryAsync(int slotIndex, MultiTapEntry entry, int profileIndex = 0,
		CancellationToken ct = default);

	// ── Toggle keys ───────────────────────────────────────────────────────────

	/// <inheritdoc cref="KeyboardSession.ReadToggleKeyEntries"/>
	UserKey[] ReadToggleKeyEntries(int profileIndex = 0);
	/// <inheritdoc cref="KeyboardSession.ReadToggleKeyEntries"/>
	Task<UserKey[]> ReadToggleKeyEntriesAsync(int profileIndex = 0, CancellationToken ct = default);

	/// <inheritdoc cref="KeyboardSession.WriteToggleKeyEntries"/>
	void WriteToggleKeyEntries(UserKey[] entries, int profileIndex = 0);
	/// <inheritdoc cref="KeyboardSession.WriteToggleKeyEntries"/>
	Task WriteToggleKeyEntriesAsync(UserKey[] entries, int profileIndex = 0, CancellationToken ct = default);

	/// <inheritdoc cref="KeyboardSession.SetToggleKeyEntry"/>
	void SetToggleKeyEntry(int slotIndex, UserKey entry, int profileIndex = 0);
	/// <inheritdoc cref="KeyboardSession.SetToggleKeyEntry"/>
	Task SetToggleKeyEntryAsync(int slotIndex, UserKey entry, int profileIndex = 0,
		CancellationToken ct = default);

	// ── Macros ────────────────────────────────────────────────────────────────

	/// <inheritdoc cref="KeyboardSession.ReadMacroSlots"/>
	MacroAction[][] ReadMacroSlots(int profileIndex = 0);
	/// <inheritdoc cref="KeyboardSession.ReadMacroSlots"/>
	Task<MacroAction[][]> ReadMacroSlotsAsync(int profileIndex = 0, CancellationToken ct = default);

	/// <inheritdoc cref="KeyboardSession.WriteMacroSlots"/>
	void WriteMacroSlots(MacroAction[]?[] slots, int profileIndex = 0);
	/// <inheritdoc cref="KeyboardSession.WriteMacroSlots"/>
	Task WriteMacroSlotsAsync(MacroAction[]?[] slots, int profileIndex = 0, CancellationToken ct = default);

	/// <inheritdoc cref="KeyboardSession.SetMacroSlot"/>
	void SetMacroSlot(int slotIndex, MacroAction[] actions, int profileIndex = 0);
	/// <inheritdoc cref="KeyboardSession.SetMacroSlot"/>
	Task SetMacroSlotAsync(int slotIndex, MacroAction[] actions, int profileIndex = 0,
		CancellationToken ct = default);

	// ── Profile slots ─────────────────────────────────────────────────────────

	/// <inheritdoc cref="KeyboardSession.SwitchProfile"/>
	void SwitchProfile(int profileIndex);
	/// <inheritdoc cref="KeyboardSession.SwitchProfile"/>
	Task SwitchProfileAsync(int profileIndex, CancellationToken ct = default);

	/// <inheritdoc cref="KeyboardSession.GetCurrentProfile"/>
	int GetCurrentProfile();
	/// <inheritdoc cref="KeyboardSession.GetCurrentProfile"/>
	Task<int> GetCurrentProfileAsync(CancellationToken ct = default);

	/// <inheritdoc cref="KeyboardSession.PullFullProfile"/>
	FullProfileData PullFullProfile(int profileIndex = 0);
	/// <inheritdoc cref="KeyboardSession.PullFullProfile"/>
	Task<FullProfileData> PullFullProfileAsync(int profileIndex = 0, CancellationToken ct = default);

	/// <inheritdoc cref="KeyboardSession.PushFullProfile"/>
	void PushFullProfile(FullProfileData data, int profileIndex = 0);
	/// <inheritdoc cref="KeyboardSession.PushFullProfile"/>
	Task PushFullProfileAsync(FullProfileData data, int profileIndex = 0, CancellationToken ct = default);

	/// <inheritdoc cref="KeyboardSession.CopyProfile"/>
	void CopyProfile(int fromSlot, int toSlot);
	/// <inheritdoc cref="KeyboardSession.CopyProfile"/>
	Task CopyProfileAsync(int fromSlot, int toSlot, CancellationToken ct = default);

	/// <inheritdoc cref="KeyboardSession.ReadFuncBlock"/>
	KeyboardFuncBlock ReadFuncBlock(int profileIndex = 0);
	/// <inheritdoc cref="KeyboardSession.ReadFuncBlock"/>
	Task<KeyboardFuncBlock> ReadFuncBlockAsync(int profileIndex = 0, CancellationToken ct = default);

	/// <inheritdoc cref="KeyboardSession.WriteFuncBlock"/>
	void WriteFuncBlock(KeyboardFuncBlock block, int profileIndex = 0);
	/// <inheritdoc cref="KeyboardSession.WriteFuncBlock"/>
	Task WriteFuncBlockAsync(KeyboardFuncBlock block, int profileIndex = 0, CancellationToken ct = default);

	// ── Stored lighting ───────────────────────────────────────────────────────

	/// <inheritdoc cref="KeyboardSession.ReadStoredColors"/>
	(byte R, byte G, byte B)[] ReadStoredColors(int profileIndex = 0);
	/// <inheritdoc cref="KeyboardSession.ReadStoredColors"/>
	Task<(byte R, byte G, byte B)[]> ReadStoredColorsAsync(int profileIndex = 0, CancellationToken ct = default);

	/// <inheritdoc cref="KeyboardSession.WriteStoredColors"/>
	void WriteStoredColors((byte R, byte G, byte B)[] colors, int profileIndex = 0);
	/// <inheritdoc cref="KeyboardSession.WriteStoredColors"/>
	Task WriteStoredColorsAsync((byte R, byte G, byte B)[] colors, int profileIndex = 0,
		CancellationToken ct = default);

	/// <inheritdoc cref="KeyboardSession.SaveLightingToProfile"/>
	void SaveLightingToProfile(int profileIndex = 0);
	/// <inheritdoc cref="KeyboardSession.SaveLightingToProfile"/>
	Task SaveLightingToProfileAsync(int profileIndex = 0, CancellationToken ct = default);

	/// <inheritdoc cref="KeyboardSession.LoadLightingFromProfile"/>
	void LoadLightingFromProfile(int profileIndex = 0, byte brightness = 9);
	/// <inheritdoc cref="KeyboardSession.LoadLightingFromProfile"/>
	Task LoadLightingFromProfileAsync(int profileIndex = 0, byte brightness = 9,
		CancellationToken ct = default);

	/// <inheritdoc cref="KeyboardSession.ReadLiveColors"/>
	(byte R, byte G, byte B)[] ReadLiveColors();
	/// <inheritdoc cref="KeyboardSession.ReadLiveColors"/>
	Task<(byte R, byte G, byte B)[]> ReadLiveColorsAsync(CancellationToken ct = default);

	// ── Maintenance ───────────────────────────────────────────────────────────

	/// <inheritdoc cref="KeyboardSession.StartFastTransferMode"/>
	void StartFastTransferMode();
	/// <inheritdoc cref="KeyboardSession.StartFastTransferMode"/>
	Task StartFastTransferModeAsync(CancellationToken ct = default);

	/// <inheritdoc cref="KeyboardSession.StopFastTransferMode"/>
	void StopFastTransferMode();
	/// <inheritdoc cref="KeyboardSession.StopFastTransferMode"/>
	Task StopFastTransferModeAsync(CancellationToken ct = default);

	/// <inheritdoc cref="KeyboardSession.StartCalibration"/>
	void StartCalibration();
	/// <inheritdoc cref="KeyboardSession.StartCalibration"/>
	Task StartCalibrationAsync(CancellationToken ct = default);

	/// <inheritdoc cref="KeyboardSession.EndCalibration"/>
	void EndCalibration();
	/// <inheritdoc cref="KeyboardSession.EndCalibration"/>
	Task EndCalibrationAsync(CancellationToken ct = default);

	/// <inheritdoc cref="KeyboardSession.Reset"/>
	void Reset();
	/// <inheritdoc cref="KeyboardSession.Reset"/>
	Task ResetAsync(CancellationToken ct = default);
}

/// <summary>
/// Read-back of the keyboard's stored key-point profiles, available only on models that encode
/// depths at 0.005 mm (A75 Ultra, A75 Master, X60 Future). These are the only models whose
/// firmware answers a key-point read at all; everywhere else the host has to remember what it
/// wrote.
/// </summary>
/// <remarks>
/// Obtain this from <see cref="KeyboardSession.GetFeatures{TFeatures}"/>; it is the runtime
/// equivalent of the compile-time <see cref="IHasHighPrecision"/> gate. Unlike
/// <see cref="IProgrammableKeyboardFeatures"/>, availability here is a fixed property of the
/// model, so <see cref="KeyboardSession.Supports"/> with
/// <see cref="Capabilities.HighPrecision"/> answers the same question.
/// </remarks>
public interface IHighPrecisionFeatures
{
	/// <inheritdoc cref="KeyboardSession.ReadActuationPoint"/>
	float[] ReadActuationPoint();
	/// <summary>Reads the per-key actuation point profile, keyed by <see cref="DDKey"/>.</summary>
	IReadOnlyDictionary<DDKey, float> ReadActuationPointByKey();

	/// <inheritdoc cref="KeyboardSession.ReadDownstrokePoint"/>
	float[] ReadDownstrokePoint();
	/// <summary>Reads the per-key downstroke point profile, keyed by <see cref="DDKey"/>.</summary>
	IReadOnlyDictionary<DDKey, float> ReadDownstrokePointByKey();

	/// <inheritdoc cref="KeyboardSession.ReadUpstrokePoint"/>
	float[] ReadUpstrokePoint();
	/// <summary>Reads the per-key upstroke point profile, keyed by <see cref="DDKey"/>.</summary>
	IReadOnlyDictionary<DDKey, float> ReadUpstrokePointByKey();

	/// <inheritdoc cref="KeyboardSession.CaptureProfile"/>
	KeyboardProfile CaptureProfile(int profileIndex = 0);
}

/// <summary>
/// The dedicated logo LED zone (e.g. the DRUNKDEER logo on the A75 Ultra).
/// </summary>
/// <remarks>
/// Obtain this from <see cref="KeyboardSession.GetFeatures{TFeatures}"/>; it is the runtime
/// equivalent of the compile-time <see cref="IHasLogoLight"/> gate. Availability is a fixed
/// property of the model, so <see cref="KeyboardSession.Supports"/> with
/// <see cref="Capabilities.LogoLight"/> answers the same question.
/// </remarks>
public interface ILogoLightFeatures
{
	/// <inheritdoc cref="KeyboardSession.SetLogoLightPreset"/>
	void SetLogoLightPreset(LightPreset effect, byte brightness = 9, byte speed = 5);
	/// <inheritdoc cref="KeyboardSession.SetLogoLightPreset"/>
	Task SetLogoLightPresetAsync(LightPreset effect, byte brightness = 9, byte speed = 5,
		CancellationToken ct = default);

	/// <inheritdoc cref="KeyboardSession.SetLogoLightOff"/>
	void SetLogoLightOff();
	/// <inheritdoc cref="KeyboardSession.SetLogoLightOff"/>
	Task SetLogoLightOffAsync(CancellationToken ct = default);

	/// <inheritdoc cref="KeyboardSession.SetLogoLightColor(byte, byte, byte)"/>
	void SetLogoLightColor(byte r, byte g, byte b);
	/// <inheritdoc cref="KeyboardSession.SetLogoLightColor(RgbColor)"/>
	void SetLogoLightColor(RgbColor color);
	/// <inheritdoc cref="KeyboardSession.SetLogoLightColor(byte, byte, byte)"/>
	Task SetLogoLightColorAsync(byte r, byte g, byte b, CancellationToken ct = default);
	/// <inheritdoc cref="KeyboardSession.SetLogoLightColor(RgbColor)"/>
	Task SetLogoLightColorAsync(RgbColor color, CancellationToken ct = default);
}

/// <summary>
/// The dedicated side LED strip zone (e.g. the X60 Future).
/// </summary>
/// <remarks>
/// Obtain this from <see cref="KeyboardSession.GetFeatures{TFeatures}"/>; it is the runtime
/// equivalent of the compile-time <see cref="IHasSideLight"/> gate. As with the logo zone,
/// availability is a fixed property of the model.
/// </remarks>
public interface ISideLightFeatures
{
	/// <inheritdoc cref="KeyboardSession.SetSideLightPreset"/>
	void SetSideLightPreset(LightPreset effect, byte brightness = 9, byte speed = 5);
	/// <inheritdoc cref="KeyboardSession.SetSideLightPreset"/>
	Task SetSideLightPresetAsync(LightPreset effect, byte brightness = 9, byte speed = 5,
		CancellationToken ct = default);

	/// <inheritdoc cref="KeyboardSession.SetSideLightOff"/>
	void SetSideLightOff();
	/// <inheritdoc cref="KeyboardSession.SetSideLightOff"/>
	Task SetSideLightOffAsync(CancellationToken ct = default);

	/// <inheritdoc cref="KeyboardSession.SetSideLightColor(byte, byte, byte)"/>
	void SetSideLightColor(byte r, byte g, byte b);
	/// <inheritdoc cref="KeyboardSession.SetSideLightColor(RgbColor)"/>
	void SetSideLightColor(RgbColor color);
	/// <inheritdoc cref="KeyboardSession.SetSideLightColor(byte, byte, byte)"/>
	Task SetSideLightColorAsync(byte r, byte g, byte b, CancellationToken ct = default);
	/// <inheritdoc cref="KeyboardSession.SetSideLightColor(RgbColor)"/>
	Task SetSideLightColorAsync(RgbColor color, CancellationToken ct = default);
}
