namespace DrunkDeer.Protocol;

/// <summary>
/// Provides per-model keyboard layout data: key name arrays, LED index arrays,
/// and DDKey-to-firmware-index lookup tables.
/// </summary>
/// <remarks>
/// Layout strings are internal firmware token names (e.g. "ESC", "SHF_L", "DELETE").
/// The layout index (position in the layout array) is the unified key index used
/// for both actuation-point profiles and RGB lighting.
/// </remarks>
internal static class KeyLayout
{
	// Each position maps to a firmware key slot. Empty string = unused slot.

	private static readonly string[] LayoutA75 =
	[
		"ESC",    "",        "F1",     "F2",     "F3",      "F4",      "F5",
		"F6",     "F7",      "F8",     "F9",     "F10",     "F11",     "F12",
		"DELETE", "",        "",       "u1",     "u2",      "u3",      "u4",   // 14=Delete (top-right of F-row)
        "SWUNG",  "1",       "2",      "3",      "4",       "5",       "6",
		"7",      "8",       "9",      "0",      "MINUS",   "PLUS",    "BACK",
		"",       "HOME",    "",       "u5",     "u6",      "u7",      "u8",   // 36=Home (right of number row)
        "TAB",    "Q",       "W",      "E",      "R",       "T",       "Y",
		"U",      "I",       "O",      "P",      "BRKTS_L", "BRKTS_R", "SLASH_K29",
		"",       "PAGEUP",  "",       "u9",     "u10",     "u11",     "u12",  // 57=PgUp (right of QWERTY row)
        "CAPS",   "A",       "S",      "D",      "F",       "G",       "H",
		"J",      "K",       "L",      "COLON",  "QOTATN",  "u13",     "RETURN",
		"",       "PAGEDW",  "",       "u15",    "u16",     "u17",     "u18",  // 78=PgDn (right of home row)
        "SHF_L",  "EUR_K45", "Z",      "X",      "C",       "V",       "B",
		"N",      "M",       "COMMA",  "PERIOD", "VIRGUE",  "u19",     "SHF_R",
		"ARR_UP", "END",     "",       "u21",    "u22",     "u23",     "u24",  // 98=ArrowUp, 99=End
        "CTRL_L", "WIN_L",   "ALT_L",  "u25",    "u26",     "u27",     "SPACE",
		"u28",    "u29",     "u30",    "ALT_R",  "FN1",     "APP",     "",
		"ARR_L",  "ARR_DW",  "ARR_R",  "CTRL_R", "u31",     "u32",     "u33",
		"u34",
	];

	private static readonly string[] LayoutG75 =
	[
		"ESC",    "F1",      "F2",    "F3",     "F4",      "F5",      "F6",
		"F7",     "F8",      "F9",    "F10",    "F11",     "F12",     "PRINT",
		"INSERT", "DELETE",  "",      "",       "",        "",        "",
		"SWUNG",  "1",       "2",     "3",      "4",       "5",       "6",
		"7",      "8",       "9",     "0",      "MINUS",   "PLUS",    "BACK",
		"",       "HOME",    "",      "",       "",        "",        "",
		"TAB",    "Q",       "W",     "E",      "R",       "T",       "Y",
		"U",      "I",       "O",     "P",      "BRKTS_L", "BRKTS_R", "SLASH_K29",
		"",       "PAGEUP",  "",      "",       "",        "",        "",
		"CAPS",   "A",       "S",     "D",      "F",       "G",       "H",
		"J",      "K",       "L",     "COLON",  "QOTATN",  "EUR_K42", "RETURN",
		"",       "PAGEDW",  "",      "",       "",        "",        "",
		"SHF_L",  "EUR_K45", "Z",     "X",      "C",       "V",       "B",
		"N",      "M",       "COMMA", "PERIOD", "VIRGUE",  "SHF_R",   "ARR_UP",
		"",       "END",     "",      "",       "",        "",        "",
		"CTRL_L", "WIN_L",   "ALT_L", "",       "",        "",        "SPACE",
		"",       "",        "ALT_R", "FN1",    "",        "FN2",     "ARR_L",
		"ARR_DW", "ARR_R",   "",      "",       "",        "",        "",
		"",
	];

	private static readonly string[] LayoutG65 =
	[
		"",       "",        "",      "",       "",        "",        "",
		"",       "",        "",      "",       "",        "",        "",
		"",       "",        "",      "",       "",        "",        "",
		"ESC",    "1",       "2",     "3",      "4",       "5",       "6",
		"7",      "8",       "9",     "0",      "MINUS",   "PLUS",    "BACK",
		"DELETE", "",        "",      "",       "",        "",        "",
		"TAB",    "Q",       "W",     "E",      "R",       "T",       "Y",
		"U",      "I",       "O",     "P",      "BRKTS_L", "BRKTS_R", "SLASH_K29",
		"END",    "",        "",      "",       "",        "",        "",
		"CAPS",   "A",       "S",     "D",      "F",       "G",       "H",
		"J",      "K",       "L",     "COLON",  "QOTATN",  "EUR_K42", "RETURN",
		"PAGEUP", "",        "",      "",       "",        "",        "",
		"SHF_L",  "EUR_K45", "Z",     "X",      "C",       "V",       "B",
		"N",      "M",       "COMMA", "PERIOD", "VIRGUE",  "SHF_R",   "ARR_UP",
		"PAGEDW", "",        "",      "",       "",        "",        "",
		"CTRL_L", "WIN_L",   "ALT_L", "",       "",        "",        "SPACE",
		"",       "",        "ALT_R", "FN1",    "FN2",     "ARR_L",   "ARR_DW",
		"ARR_R",  "",        "",      "",       "",        "",        "",
		"",
	];

	private static readonly string[] LayoutG60 =
	[
		"",       "",        "",      "",       "",        "",        "",
		"",       "",        "",      "",       "",        "",        "",
		"",       "",        "",      "",       "",        "",        "",
		"ESC",    "1",       "2",     "3",      "4",       "5",       "6",
		"7",      "8",       "9",     "0",      "MINUS",   "PLUS",    "BACK",
		"",       "",        "",      "",       "",        "",        "",
		"TAB",    "Q",       "W",     "E",      "R",       "T",       "Y",
		"U",      "I",       "O",     "P",      "BRKTS_L", "BRKTS_R", "SLASH_K29",
		"",       "",        "",      "",       "",        "",        "",
		"CAPS",   "A",       "S",     "D",      "F",       "G",       "H",
		"J",      "K",       "L",     "COLON",  "QOTATN",  "EUR_K42", "RETURN",
		"",       "",        "",      "",       "",        "",        "",
		"SHF_L",  "EUR_K45", "Z",     "X",      "C",       "V",       "B",
		"N",      "M",       "COMMA", "PERIOD", "VIRGUE",  "",        "SHF_R",
		"",       "",        "",      "",       "",        "",        "",
		"CTRL_L", "WIN_L",   "ALT_L", "",       "",        "",        "SPACE",
		"",       "",        "",      "ALT_R",  "FN1",     "FN2",     "CTRL_R",
		"",       "",        "",      "",       "",        "",        "",
		"",
	];

	private static readonly int[] RgbA75 =
	[
		0,2,3,4,5,6,7,8,9,10,11,12,13,14,
		21,22,23,24,25,26,27,28,29,30,31,32,33,34,36,
		42,43,44,45,46,47,48,49,50,51,52,53,54,55,57,
		63,64,65,66,67,68,69,70,71,72,73,74,76,78,
		84,86,87,88,89,90,91,92,93,94,95,97,98,99,
		105,106,107,111,115,116,117,119,120,121,
	];

	private static readonly int[] RgbA75Iso =
	[
		0,2,3,4,5,6,7,8,9,10,11,12,13,14,
		21,22,23,24,25,26,27,28,29,30,31,32,33,34,36,
		42,43,44,45,46,47,48,49,50,51,52,53,54,57,
		63,64,65,66,67,68,69,70,71,72,73,74,75,76,78,
		84,85,86,87,88,89,90,91,92,93,94,95,97,98,99,
		105,106,107,111,115,116,117,119,120,121,
	];

	private static readonly int[] RgbA75Master =
	[
		0,2,3,4,5,6,7,8,9,10,11,12,13,15,
		21,22,23,24,25,26,27,28,29,30,31,32,33,34,14,
		42,43,44,45,46,47,48,49,50,51,52,53,54,55,57,
		63,64,65,66,67,68,69,70,71,72,73,74,76,78,
		84,86,87,88,89,90,91,92,93,94,95,97,98,99,
		105,106,107,111,115,116,117,119,120,121,
	];

	private static readonly int[] RgbG75 =
	[
		0,1,2,3,4,5,6,7,8,9,10,11,12,13,14,15,
		21,22,23,24,25,26,27,28,29,30,31,32,33,34,36,
		42,43,44,45,46,47,48,49,50,51,52,53,54,55,57,
		63,64,65,66,67,68,69,70,71,72,73,74,76,78,
		84,86,87,88,89,90,91,92,93,94,95,96,97,99,
		105,106,107,111,114,115,117,118,119,120,
	];

	private static readonly int[] RgbG75Jp =
	[
		0,1,2,3,4,5,6,7,8,9,10,11,12,13,14,15,
		21,22,23,24,25,26,27,28,29,30,31,32,33,34,35,36,
		42,43,44,45,46,47,48,49,50,51,52,53,54,57,
		63,64,65,66,67,68,69,70,71,72,73,74,75,77,78,
		84,86,87,88,89,90,91,92,93,94,95,96,97,98,99,
		105,106,107,111,114,115,117,118,119,120,
	];

	private static readonly int[] RgbG65 =
	[
		21,22,23,24,25,26,27,28,29,30,31,32,33,34,35,
		42,43,44,45,46,47,48,49,50,51,52,53,54,55,56,
		63,64,65,66,67,68,69,70,71,72,73,74,76,77,
		84,86,87,88,89,90,91,92,93,94,95,96,97,98,
		105,106,107,111,114,115,116,117,118,119,
	];

	private static readonly int[] RgbG60 =
	[
		21,22,23,24,25,26,27,28,29,30,31,32,33,34,
		42,43,44,45,46,47,48,49,50,51,52,53,54,55,
		63,64,65,66,67,68,69,70,71,72,73,74,76,
		84,86,87,88,89,90,91,92,93,94,95,97,
		105,106,107,111,115,116,117,118,
	];

	/// <summary>Returns the layout string array for the given model slug.</summary>
	public static string[] GetLayout(string modelSlug) => modelSlug switch
	{
		ModelSlugs.A75       or
		ModelSlugs.A75Pro    or
		ModelSlugs.A75Ultra  or
		ModelSlugs.A75Master => LayoutA75,
		ModelSlugs.G75   or
		ModelSlugs.G75Jp => LayoutG75,
		ModelSlugs.G65       or
		ModelSlugs.G65Lite   or
		ModelSlugs.G65M1     or
		ModelSlugs.G65M2     or
		ModelSlugs.G65M3 => LayoutG65,
		// TODO: verify via capture. X60 Future is a 60% board; G60's layout is a closer
		// approximation than the 75% A75's (the previous silent default), but has not been
		// confirmed against the real firmware slot map. See README verification table.
		ModelSlugs.G60   or
		ModelSlugs.G60V600 or
		ModelSlugs.X60Future => LayoutG60,
		_ => throw new NotSupportedException(
			$"No layout table for model '{modelSlug}'. Add a case to KeyLayout.GetLayout " +
			"rather than falling back to another model's table."),
	};

	/// <summary>
	/// Returns the sorted array of layout indices that have physical RGB LEDs
	/// for the given model and variant.
	/// </summary>
	public static int[] GetRgbIndices(string modelSlug, string variant) =>
		(modelSlug, variant) switch
		{
			(ModelSlugs.A75Master, _) => RgbA75Master,
			(ModelSlugs.A75, "iso") => RgbA75Iso,
			(ModelSlugs.A75, _)        or
			(ModelSlugs.A75Pro, _)     or
			(ModelSlugs.A75Ultra, _) => RgbA75,
			(ModelSlugs.G75Jp, _) => RgbG75Jp,
			(ModelSlugs.G75, _) => RgbG75,
			(ModelSlugs.G65, _)        or
			(ModelSlugs.G65Lite, _)    or
			(ModelSlugs.G65M1, _)      or
			(ModelSlugs.G65M2, _)      or
			(ModelSlugs.G65M3, _) => RgbG65,
			// TODO: verify via capture, same caveat as GetLayout above.
			(ModelSlugs.G60, _)    or
			(ModelSlugs.G60V600, _) or
			(ModelSlugs.X60Future, _) => RgbG60,
			_ => throw new NotSupportedException(
				$"No RGB index table for model '{modelSlug}' variant '{variant}'. Add a case to " +
				"KeyLayout.GetRgbIndices rather than falling back to another model's table."),
		};

	/// <summary>
	/// Builds a <see cref="DDKey"/> -> layout-index lookup for the given layout array.
	/// Keys whose token string does not appear in <paramref name="layout"/> are omitted.
	/// </summary>
	public static IReadOnlyDictionary<DDKey, int> BuildIndexMap(string[] layout)
	{
		var map = new Dictionary<DDKey, int>();
		foreach (var (key, token) in KeyLayoutNames.Names)
		{
			int idx = Array.IndexOf(layout, token);
			if (idx >= 0)
				map[key] = idx;
		}
		return map;
	}
}
