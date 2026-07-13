namespace DrunkDeer.Protocol;

/// <summary>
/// Fluent builder for <see cref="KeyboardTheme"/>.
/// </summary>
/// <example>
/// <code>
/// // Solid red
/// var theme = new KeyboardThemeBuilder().Base(255, 0, 0).Build();
///
/// // Orange WASD on dark-blue, Escape in white
/// var theme = new KeyboardThemeBuilder()
///     .Base(0, 0, 40)
///     .Brightness(9)
///     .Keys([DDKey.W, DDKey.A, DDKey.S, DDKey.D], 255, 140, 0)
///     .Key(DDKey.Escape, 255, 255, 255)
///     .Build();
/// </code>
/// </example>
public sealed class KeyboardThemeBuilder
{
	private RgbColor _base;
	private byte     _brightness = 9;
	private Dictionary<string, KeyColor>? _keys;

	/// <summary>Sets the uniform base colour applied to every key.</summary>
	public KeyboardThemeBuilder Base(byte r, byte g, byte b)
	{
		_base = new RgbColor(r, g, b);
		return this;
	}

	/// <summary>Sets the uniform base colour applied to every key.</summary>
	public KeyboardThemeBuilder Base(RgbColor color)
	{
		_base = color;
		return this;
	}

	/// <summary>Sets the firmware brightness level (0-9). Default is 9.</summary>
	public KeyboardThemeBuilder Brightness(byte level)
	{
		_brightness = level;
		return this;
	}

	/// <summary>Sets the colour for a single key.</summary>
	public KeyboardThemeBuilder Key(DDKey key, byte r, byte g, byte b)
	{
		(_keys ??= [])[key.ToString()] = new KeyColor { R = r, G = g, B = b };
		return this;
	}

	/// <summary>Sets the same colour for multiple keys.</summary>
	public KeyboardThemeBuilder Keys(IEnumerable<DDKey> keys, byte r, byte g, byte b)
	{
		var color = new KeyColor { R = r, G = g, B = b };
		_keys ??= [];
		foreach (var key in keys)
			_keys[key.ToString()] = color;
		return this;
	}

	/// <summary>Builds and returns the <see cref="KeyboardTheme"/>.</summary>
	public KeyboardTheme Build() => new()
	{
		BaseColor  = _base,
		Brightness = _brightness,
		Keys       = _keys,
	};
}
