namespace DrunkDeer.Protocol;

/// <summary>
/// A complete snapshot of all per-profile data stored on the keyboard.
/// Any property left <see langword="null"/> is skipped when the data is pushed via
/// <see cref="KeyboardSession.PushFullProfile"/>.
/// </summary>
/// <remarks>
/// Obtain a populated instance via <see cref="KeyboardSession.PullFullProfile"/>,
/// modify individual properties, then write back with
/// <see cref="KeyboardSession.PushFullProfile"/>.
/// </remarks>
public sealed class FullProfileData
{
	/// <summary>
	/// The 64-byte function configuration block (report rate, debounce, lighting preset,
	/// key-combination locks, etc.). Use <see cref="KeyboardFuncBlock"/> for typed access.
	/// </summary>
	public KeyboardFuncBlock? FuncBlock { get; set; }

	/// <summary>
	/// Per-key rapid-trigger and actuation configurations.
	/// Must be exactly 128 entries when non-<see langword="null"/>.
	/// </summary>
	public KeyTriggerConfig[]? KeyTriggers { get; set; }

	/// <summary>
	/// Key assignment map for each of the four layers: base (0), Fn1 (1), Fn2 (2), Fn3 (3).
	/// Each non-<see langword="null"/> element must contain exactly 128 <see cref="UserKey"/> values.
	/// </summary>
	public UserKey[]?[]? KeyMapLayers { get; set; }

	/// <summary>
	/// Dynamic Keystroke slot configurations.
	/// Must be exactly 32 entries when non-<see langword="null"/>.
	/// </summary>
	public DynamicKeystrokeEntry[]? DynamicKeystrokeEntries { get; set; }

	/// <summary>
	/// Multi-Tap slot configurations.
	/// Must be exactly 32 entries when non-<see langword="null"/>.
	/// </summary>
	public MultiTapEntry[]? MultiTapEntries { get; set; }

	/// <summary>
	/// Toggle key slot configurations (one <see cref="UserKey"/> per slot).
	/// Must be exactly 32 entries when non-<see langword="null"/>.
	/// </summary>
	public UserKey[]? ToggleKeyEntries { get; set; }

	/// <summary>
	/// Macro slot data (32 slots). Each element is an array of <see cref="MacroAction"/>
	/// steps; <see langword="null"/> or empty arrays represent unused slots.
	/// Must be exactly 32 elements when non-<see langword="null"/>.
	/// </summary>
	public MacroAction[]?[]? MacroSlots { get; set; }
}
