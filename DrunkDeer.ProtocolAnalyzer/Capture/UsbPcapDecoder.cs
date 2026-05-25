using System.Buffers.Binary;

namespace DrunkDeer.ProtocolAnalyzer.Capture;

/// <summary>
/// Decodes raw USBPcap packets (link type 249) into <see cref="HidPacket"/> records.
/// Only interrupt-transfer packets are decoded; all others return <see langword="null"/>.
/// </summary>
public static class UsbPcapDecoder
{
    // USBPcap header field offsets (USBPCAP_BUFFER_PACKET_HEADER, 27 bytes total)
    const int OffHeaderLen  = 0;   // uint16 LE: total header size (payload starts at headerLen)
    const int OffInfo       = 16;  // uint8: bit 0 = direction (1 = device->host)
    const int OffBus        = 17;  // uint16 LE: USB bus number
    const int OffDevice     = 19;  // uint16 LE: USB device address
    const int OffEndpoint   = 21;  // uint8: endpoint address
    const int OffTransfer   = 22;  // uint8: transfer type
    const int OffDataLength = 23;  // uint32 LE: payload byte count

    const byte TransferInterrupt = 0x01;
    const byte DirIn             = 0x01; // info bit 0 set -> device-to-host (IN)

    public static HidPacket? TryDecode(uint linkType, long timestampUs, byte[] data)
    {
        if (linkType != PcapFileReader.LinkTypeUsbPcap) return null;
        if (data.Length < 27) return null;

        ushort headerLen = BinaryPrimitives.ReadUInt16LittleEndian(data);
        if (headerLen < 27 || headerLen > data.Length) return null;

        byte transfer = data[OffTransfer];
        if (transfer != TransferInterrupt) return null;

        uint dataLen = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(OffDataLength, 4));
        if (dataLen == 0 || headerLen + dataLen > (uint)data.Length) return null;

        byte     info     = data[OffInfo];
        ushort   bus      = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(OffBus, 2));
        ushort   device   = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(OffDevice, 2));
        byte     endpoint = data[OffEndpoint];
        var      direction = (info & DirIn) != 0 ? PacketDirection.DeviceToHost
                                                  : PacketDirection.HostToDevice;

        // DrunkDeer vendor HID reports are 64 bytes on the wire (report ID + 63 protocol bytes).
        // Shorter packets are standard HID keyboard/mouse reports — not protocol traffic.
        if (dataLen < 64) return null;

        var raw = data.AsSpan((int)headerLen, (int)dataLen);
        var payload = NormalizePayload(raw, direction);

        return new HidPacket(direction, bus, device, endpoint, payload, timestampUs);
    }

    // HidTransport.Send() writes [0x04, proto_bytes...] = 64 bytes on the wire.
    // HidTransport.ReadFrom() strips byte[0] after reading; the raw USB capture still has it.
    // Both directions: if byte[0] == 0x04, strip the report ID so callers see protocol bytes.
    static byte[] NormalizePayload(ReadOnlySpan<byte> raw, PacketDirection _)
    {
        if (raw[0] == 0x04) return raw[1..].ToArray();
        return raw.ToArray();
    }
}
