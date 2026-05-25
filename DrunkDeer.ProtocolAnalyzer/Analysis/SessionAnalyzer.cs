using DrunkDeer.ProtocolAnalyzer.Capture;
using DrunkDeer.ProtocolAnalyzer.Protocol;

namespace DrunkDeer.ProtocolAnalyzer.Analysis;

/// <summary>Classified, annotated record for a single decoded HID packet.</summary>
public sealed record PacketEntry(
    int    Seq,
    long   TimestampUs,
    string Direction,        // "out" | "in"
    ushort Bus,
    ushort Device,
    string Hex,              // full 64-byte payload as lowercase hex
    string MessageName,
    bool   StructuralOk,
    IReadOnlyList<string>         StructuralFailures,
    IReadOnlyList<ExtractedField> Fields,
    string?                       SequenceFailure  // null when sequence is correct; populated for IN only
);

/// <summary>Aggregate statistics and firmware-sensitive field snapshot for the session.</summary>
public sealed record SummaryEntry(
    int TotalOut,
    int TotalIn,
    int ClassifiedOut,
    int ClassifiedIn,
    int SequenceErrors,
    int StructuralFailures,
    IReadOnlyDictionary<string, string> FirmwareFields,
    IReadOnlyDictionary<string, int>    DeviceCounts  // "bus.device" -> interrupt-packet count
);

/// <summary>
/// Processes a stream of <see cref="HidPacket"/> records in order, classifies each one via
/// <see cref="ProtocolOracle"/>, and validates OUT->IN sequences against protocol expectations.
/// </summary>
public sealed class SessionAnalyzer
{
    readonly ushort? _busFilter;
    readonly ushort? _deviceFilter;

    OracleResult? _pendingOut;      // last OUT classification awaiting a response
    int           _seq;
    int           _totalOut, _totalIn, _classifiedOut, _classifiedIn, _seqErrors, _structFail;

    readonly Dictionary<string, string> _fwFields    = new();
    readonly Dictionary<string, int>    _devCounts   = new();

    public SessionAnalyzer(ushort? busFilter = null, ushort? deviceFilter = null)
    {
        _busFilter    = busFilter;
        _deviceFilter = deviceFilter;
    }

    /// <summary>
    /// Processes one packet and returns a <see cref="PacketEntry"/>, or <see langword="null"/>
    /// if the packet was filtered out by the bus/device filter.
    /// </summary>
    public PacketEntry? Process(HidPacket pkt)
    {
        if (_busFilter.HasValue    && pkt.Bus    != _busFilter.Value)    return null;
        if (_deviceFilter.HasValue && pkt.Device != _deviceFilter.Value) return null;

        _seq++;
        string devKey = $"{pkt.Bus}.{pkt.Device}";
        _devCounts[devKey] = _devCounts.GetValueOrDefault(devKey) + 1;

        var oracle = ProtocolOracle.Classify(pkt.Payload, pkt.Direction);
        bool isOut = pkt.Direction == PacketDirection.HostToDevice;
        string hex = Convert.ToHexString(pkt.Payload).ToLower();

        if (isOut)
        {
            _totalOut++;
            if (!oracle.MessageName.StartsWith("Unknown")) _classifiedOut++;
            _pendingOut = oracle;

            return new PacketEntry(_seq, pkt.TimestampUs, "out", pkt.Bus, pkt.Device, hex,
                oracle.MessageName, oracle.StructuralOk, oracle.StructuralFailures, oracle.Fields,
                SequenceFailure: null);
        }
        else
        {
            _totalIn++;
            if (!oracle.MessageName.StartsWith("Unknown")) _classifiedIn++;
            if (!oracle.StructuralOk) _structFail++;

            string? seqFail = null;

            // 0xB7 TravelResponse arrives unsolicited on the data stream when the host
            // is in a polling loop. Only validate sequence when the last OUT was actually
            // a TravelRequest; otherwise leave _pendingOut unconsumed.
            bool isUnsolicited = oracle.MessageName == "TravelResponse"
                              && _pendingOut?.ExpectedResponseName != "TravelResponse";

            if (!isUnsolicited && _pendingOut is not null)
            {
                seqFail = ProtocolOracle.ValidateSequence(_pendingOut, oracle, pkt.Payload);
                if (seqFail is not null) _seqErrors++;
                _pendingOut = null;
            }

            foreach (var f in oracle.Fields)
                if (f.FirmwareSensitive) _fwFields[f.Name] = f.Value;

            return new PacketEntry(_seq, pkt.TimestampUs, "in", pkt.Bus, pkt.Device, hex,
                oracle.MessageName, oracle.StructuralOk, oracle.StructuralFailures, oracle.Fields,
                seqFail);
        }
    }

    public SummaryEntry GetSummary() =>
        new(_totalOut, _totalIn, _classifiedOut, _classifiedIn,
            _seqErrors, _structFail, _fwFields, _devCounts);
}
