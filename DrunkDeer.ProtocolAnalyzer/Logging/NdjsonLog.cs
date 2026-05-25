using System.Text.Json;
using System.Text.Json.Serialization;
using DrunkDeer.ProtocolAnalyzer.Analysis;
using DrunkDeer.ProtocolAnalyzer.Protocol;

namespace DrunkDeer.ProtocolAnalyzer.Logging;

/// <summary>
/// Writes analysis results as newline-delimited JSON (NDJSON).
/// Each line is a self-contained JSON object with a <c>type</c> discriminator field.
/// Parse with: <c>cat file.ndjson | jq 'select(.type=="packet" and .sequence_failure != null)'</c>
/// </summary>
public sealed class NdjsonLog : IDisposable
{
    static readonly JsonSerializerOptions Opts = new()
    {
        PropertyNamingPolicy     = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition   = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented            = false,
    };

    readonly StreamWriter _w;

    public NdjsonLog(string filePath) => _w = new StreamWriter(filePath, append: false, System.Text.Encoding.UTF8);

    // ── Entry types ───────────────────────────────────────────────────────────

    public void WriteSession(string source, string? firmwareTag, string? deviceFilter)
    {
        Write(new
        {
            type         = "session",
            analyzed_at  = DateTimeOffset.UtcNow,
            source,
            firmware_tag = firmwareTag,
            device_filter= deviceFilter,
        });
    }

    public void WritePacket(PacketEntry e)
    {
        Dictionary<string, string>? fields = e.Fields.Count > 0
            ? e.Fields.ToDictionary(f => f.Name, f => f.Value)
            : null;

        string[]? fwSensitive = e.Fields.Any(f => f.FirmwareSensitive)
            ? e.Fields.Where(f => f.FirmwareSensitive).Select(f => f.Name).ToArray()
            : null;

        Write(new
        {
            type                 = "packet",
            seq                  = e.Seq,
            timestamp_us         = e.TimestampUs,
            direction            = e.Direction,
            bus                  = e.Bus,
            device               = e.Device,
            hex                  = e.Hex,
            message              = e.MessageName,
            structural_ok        = e.StructuralOk,
            structural_failures  = e.StructuralFailures.Count > 0 ? e.StructuralFailures : null,
            sequence_failure     = e.SequenceFailure,
            fields               = fields,
            firmware_sensitive   = fwSensitive,
        });
    }

    public void WriteSummary(SummaryEntry s)
    {
        Write(new
        {
            type                 = "summary",
            total_out            = s.TotalOut,
            total_in             = s.TotalIn,
            classified_out       = s.ClassifiedOut,
            classified_in        = s.ClassifiedIn,
            sequence_errors      = s.SequenceErrors,
            structural_failures  = s.StructuralFailures,
            firmware_fields      = s.FirmwareFields.Count  > 0 ? s.FirmwareFields  : null,
            devices              = s.DeviceCounts.Count    > 0 ? s.DeviceCounts    : null,
        });
    }

    // ── Internal ──────────────────────────────────────────────────────────────

    void Write(object obj) => _w.WriteLine(JsonSerializer.Serialize(obj, Opts));

    public void Dispose() => _w.Dispose();
}
