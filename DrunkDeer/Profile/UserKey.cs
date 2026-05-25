namespace DrunkDeer.Protocol;

/// <summary>
/// Bitmask for HID modifier keys. Combine flags with bitwise OR to build the
/// <c>modifiers</c> argument of <see cref="UserKey.FromHid"/>.
/// </summary>
[Flags]
public enum KeyModifiers : byte
{
	/// <summary>No modifier keys held.</summary>
	None = 0,
	/// <summary>Left Control key.</summary>
	LeftCtrl = 1 << 0,
	/// <summary>Left Shift key.</summary>
	LeftShift = 1 << 1,
	/// <summary>Left Alt key.</summary>
	LeftAlt = 1 << 2,
	/// <summary>Left Windows / Super key.</summary>
	LeftWin = 1 << 3,
	/// <summary>Right Control key.</summary>
	RightCtrl = 1 << 4,
	/// <summary>Right Shift key.</summary>
	RightShift = 1 << 5,
	/// <summary>Right Alt / AltGr key.</summary>
	RightAlt = 1 << 6,
	/// <summary>Right Windows / Super key.</summary>
	RightWin = 1 << 7,
}

/// <summary>
/// Bitmask for mouse buttons. Combine flags with bitwise OR to build the
/// <c>buttons</c> argument of <see cref="UserKey.FromMouseButton"/>.
/// </summary>
[Flags]
public enum MouseButtons : byte
{
	/// <summary>No buttons.</summary>
	None = 0,
	/// <summary>Primary (left) mouse button.</summary>
	Left = 1 << 0,
	/// <summary>Secondary (right) mouse button.</summary>
	Right = 1 << 1,
	/// <summary>Middle (scroll-wheel click) button.</summary>
	Middle = 1 << 2,
	/// <summary>Forward side button (button 4).</summary>
	Forward = 1 << 3,
	/// <summary>Back side button (button 5).</summary>
	Back = 1 << 4,
}

/// <summary>
/// Key assignment type codes for the three-byte entries in the user key matrix.
/// Pass as the <see cref="UserKey.Type"/> field.
/// </summary>
public static class UserKeyType
{
	/// <summary>No assignment - key is disabled.</summary>
	public const byte None = 0x00;

	/// <summary>
	/// Standard HID keyboard key.
	/// <c>Param1</c> = modifier bitmask (bit 0 = Left Ctrl, 1 = Left Shift, 2 = Left Alt,
	/// 3 = Left Win, 4 = Right Ctrl, 5 = Right Shift, 6 = Right Alt, 7 = Right Win).
	/// <c>Param2</c> = HID keyboard usage code.
	/// </summary>
	public const byte Keyboard = 0x10;

	/// <summary>
	/// Mouse button.
	/// <c>Param1</c> = button bitmask (1 = Left, 2 = Right, 4 = Middle, 8 = Forward, 16 = Back).
	/// </summary>
	public const byte MouseButton = 0x20;

	/// <summary>
	/// Mouse scroll wheel.
	/// <c>Param2</c> = scroll delta: 1 = up one tick, 255 = down one tick.
	/// </summary>
	public const byte MouseScroll = 0x21;

	/// <summary>
	/// Multimedia / consumer key.
	/// <c>Param1</c> = HID consumer usage code. <c>Param2</c> = usage-page prefix byte.
	/// </summary>
	public const byte Multimedia = 0x30;

	/// <summary>
	/// System key.
	/// <c>Param1</c>: <see cref="SystemControl"/> flags bitmask.
	/// </summary>
	public const byte System = 0x40;

	/// <summary>
	/// Macro playback.
	/// <c>Param1</c> = zero-based macro slot index.
	/// </summary>
	public const byte Macro = 0x70;

	/// <summary>Layer / special function (Fn, layer switch). Exact encoding is firmware-specific.</summary>
	public const byte Special = 0xF0;

	/// <summary>
	/// Dynamic Keystroke slot reference.
	/// <c>Param1</c> = zero-based DKS slot index (0-31).
	/// </summary>
	public const byte DynamicKeystroke = 0x90;

	/// <summary>
	/// Toggle slot reference.
	/// <c>Param1</c> = zero-based Toggle slot index (0-31).
	/// </summary>
	public const byte Toggle = 0x91;

	/// <summary>
	/// Multi-Tap slot reference.
	/// <c>Param1</c> = zero-based Multi-Tap slot index (0-31).
	/// </summary>
	public const byte MultiTap = 0x92;
}

/// <summary>
/// System-control action for <see cref="UserKey.FromSystem"/>.
/// Combine flags with bitwise OR to trigger multiple actions simultaneously.
/// </summary>
[Flags]
public enum SystemControl : byte
{
	/// <summary>Power button.</summary>
	Power = 1,
	/// <summary>Sleep.</summary>
	Sleep = 2,
	/// <summary>Wake from sleep.</summary>
	Wake = 4,
}

/// <summary>
/// A three-byte key assignment stored in the keyboard's user key matrix.
/// Describes what a key reports when pressed within a given layer.
/// </summary>
/// <remarks>
/// For standard keyboard keys, use <see cref="FromHid"/>. For other types, construct
/// directly and set <see cref="Type"/> to one of the <see cref="UserKeyType"/> constants.
/// </remarks>
public readonly record struct UserKey
{
	/// <summary>Assignment type. See <see cref="UserKeyType"/> for well-known values.</summary>
	public byte Type { get; init; }

	/// <summary>
	/// First parameter byte. Meaning depends on <see cref="Type"/>:
	/// for <see cref="UserKeyType.Keyboard"/> this is the modifier bitmask.
	/// </summary>
	public byte Param1 { get; init; }

	/// <summary>
	/// Second parameter byte. Meaning depends on <see cref="Type"/>:
	/// for <see cref="UserKeyType.Keyboard"/> this is the HID usage code.
	/// </summary>
	public byte Param2 { get; init; }

	/// <summary>A disabled (unassigned) key slot.</summary>
	public static readonly UserKey Disabled = new() { Type = UserKeyType.None };

	/// <summary>Creates a standard HID keyboard key assignment.</summary>
	/// <param name="usageCode">
	/// HID keyboard usage code (e.g. 0x04 = 'a', 0x28 = Enter, 0x29 = Escape).
	/// </param>
	/// <param name="modifiers">Modifier keys to hold (default: none).</param>
	public static UserKey FromHid(byte usageCode, KeyModifiers modifiers = KeyModifiers.None) =>
		new() { Type = UserKeyType.Keyboard, Param1 = (byte)modifiers, Param2 = usageCode };

	/// <summary>
	/// Creates a mouse button assignment.
	/// </summary>
	/// <param name="buttons">One or more mouse buttons (combine with bitwise OR).</param>
	/// <example><code>UserKey.FromMouseButton(MouseButtons.Left | MouseButtons.Right)</code></example>
	public static UserKey FromMouseButton(MouseButtons buttons) =>
		new() { Type = UserKeyType.MouseButton, Param1 = (byte)buttons };

	/// <summary>
	/// Creates a mouse scroll-wheel assignment.
	/// </summary>
	/// <param name="ticks">
	/// Scroll amount: positive = scroll up, negative = scroll down.
	/// The firmware encodes this as an unsigned byte (1 = up one tick, 255 = down one tick).
	/// Values are clamped to +-127 ticks.
	/// </param>
	public static UserKey FromMouseScroll(int ticks)
	{
		byte raw = ticks >= 0
			? (byte)Math.Min(ticks, 127)
			: (byte)(256 + Math.Max(ticks, -127));
		return new() { Type = UserKeyType.MouseScroll, Param2 = raw };
	}

	/// <summary>
	/// Creates a multimedia / consumer-control key assignment.
	/// </summary>
	/// <param name="usageCode">HID consumer usage code (e.g. 0xE9 = Volume Up, 0xB5 = Next Track).</param>
	/// <param name="usagePage">
	/// Usage-page prefix byte required by some consumer controls. 0 for standard consumer page.
	/// </param>
	public static UserKey FromMultimedia(byte usageCode, byte usagePage = 0) =>
		new() { Type = UserKeyType.Multimedia, Param1 = usageCode, Param2 = usagePage };

	/// <summary>
	/// Creates a system-control key assignment (power, sleep, wake).
	/// </summary>
	/// <param name="control">One or more <see cref="SystemControl"/> flags to trigger.</param>
	public static UserKey FromSystem(SystemControl control) =>
		new() { Type = UserKeyType.System, Param1 = (byte)control };

	/// <summary>
	/// Creates a reference to a macro slot. Assign a key to a macro by setting its
	/// <see cref="UserKey"/> to this value; the firmware will play back the macro on press.
	/// </summary>
	/// <param name="slotIndex">Zero-based macro slot index.</param>
	public static UserKey FromMacro(byte slotIndex) =>
		new() { Type = UserKeyType.Macro, Param1 = slotIndex };

	/// <summary>
	/// Creates a reference to a Dynamic Keystroke slot.
	/// The firmware will use the slot's depth-zone configuration when this key is pressed.
	/// </summary>
	/// <param name="slotIndex">Zero-based Dynamic Keystroke slot index (0-31).</param>
	public static UserKey FromDynamicKeystroke(byte slotIndex) =>
		new() { Type = UserKeyType.DynamicKeystroke, Param1 = slotIndex };

	/// <summary>
	/// Creates a reference to a Toggle slot.
	/// The key will toggle the assigned action on or off with each press.
	/// </summary>
	/// <param name="slotIndex">Zero-based Toggle slot index (0-31).</param>
	public static UserKey FromToggle(byte slotIndex) =>
		new() { Type = UserKeyType.Toggle, Param1 = slotIndex };

	/// <summary>
	/// Creates a reference to a Multi-Tap slot.
	/// The key fires a quick-tap action on a light press and a hold action when held.
	/// </summary>
	/// <param name="slotIndex">Zero-based Multi-Tap slot index (0-31).</param>
	public static UserKey FromMultiTap(byte slotIndex) =>
		new() { Type = UserKeyType.MultiTap, Param1 = slotIndex };
}
