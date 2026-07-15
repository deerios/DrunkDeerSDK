using Scriban;
using Scriban.Runtime;

namespace DrunkDeer.Codegen;

internal static class Generator
{
	public static void Run(string protocolDir, string templatesDir, string outputDir)
	{
		var def = YamlLoader.Load(protocolDir);
		Directory.CreateDirectory(outputDir);

		RenderMessages(def, templatesDir, outputDir);
		RenderModels(def, templatesDir, outputDir);
		RenderStructs(def, templatesDir, outputDir);
		RenderProfileBlocks(def, templatesDir, outputDir);
		RenderGeometry(def, templatesDir, outputDir);
	}

	private static void RenderGeometry(ProtocolDef def, string templatesDir, string outputDir)
	{
		// Flatten every model's variants into one list of arrays + switch arms.
		var variants = def.Geometries
			.SelectMany(g => g.Variants.Select(v => BuildGeometryVariantContext(g, v)))
			.ToList();

		Render(
			Path.Combine(templatesDir, "Geometry.sbn"),
			Path.Combine(outputDir, "KeyGeometry.g.cs"),
			ctx => ctx["variants"] = variants);
	}

	private static ScriptObject BuildGeometryVariantContext(GeometryDef g, GeometryVariant v)
	{
		string slugConst = "ModelSlugs." + ToPascal(g.Slug);
		string arrayName = ToPascal(g.Slug) + ToPascal(v.Variant);

		// One case label per variant name (primary + aliases): (ModelSlugs.X, "ansi") or (...)
		var labels = new[] { v.Variant }.Concat(v.Aliases)
			.Select(name => $"({slugConst}, \"{name}\")");
		string matchArm = string.Join(" or ", labels);

		var keys = v.Keys.Select(k =>
		{
			string secondary = k.HasSecondary
				? $", new KeyRect({F(k.X2!.Value)}, {F(k.Y2!.Value)}, {F(k.W2!.Value)}, {F(k.H2!.Value)})"
				: "";
			string ctor =
				$"new(DDKey.{k.Key}, {k.Slot}, {Quote(k.Legend)}, " +
				$"{F(k.X)}, {F(k.Y)}, {F(k.W)}, {F(k.H)}{secondary})";
			return new ScriptObject { ["ctor"] = ctor };
		}).ToList();

		return new ScriptObject
		{
			["array_name"] = arrayName,
			["match_arm"]  = matchArm,
			["board_name"] = g.BoardName,
			["keys"]       = keys,
		};
	}

	private static string F(float v) =>
		v.ToString(System.Globalization.CultureInfo.InvariantCulture) + "f";

	private static string Quote(string s) =>
		"\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

	private static void RenderMessages(ProtocolDef def, string templatesDir, string outputDir)
	{
		var msgs = def.Messages.Values
			.Select(m => BuildMessageContext(m, def))
			.ToList();

		Render(
			Path.Combine(templatesDir, "Messages.sbn"),
			Path.Combine(outputDir, "Messages.g.cs"),
			ctx => ctx["messages"] = msgs);
	}

	private static ScriptObject BuildMessageContext(MessageDef msg, ProtocolDef def)
	{
		var ctx = new ScriptObject
		{
			["name"]       = msg.Name,
			["direction"]  = msg.Direction.ToString().ToLowerInvariant(),
			["is_request"] = msg.IsRequest,
			["is_response"] = msg.IsResponse,
			["header_hex"] = FormatHeaderHex(msg.Header),
			["header_len"] = msg.Header.Count,
			["match_expr"] = BuildMatchExpr(msg.Header),
		};

		if (msg.IsRequest)
		{
			var (paramList, buildStmts) = BuildRequestCode(msg, def);
			ctx["param_list"]  = paramList;
			ctx["build_stmts"] = buildStmts;
		}

		if (msg.IsResponse)
			ctx["accessors"] = BuildAccessors(msg);

		return ctx;
	}

	private static (string ParamList, List<string> BuildStmts) BuildRequestCode(MessageDef msg, ProtocolDef def)
	{
		var @params = new List<string>();
		var stmts = new List<string>();

		// Write header into buffer
		for (int i = 0; i < msg.Header.Count; i++)
			stmts.Add($"buf[{i}] = 0x{msg.Header[i]:X2};");

		int offset = msg.PayloadOffset;
		bool afterDynArray = false;   // fields after a variable-length array have no static offset

		foreach (var field in msg.Payload)
		{
			switch (field)
			{
				case ConstantField cf:
					if (afterDynArray)
					{
						// Static offset is unknown after a variable-length array; caller must place this.
						var hex = string.Join(", ", cf.Value.Select(b => $"0x{b:X2}"));
						stmts.Add($"// [{cf.Name}] constant [{hex}] - write immediately after the variable-length region.");
						break;
					}
					foreach (var b in cf.Value)
						stmts.Add($"buf[{offset++}] = 0x{b:X2};");
					break;

				case TypedField tf:
					int size = tf.ByteSize;
					if (afterDynArray)
					{
						stmts.Add($"// [{tf.Name}] field after variable-length region - set at runtime.");
						break;
					}
					if (tf.IsReserved || size <= 0)
					{
						if (size > 0) offset += size;
						break;
					}
					var ptype = tf.CsParamType ?? "byte";
					@params.Add($"{ptype} {ToCamel(tf.Name)}");
					stmts.AddRange(EmitWrite(ToCamel(tf.Name), tf.TypeSpec, offset, size));
					offset += size;
					break;

				case ArrayField af:
					// Array data must be serialized by the transport layer; this method
					// returns the base buffer with all fixed fields populated.
					// Element type: {af.ElementType}, max {af.MaxCount} entries.
					stmts.Add($"// [{af.Name}] Variable-length array - serialize {af.ElementType} entries after offset {offset}.");
					if (af.TerminatedBy.HasValue)
						stmts.Add($"// Write sentinel 0x{af.TerminatedBy:X2} immediately after the last entry.");
					afterDynArray = true;
					break;
			}
		}

		return (string.Join(", ", @params), stmts);
	}

	private static List<string> BuildAccessors(MessageDef msg)
	{
		var accessors = new List<string>();
		int offset = msg.PayloadOffset;

		foreach (var field in msg.Payload)
		{
			switch (field)
			{
				case ConstantField cf:
					offset += cf.ByteSize;
					break;

				case TypedField tf:
					int size = tf.ByteSize;
					if (size <= 0) break;
					if (!tf.IsReserved)
						accessors.AddRange(EmitAccessor(tf.Name, tf.TypeSpec, offset, size));
					offset += size;
					break;

				case ArrayField:
					// Array data accessors are not generated; transport handles them.
					break;
			}
		}

		return accessors;
	}

	private static IEnumerable<string> EmitWrite(string paramName, string typeSpec, int offset, int size)
	{
		if (typeSpec == "u16le")
			return [$"BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan({offset}), {paramName});"];
		if (size == 1)
			return [$"buf[{offset}] = {(typeSpec == "i8" ? $"(byte){paramName}" : paramName)};"];
		return [$"{paramName}.CopyTo(buf.AsSpan({offset}));"];
	}

	private static IEnumerable<string> EmitAccessor(string fieldName, string typeSpec, int offset, int size)
	{
		var methodName = "Get" + ToPascal(fieldName);
		if (typeSpec == "u8")
			return [$"public static byte {methodName}(ReadOnlySpan<byte> buf) => buf[{offset}];"];
		if (typeSpec == "i8")
			return [$"public static sbyte {methodName}(ReadOnlySpan<byte> buf) => (sbyte)buf[{offset}];"];
		if (typeSpec == "u16le")
			return [$"public static ushort {methodName}(ReadOnlySpan<byte> buf) => BinaryPrimitives.ReadUInt16LittleEndian(buf.Slice({offset}));"];
		return [$"public static ReadOnlySpan<byte> {methodName}(ReadOnlySpan<byte> buf) => buf.Slice({offset}, {size});"];
	}

	private static string BuildMatchExpr(List<byte> header)
	{
		if (header.Count == 0) return "true";
		return string.Join(" && ", header.Select((b, i) => $"buf[{i}] == 0x{b:X2}"));
	}

	private static void RenderModels(ProtocolDef def, string templatesDir, string outputDir)
	{
		var models = def.Models.Values
			.Select(BuildModelContext)
			.ToList();

		var discovery = def.Discovery
			.Select(p => new ScriptObject { ["vid"] = $"0x{p.Vid:X4}", ["pid"] = $"0x{p.Pid:X4}" })
			.ToList();

		Render(
			Path.Combine(templatesDir, "Models.sbn"),
			Path.Combine(outputDir, "ModelRegistry.g.cs"),
			ctx =>
			{
				ctx["models"]    = models;
				ctx["discovery"] = discovery;
			});
	}

	private static ScriptObject BuildModelContext(ModelDef m)
	{
		var capParts = m.Capabilities
			.Select(c => c switch
			{
				"high_precision" => "Capabilities.HighPrecision",
				"kun_precision" => "Capabilities.KunPrecision",
				"logo_light" => "Capabilities.LogoLight",
				"side_light" => "Capabilities.SideLight",
				"turbo_mode" => "Capabilities.TurboMode",
				_ => "Capabilities.None",
			})
			.Where(s => s != "Capabilities.None")
			.ToList();

		var capsExpr = capParts.Count > 0
			? string.Join(" | ", capParts)
			: "Capabilities.None";

		m.FirmwareOverrides.TryGetValue("min_depth_mm", out var minDepthObj);
		m.FirmwareOverrides.TryGetValue("max_depth_mm", out var maxDepthObj);
		float minDepth = minDepthObj is null ? 0.2f : Convert.ToSingle(minDepthObj);
		float maxDepth = maxDepthObj is null ? 3.3f : Convert.ToSingle(maxDepthObj);

		// kun_precision block: optional top-level key in the model YAML node.
		// Carries min_firmware threshold and kun-specific max depth.
		string kunMinFirmwareExpr = "null";
		float kunMaxDepth = 2.0f;
		if (m.KunPrecision is { } kun)
		{
			if (kun.TryGetValue("min_firmware", out var fwObj))
				kunMinFirmwareExpr = $"(byte){Convert.ToInt32(fwObj)}";
			if (kun.TryGetValue("max_depth_mm", out var kmaxObj))
				kunMaxDepth = Convert.ToSingle(kmaxObj);
		}

		var identities = m.Identities.Select(id =>
		{
			var o = new ScriptObject
			{
				["b0"]      = $"0x{id.Bytes[0]:X2}",
				["b1"]      = id.Bytes.Count > 1 ? $"0x{id.Bytes[1]:X2}" : "0x00",
				["b2"]      = id.Bytes.Count > 2 ? $"0x{id.Bytes[2]:X2}" : "0x00",
				["variant"] = id.Variant,
			};
			return o;
		}).ToList();

		// Capability marker interfaces for compile-time API gating.
		var interfaces = new List<string>();
		if (m.Capabilities.Contains("high_precision")) interfaces.Add("IHasHighPrecision");
		if (m.Capabilities.Contains("turbo_mode"))      interfaces.Add("IHasTurboMode");
		if (m.Capabilities.Contains("logo_light"))     interfaces.Add("IHasLogoLight");
		if (m.Capabilities.Contains("side_light"))     interfaces.Add("IHasSideLight");
		// kun_precision without any other FuncBlock-implying capability needs explicit IHasFuncBlock.
		bool hasFuncBlock = m.Capabilities.Contains("kun_precision") || m.Capabilities.Contains("high_precision");
		if (hasFuncBlock && interfaces.Count == 0) interfaces.Add("IHasFuncBlock");
		string interfacesExpr = interfaces.Count > 0 ? string.Join(", ", interfaces) : "";

		return new ScriptObject
		{
			["slug"]              = m.Slug,
			["const_name"]        = ToPascal(m.Slug),
			["name"]              = m.Name,
			["capabilities_expr"] = capsExpr,
			["min_depth_mm"]      = $"{minDepth}f",
			["max_depth_mm"]      = $"{maxDepth}f",
			["kun_min_firmware"]  = kunMinFirmwareExpr,
			["kun_max_depth_mm"]  = $"{kunMaxDepth}f",
			["identities"]        = identities,
			["interfaces_list"]   = interfacesExpr,
		};
	}

	private static void RenderStructs(ProtocolDef def, string templatesDir, string outputDir)
	{
		var structs = def.Structs.Values
			.Select(BuildStructContext)
			.ToList();

		Render(
			Path.Combine(templatesDir, "Structs.sbn"),
			Path.Combine(outputDir, "Structs.g.cs"),
			ctx => ctx["structs"] = structs);
	}

	private static ScriptObject BuildStructContext(StructDef s) =>
		s.BitFields.Count > 0 ? BuildBitFieldStructContext(s) : BuildSimpleStructContext(s);

	private static ScriptObject BuildBitFieldStructContext(StructDef s)
	{
		int byteSize = s.ExplicitByteSize
			?? throw new InvalidOperationException($"Struct '{s.Name}' has bit_fields but no byte_size.");

		var fieldDecls = new List<string>();
		var ctorParams = new List<string>();
		var ctorStmts = new List<string>();
		var readArgs = new List<string>();

		// byte index -> OR-able write expressions collected across all fields
		var writeContribs = new Dictionary<int, List<string>>();
		var writePrecompute = new List<string>();   // int _fooRaw = ...; lines before buf[i] assignments

		void AddContrib(int byteIdx, string expr)
		{
			if (!writeContribs.TryGetValue(byteIdx, out var list))
				writeContribs[byteIdx] = list = [];
			list.Add(expr);
		}

		foreach (var bf in s.BitFields)
		{
			bool isLe9 = bf.ByteLo.HasValue;
			bool isConst = bf.Const.HasValue;

			if (isConst)
			{
				// Constant bit range: written as a literal on encode, ignored on decode.
				int lo = bf.BitLo ?? 0;
				int hi = bf.BitHi ?? 7;
				int mask = (1 << (hi - lo + 1)) - 1;
				int val = (bf.Const!.Value & mask) << lo;
				AddContrib(bf.SingleByte!.Value, $"0x{val:X2}");
				continue;
			}

			if (bf.IsReserved) continue;

			string pascal = ToPascal(bf.Name);
			string camel = ToCamel(bf.Name);

			if (isLe9)
			{
				// 9-bit value: lower byte + bit 0 of upper byte. int property (can reach 512 + bias).
				fieldDecls.Add($"public int {pascal} {{ get; }}");
				ctorParams.Add($"int {camel}");
				ctorStmts.Add($"{pascal} = {camel};");

				int bLo = bf.ByteLo!.Value;
				int bHi = bf.ByteHi!.Value;

				string readExpr = $"((buf[{bHi}] & 0x01) << 8 | buf[{bLo}])";
				if (bf.Bias != 0) readExpr += $" + {bf.Bias}";
				readArgs.Add(readExpr);

				string rawVar = $"_{camel}Raw";
				string rawExpr = bf.Bias != 0 ? $"Math.Max(0, {pascal} - {bf.Bias})" : pascal;
				writePrecompute.Add($"int {rawVar} = {rawExpr};");
				AddContrib(bLo, $"({rawVar} & 0xFF)");
				AddContrib(bHi, $"(({rawVar} >> 8) & 0x01)");
			}
			else
			{
				// Single-byte bit-range field.
				int byteIdx = bf.SingleByte!.Value;
				int lo = bf.BitLo ?? 0;
				int hi = bf.BitHi ?? 7;
				int mask = (1 << (hi - lo + 1)) - 1;

				fieldDecls.Add($"public byte {pascal} {{ get; }}");
				ctorParams.Add($"byte {camel}");
				ctorStmts.Add($"{pascal} = {camel};");

				string readExpr = lo == 0
					? $"(byte)(buf[{byteIdx}] & 0x{mask:X2})"
					: $"(byte)((buf[{byteIdx}] >> {lo}) & 0x{mask:X2})";
				readArgs.Add(readExpr);

				string writeExpr = lo == 0
					? $"({pascal} & 0x{mask:X2})"
					: $"(({pascal} & 0x{mask:X2}) << {lo})";
				AddContrib(byteIdx, writeExpr);
			}
		}

		// Assemble write statements: precompute first, then one buf[i] = ... per byte.
		var writeStmts = new List<string>(writePrecompute);
		for (int i = 0; i < byteSize; i++)
		{
			if (writeContribs.TryGetValue(i, out var contribs) && contribs.Count > 0)
				writeStmts.Add($"buf[{i}] = (byte)({string.Join(" | ", contribs)});");
			else
				writeStmts.Add($"buf[{i}] = 0;");
		}

		return new ScriptObject
		{
			["name"]           = s.Name,
			["byte_size"]      = byteSize,
			["backing_fields"] = new List<string>(),
			["field_decls"]    = fieldDecls,
			["span_props"]     = new List<string>(),
			["has_ctor"]       = ctorParams.Count > 0,
			["ctor_params"]    = string.Join(", ", ctorParams),
			["ctor_stmts"]     = ctorStmts,
			["read_args"]      = readArgs,   // list here, not joined string
			["write_stmts"]    = writeStmts,
			["has_bit_fields"] = true,
		};
	}

	private static ScriptObject BuildSimpleStructContext(StructDef s)
	{
		var backingFields = new List<string>();
		var fieldDecls = new List<string>();
		var spanProps = new List<string>();
		var ctorParams = new List<string>();
		var ctorStmts = new List<string>();
		var readArgs = new List<string>();
		var writeStmts = new List<string>();

		int offset = 0;
		foreach (var f in s.Fields)
		{
			bool isReserved = f.Name.StartsWith('_');
			int size = TypedField.ParseSize(f.Type);
			bool isBytes = f.Type.StartsWith("bytes[");

			if (!isReserved)
			{
				var pascal = ToPascal(f.Name);
				var camel = ToCamel(f.Name);
				var backing = "_" + camel;

				if (isBytes)
				{
					backingFields.Add($"private readonly byte[] {backing};");
					spanProps.Add($"public ReadOnlySpan<byte> {pascal} => {backing};");
					ctorParams.Add($"ReadOnlySpan<byte> {camel}");
					ctorStmts.Add($"{backing} = {camel}.ToArray();");
					readArgs.Add($"buf.Slice({offset}, {size})");
					writeStmts.Add($"{backing}.AsSpan().CopyTo(buf.Slice({offset}));");
				}
				else
				{
					string csType = f.Type switch
					{
						"u8" => "byte",
						"i8" => "sbyte",
						"u16le" => "ushort",
						_ => "byte",
					};
					fieldDecls.Add($"public {csType} {pascal} {{ get; }}");
					ctorParams.Add($"{csType} {camel}");
					ctorStmts.Add($"{pascal} = {camel};");

					if (f.Type == "u16le")
					{
						readArgs.Add($"BinaryPrimitives.ReadUInt16LittleEndian(buf.Slice({offset}))");
						writeStmts.Add($"BinaryPrimitives.WriteUInt16LittleEndian(buf.Slice({offset}), {pascal});");
					}
					else if (f.Type == "i8")
					{
						readArgs.Add($"(sbyte)buf[{offset}]");
						writeStmts.Add($"buf[{offset}] = (byte){pascal};");
					}
					else
					{
						readArgs.Add($"buf[{offset}]");
						writeStmts.Add($"buf[{offset}] = {pascal};");
					}
				}
			}
			else if (isBytes)
			{
				for (int i = 0; i < size; i++)
					writeStmts.Add($"buf[{offset + i}] = 0;");
			}
			else
			{
				writeStmts.Add($"buf[{offset}] = 0;");
			}

			offset += size;
		}

		return new ScriptObject
		{
			["name"]           = s.Name,
			["byte_size"]      = offset,
			["backing_fields"] = backingFields,
			["field_decls"]    = fieldDecls,
			["span_props"]     = spanProps,
			["has_ctor"]       = ctorParams.Count > 0,
			["ctor_params"]    = string.Join(", ", ctorParams),
			["ctor_stmts"]     = ctorStmts,
			["read_args"]      = string.Join(", ", readArgs),
			["write_stmts"]    = writeStmts,
			["has_bit_fields"] = false,
		};
	}

	private static void RenderProfileBlocks(ProtocolDef def, string templatesDir, string outputDir)
	{
		var blocks = def.ProfileBlocks.Values
			.Select(BuildProfileBlockContext)
			.ToList();

		Render(
			Path.Combine(templatesDir, "ProfileBlocks.sbn"),
			Path.Combine(outputDir, "ProfileBlocks.g.cs"),
			ctx => ctx["profile_blocks"] = blocks);
	}

	private static ScriptObject BuildProfileBlockContext(ProfileBlockDef block)
	{
		var fields = block.Fields
			.OrderBy(f => f.Byte)
			.ThenBy(f => f.ResolvedBitLo)
			.Select(BuildProfileBlockFieldContext)
			.ToList();

		return new ScriptObject
		{
			["name"]      = block.Name,
			["byte_size"] = block.ByteSize,
			["fields"]    = fields,
		};
	}

	private static ScriptObject BuildProfileBlockFieldContext(ProfileBlockFieldDef f)
	{
		int byteIdx = f.Byte;
		int bitLo = f.ResolvedBitLo;
		int bitHi = f.ResolvedBitHi;
		int mask = (1 << (bitHi - bitLo + 1)) - 1;
		int keepMask = (~(mask << bitLo)) & 0xFF;

		string csType = f.Type switch
		{
			"bool" => "bool",
			var t when t.StartsWith("enum:") => t["enum:".Length..],
			_ => "byte",
		};

		string getBody, setBody;

		if (f.IsFullByte && f.Type == "u8")
		{
			getBody = $"RawBytes[{byteIdx}]";
			setBody = $"RawBytes[{byteIdx}] = value";
		}
		else if (f.Type == "bool" && f.Inverted)
		{
			// Inverted bool: byte == 0 means true (e.g. single-color mode where 0 = enabled).
			getBody = $"RawBytes[{byteIdx}] == 0x00";
			setBody = $"RawBytes[{byteIdx}] = (byte)(value ? 0x00 : 0x01)";
		}
		else if (f.Type == "bool")
		{
			string bitMaskHex = $"0x{mask << bitLo:X2}";
			string keepHex = $"0x{keepMask:X2}";
			getBody = $"(RawBytes[{byteIdx}] & {bitMaskHex}) != 0";
			setBody = $"RawBytes[{byteIdx}] = (byte)(value ? RawBytes[{byteIdx}] | {bitMaskHex} : RawBytes[{byteIdx}] & {keepHex})";
		}
		else if (f.Type.StartsWith("enum:"))
		{
			string enumName = f.Type["enum:".Length..];
			string maskHex = $"0x{mask:X2}";
			string keepHex = $"0x{keepMask:X2}";
			getBody = bitLo == 0
				? $"({enumName})(RawBytes[{byteIdx}] & {maskHex})"
				: $"({enumName})((RawBytes[{byteIdx}] >> {bitLo}) & {maskHex})";
			setBody = bitLo == 0
				? $"RawBytes[{byteIdx}] = (byte)((RawBytes[{byteIdx}] & {keepHex}) | ((byte)value & {maskHex}))"
				: $"RawBytes[{byteIdx}] = (byte)((RawBytes[{byteIdx}] & {keepHex}) | (((byte)value & {maskHex}) << {bitLo}))";
		}
		else
		{
			// u8 bit-range field
			string maskHex = $"0x{mask:X2}";
			string keepHex = $"0x{keepMask:X2}";
			getBody = bitLo == 0
				? $"(byte)(RawBytes[{byteIdx}] & {maskHex})"
				: $"(byte)((RawBytes[{byteIdx}] >> {bitLo}) & {maskHex})";
			setBody = bitLo == 0
				? $"RawBytes[{byteIdx}] = (byte)((RawBytes[{byteIdx}] & {keepHex}) | (value & {maskHex}))"
				: $"RawBytes[{byteIdx}] = (byte)((RawBytes[{byteIdx}] & {keepHex}) | ((value & {maskHex}) << {bitLo}))";
		}

		return new ScriptObject
		{
			["prop_name"] = ToPascal(f.Name),
			["cs_type"]   = csType,
			["is_simple"] = f.IsFullByte && f.Type == "u8",
			["get_body"]  = getBody,
			["set_body"]  = setBody,
		};
	}

	private static void Render(string templatePath, string outputPath, Action<ScriptObject> populate)
	{
		var templateText = File.ReadAllText(templatePath);
		var template = Template.Parse(templateText);

		if (template.HasErrors)
		{
			foreach (var err in template.Messages)
				Console.Error.WriteLine($"Template error in {templatePath}: {err}");
			return;
		}

		var scriptCtx = new ScriptObject();
		populate(scriptCtx);
		var context = new TemplateContext { MemberRenamer = m => m.Name };
		context.PushGlobal(scriptCtx);

		var output = template.Render(context);
		File.WriteAllText(outputPath, output);
		Console.WriteLine($"Generated: {outputPath}");
	}

	private static string FormatHeaderHex(List<byte> header) =>
		string.Join(", ", header.Select(b => $"0x{b:X2}"));

	private static string ToPascal(string snake) =>
		string.Concat(snake.Split('_').Select(w => char.ToUpperInvariant(w[0]) + w[1..]));

	private static string ToCamel(string snake)
	{
		var pascal = ToPascal(snake);
		return char.ToLowerInvariant(pascal[0]) + pascal[1..];
	}
}
