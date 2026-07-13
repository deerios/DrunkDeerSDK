namespace DrunkDeer.Protocol;

/// <summary>
/// Per-key rapid-trigger and actuation configuration stored in the keyboard's key-trigger region.
/// Read back with <see cref="KeyboardSession.ReadKeyTriggers"/> and applied with
/// <see cref="KeyboardSession.WriteKeyTriggers"/> or
/// <see cref="KeyboardSession.SetKeyTrigger(int, KeyTriggerConfig, int)"/>.
/// </summary>
/// <remarks>
/// All depth fields (<see cref="Actuation"/>, <see cref="RtPress"/>, <see cref="RtRelease"/>)
/// are in raw firmware units. One unit = 0.01 mm (100 units per mm). Note: this is a finer
/// scale than the actuation-point commands (SetActuationPoint uses 0.1 mm units).
/// The default factory values (actuation 75 = 0.75 mm, rt_press/rt_release 25 = 0.25 mm)
/// reflect firmware defaults and are preserved when no per-key override has been written.
/// </remarks>
public readonly record struct KeyTriggerConfig
{
	/// <summary>Switch hardware type (0-15). Identifies the physical switch variant.</summary>
	public byte SwitchType { get; init; }

	/// <summary>Key mode (0-15). 1 = standard trigger mode.</summary>
	public byte KeyMode { get; init; }

	/// <summary>Priority (0-15). Used when multiple keys compete for the same slot.</summary>
	public byte Priority { get; init; }

	/// <summary>
	/// Actuation depth in firmware units (1-512). One unit = 0.01 mm.
	/// </summary>
	public int Actuation { get; init; }

	/// <summary>
	/// Rapid Trigger press sensitivity in firmware units (1-512). One unit = 0.01 mm.
	/// The key must travel at least this far downward before RT fires again.
	/// </summary>
	public int RtPress { get; init; }

	/// <summary>
	/// Rapid Trigger release sensitivity in firmware units (1-512). One unit = 0.01 mm.
	/// The key must travel at least this far upward before RT resets.
	/// </summary>
	public int RtRelease { get; init; }

	/// <summary>Press deadzone (0-127). Suppresses noise near the actuation point on press.</summary>
	public byte PressDeadzone { get; init; }

	/// <summary>Release deadzone (0-127). Suppresses noise near the actuation point on release.</summary>
	public byte ReleaseDeadzone { get; init; }

	/// <summary>Press precision mode (0-3).</summary>
	public byte PressPrecision { get; init; }

	/// <summary>Release precision mode (0-3).</summary>
	public byte ReleasePrecision { get; init; }

	/// <summary>Factory-default trigger configuration matching keyboard firmware defaults.</summary>
	public static readonly KeyTriggerConfig Default = new()
	{
		SwitchType       = 0,
		KeyMode          = 1,
		Priority         = 0,
		Actuation        = 75,
		RtPress          = 25,
		RtRelease        = 25,
		PressDeadzone    = 0,
		ReleaseDeadzone  = 0,
		PressPrecision   = 0,
		ReleasePrecision = 0,
	};

	// 1 unit = 0.01 mm, so raw = (int)Round(mm × 100).
	// Stored values are (raw − 1), clamped to [0, 511].
	private static int MmToRaw(float mm) =>
		Math.Clamp((int)Math.Round(mm * 100, MidpointRounding.AwayFromZero), 1, 512);

	/// <summary>
	/// Creates a <see cref="KeyTriggerConfig"/> from mm-space depths.
	/// All other fields use the firmware defaults from <see cref="Default"/>.
	/// </summary>
	/// <param name="actuationMm">Actuation depth in mm (0.01–5.12 mm).</param>
	/// <param name="rtPressMm">
	/// Rapid Trigger press sensitivity in mm. The key must travel at least this
	/// distance downward before RT fires again.
	/// </param>
	/// <param name="rtReleaseMm">
	/// Rapid Trigger release sensitivity in mm. The key must travel at least this
	/// distance upward before RT resets.
	/// </param>
	/// <example>
	/// <code>
	/// var config = KeyTriggerConfig.FromMm(actuationMm: 2.0f, rtPressMm: 0.2f, rtReleaseMm: 0.2f);
	/// session.SetKeyTrigger(DDKey.Space, config);
	/// </code>
	/// </example>
	public static KeyTriggerConfig FromMm(float actuationMm, float rtPressMm, float rtReleaseMm) =>
		Default with
		{
			Actuation = MmToRaw(actuationMm),
			RtPress   = MmToRaw(rtPressMm),
			RtRelease = MmToRaw(rtReleaseMm),
		};

	/// <summary>
	/// Returns a copy of this config with the depth fields replaced by mm-space values.
	/// All other fields (switch type, key mode, deadzone, precision) are preserved.
	/// </summary>
	/// <param name="actuationMm">Actuation depth in mm.</param>
	/// <param name="rtPressMm">Rapid Trigger press sensitivity in mm.</param>
	/// <param name="rtReleaseMm">Rapid Trigger release sensitivity in mm.</param>
	public KeyTriggerConfig WithMm(float actuationMm, float rtPressMm, float rtReleaseMm) =>
		this with
		{
			Actuation = MmToRaw(actuationMm),
			RtPress   = MmToRaw(rtPressMm),
			RtRelease = MmToRaw(rtReleaseMm),
		};

	// Wire-format is described in protocol/structs.yaml (KeyTriggerEntry bit_fields).

	internal static KeyTriggerConfig Decode(ReadOnlySpan<byte> data)
	{
		var e = KeyTriggerEntry.Read(data);
		return new KeyTriggerConfig
		{
			SwitchType       = e.SwitchType,
			KeyMode          = e.KeyMode,
			Priority         = e.Priority,
			Actuation        = e.Actuation,
			RtPress          = e.RtPress,
			RtRelease        = e.RtRelease,
			PressDeadzone    = e.PressDeadzone,
			ReleaseDeadzone  = e.ReleaseDeadzone,
			PressPrecision   = e.PressPrecision,
			ReleasePrecision = e.ReleasePrecision,
		};
	}

	internal static void Encode(in KeyTriggerConfig c, Span<byte> data) =>
		new KeyTriggerEntry(
			c.SwitchType, c.KeyMode, c.Priority,
			c.Actuation, c.ReleasePrecision, c.PressPrecision,
			c.RtPress, c.PressDeadzone,
			c.RtRelease, c.ReleaseDeadzone
		).Write(data);
}
