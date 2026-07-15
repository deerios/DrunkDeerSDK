namespace DrunkDeer.Protocol;

/// <summary>Which way a packet crossed the wire.</summary>
public enum PacketDirection
{
    /// <summary>A packet the host sent to the keyboard.</summary>
    HostToDevice,
    /// <summary>A packet the keyboard sent to the host.</summary>
    DeviceToHost
}

/// <summary>A named field extracted from an IN packet payload.</summary>
/// <param name="FirmwareSensitive">
/// <see langword="true"/> when this field is known to vary across firmware versions
/// and should be surfaced in the per-session firmware diff summary.
/// </param>
public sealed record ExtractedField(string Name, string Value, bool FirmwareSensitive = false);

/// <summary>Oracle classification result for a single HID packet.</summary>
public sealed record OracleResult(
    /// <summary>Human-readable protocol message name; <c>"Unknown(0xXX)"</c> when unmatched.</summary>
    string MessageName,

    /// <summary><see langword="false"/> when the packet violates a structural invariant in the protocol definition.</summary>
    bool StructuralOk,

    IReadOnlyList<string> StructuralFailures,

    /// <summary>Named field values extracted from the payload; populated for IN packets only.</summary>
    IReadOnlyList<ExtractedField> Fields,

    /// <summary>For OUT packets: the name of the IN message this command expects in response.</summary>
    string? ExpectedResponseName,

    /// <summary>For OUT packets: the expected byte[0] of the IN response.</summary>
    byte? ExpectedResponseByte0
);

/// <summary>
/// Classifies HID packets against the generated protocol definitions and validates their structure.
/// Uses the static <c>Matches()</c> methods from <c>Messages.g.cs</c> as the ground truth.
/// </summary>
public static class ProtocolOracle
{
    static readonly IReadOnlyList<string>          NoFailures = [];
    static readonly IReadOnlyList<ExtractedField>  NoFields   = [];

    // ── Public API ────────────────────────────────────────────────────────────

    public static OracleResult Classify(byte[] buf, PacketDirection direction)
    {
        if (buf.Length < 1)
            return new OracleResult("Empty", false, ["Payload is empty"], NoFields, null, null);

        return direction == PacketDirection.HostToDevice ? ClassifyOut(buf) : ClassifyIn(buf);
    }

    /// <summary>
    /// <see langword="true"/> if this packet is part of the continuous key-travel polling: the
    /// request the poll loop repeats, or one of the travel packets answering it.
    /// </summary>
    /// <remarks>
    /// Polling is almost all the traffic a connected keyboard ever carries - hundreds of packets a
    /// second, against a handful for any configuration the user actually performs. Anything
    /// presenting or recording live traffic needs to separate the two, and this is the cheap test
    /// for it: no allocation and no classification, so it is safe to call on every packet.
    /// </remarks>
    public static bool IsTravelPolling(ReadOnlySpan<byte> buf, PacketDirection direction) =>
        direction == PacketDirection.HostToDevice
            ? buf.StartsWith(TravelRequest.Header)
            : TravelResponse.Matches(buf) || KeyTravelHighPrecision.Matches(buf);

    /// <summary>
    /// Validates that an observed IN packet is the expected response to the preceding OUT.
    /// Returns <see langword="null"/> when the sequence is correct.
    /// </summary>
    public static string? ValidateSequence(OracleResult outResult, OracleResult inResult, byte[] inPayload)
    {
        if (outResult.ExpectedResponseByte0 is not byte expected) return null;
        if (inPayload.Length < 1)
            return $"Expected {outResult.ExpectedResponseName} (0x{expected:X2}) but got an empty packet";

        byte actual = inPayload[0];
        if (actual != expected)
            return $"Expected {outResult.ExpectedResponseName} (0x{expected:X2}), got 0x{actual:X2} ({inResult.MessageName})";

        return null;
    }

    // ── OUT classification (HostToDevice) ─────────────────────────────────────

    static OracleResult ClassifyOut(byte[] b)
    {
        var (name, respName, respByte) = MatchOut(b);
        return new OracleResult(name, true, NoFailures, NoFields, respName, respByte);
    }

    static (string name, string? respName, byte? respByte) MatchOut(byte[] b) => b[0] switch
    {
        // Identity & key remap both start with 0xA0 0x02 - distinguish by byte[2]
        0xA0 when b.Length > 2 && b[2] == 0x04 => ("KeyRemapPacket",     "KeyRemapAck",          0xA0),
        0xA0                                    => ("IdentityRequest",    "IdentityResponse",      0xA0),

        0xB5 => ("CommonConfig", "CommonConfigAcknowledge", 0xB5),

        // 0xB6 sub-commands: TravelRequest is 0x03 0x01; write operations use 0x01/0x04/0x05
        0xB6 when b.Length > 2 && b[1] == 0x03 && b[2] == 0x01 => ("TravelRequest",                "TravelResponse",                    0xB7),
        0xB6 when b.Length > 1 && b[1] == 0x01                  => ("WriteActuationPointStandard",  "WriteKeyPointAcknowledgeStandard",  0xB6),
        0xB6 when b.Length > 1 && b[1] == 0x04                  => ("WriteDownstrokePointStandard", "WriteKeyPointAcknowledgeStandard",  0xB6),
        0xB6 when b.Length > 1 && b[1] == 0x05                  => ("WriteUpstrokePointStandard",   "WriteKeyPointAcknowledgeStandard",  0xB6),
        0xB6 when b.Length > 1 && b[1] == 0x03                  => ("SetKeystrokeTracking",         "WriteKeyPointAcknowledgeStandard",  0xB6),

        // Extended gateway (0x55): all sub-commands respond with 0xAA
        0x55 when b.Length > 1 && b[1] == 0x04 => ("ReadBaseBlock",         "ExtendedGatewayResponse", 0xAA),
        0x55 when b.Length > 1 && b[1] == 0x05 => ("ReadFuncBlock",         "ExtendedGatewayResponse", 0xAA),
        0x55 when b.Length > 1 && b[1] == 0x06 => ("WriteFuncBlockChunk",   "ExtendedGatewayResponse", 0xAA),
        0x55 when b.Length > 1 && b[1] == 0x0C => ("ReadMacroChunk",        "ExtendedGatewayResponse", 0xAA),
        0x55 when b.Length > 1 && b[1] == 0x0D => ("WriteMacroChunk",       "ExtendedGatewayResponse", 0xAA),
        0x55 when b.Length > 1 && b[1] == 0x0E => ("WriteActiveProfile",    "ExtendedGatewayResponse", 0xAA),
        0x55 when b.Length > 1 && b[1] == 0xA0 => ("ReadKeyTrigger",        "ExtendedGatewayResponse", 0xAA),
        0x55 when b.Length > 1 && b[1] == 0xA1 => ("WriteKeyTriggerChunk",  "ExtendedGatewayResponse", 0xAA),
        0x55                                    => ("ExtendedGateway",       "ExtendedGatewayResponse", 0xAA),

        // 0xAA sent by host: ClearRtpUpper (no expected response known)
        0xAA when b.Length > 2 && b[1] == 0x00 && b[2] == 0x01 => ("ClearRtpUpper", null, null),

        // RGB (0xAE): SetLightingOff has a more specific header than RgbKeyDataPacket
        0xAE when b.Length > 5 && b[1] == 0x01 && b[2] == 0x00 && b[4] == 0x05 => ("SetLightingOff",    "RgbAcknowledge", 0xAE),
        0xAE when b.Length > 1 && b[1] == 0x01                                  => ("RgbKeyDataPacket",  "RgbAcknowledge", 0xAE),

        // Last-Win / RTP (0xFC)
        0xFC when b.Length > 1 && b[1] == 0x0A => ("SetLastWinRapidTriggerMode", "FcAck", 0xFC),
        0xFC when b.Length > 1 && b[1] == 0x0B => ("SetLastWinReplace",          "FcAck", 0xFC),
        0xFC when b.Length > 1 && b[1] == 0x01 => ("CreateLwPairs",              "FcAck", 0xFC),

        // High-precision FD commands
        0xFD when b.Length > 2 && b[1] == 0x07 && b[2] == 0x01 => ("ReadActuationPointHighPrecision",  "ActuationPointResponseHighPrecision", 0xFD),
        0xFD when b.Length > 1 && b[1] == 0x01                  => ("WriteActuationPointHighPrecision", "FdAck",                              0xFD),
        0xFD when b.Length > 1 && b[1] == 0x0C                  => ("SetAutoMatchMode",                 "FdAck",                              0xFD),
        0xFD                                                     => ("FdCommand",                        "FdAck",                              0xFD),

        0xA7 => ("RTPAuthority",         "RTPAck",          0xA7),
        0xA8 => ("RTPAuthorityDownload", "RTPDownloadAck",  0xA8),

        _ => ($"Unknown(0x{b[0]:X2})", null, null),
    };

    // ── IN classification (DeviceToHost) ──────────────────────────────────────

    static OracleResult ClassifyIn(byte[] b)
    {
        var (name, ok, fail, fields) = MatchIn(b);
        return new OracleResult(name, ok, fail, fields, null, null);
    }

    static (string name, bool ok, IReadOnlyList<string> fail, IReadOnlyList<ExtractedField> fields) MatchIn(byte[] b)
    {
        var fail   = new List<string>();
        var fields = new List<ExtractedField>();

        // IdentityResponse: 0xA0 0x02 0x00 - richest field set, most firmware-sensitive
        if (IdentityResponse.Matches(b))
        {
            if (b.Length < 33) { fail.Add($"Too short for IdentityResponse: {b.Length} < 33"); return ("IdentityResponse", false, fail, fields); }
            fields.Add(new("firmware_version",  b[7].ToString(),                     FirmwareSensitive: true));
            fields.Add(new("model",             $"[{b[4]:X2},{b[5]:X2},{b[6]:X2}]", FirmwareSensitive: false));
            fields.Add(new("turbo_value",       b[15].ToString(),                    FirmwareSensitive: true));
            fields.Add(new("rt_enabled",        b[16].ToString(),                    FirmwareSensitive: true));
            fields.Add(new("rt_plus_enabled",   b[18].ToString(),                    FirmwareSensitive: true));
            fields.Add(new("last_win_value",    b[19].ToString(),                    FirmwareSensitive: true));
            fields.Add(new("rt_auto_match",     b[30].ToString(),                    FirmwareSensitive: true));
            fields.Add(new("auto_match_mode",   b[31].ToString(),                    FirmwareSensitive: true));
            fields.Add(new("last_win_replace",  b[32].ToString(),                    FirmwareSensitive: true));
            return ("IdentityResponse", true, fail, fields);
        }

        // TravelResponse: 0xB7 - unsolicited key-height data
        if (TravelResponse.Matches(b))
        {
            if (b.Length < 4) { fail.Add($"Too short for TravelResponse: {b.Length} < 4"); return ("TravelResponse", false, fail, fields); }
            byte pktIdx = b[3];
            fields.Add(new("packet_index", pktIdx.ToString()));
            if (pktIdx > 4) fail.Add($"packet_index {pktIdx} outside expected range 0–4");
            return ("TravelResponse", fail.Count == 0, fail, fields);
        }

        // WriteKeyPointAcknowledgeStandard: 0xB6
        if (WriteKeyPointAcknowledgeStandard.Matches(b))
            return ("WriteKeyPointAcknowledgeStandard", true, NoFailures, NoFields);

        // CommonConfigAcknowledge: 0xB5
        if (CommonConfigAcknowledge.Matches(b))
            return ("CommonConfigAcknowledge", true, NoFailures, NoFields);

        // ExtendedGatewayResponse: 0xAA - gateway read data or write ack
        if (ExtendedGatewayResponse.Matches(b))
        {
            if (b.Length >= 64)
                fields.Add(new("data_hex", Convert.ToHexString(b.AsSpan(8, 56)).ToLower()));
            return ("ExtendedGatewayResponse", true, NoFailures, fields);
        }

        // RgbAcknowledge: 0xAE
        if (RgbAcknowledge.Matches(b))
            return ("RgbAcknowledge", true, NoFailures, NoFields);

        // ActuationPointResponseHighPrecision: 0xFD 0x08
        if (ActuationPointResponseHighPrecision.Matches(b))
        {
            if (b.Length >= 3) fields.Add(new("section", b[2].ToString()));
            return ("ActuationPointResponseHighPrecision", true, NoFailures, fields);
        }

        // Generic single-byte acks
        return b[0] switch
        {
            0xA0 => ("A0Ack",          true, NoFailures, NoFields),
            0xA7 => ("RTPAck",         true, NoFailures, NoFields),
            0xA8 => ("RTPDownloadAck", true, NoFailures, NoFields),
            0xFC => ("FcAck",          true, NoFailures, NoFields),
            0xFD => ("FdResponse",     true, NoFailures, NoFields),
            _    => ($"Unknown(0x{b[0]:X2})", false, NoFailures, NoFields),
        };
    }
}
