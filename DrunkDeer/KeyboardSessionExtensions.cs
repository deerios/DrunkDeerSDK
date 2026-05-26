using System.ComponentModel.DataAnnotations;

namespace DrunkDeer.Protocol;

/// <summary>
/// Capability-gated extension methods for <see cref="KeyboardSession{TModel}"/>.
/// Each group is constrained to a marker interface so only models that actually support
/// a feature expose it in intellisense.
/// </summary>
public static class KeyboardSessionExtensions
{
    // ── IHasFuncBlock ────────────────────────────────────────────────────────
    // FuncBlock extended gateway (0x55/0x05-0x06). Available on all models with
    // always-Kun or FD precision (G65 m1/m2/m3, G60 v600, A75 Ultra, A75 Master,
    // X60 Future, and the Unk-601/602 variants).

    /// <summary>Switches the keyboard between Windows and Mac compatibility modes.</summary>
    public static void SetKeyboardMode<T>(this KeyboardSession<T> s, KeyboardMode mode)
        where T : IHasFuncBlock => s.SetKeyboardMode(mode);

    /// <summary>Sets the USB polling rate.</summary>
    public static void SetReportRate<T>(this KeyboardSession<T> s, ReportRate rate)
        where T : IHasFuncBlock => s.SetReportRate(rate);

    /// <summary>Sets the debounce level (0-7).</summary>
    public static void SetDebounce<T>(this KeyboardSession<T> s, byte level)
        where T : IHasFuncBlock => s.SetDebounce(level);

    /// <summary>Sets the contact stability mode level (0-3).</summary>
    public static void SetStabilityMode<T>(this KeyboardSession<T> s, byte level)
        where T : IHasFuncBlock => s.SetStabilityMode(level);

    /// <summary>Configures key-combination locks (Win, Alt+Tab, Alt+F4).</summary>
    public static void ConfigureKeyLocks<T>(this KeyboardSession<T> s,
        bool? winLock = null, bool? altTabLock = null, bool? altF4Lock = null)
        where T : IHasFuncBlock => s.ConfigureKeyLocks(winLock, altTabLock, altF4Lock);

    /// <summary>Activates a built-in firmware lighting animation preset.</summary>
    public static void SetLightPreset<T>(this KeyboardSession<T> s,
        LightPreset effect, [Range(0, 9)] byte brightness = 9, byte speed = 5)
        where T : IHasFuncBlock => s.SetLightPreset(effect, brightness, speed);

    /// <summary>Switches lighting back to custom RGB mode.</summary>
    public static void SetLightCustom<T>(this KeyboardSession<T> s)
        where T : IHasFuncBlock => s.SetLightCustom();

    /// <summary>Sets the single-colour tint used by the main key preset lighting effect.</summary>
    public static void SetLightPresetColor<T>(this KeyboardSession<T> s, byte r, byte g, byte b)
        where T : IHasFuncBlock => s.SetLightPresetColor(r, g, b);

    /// <inheritdoc cref="SetLightPresetColor{T}(KeyboardSession{T}, byte, byte, byte)"/>
    public static void SetLightPresetColor<T>(this KeyboardSession<T> s, RgbColor color)
        where T : IHasFuncBlock => s.SetLightPresetColor(color);

    /// <summary>Sets the firmware sensor sampling tick rate (bits 4-7, values 0-15).</summary>
    public static void SetTickRate<T>(this KeyboardSession<T> s, byte rate)
        where T : IHasFuncBlock => s.SetTickRate(rate);

    /// <summary>Reads all 128 per-key trigger configurations for the specified profile.</summary>
    public static KeyTriggerConfig[] ReadKeyTriggers<T>(this KeyboardSession<T> s, int profileIndex = 0)
        where T : IHasFuncBlock => s.ReadKeyTriggers(profileIndex);

    /// <summary>Writes all 128 per-key trigger configurations for the specified profile.</summary>
    public static void WriteKeyTriggers<T>(this KeyboardSession<T> s,
        KeyTriggerConfig[] configs, int profileIndex = 0)
        where T : IHasFuncBlock => s.WriteKeyTriggers(configs, profileIndex);

    /// <summary>Writes the trigger configuration for a single key by layout index.</summary>
    public static void SetKeyTrigger<T>(this KeyboardSession<T> s,
        int keyIndex, KeyTriggerConfig config, int profileIndex = 0)
        where T : IHasFuncBlock => s.SetKeyTrigger(keyIndex, config, profileIndex);

    /// <summary>Writes the trigger configuration for a single named key.</summary>
    public static void SetKeyTrigger<T>(this KeyboardSession<T> s,
        DDKey key, KeyTriggerConfig config, int profileIndex = 0)
        where T : IHasFuncBlock => s.SetKeyTrigger(key, config, profileIndex);

    /// <summary>Reads the user-assigned key map for a layer.</summary>
    public static UserKey[] ReadKeyMap<T>(this KeyboardSession<T> s,
        int layerIndex = 0, int profileIndex = 0)
        where T : IHasFuncBlock => s.ReadKeyMap(layerIndex, profileIndex);

    /// <summary>Reads the factory-default key map for a layer.</summary>
    public static UserKey[] ReadDefaultKeyMap<T>(this KeyboardSession<T> s,
        int layerIndex = 0, int profileIndex = 0)
        where T : IHasFuncBlock => s.ReadDefaultKeyMap(layerIndex, profileIndex);

    /// <summary>Writes all 128 key assignments for a layer.</summary>
    public static void WriteKeyMap<T>(this KeyboardSession<T> s,
        UserKey[] keys, int layerIndex = 0, int profileIndex = 0)
        where T : IHasFuncBlock => s.WriteKeyMap(keys, layerIndex, profileIndex);

    /// <summary>Sets a single key assignment by layout index.</summary>
    public static void SetKey<T>(this KeyboardSession<T> s,
        int keyIndex, UserKey key, int layerIndex = 0, int profileIndex = 0)
        where T : IHasFuncBlock => s.SetKey(keyIndex, key, layerIndex, profileIndex);

    /// <summary>Sets a single key assignment by named key.</summary>
    public static void SetKey<T>(this KeyboardSession<T> s,
        DDKey key, UserKey value, int layerIndex = 0, int profileIndex = 0)
        where T : IHasFuncBlock => s.SetKey(key, value, layerIndex, profileIndex);

    /// <summary>Reads all 32 Dynamic Keystroke slot configurations for the specified profile.</summary>
    public static DynamicKeystrokeEntry[] ReadDynamicKeystrokeEntries<T>(this KeyboardSession<T> s,
        int profileIndex = 0)
        where T : IHasFuncBlock => s.ReadDynamicKeystrokeEntries(profileIndex);

    /// <summary>Writes all 32 Dynamic Keystroke slot configurations for the specified profile.</summary>
    public static void WriteDynamicKeystrokeEntries<T>(this KeyboardSession<T> s,
        DynamicKeystrokeEntry[] entries, int profileIndex = 0)
        where T : IHasFuncBlock => s.WriteDynamicKeystrokeEntries(entries, profileIndex);

    /// <summary>Writes a single Dynamic Keystroke slot configuration.</summary>
    public static void SetDynamicKeystrokeEntry<T>(this KeyboardSession<T> s,
        int slotIndex, DynamicKeystrokeEntry entry, int profileIndex = 0)
        where T : IHasFuncBlock => s.SetDynamicKeystrokeEntry(slotIndex, entry, profileIndex);

    /// <summary>Reads all 32 Multi-Tap slot configurations for the specified profile.</summary>
    public static MultiTapEntry[] ReadMultiTapEntries<T>(this KeyboardSession<T> s, int profileIndex = 0)
        where T : IHasFuncBlock => s.ReadMultiTapEntries(profileIndex);

    /// <summary>Writes all 32 Multi-Tap slot configurations for the specified profile.</summary>
    public static void WriteMultiTapEntries<T>(this KeyboardSession<T> s,
        MultiTapEntry[] entries, int profileIndex = 0)
        where T : IHasFuncBlock => s.WriteMultiTapEntries(entries, profileIndex);

    /// <summary>Writes a single Multi-Tap slot configuration.</summary>
    public static void SetMultiTapEntry<T>(this KeyboardSession<T> s,
        int slotIndex, MultiTapEntry entry, int profileIndex = 0)
        where T : IHasFuncBlock => s.SetMultiTapEntry(slotIndex, entry, profileIndex);

    /// <summary>Reads all 32 Toggle slot configurations for the specified profile.</summary>
    public static UserKey[] ReadToggleKeyEntries<T>(this KeyboardSession<T> s, int profileIndex = 0)
        where T : IHasFuncBlock => s.ReadToggleKeyEntries(profileIndex);

    /// <summary>Writes all 32 Toggle slot configurations for the specified profile.</summary>
    public static void WriteToggleKeyEntries<T>(this KeyboardSession<T> s,
        UserKey[] entries, int profileIndex = 0)
        where T : IHasFuncBlock => s.WriteToggleKeyEntries(entries, profileIndex);

    /// <summary>Writes a single Toggle slot configuration.</summary>
    public static void SetToggleKeyEntry<T>(this KeyboardSession<T> s,
        int slotIndex, UserKey entry, int profileIndex = 0)
        where T : IHasFuncBlock => s.SetToggleKeyEntry(slotIndex, entry, profileIndex);

    /// <summary>Reads all 32 macro slots for the specified profile.</summary>
    public static MacroAction[][] ReadMacroSlots<T>(this KeyboardSession<T> s, int profileIndex = 0)
        where T : IHasFuncBlock => s.ReadMacroSlots(profileIndex);

    /// <summary>Writes all 32 macro slots for the specified profile.</summary>
    public static void WriteMacroSlots<T>(this KeyboardSession<T> s,
        MacroAction[]?[] slots, int profileIndex = 0)
        where T : IHasFuncBlock => s.WriteMacroSlots(slots, profileIndex);

    /// <summary>Writes a single macro slot.</summary>
    public static void SetMacroSlot<T>(this KeyboardSession<T> s,
        int slotIndex, MacroAction[] actions, int profileIndex = 0)
        where T : IHasFuncBlock => s.SetMacroSlot(slotIndex, actions, profileIndex);

    /// <summary>Tells the keyboard to switch to the specified profile immediately.</summary>
    public static void SwitchProfile<T>(this KeyboardSession<T> s, int profileIndex)
        where T : IHasFuncBlock => s.SwitchProfile(profileIndex);

    /// <summary>Reads the currently active profile index from the keyboard.</summary>
    public static int GetCurrentProfile<T>(this KeyboardSession<T> s)
        where T : IHasFuncBlock => s.GetCurrentProfile();

    /// <summary>Reads every sub-block for the specified profile.</summary>
    public static FullProfileData PullFullProfile<T>(this KeyboardSession<T> s, int profileIndex = 0)
        where T : IHasFuncBlock => s.PullFullProfile(profileIndex);

    /// <summary>Writes all non-null sections of data to the specified profile.</summary>
    public static void PushFullProfile<T>(this KeyboardSession<T> s,
        FullProfileData data, int profileIndex = 0)
        where T : IHasFuncBlock => s.PushFullProfile(data, profileIndex);

    /// <summary>Copies all profile data from one slot to another.</summary>
    public static void CopyProfile<T>(this KeyboardSession<T> s, int fromSlot, int toSlot)
        where T : IHasFuncBlock => s.CopyProfile(fromSlot, toSlot);

    /// <summary>Reads the function configuration block for the specified profile.</summary>
    public static KeyboardFuncBlock ReadFuncBlock<T>(this KeyboardSession<T> s, int profileIndex = 0)
        where T : IHasFuncBlock => s.ReadFuncBlock(profileIndex);

    /// <summary>Writes a function configuration block back to the specified profile.</summary>
    public static void WriteFuncBlock<T>(this KeyboardSession<T> s,
        KeyboardFuncBlock block, int profileIndex = 0)
        where T : IHasFuncBlock => s.WriteFuncBlock(block, profileIndex);

    /// <summary>Reads the stored per-key RGB colours from the keyboard's flash for the specified profile.</summary>
    public static (byte R, byte G, byte B)[] ReadStoredColors<T>(this KeyboardSession<T> s,
        int profileIndex = 0)
        where T : IHasFuncBlock => s.ReadStoredColors(profileIndex);

    /// <summary>Writes per-key RGB colours to the keyboard's flash for the specified profile.</summary>
    public static void WriteStoredColors<T>(this KeyboardSession<T> s,
        (byte R, byte G, byte B)[] colors, int profileIndex = 0)
        where T : IHasFuncBlock => s.WriteStoredColors(colors, profileIndex);

    /// <summary>Saves the current in-memory colour profile to the keyboard's flash for the specified profile slot.</summary>
    public static void SaveLightingToProfile<T>(this KeyboardSession<T> s, int profileIndex = 0)
        where T : IHasFuncBlock => s.SaveLightingToProfile(profileIndex);

    /// <summary>Loads the stored per-key colours for the specified profile from flash and applies them live.</summary>
    public static void LoadLightingFromProfile<T>(this KeyboardSession<T> s,
        int profileIndex = 0, [Range(0, 9)] byte brightness = 9)
        where T : IHasFuncBlock => s.LoadLightingFromProfile(profileIndex, brightness);

    /// <summary>Reads the currently displayed per-key RGB colours from the keyboard regardless of the active lighting effect.</summary>
    public static (byte R, byte G, byte B)[] ReadLiveColors<T>(this KeyboardSession<T> s)
        where T : IHasFuncBlock => s.ReadLiveColors();

    /// <summary>Enters fast-transfer mode, suspending normal key processing on the firmware.</summary>
    public static void StartFastTransferMode<T>(this KeyboardSession<T> s)
        where T : IHasFuncBlock => s.StartFastTransferMode();

    /// <summary>Exits fast-transfer mode, resuming normal key processing on the firmware.</summary>
    public static void StopFastTransferMode<T>(this KeyboardSession<T> s)
        where T : IHasFuncBlock => s.StopFastTransferMode();

    /// <summary>Signals the keyboard to begin analog sensor calibration.</summary>
    public static void StartCalibration<T>(this KeyboardSession<T> s)
        where T : IHasFuncBlock => s.StartCalibration();

    /// <summary>Signals the keyboard to end analog sensor calibration.</summary>
    public static void EndCalibration<T>(this KeyboardSession<T> s)
        where T : IHasFuncBlock => s.EndCalibration();

    /// <summary>Performs a soft reset (firmware reboot) of the keyboard without clearing settings.</summary>
    public static void Reset<T>(this KeyboardSession<T> s)
        where T : IHasFuncBlock => s.Reset();

    // ── IHasHighPrecision ────────────────────────────────────────────────────
    // FD × 200 (0.005 mm/unit) models: A75 Ultra, A75 Master, X60 Future.
    // These are the only models that support read-back of stored key-point profiles.

    /// <summary>Reads the per-key actuation point profile from the keyboard, returning one depth in mm per key slot.</summary>
    public static float[] ReadActuationPoint<T>(this KeyboardSession<T> s)
        where T : IHasHighPrecision => s.ReadActuationPoint();

    /// <summary>Reads the per-key actuation point profile, keyed by <see cref="DDKey"/>.</summary>
    public static IReadOnlyDictionary<DDKey, float> ReadActuationPointByKey<T>(this KeyboardSession<T> s)
        where T : IHasHighPrecision => s.ToKeyDictionary(s.ReadActuationPoint());

    /// <summary>Reads the per-key downstroke point profile from the keyboard, returning one depth in mm per key slot.</summary>
    public static float[] ReadDownstrokePoint<T>(this KeyboardSession<T> s)
        where T : IHasHighPrecision => s.ReadDownstrokePoint();

    /// <summary>Reads the per-key downstroke point profile, keyed by <see cref="DDKey"/>.</summary>
    public static IReadOnlyDictionary<DDKey, float> ReadDownstrokePointByKey<T>(this KeyboardSession<T> s)
        where T : IHasHighPrecision => s.ToKeyDictionary(s.ReadDownstrokePoint());

    /// <summary>Reads the per-key upstroke point profile from the keyboard, returning one depth in mm per key slot.</summary>
    public static float[] ReadUpstrokePoint<T>(this KeyboardSession<T> s)
        where T : IHasHighPrecision => s.ReadUpstrokePoint();

    /// <summary>Reads the per-key upstroke point profile, keyed by <see cref="DDKey"/>.</summary>
    public static IReadOnlyDictionary<DDKey, float> ReadUpstrokePointByKey<T>(this KeyboardSession<T> s)
        where T : IHasHighPrecision => s.ToKeyDictionary(s.ReadUpstrokePoint());

    // ── IHasLogoLight ────────────────────────────────────────────────────────

    /// <summary>Activates a built-in firmware lighting animation on the logo light zone.</summary>
    public static void SetLogoLightPreset<T>(this KeyboardSession<T> s,
        LightPreset effect, [Range(0, 9)] byte brightness = 9, byte speed = 5)
        where T : IHasLogoLight => s.SetLogoLightPreset(effect, brightness, speed);

    /// <summary>Turns off the logo light zone.</summary>
    public static void SetLogoLightOff<T>(this KeyboardSession<T> s)
        where T : IHasLogoLight => s.SetLogoLightOff();

    /// <summary>Sets the single-colour tint used by the logo light preset effect.</summary>
    public static void SetLogoLightColor<T>(this KeyboardSession<T> s, byte r, byte g, byte b)
        where T : IHasLogoLight => s.SetLogoLightColor(r, g, b);

    /// <inheritdoc cref="SetLogoLightColor{T}(KeyboardSession{T}, byte, byte, byte)"/>
    public static void SetLogoLightColor<T>(this KeyboardSession<T> s, RgbColor color)
        where T : IHasLogoLight => s.SetLogoLightColor(color);

    // ── IHasSideLight ────────────────────────────────────────────────────────

    /// <summary>Activates a built-in firmware lighting animation on the side light zone.</summary>
    public static void SetSideLightPreset<T>(this KeyboardSession<T> s,
        LightPreset effect, [Range(0, 9)] byte brightness = 9, byte speed = 5)
        where T : IHasSideLight => s.SetSideLightPreset(effect, brightness, speed);

    /// <summary>Turns off the side light zone.</summary>
    public static void SetSideLightOff<T>(this KeyboardSession<T> s)
        where T : IHasSideLight => s.SetSideLightOff();

    /// <summary>Sets the single-colour tint used by the side light preset effect.</summary>
    public static void SetSideLightColor<T>(this KeyboardSession<T> s, byte r, byte g, byte b)
        where T : IHasSideLight => s.SetSideLightColor(r, g, b);

    /// <inheritdoc cref="SetSideLightColor{T}(KeyboardSession{T}, byte, byte, byte)"/>
    public static void SetSideLightColor<T>(this KeyboardSession<T> s, RgbColor color)
        where T : IHasSideLight => s.SetSideLightColor(color);
}
