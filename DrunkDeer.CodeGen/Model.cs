namespace DrunkDeer.Codegen;

/// <summary>Top-level protocol definition assembled from all YAML sources.</summary>
internal sealed class ProtocolDef
{
	public Dictionary<string, StructDef> Structs { get; } = [];
	public Dictionary<string, MessageDef> Messages { get; } = [];
	public Dictionary<string, ModelDef> Models { get; } = [];
	public Dictionary<string, ProfileBlockDef> ProfileBlocks { get; } = [];

	/// <summary>
	/// Flattened (VID, PID) discovery pairs from the discovery section of models.yaml,
	/// in declaration order. Each entry is a specific pair, not a cross-product.
	/// </summary>
	public List<DiscoveryPair> Discovery { get; } = [];

	/// <summary>
	/// Physical key geometry per model+variant, loaded from protocol/geometry/*.yaml.
	/// In declaration order across files.
	/// </summary>
	public List<GeometryDef> Geometries { get; } = [];
}

/// <summary>Physical key geometry for one model slug, split by layout variant.</summary>
internal sealed class GeometryDef
{
	public string Slug { get; set; } = "";
	public string BoardName { get; set; } = "";
	public List<GeometryVariant> Variants { get; set; } = [];
}

/// <summary>Geometry for one variant (e.g. "ansi") plus any variant aliases that share it.</summary>
internal sealed class GeometryVariant
{
	public string Variant { get; set; } = "";
	public List<string> Aliases { get; set; } = [];
	public List<GeometryKey> Keys { get; set; } = [];
}

/// <summary>One physical key: firmware slot, DDKey, legend, and KLE 1u placement.</summary>
internal sealed class GeometryKey
{
	public string Key { get; set; } = "";
	public int Slot { get; set; }
	public string Legend { get; set; } = "";
	public float X { get; set; }
	public float Y { get; set; }
	public float W { get; set; } = 1f;
	public float H { get; set; } = 1f;

	// Secondary rectangle for non-rectangular keys (ISO Enter). Null when unset.
	public float? X2 { get; set; }
	public float? Y2 { get; set; }
	public float? W2 { get; set; }
	public float? H2 { get; set; }

	public bool HasSecondary => X2.HasValue;
}

/// <summary>A single (VID, PID) pair used to find candidate DrunkDeer command interfaces.</summary>
internal sealed class DiscoveryPair
{
	public int Vid { get; set; }
	public int Pid { get; set; }
}

internal sealed class StructDef
{
	public string Name { get; set; } = "";
	public List<StructField> Fields { get; set; } = [];
	public int? ExplicitByteSize { get; set; }
	public List<BitFieldDef> BitFields { get; set; } = [];
}

/// <summary>
/// One logical field within a bit-field struct declaration.
/// Either a single-byte bit range (Byte + BitLo/BitHi) or a two-byte LE9 span (ByteLo + ByteHi).
/// </summary>
internal sealed class BitFieldDef
{
	public string Name { get; set; } = "";
	public bool IsReserved => Name.StartsWith('_');

	// Single-byte field: which byte and which bit range within it (inclusive).
	public int? SingleByte { get; set; }
	public int? BitLo { get; set; }
	public int? BitHi { get; set; }

	// Two-byte LE9 field: full lower byte + bit 0 of upper byte = 9-bit value.
	public int? ByteLo { get; set; }
	public int? ByteHi { get; set; }

	// decoded = raw + Bias; encoded raw = value - Bias (clamped to 0).
	public int Bias { get; set; } = 0;

	// Constant value written into this bit range on encode; ignored on decode.
	public byte? Const { get; set; }
}

internal sealed class StructField
{
	public string Name { get; set; } = "";
	public string Type { get; set; } = ""; // e.g. "u8", "bytes[4]"
}

internal sealed class MessageDef
{
	public string Name { get; set; } = "";
	public MessageDir Direction { get; set; }
	public List<byte> Header { get; set; } = [];
	public List<IField> Payload { get; set; } = [];
	public int PaddingTo { get; set; } = 64;

	public bool IsRequest => Direction == MessageDir.Request;
	public bool IsResponse => Direction == MessageDir.Response;

	/// <summary>Byte offset immediately after the header (start of payload).</summary>
	public int PayloadOffset => Header.Count;
}

internal enum MessageDir { Request, Response }

internal interface IField
{
	string Name { get; }
	bool IsReserved { get; }   // name starts with '_'
}

/// <summary>A scalar or fixed-size array field (u8, bytes[N], i8[N], u16le).</summary>
internal sealed class TypedField : IField
{
	public string Name { get; set; } = "";
	public string TypeSpec { get; set; } = "";  // "u8" | "i8" | "u16le" | "bytes[N]" | "i8[N]" | "u8[N]" | "u16le[N]"
	public bool IsReserved => Name.StartsWith('_');

	/// <summary>Size in bytes, -1 if unknown.</summary>
	public int ByteSize => ParseSize(TypeSpec);

	public static int ParseSize(string spec)
	{
		if (spec is "u8" or "i8") return 1;
		if (spec == "u16le") return 2;
		var bracket = spec.IndexOf('[');
		if (bracket < 0) return -1;
		if (!int.TryParse(spec[(bracket + 1)..^1], out int n)) return -1;
		return spec.StartsWith("u16le") ? n * 2 : n;
	}

	/// <summary>C# parameter type for this field (null if not a simple parameter).</summary>
	public string? CsParamType => TypeSpec switch
	{
		"u8" => "byte",
		"i8" => "sbyte",
		"u16le" => "ushort",
		_ when TypeSpec.StartsWith("bytes[") || TypeSpec.StartsWith("u8[") => "ReadOnlySpan<byte>",
		_ when TypeSpec.StartsWith("i8[") => "ReadOnlySpan<sbyte>",
		_ when TypeSpec.StartsWith("u16le[") => "ReadOnlySpan<ushort>",
		_ => null,
	};
}

/// <summary>A constant byte sequence embedded in the payload.</summary>
internal sealed class ConstantField : IField
{
	public string Name { get; set; } = "";
	public List<byte> Value { get; set; } = [];
	public bool IsReserved => Name.StartsWith('_');
	public int ByteSize => Value.Count;
}

/// <summary>A variable-length array of struct entries.</summary>
internal sealed class ArrayField : IField
{
	public string Name { get; set; } = "";
	public string ElementType { get; set; } = "";  // struct name
	public int MaxCount { get; set; }
	public byte? TerminatedBy { get; set; }
	public bool IsReserved => Name.StartsWith('_');
}

internal sealed class ModelDef
{
	public string Slug { get; set; } = "";
	public string Name { get; set; } = "";
	public List<string> Capabilities { get; set; } = [];
	public List<ModelIdentity> Identities { get; set; } = [];
	public Dictionary<string, object> FirmwareOverrides { get; set; } = [];
	/// <summary>
	/// Optional kun_precision block from the YAML. Carries "min_firmware" (byte) and
	/// "max_depth_mm" (float) for models that conditionally enter Kun precision mode.
	/// Null when the model has no conditional Kun upgrade path.
	/// </summary>
	public Dictionary<string, object>? KunPrecision { get; set; }
}

internal sealed class ModelIdentity
{
	public List<byte> Bytes { get; set; } = [];
	public string Variant { get; set; } = "";
}

/// <summary>
/// A mutable, byte-array-backed class block defined in profile_blocks.yaml.
/// Generates a sealed partial class with typed property accessors over RawBytes.
/// </summary>
internal sealed class ProfileBlockDef
{
	public string Name { get; set; } = "";
	public int ByteSize { get; set; }
	public List<ProfileBlockFieldDef> Fields { get; set; } = [];
}

/// <summary>
/// One field within a <see cref="ProfileBlockDef"/>.
/// Encoding: single bit (<see cref="Bit"/>), bit range (<see cref="BitLo"/>/<see cref="BitHi"/>),
/// or full byte (neither specified).
/// </summary>
internal sealed class ProfileBlockFieldDef
{
	public string Name { get; set; } = "";
	public int Byte { get; set; }

	// Single-bit shorthand; sets both BitLo and BitHi to the same value.
	public int? Bit { get; set; }

	// Explicit bit range (inclusive). Null = full byte (0..7).
	public int? BitLo { get; set; }
	public int? BitHi { get; set; }

	// "u8" (default), "bool", or "enum:TypeName"
	public string Type { get; set; } = "u8";

	// When true, bool semantics are inverted: byte == 0 means true.
	// Used for fields where the firmware stores 0 for "enabled" and non-zero for "disabled".
	public bool Inverted { get; set; } = false;

	// Resolved bit_lo (after normalising Bit shorthand)
	public int ResolvedBitLo => Bit ?? BitLo ?? 0;
	// Resolved bit_hi
	public int ResolvedBitHi => Bit ?? BitHi ?? 7;
	// True when the field covers all 8 bits of its byte.
	public bool IsFullByte => ResolvedBitLo == 0 && ResolvedBitHi == 7;
}
