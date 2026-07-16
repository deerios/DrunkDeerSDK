using System.Text.Json;
using System.Text.Json.Serialization;

namespace DrunkDeer.Protocol;

/// <summary>
/// A portable, serialisable snapshot of keyboard configuration settings.
/// Any property left <see langword="null"/> is ignored when the profile is applied;
/// only non-null fields are written to the keyboard.
/// </summary>
/// <remarks>
/// <para>
/// When a profile contains only <see cref="Theme"/> data and no actuation, depth, or
/// feature-flag properties, <see cref="IsThemeOnly"/> returns <see langword="true"/> and
/// <see cref="KeyboardSession.ApplyProfile"/> will only update RGB lighting.
/// </para>
/// <para>
/// Use <see cref="FromJson"/> or <see cref="FromFile"/> to deserialise, and
/// <see cref="ToJson"/> to serialise.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// // Build a profile programmatically and apply it
/// var profile = new KeyboardProfile
/// {
///     RapidTrigger = true,
///     Actuation    = new KeyDepthProfileBuilder().Default(2.0f).Build(),
///     Theme        = new KeyboardThemeBuilder().Base(0, 80, 255).Brightness(7).Build()
/// };
/// session.ApplyProfile(profile);
///
/// // Load from JSON file (theme-only: only RGB is applied)
/// var theme = KeyboardProfile.FromFile("gaming_theme.json");
/// session.ApplyProfile(theme);
/// </code>
/// </example>
public sealed class KeyboardProfile
{
	/// <summary>
	/// Actuation point depths. When non-null, <see cref="KeyDepthProfile.Default"/> is applied
	/// to every key and any entries in <see cref="KeyDepthProfile.Keys"/> override individual keys.
	/// Null means actuation is left unchanged on apply.
	/// </summary>
	[JsonPropertyName("actuation")]
	public KeyDepthProfile? Actuation { get; set; }

	/// <summary>
	/// Rapid Trigger downstroke thresholds. Same null-means-unchanged semantics as <see cref="Actuation"/>.
	/// </summary>
	[JsonPropertyName("downstroke")]
	public KeyDepthProfile? Downstroke { get; set; }

	/// <summary>
	/// Rapid Trigger upstroke thresholds. Same null-means-unchanged semantics as <see cref="Actuation"/>.
	/// </summary>
	[JsonPropertyName("upstroke")]
	public KeyDepthProfile? Upstroke { get; set; }

	/// <summary>Enables (<see langword="true"/>) or disables (<see langword="false"/>) Rapid Trigger globally.</summary>
	[JsonPropertyName("rapidTrigger")]
	public bool? RapidTrigger { get; set; }

	/// <summary>
	/// Enables Rapid Trigger Auto Match - the release threshold automatically mirrors the press threshold.
	/// Only meaningful when <see cref="RapidTrigger"/> is <see langword="true"/>.
	/// </summary>
	[JsonPropertyName("rapidTriggerAutoMatch")]
	public bool? RapidTriggerAutoMatch { get; set; }

	/// <summary>Enables (<see langword="true"/>) or disables (<see langword="false"/>) Turbo mode globally.</summary>
	[JsonPropertyName("turboMode")]
	public bool? TurboMode { get; set; }

	/// <summary>
	/// RGB lighting configuration. When this is the only populated section and all actuation /
	/// feature-flag properties are <see langword="null"/>, only lighting is updated on apply.
	/// </summary>
	[JsonPropertyName("theme")]
	public KeyboardTheme? Theme { get; set; }

	/// <summary>
	/// <see langword="true"/> when this profile carries only <see cref="Theme"/> data and no
	/// actuation or feature-flag settings. <see cref="KeyboardSession.ApplyProfile"/> will
	/// skip all non-lighting writes when this is <see langword="true"/>.
	/// </summary>
	[JsonIgnore]
	public bool IsThemeOnly =>
		Actuation == null && Downstroke == null && Upstroke == null &&
		RapidTrigger == null && RapidTriggerAutoMatch == null && TurboMode == null;

	private static readonly JsonSerializerOptions _options = new()
	{
		WriteIndented              = true,
		DefaultIgnoreCondition     = JsonIgnoreCondition.WhenWritingNull,
		PropertyNameCaseInsensitive = true,
	};

	/// <summary>Deserialises a <see cref="KeyboardProfile"/> from a JSON string.</summary>
	/// <exception cref="ArgumentException">Thrown when the JSON is null or invalid.</exception>
	public static KeyboardProfile FromJson(string json) =>
		JsonSerializer.Deserialize<KeyboardProfile>(json, _options)
			?? throw new ArgumentException("JSON produced a null profile.", nameof(json));

	/// <summary>Deserialises a <see cref="KeyboardProfile"/> from a JSON file.</summary>
	/// <exception cref="FileNotFoundException">Thrown when the file does not exist.</exception>
	public static KeyboardProfile FromFile(string path) =>
		FromJson(File.ReadAllText(path));

	/// <summary>Serialises this profile to a JSON string.</summary>
	public string ToJson() => JsonSerializer.Serialize(this, _options);

	/// <summary>Writes this profile to a JSON file, creating or overwriting it.</summary>
	public void SaveToFile(string path) => File.WriteAllText(path, ToJson());
}

/// <summary>An RGB colour value.</summary>
public readonly struct RgbColor
{
	/// <summary>Red channel (0-255).</summary>
	[JsonPropertyName("r")]
	public byte R { get; init; }

	/// <summary>Green channel (0-255).</summary>
	[JsonPropertyName("g")]
	public byte G { get; init; }

	/// <summary>Blue channel (0-255).</summary>
	[JsonPropertyName("b")]
	public byte B { get; init; }

	/// <param name="r">Red channel (0-255).</param>
	/// <param name="g">Green channel (0-255).</param>
	/// <param name="b">Blue channel (0-255).</param>
	[JsonConstructor]
	public RgbColor(byte r, byte g, byte b) => (R, G, B) = (r, g, b);

	public void Deconstruct(out byte r, out byte g, out byte b) => (r, g, b) = (R, G, B);

	/// <summary>
	/// Scales each channel by <paramref name="level"/>/9 (clamped to 0-9). The firmware has no
	/// per-key brightness — a single brightness byte applies to the whole RGB frame — so this is
	/// how a color can still look dimmer than others in the same frame: darken its RGB values in
	/// software before sending. Used for <see cref="KeyboardTheme.BaseBrightness"/>.
	/// </summary>
	public RgbColor Scale(byte level)
	{
		byte clamped = Math.Min(level, (byte)9);
		return new RgbColor((byte)(R * clamped / 9), (byte)(G * clamped / 9), (byte)(B * clamped / 9));
	}
}

/// <summary>Per-key colour override.</summary>
public sealed class KeyColor
{
	/// <summary>Red channel (0-255).</summary>
	[JsonPropertyName("r")]
	public byte R { get; set; }

	/// <summary>Green channel (0-255).</summary>
	[JsonPropertyName("g")]
	public byte G { get; set; }

	/// <summary>Blue channel (0-255).</summary>
	[JsonPropertyName("b")]
	public byte B { get; set; }
}

/// <summary>
/// RGB lighting configuration included in a <see cref="KeyboardProfile"/>.
/// Build instances with <see cref="KeyboardThemeBuilder"/>.
/// </summary>
/// <remarks>
/// If <see cref="Keys"/> is null or empty the entire keyboard is set to <see cref="BaseColor"/>.
/// If <see cref="Keys"/> is non-empty, per-key colours are applied on top; any key not listed
/// keeps the base colour. <see cref="Brightness"/> is a single firmware value applied to the
/// whole keyboard - the wire format has no per-key brightness, so it cannot be overridden
/// per key.
/// </remarks>
/// <example>
/// <code>
/// // Solid cyan at 70% brightness
/// var theme = new KeyboardThemeBuilder().Base(0, 255, 200).Brightness(6).Build();
///
/// // Orange WASD on a dark-blue background
/// var theme = new KeyboardThemeBuilder()
///     .Base(0, 0, 40)
///     .Brightness(9)
///     .Keys([DDKey.W, DDKey.A, DDKey.S, DDKey.D], 255, 140, 0)
///     .Build();
/// </code>
/// </example>
public sealed class KeyboardTheme
{
	/// <summary>Firmware brightness level (0-9). Default is 9 (maximum).</summary>
	[JsonPropertyName("brightness")]
	public byte Brightness { get; set; } = 9;

	/// <summary>Uniform base colour applied to every key before per-key overrides.</summary>
	[JsonPropertyName("baseColor")]
	public RgbColor BaseColor { get; set; }

	/// <summary>
	/// Optional brightness scale (0-9) applied only to <see cref="BaseColor"/>, independent of
	/// <see cref="Brightness"/> (the single firmware brightness byte sent for the whole frame).
	/// Since the firmware has no per-key brightness, this dims <see cref="BaseColor"/>'s RGB
	/// values in software (see <see cref="RgbColor.Scale"/>) before sending, so a background can
	/// look dimmer than per-key highlights that share the same firmware brightness. Null leaves
	/// <see cref="BaseColor"/> unscaled.
	/// </summary>
	[JsonPropertyName("baseBrightness")]
	public byte? BaseBrightness { get; set; }

	/// <summary>
	/// Per-key colour overrides. Key is a <see cref="DDKey"/> name (case-insensitive).
	/// Keys absent from this map keep <see cref="BaseColor"/>.
	/// </summary>
	[JsonPropertyName("keys")]
	public Dictionary<string, KeyColor>? Keys { get; set; }
}
