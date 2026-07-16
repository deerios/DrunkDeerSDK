using System.Diagnostics.CodeAnalysis;

namespace DrunkDeer.Protocol;

/// <summary>
/// Runtime capability discovery for the untyped <see cref="KeyboardSession"/>.
/// </summary>
/// <remarks>
/// <para><see cref="KeyboardSession{TModel}"/> gates the programmable API at compile time through
/// marker interfaces, which is useless to a consumer that discovers the model at runtime — a CLI
/// or a web app has no type argument to name. These facades are the runtime equivalent: ask the
/// session what the keyboard in front of you can do, and get back an interface or a clear refusal.
/// </para>
/// <para>The facades are implemented explicitly, so they add nothing to this class's own surface;
/// the only way in is <see cref="TryGetFeatures{TFeatures}"/> or <see cref="GetFeatures{TFeatures}"/>.
/// Casting straight to a facade is not a way around the gate: the underlying methods still check,
/// and still throw.</para>
/// </remarks>
public partial class KeyboardSession :
	IProgrammableKeyboardFeatures,
	IHighPrecisionFeatures,
	ILogoLightFeatures,
	ISideLightFeatures
{
	/// <summary>
	/// <see langword="true"/> when the connected model advertises every capability in
	/// <paramref name="capabilities"/>.
	/// </summary>
	/// <remarks>
	/// This reads the model's static capability set, which is the right question for fixed hardware
	/// facts: does this board have a logo LED, does it encode depths at 0.005 mm. It is the wrong
	/// question for the FuncBlock gateway, which has no flag of its own and is instead implied by
	/// <see cref="Capabilities.KunPrecision"/> or <see cref="Capabilities.HighPrecision"/>. Ask
	/// <see cref="TryGetFeatures{TFeatures}"/> for that one rather than rebuilding the rule at the
	/// call site.
	/// </remarks>
	public bool Supports(Capabilities capabilities) =>
		(Model.Capabilities & capabilities) == capabilities;

	// Maps a facade to the condition that makes it real. Deliberately not generated: every
	// condition reads the model's capability data, so a new model in models.yaml flows through
	// here with no change. Throws for a type that isn't a facade, so a typo can't quietly hand
	// back a session that supports nothing.
	private bool SupportsFeatures(Type featureType)
	{
		if (featureType == typeof(IProgrammableKeyboardFeatures)) return HasFuncBlock;
		if (featureType == typeof(IHighPrecisionFeatures))        return Supports(Capabilities.HighPrecision);
		if (featureType == typeof(ILogoLightFeatures))            return Supports(Capabilities.LogoLight);
		if (featureType == typeof(ISideLightFeatures))            return Supports(Capabilities.SideLight);

		throw new ArgumentException(
			$"{featureType.Name} is not a keyboard feature facade. Expected one of " +
			$"{nameof(IProgrammableKeyboardFeatures)}, {nameof(IHighPrecisionFeatures)}, " +
			$"{nameof(ILogoLightFeatures)}, or {nameof(ISideLightFeatures)}.",
			nameof(featureType));
	}

	/// <summary>
	/// Gets the requested feature facade if the connected keyboard supports it.
	/// </summary>
	/// <typeparam name="TFeatures">
	/// One of <see cref="IProgrammableKeyboardFeatures"/>, <see cref="IHighPrecisionFeatures"/>,
	/// <see cref="ILogoLightFeatures"/>, or <see cref="ISideLightFeatures"/>.
	/// </typeparam>
	/// <param name="features">The facade, or <see langword="null"/> when unsupported.</param>
	/// <returns><see langword="true"/> when the facade is available.</returns>
	/// <exception cref="ArgumentException">
	/// <typeparamref name="TFeatures"/> is not one of the facade interfaces.
	/// </exception>
	public bool TryGetFeatures<TFeatures>([NotNullWhen(true)] out TFeatures? features)
		where TFeatures : class
	{
		features = SupportsFeatures(typeof(TFeatures)) ? this as TFeatures : null;
		return features is not null;
	}

	/// <summary>
	/// Gets the requested feature facade, or throws explaining why this keyboard cannot offer it.
	/// </summary>
	/// <typeparam name="TFeatures">
	/// One of <see cref="IProgrammableKeyboardFeatures"/>, <see cref="IHighPrecisionFeatures"/>,
	/// <see cref="ILogoLightFeatures"/>, or <see cref="ISideLightFeatures"/>.
	/// </typeparam>
	/// <exception cref="DrunkDeerCapabilityException">The keyboard does not support the facade.</exception>
	/// <exception cref="ArgumentException">
	/// <typeparamref name="TFeatures"/> is not one of the facade interfaces.
	/// </exception>
	public TFeatures GetFeatures<TFeatures>() where TFeatures : class
	{
		if (TryGetFeatures<TFeatures>(out var features))
			return features;

		// The model is what settles every facade, but naming the variant, firmware and precision mode
		// as well means a user pasting this message has described their whole keyboard.
		throw new DrunkDeerCapabilityException(
			$"{Model.Name} (variant {Variant}, fw {FirmwareVersion}, {PrecisionMode} precision) " +
			$"does not support {typeof(TFeatures).Name}.");
	}

	// ── IProgrammableKeyboardFeatures ─────────────────────────────────────────

	void IProgrammableKeyboardFeatures.SetKeyboardMode(KeyboardMode mode) => SetKeyboardMode(mode);
	Task IProgrammableKeyboardFeatures.SetKeyboardModeAsync(KeyboardMode mode, CancellationToken ct) =>
		SetKeyboardModeAsync(mode, ct);

	void IProgrammableKeyboardFeatures.SetReportRate(ReportRate rate) => SetReportRate(rate);
	Task IProgrammableKeyboardFeatures.SetReportRateAsync(ReportRate rate, CancellationToken ct) =>
		SetReportRateAsync(rate, ct);

	void IProgrammableKeyboardFeatures.SetDebounce(byte level) => SetDebounce(level);
	Task IProgrammableKeyboardFeatures.SetDebounceAsync(byte level, CancellationToken ct) =>
		SetDebounceAsync(level, ct);

	void IProgrammableKeyboardFeatures.SetStabilityMode(byte level) => SetStabilityMode(level);
	Task IProgrammableKeyboardFeatures.SetStabilityModeAsync(byte level, CancellationToken ct) =>
		SetStabilityModeAsync(level, ct);

	void IProgrammableKeyboardFeatures.SetTickRate(byte rate) => SetTickRate(rate);
	Task IProgrammableKeyboardFeatures.SetTickRateAsync(byte rate, CancellationToken ct) =>
		SetTickRateAsync(rate, ct);

	void IProgrammableKeyboardFeatures.ConfigureKeyLocks(bool? winLock, bool? altTabLock, bool? altF4Lock) =>
		ConfigureKeyLocks(winLock, altTabLock, altF4Lock);
	Task IProgrammableKeyboardFeatures.ConfigureKeyLocksAsync(bool? winLock, bool? altTabLock,
		bool? altF4Lock, CancellationToken ct) => ConfigureKeyLocksAsync(winLock, altTabLock, altF4Lock, ct);

	void IProgrammableKeyboardFeatures.SetLightPreset(LightPreset effect, byte brightness, byte speed) =>
		SetLightPreset(effect, brightness, speed);
	Task IProgrammableKeyboardFeatures.SetLightPresetAsync(LightPreset effect, byte brightness,
		byte speed, CancellationToken ct) => SetLightPresetAsync(effect, brightness, speed, ct);

	void IProgrammableKeyboardFeatures.SetLightCustom() => SetLightCustom();
	Task IProgrammableKeyboardFeatures.SetLightCustomAsync(CancellationToken ct) => SetLightCustomAsync(ct);

	void IProgrammableKeyboardFeatures.SetLightPresetColor(byte r, byte g, byte b) =>
		SetLightPresetColor(r, g, b);
	void IProgrammableKeyboardFeatures.SetLightPresetColor(RgbColor color) => SetLightPresetColor(color);
	Task IProgrammableKeyboardFeatures.SetLightPresetColorAsync(byte r, byte g, byte b, CancellationToken ct) =>
		SetLightPresetColorAsync(r, g, b, ct);
	Task IProgrammableKeyboardFeatures.SetLightPresetColorAsync(RgbColor color, CancellationToken ct) =>
		SetLightPresetColorAsync(color, ct);

	KeyTriggerConfig[] IProgrammableKeyboardFeatures.ReadKeyTriggers(int profileIndex) =>
		ReadKeyTriggers(profileIndex);
	Task<KeyTriggerConfig[]> IProgrammableKeyboardFeatures.ReadKeyTriggersAsync(int profileIndex, CancellationToken ct) =>
		ReadKeyTriggersAsync(profileIndex, ct);

	void IProgrammableKeyboardFeatures.WriteKeyTriggers(KeyTriggerConfig[] configs, int profileIndex) =>
		WriteKeyTriggers(configs, profileIndex);
	Task IProgrammableKeyboardFeatures.WriteKeyTriggersAsync(KeyTriggerConfig[] configs, int profileIndex,
		CancellationToken ct) => WriteKeyTriggersAsync(configs, profileIndex, ct);

	void IProgrammableKeyboardFeatures.SetKeyTrigger(int keyIndex, KeyTriggerConfig config, int profileIndex) =>
		SetKeyTrigger(keyIndex, config, profileIndex);
	void IProgrammableKeyboardFeatures.SetKeyTrigger(DDKey key, KeyTriggerConfig config, int profileIndex) =>
		SetKeyTrigger(key, config, profileIndex);
	Task IProgrammableKeyboardFeatures.SetKeyTriggerAsync(int keyIndex, KeyTriggerConfig config,
		int profileIndex, CancellationToken ct) => SetKeyTriggerAsync(keyIndex, config, profileIndex, ct);
	Task IProgrammableKeyboardFeatures.SetKeyTriggerAsync(DDKey key, KeyTriggerConfig config,
		int profileIndex, CancellationToken ct) => SetKeyTriggerAsync(key, config, profileIndex, ct);

	UserKey[] IProgrammableKeyboardFeatures.ReadKeyMap(int layerIndex, int profileIndex) =>
		ReadKeyMap(layerIndex, profileIndex);
	Task<UserKey[]> IProgrammableKeyboardFeatures.ReadKeyMapAsync(int layerIndex, int profileIndex,
		CancellationToken ct) => ReadKeyMapAsync(layerIndex, profileIndex, ct);

	UserKey[] IProgrammableKeyboardFeatures.ReadDefaultKeyMap(int layerIndex, int profileIndex) =>
		ReadDefaultKeyMap(layerIndex, profileIndex);
	Task<UserKey[]> IProgrammableKeyboardFeatures.ReadDefaultKeyMapAsync(int layerIndex, int profileIndex,
		CancellationToken ct) => ReadDefaultKeyMapAsync(layerIndex, profileIndex, ct);

	void IProgrammableKeyboardFeatures.WriteKeyMap(UserKey[] keys, int layerIndex, int profileIndex) =>
		WriteKeyMap(keys, layerIndex, profileIndex);
	Task IProgrammableKeyboardFeatures.WriteKeyMapAsync(UserKey[] keys, int layerIndex, int profileIndex,
		CancellationToken ct) => WriteKeyMapAsync(keys, layerIndex, profileIndex, ct);

	void IProgrammableKeyboardFeatures.SetKey(int keyIndex, UserKey key, int layerIndex, int profileIndex) =>
		SetKey(keyIndex, key, layerIndex, profileIndex);
	void IProgrammableKeyboardFeatures.SetKey(DDKey key, UserKey value, int layerIndex, int profileIndex) =>
		SetKey(key, value, layerIndex, profileIndex);
	Task IProgrammableKeyboardFeatures.SetKeyAsync(int keyIndex, UserKey key, int layerIndex,
		int profileIndex, CancellationToken ct) => SetKeyAsync(keyIndex, key, layerIndex, profileIndex, ct);
	Task IProgrammableKeyboardFeatures.SetKeyAsync(DDKey key, UserKey value, int layerIndex,
		int profileIndex, CancellationToken ct) => SetKeyAsync(key, value, layerIndex, profileIndex, ct);

	DynamicKeystrokeEntry[] IProgrammableKeyboardFeatures.ReadDynamicKeystrokeEntries(int profileIndex) =>
		ReadDynamicKeystrokeEntries(profileIndex);
	Task<DynamicKeystrokeEntry[]> IProgrammableKeyboardFeatures.ReadDynamicKeystrokeEntriesAsync(
		int profileIndex, CancellationToken ct) => ReadDynamicKeystrokeEntriesAsync(profileIndex, ct);

	void IProgrammableKeyboardFeatures.WriteDynamicKeystrokeEntries(DynamicKeystrokeEntry[] entries, int profileIndex) =>
		WriteDynamicKeystrokeEntries(entries, profileIndex);
	Task IProgrammableKeyboardFeatures.WriteDynamicKeystrokeEntriesAsync(DynamicKeystrokeEntry[] entries,
		int profileIndex, CancellationToken ct) => WriteDynamicKeystrokeEntriesAsync(entries, profileIndex, ct);

	void IProgrammableKeyboardFeatures.SetDynamicKeystrokeEntry(int slotIndex, DynamicKeystrokeEntry entry,
		int profileIndex) => SetDynamicKeystrokeEntry(slotIndex, entry, profileIndex);
	Task IProgrammableKeyboardFeatures.SetDynamicKeystrokeEntryAsync(int slotIndex, DynamicKeystrokeEntry entry,
		int profileIndex, CancellationToken ct) => SetDynamicKeystrokeEntryAsync(slotIndex, entry, profileIndex, ct);

	MultiTapEntry[] IProgrammableKeyboardFeatures.ReadMultiTapEntries(int profileIndex) =>
		ReadMultiTapEntries(profileIndex);
	Task<MultiTapEntry[]> IProgrammableKeyboardFeatures.ReadMultiTapEntriesAsync(int profileIndex,
		CancellationToken ct) => ReadMultiTapEntriesAsync(profileIndex, ct);

	void IProgrammableKeyboardFeatures.WriteMultiTapEntries(MultiTapEntry[] entries, int profileIndex) =>
		WriteMultiTapEntries(entries, profileIndex);
	Task IProgrammableKeyboardFeatures.WriteMultiTapEntriesAsync(MultiTapEntry[] entries, int profileIndex,
		CancellationToken ct) => WriteMultiTapEntriesAsync(entries, profileIndex, ct);

	void IProgrammableKeyboardFeatures.SetMultiTapEntry(int slotIndex, MultiTapEntry entry, int profileIndex) =>
		SetMultiTapEntry(slotIndex, entry, profileIndex);
	Task IProgrammableKeyboardFeatures.SetMultiTapEntryAsync(int slotIndex, MultiTapEntry entry,
		int profileIndex, CancellationToken ct) => SetMultiTapEntryAsync(slotIndex, entry, profileIndex, ct);

	UserKey[] IProgrammableKeyboardFeatures.ReadToggleKeyEntries(int profileIndex) =>
		ReadToggleKeyEntries(profileIndex);
	Task<UserKey[]> IProgrammableKeyboardFeatures.ReadToggleKeyEntriesAsync(int profileIndex,
		CancellationToken ct) => ReadToggleKeyEntriesAsync(profileIndex, ct);

	void IProgrammableKeyboardFeatures.WriteToggleKeyEntries(UserKey[] entries, int profileIndex) =>
		WriteToggleKeyEntries(entries, profileIndex);
	Task IProgrammableKeyboardFeatures.WriteToggleKeyEntriesAsync(UserKey[] entries, int profileIndex,
		CancellationToken ct) => WriteToggleKeyEntriesAsync(entries, profileIndex, ct);

	void IProgrammableKeyboardFeatures.SetToggleKeyEntry(int slotIndex, UserKey entry, int profileIndex) =>
		SetToggleKeyEntry(slotIndex, entry, profileIndex);
	Task IProgrammableKeyboardFeatures.SetToggleKeyEntryAsync(int slotIndex, UserKey entry,
		int profileIndex, CancellationToken ct) => SetToggleKeyEntryAsync(slotIndex, entry, profileIndex, ct);

	MacroAction[][] IProgrammableKeyboardFeatures.ReadMacroSlots(int profileIndex) =>
		ReadMacroSlots(profileIndex);
	Task<MacroAction[][]> IProgrammableKeyboardFeatures.ReadMacroSlotsAsync(int profileIndex,
		CancellationToken ct) => ReadMacroSlotsAsync(profileIndex, ct);

	void IProgrammableKeyboardFeatures.WriteMacroSlots(MacroAction[]?[] slots, int profileIndex) =>
		WriteMacroSlots(slots, profileIndex);
	Task IProgrammableKeyboardFeatures.WriteMacroSlotsAsync(MacroAction[]?[] slots, int profileIndex,
		CancellationToken ct) => WriteMacroSlotsAsync(slots, profileIndex, ct);

	void IProgrammableKeyboardFeatures.SetMacroSlot(int slotIndex, MacroAction[] actions, int profileIndex) =>
		SetMacroSlot(slotIndex, actions, profileIndex);
	Task IProgrammableKeyboardFeatures.SetMacroSlotAsync(int slotIndex, MacroAction[] actions,
		int profileIndex, CancellationToken ct) => SetMacroSlotAsync(slotIndex, actions, profileIndex, ct);

	void IProgrammableKeyboardFeatures.SwitchProfile(int profileIndex) => SwitchProfile(profileIndex);
	Task IProgrammableKeyboardFeatures.SwitchProfileAsync(int profileIndex, CancellationToken ct) =>
		SwitchProfileAsync(profileIndex, ct);

	int IProgrammableKeyboardFeatures.GetCurrentProfile() => GetCurrentProfile();
	Task<int> IProgrammableKeyboardFeatures.GetCurrentProfileAsync(CancellationToken ct) =>
		GetCurrentProfileAsync(ct);

	FullProfileData IProgrammableKeyboardFeatures.PullFullProfile(int profileIndex) =>
		PullFullProfile(profileIndex);
	Task<FullProfileData> IProgrammableKeyboardFeatures.PullFullProfileAsync(int profileIndex,
		CancellationToken ct) => PullFullProfileAsync(profileIndex, ct);

	void IProgrammableKeyboardFeatures.PushFullProfile(FullProfileData data, int profileIndex) =>
		PushFullProfile(data, profileIndex);
	Task IProgrammableKeyboardFeatures.PushFullProfileAsync(FullProfileData data, int profileIndex,
		CancellationToken ct) => PushFullProfileAsync(data, profileIndex, ct);

	void IProgrammableKeyboardFeatures.CopyProfile(int fromSlot, int toSlot) => CopyProfile(fromSlot, toSlot);
	Task IProgrammableKeyboardFeatures.CopyProfileAsync(int fromSlot, int toSlot, CancellationToken ct) =>
		CopyProfileAsync(fromSlot, toSlot, ct);

	KeyboardFuncBlock IProgrammableKeyboardFeatures.ReadFuncBlock(int profileIndex) =>
		ReadFuncBlock(profileIndex);
	Task<KeyboardFuncBlock> IProgrammableKeyboardFeatures.ReadFuncBlockAsync(int profileIndex,
		CancellationToken ct) => ReadFuncBlockAsync(profileIndex, ct);

	void IProgrammableKeyboardFeatures.WriteFuncBlock(KeyboardFuncBlock block, int profileIndex) =>
		WriteFuncBlock(block, profileIndex);
	Task IProgrammableKeyboardFeatures.WriteFuncBlockAsync(KeyboardFuncBlock block, int profileIndex,
		CancellationToken ct) => WriteFuncBlockAsync(block, profileIndex, ct);

	(byte R, byte G, byte B)[] IProgrammableKeyboardFeatures.ReadStoredColors(int profileIndex) =>
		ReadStoredColors(profileIndex);
	Task<(byte R, byte G, byte B)[]> IProgrammableKeyboardFeatures.ReadStoredColorsAsync(int profileIndex,
		CancellationToken ct) => ReadStoredColorsAsync(profileIndex, ct);

	void IProgrammableKeyboardFeatures.WriteStoredColors((byte R, byte G, byte B)[] colors, int profileIndex) =>
		WriteStoredColors(colors, profileIndex);
	Task IProgrammableKeyboardFeatures.WriteStoredColorsAsync((byte R, byte G, byte B)[] colors,
		int profileIndex, CancellationToken ct) => WriteStoredColorsAsync(colors, profileIndex, ct);

	void IProgrammableKeyboardFeatures.SaveLightingToProfile(int profileIndex) =>
		SaveLightingToProfile(profileIndex);
	Task IProgrammableKeyboardFeatures.SaveLightingToProfileAsync(int profileIndex, CancellationToken ct) =>
		SaveLightingToProfileAsync(profileIndex, ct);

	void IProgrammableKeyboardFeatures.LoadLightingFromProfile(int profileIndex, byte brightness) =>
		LoadLightingFromProfile(profileIndex, brightness);
	Task IProgrammableKeyboardFeatures.LoadLightingFromProfileAsync(int profileIndex, byte brightness,
		CancellationToken ct) => LoadLightingFromProfileAsync(profileIndex, brightness, ct);

	(byte R, byte G, byte B)[] IProgrammableKeyboardFeatures.ReadLiveColors() => ReadLiveColors();
	Task<(byte R, byte G, byte B)[]> IProgrammableKeyboardFeatures.ReadLiveColorsAsync(CancellationToken ct) =>
		ReadLiveColorsAsync(ct);

	void IProgrammableKeyboardFeatures.StartFastTransferMode() => StartFastTransferMode();
	Task IProgrammableKeyboardFeatures.StartFastTransferModeAsync(CancellationToken ct) =>
		StartFastTransferModeAsync(ct);

	void IProgrammableKeyboardFeatures.StopFastTransferMode() => StopFastTransferMode();
	Task IProgrammableKeyboardFeatures.StopFastTransferModeAsync(CancellationToken ct) =>
		StopFastTransferModeAsync(ct);

	void IProgrammableKeyboardFeatures.StartCalibration() => StartCalibration();
	Task IProgrammableKeyboardFeatures.StartCalibrationAsync(CancellationToken ct) => StartCalibrationAsync(ct);

	void IProgrammableKeyboardFeatures.EndCalibration() => EndCalibration();
	Task IProgrammableKeyboardFeatures.EndCalibrationAsync(CancellationToken ct) => EndCalibrationAsync(ct);

	void IProgrammableKeyboardFeatures.Reset() => Reset();
	Task IProgrammableKeyboardFeatures.ResetAsync(CancellationToken ct) => ResetAsync(ct);

	// ── IHighPrecisionFeatures ────────────────────────────────────────────────

	float[] IHighPrecisionFeatures.ReadActuationPoint() => ReadActuationPoint();
	IReadOnlyDictionary<DDKey, float> IHighPrecisionFeatures.ReadActuationPointByKey() =>
		ToKeyDictionary(ReadActuationPoint());

	float[] IHighPrecisionFeatures.ReadDownstrokePoint() => ReadDownstrokePoint();
	IReadOnlyDictionary<DDKey, float> IHighPrecisionFeatures.ReadDownstrokePointByKey() =>
		ToKeyDictionary(ReadDownstrokePoint());

	float[] IHighPrecisionFeatures.ReadUpstrokePoint() => ReadUpstrokePoint();
	IReadOnlyDictionary<DDKey, float> IHighPrecisionFeatures.ReadUpstrokePointByKey() =>
		ToKeyDictionary(ReadUpstrokePoint());

	KeyboardProfile IHighPrecisionFeatures.CaptureProfile(int profileIndex) => CaptureProfile(profileIndex);

	// ── ILogoLightFeatures ────────────────────────────────────────────────────

	void ILogoLightFeatures.SetLogoLightPreset(LightPreset effect, byte brightness, byte speed) =>
		SetLogoLightPreset(effect, brightness, speed);
	Task ILogoLightFeatures.SetLogoLightPresetAsync(LightPreset effect, byte brightness, byte speed,
		CancellationToken ct) => SetLogoLightPresetAsync(effect, brightness, speed, ct);

	void ILogoLightFeatures.SetLogoLightOff() => SetLogoLightOff();
	Task ILogoLightFeatures.SetLogoLightOffAsync(CancellationToken ct) => SetLogoLightOffAsync(ct);

	void ILogoLightFeatures.SetLogoLightColor(byte r, byte g, byte b) => SetLogoLightColor(r, g, b);
	void ILogoLightFeatures.SetLogoLightColor(RgbColor color) => SetLogoLightColor(color);
	Task ILogoLightFeatures.SetLogoLightColorAsync(byte r, byte g, byte b, CancellationToken ct) =>
		SetLogoLightColorAsync(r, g, b, ct);
	Task ILogoLightFeatures.SetLogoLightColorAsync(RgbColor color, CancellationToken ct) =>
		SetLogoLightColorAsync(color, ct);

	// ── ISideLightFeatures ────────────────────────────────────────────────────

	void ISideLightFeatures.SetSideLightPreset(LightPreset effect, byte brightness, byte speed) =>
		SetSideLightPreset(effect, brightness, speed);
	Task ISideLightFeatures.SetSideLightPresetAsync(LightPreset effect, byte brightness, byte speed,
		CancellationToken ct) => SetSideLightPresetAsync(effect, brightness, speed, ct);

	void ISideLightFeatures.SetSideLightOff() => SetSideLightOff();
	Task ISideLightFeatures.SetSideLightOffAsync(CancellationToken ct) => SetSideLightOffAsync(ct);

	void ISideLightFeatures.SetSideLightColor(byte r, byte g, byte b) => SetSideLightColor(r, g, b);
	void ISideLightFeatures.SetSideLightColor(RgbColor color) => SetSideLightColor(color);
	Task ISideLightFeatures.SetSideLightColorAsync(byte r, byte g, byte b, CancellationToken ct) =>
		SetSideLightColorAsync(r, g, b, ct);
	Task ISideLightFeatures.SetSideLightColorAsync(RgbColor color, CancellationToken ct) =>
		SetSideLightColorAsync(color, ct);
}
