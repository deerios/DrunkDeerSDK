using DrunkDeer.Protocol;

namespace DrunkDeer.ProtocolAnalyzer.Capture;

/// <summary>A decoded HID interrupt-transfer packet from a USB capture.</summary>
public sealed record HidPacket(
    PacketDirection Direction,
    ushort Bus,
    ushort Device,
    byte Endpoint,
    byte[] Payload,       // always 64 bytes for DrunkDeer; report-ID byte stripped
    long TimestampUs      // µs since Unix epoch
);
