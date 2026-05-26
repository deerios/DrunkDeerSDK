using System.Text.Json.Serialization;

namespace DrunkDeer.Protocol;

/// <summary>
/// A per-key depth configuration: a uniform default with optional per-key overrides.
/// When used inside <see cref="KeyboardProfile"/> and serialised to JSON, key names match
/// <see cref="DDKey"/> member names (case-insensitive). Build instances with
/// <see cref="KeyDepthProfileBuilder"/>.
/// </summary>
/// <example>
/// <code>
/// // WASD at 0.2 mm, everything else at 2.0 mm
/// var profile = new KeyDepthProfileBuilder()
///     .Default(2.0f)
///     .Keys([DDKey.W, DDKey.A, DDKey.S, DDKey.D], 0.2f)
///     .Build();
///
/// session.SetActuationPoints(profile);
/// session.SetDownstrokePoints(profile);
/// </code>
/// </example>
public sealed class KeyDepthProfile
{
	/// <summary>Depth in mm applied to every key not listed in <see cref="Keys"/>.</summary>
	[JsonPropertyName("default")]
	public float Default { get; init; }

	/// <summary>
	/// Per-key depth overrides in mm. Key is a <see cref="DDKey"/> name (case-insensitive).
	/// Keys absent from this map use <see cref="Default"/>.
	/// </summary>
	[JsonPropertyName("keys")]
	public Dictionary<string, float>? Keys { get; init; }
}

/// <summary>Fluent builder for <see cref="KeyDepthProfile"/>.</summary>
public sealed class KeyDepthProfileBuilder
{
	private float _default;
	private Dictionary<string, float>? _keys;

	/// <summary>Sets the depth applied to all keys not individually overridden.</summary>
	public KeyDepthProfileBuilder Default(float depthMm)
	{
		_default = depthMm;
		return this;
	}

	/// <summary>Sets the depth for a single key.</summary>
	public KeyDepthProfileBuilder Key(DDKey key, float depthMm)
	{
		(_keys ??= [])[key.ToString()] = depthMm;
		return this;
	}

	/// <summary>Sets the same depth for multiple keys.</summary>
	public KeyDepthProfileBuilder Keys(IEnumerable<DDKey> keys, float depthMm)
	{
		_keys ??= [];
		foreach (var key in keys)
			_keys[key.ToString()] = depthMm;
		return this;
	}

	/// <summary>Builds and returns the <see cref="KeyDepthProfile"/>.</summary>
	public KeyDepthProfile Build() => new()
	{
		Default = _default,
		Keys    = _keys,
	};
}
