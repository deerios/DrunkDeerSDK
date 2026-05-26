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
///     ActuationMm  = 2.0f,
///     RapidTrigger = true,
///     Theme        = new KeyboardTheme { R = 0, G = 80, B = 255, Brightness = 7 }
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
	/// <summary>Uniform actuation depth in mm applied to every key. Overridden per-key by <see cref="PerKeyActuationMm"/>.</summary>
	[JsonPropertyName("actuationMm")]
	public float? ActuationMm { get; set; }

	/// <summary>
	/// Per-key actuation depth overrides in mm, keyed by <see cref="DDKey"/> name (case-insensitive).
	/// Applied on top of <see cref="ActuationMm"/> if both are set, or on top of the current
	/// keyboard profile if only this map is set.
	/// </summary>
	[JsonPropertyName("perKeyActuationMm")]
	public Dictionary<string, float>? PerKeyActuationMm { get; set; }

	/// <summary>Uniform Rapid Trigger downstroke threshold in mm for every key.</summary>
	[JsonPropertyName("downstrokeMm")]
	public float? DownstrokeMm { get; set; }

	/// <summary>Per-key Rapid Trigger downstroke threshold overrides.</summary>
	[JsonPropertyName("perKeyDownstrokeMm")]
	public Dictionary<string, float>? PerKeyDownstrokeMm { get; set; }

	/// <summary>Uniform Rapid Trigger upstroke threshold in mm for every key.</summary>
	[JsonPropertyName("upstrokeMm")]
	public float? UpstrokeMm { get; set; }

	/// <summary>Per-key Rapid Trigger upstroke threshold overrides.</summary>
	[JsonPropertyName("perKeyUpstrokeMm")]
	public Dictionary<string, float>? PerKeyUpstrokeMm { get; set; }

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
		ActuationMm == null && PerKeyActuationMm == null &&
		DownstrokeMm == null && PerKeyDownstrokeMm == null &&
		UpstrokeMm == null && PerKeyUpstrokeMm == null &&
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

/// <summary>
/// RGB lighting configuration included in a <see cref="KeyboardProfile"/>.
/// </summary>
/// <remarks>
/// If <see cref="Keys"/> is null or empty the entire keyboard is set to the uniform
/// colour (<see cref="R"/>, <see cref="G"/>, <see cref="B"/>). If <see cref="Keys"/>
/// is non-empty, per-key colours are applied; any key not listed in the map keeps its
/// current colour unless a non-black uniform base is also set.
/// </remarks>
/// <example>
/// <code>
/// // Solid cyan at 70% brightness
/// var theme = new KeyboardTheme { R = 0, G = 255, B = 200, Brightness = 6 };
///
/// // Orange WASD on a dark-blue background
/// var theme = new KeyboardTheme
/// {
///     R = 0, G = 0, B = 40, Brightness = 9,
///     Keys = new Dictionary&lt;string, byte[]&gt;
///     {
///         ["W"] = [255, 140, 0],
///         ["A"] = [255, 140, 0],
///         ["S"] = [255, 140, 0],
///         ["D"] = [255, 140, 0],
///     }
/// };
/// </code>
/// </example>
public sealed class KeyboardTheme
{
	/// <summary>Firmware brightness level (0-9). Default is 9 (maximum).</summary>
	[JsonPropertyName("brightness")]
	public byte Brightness { get; set; } = 9;

	/// <summary>Uniform base colour - red channel (0-255).</summary>
	[JsonPropertyName("r")]
	public byte R { get; set; }

	/// <summary>Uniform base colour - green channel (0-255).</summary>
	[JsonPropertyName("g")]
	public byte G { get; set; }

	/// <summary>Uniform base colour - blue channel (0-255).</summary>
	[JsonPropertyName("b")]
	public byte B { get; set; }

	/// <summary>
	/// Per-key colour overrides. Key is a <see cref="DDKey"/> name (case-insensitive);
	/// value is a three-element <c>[R, G, B]</c> byte array. Keys absent from this map
	/// keep the uniform base colour (if set) or their previous colour.
	/// </summary>
	[JsonPropertyName("keys")]
	public Dictionary<string, byte[]>? Keys { get; set; }
}
