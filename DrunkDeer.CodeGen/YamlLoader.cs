using YamlDotNet.Serialization;

namespace DrunkDeer.Codegen;

internal static class YamlLoader
{
	private static readonly IDeserializer Deserializer =
		new DeserializerBuilder()
			.WithAttemptingUnquotedStringTypeDeserialization()
			.Build();

	public static ProtocolDef Load(string protocolDir)
	{
		var def = new ProtocolDef();

		LoadStructs(def, Path.Combine(protocolDir, "structs.yaml"));
		LoadMessages(def, Path.Combine(protocolDir, "base_messages.yaml"));

		var capsDir = Path.Combine(protocolDir, "capabilities");
		if (Directory.Exists(capsDir))
		{
			foreach (var capDir in Directory.EnumerateDirectories(capsDir))
			{
				var capMsgs = Path.Combine(capDir, "messages.yaml");
				if (File.Exists(capMsgs))
					LoadMessages(def, capMsgs);
			}
		}

		LoadModels(def, Path.Combine(protocolDir, "models.yaml"));
		LoadProfileBlocks(def, Path.Combine(protocolDir, "profile_blocks.yaml"));
		return def;
	}

	private static void LoadStructs(ProtocolDef def, string path)
	{
		var raw = ParseYaml(path);
		if (!raw.TryGetValue("structs", out var structsObj) || structsObj is not Dictionary<object, object> structs)
			return;

		foreach (var (nameObj, defObj) in structs)
		{
			var name = (string)nameObj;
			if (defObj is not Dictionary<object, object> fields) continue;

			var sd = new StructDef { Name = name };
			foreach (var (fn, ft) in fields)
			{
				var fieldName = (string)fn;
				if (fieldName == "byte_size")
				{
					sd.ExplicitByteSize = ToInt(ft);
					continue;
				}
				if (fieldName == "bit_fields" && ft is Dictionary<object, object> bfDict)
				{
					sd.BitFields = ParseBitFields(bfDict);
					continue;
				}
				if (ft is string typeStr)
					sd.Fields.Add(new StructField { Name = fieldName, Type = typeStr });
				// complex fields (bytes etc.) are kept as raw for now
			}
			def.Structs[name] = sd;
		}
	}

	private static void LoadMessages(ProtocolDef def, string path)
	{
		var raw = ParseYaml(path);
		if (!raw.TryGetValue("messages", out var msgsObj) || msgsObj is not Dictionary<object, object> msgs)
			return;

		foreach (var (nameObj, defObj) in msgs)
		{
			var name = (string)nameObj;
			if (defObj is not Dictionary<object, object> msgDef) continue;

			var msg = new MessageDef { Name = name };

			if (msgDef.TryGetValue("direction", out var dirObj) && dirObj is string dir)
				msg.Direction = dir == "response" ? MessageDir.Response : MessageDir.Request;

			if (msgDef.TryGetValue("header", out var hdrObj))
				msg.Header = ParseByteList(hdrObj);

			if (msgDef.TryGetValue("payload", out var payObj) && payObj is Dictionary<object, object> payload)
				msg.Payload = ParsePayload(payload);

			if (msgDef.TryGetValue("padding_to", out var padObj))
				msg.PaddingTo = ToInt(padObj);

			def.Messages[name] = msg;
		}
	}

	private static List<IField> ParsePayload(Dictionary<object, object> payload)
	{
		var fields = new List<IField>();
		foreach (var (nameObj, valObj) in payload)
		{
			var name = (string)nameObj;
			switch (valObj)
			{
				case string typeStr:
					fields.Add(new TypedField { Name = name, TypeSpec = typeStr });
					break;

				case List<object> constList:
					// Inline constant byte sequence: [0x01, 0x02, ...]
					fields.Add(new ConstantField
					{
						Name  = name,
						Value = constList.Select(ToByteValue).ToList(),
					});
					break;

				case Dictionary<object, object> mapVal:
					// Variable-length array: element / max_count / terminated_by
					if (mapVal.TryGetValue("element", out var elemObj))
					{
						var af = new ArrayField
						{
							Name        = name,
							ElementType = (string)elemObj,
							MaxCount    = mapVal.TryGetValue("max_count", out var mc) ? ToInt(mc) : 0,
						};
						if (mapVal.TryGetValue("terminated_by", out var tb))
							af.TerminatedBy = ToByteValue(tb);
						fields.Add(af);
					}
					break;
			}
		}
		return fields;
	}

	private static List<BitFieldDef> ParseBitFields(Dictionary<object, object> bfDict)
	{
		var result = new List<BitFieldDef>();
		foreach (var (nameObj, defObj) in bfDict)
		{
			var bfName = (string)nameObj;
			if (defObj is not Dictionary<object, object> def) continue;

			var bf = new BitFieldDef { Name = bfName };

			if (def.TryGetValue("byte", out var bv)) bf.SingleByte = ToInt(bv);
			if (def.TryGetValue("byte_lo", out var blo)) bf.ByteLo     = ToInt(blo);
			if (def.TryGetValue("byte_hi", out var bhi)) bf.ByteHi     = ToInt(bhi);
			if (def.TryGetValue("bit_lo", out var bllo)) bf.BitLo      = ToInt(bllo);
			if (def.TryGetValue("bit_hi", out var blhi)) bf.BitHi      = ToInt(blhi);
			if (def.TryGetValue("bias", out var bias)) bf.Bias       = ToInt(bias);
			if (def.TryGetValue("const", out var cst)) bf.Const      = ToByteValue(cst);

			result.Add(bf);
		}
		return result;
	}

	private static void LoadModels(ProtocolDef def, string path)
	{
		var raw = ParseYaml(path);
		if (!raw.TryGetValue("models", out var modelsObj) || modelsObj is not Dictionary<object, object> models)
			return;

		foreach (var (slugObj, defObj) in models)
		{
			var slug = (string)slugObj;
			if (defObj is not Dictionary<object, object> modelDef) continue;

			var md = new ModelDef { Slug = slug };

			if (modelDef.TryGetValue("name", out var nameObj))
				md.Name = (string)nameObj;

			if (modelDef.TryGetValue("capabilities", out var capsObj) && capsObj is List<object> caps)
				md.Capabilities = caps.Cast<string>().ToList();

			if (modelDef.TryGetValue("identities", out var idsObj) && idsObj is List<object> ids)
			{
				foreach (var idObj in ids)
				{
					if (idObj is not Dictionary<object, object> id) continue;
					var mi = new ModelIdentity();
					if (id.TryGetValue("bytes", out var bytesObj))
						mi.Bytes = ParseByteList(bytesObj);
					if (id.TryGetValue("variant", out var varObj))
						mi.Variant = (string)varObj;
					md.Identities.Add(mi);
				}
			}

			if (modelDef.TryGetValue("firmware_overrides", out var foObj) &&
				foObj is Dictionary<object, object> fo)
			{
				foreach (var (k, v) in fo)
					md.FirmwareOverrides[(string)k] = v;
			}

			if (modelDef.TryGetValue("kun_precision", out var kunObj) &&
				kunObj is Dictionary<object, object> kun)
			{
				md.KunPrecision = [];
				foreach (var (k, v) in kun)
					md.KunPrecision[(string)k] = v;
			}

			def.Models[slug] = md;
		}
	}

	private static void LoadProfileBlocks(ProtocolDef def, string path)
	{
		if (!File.Exists(path)) return;
		var raw = ParseYaml(path);
		if (!raw.TryGetValue("profile_blocks", out var blocksObj) || blocksObj is not Dictionary<object, object> blocks)
			return;

		foreach (var (nameObj, defObj) in blocks)
		{
			var name = (string)nameObj;
			if (defObj is not Dictionary<object, object> blockDef) continue;

			var pb = new ProfileBlockDef { Name = name };

			if (blockDef.TryGetValue("byte_size", out var sizeObj))
				pb.ByteSize = ToInt(sizeObj);

			if (blockDef.TryGetValue("fields", out var fieldsObj) && fieldsObj is Dictionary<object, object> fields)
			{
				foreach (var (fnObj, fdObj) in fields)
				{
					var fieldName = (string)fnObj;
					if (fdObj is not Dictionary<object, object> fd) continue;

					var f = new ProfileBlockFieldDef { Name = fieldName };

					if (fd.TryGetValue("byte", out var bv)) f.Byte  = ToInt(bv);
					if (fd.TryGetValue("bit", out var bit)) f.Bit   = ToInt(bit);
					if (fd.TryGetValue("bit_lo", out var blo)) f.BitLo = ToInt(blo);
					if (fd.TryGetValue("bit_hi", out var bhi)) f.BitHi = ToInt(bhi);
					if (fd.TryGetValue("type", out var tv)) f.Type     = (string)tv;
					if (fd.TryGetValue("inverted", out var inv)) f.Inverted = (bool)inv;

					pb.Fields.Add(f);
				}
			}

			def.ProfileBlocks[name] = pb;
		}
	}

	private static Dictionary<object, object> ParseYaml(string path)
	{
		var text = File.ReadAllText(path);
		return Deserializer.Deserialize<Dictionary<object, object>>(text)
			   ?? [];
	}

	private static List<byte> ParseByteList(object obj)
	{
		if (obj is List<object> list)
			return list.Select(ToByteValue).ToList();
		return [];
	}

	private static int ToInt(object obj) => obj switch
	{
		byte b => b,
		int i => i,
		long l => (int)l,
		string s => int.Parse(s),
		_ => throw new InvalidOperationException($"Cannot convert {obj} ({obj.GetType().Name}) to int"),
	};

	private static byte ToByteValue(object obj) => obj switch
	{
		byte b => b,
		int i => (byte)i,
		long l => (byte)l,
		string s => s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
					? Convert.ToByte(s, 16)
					: byte.Parse(s),
		_ => throw new InvalidOperationException($"Cannot convert {obj} ({obj.GetType().Name}) to byte"),
	};
}
