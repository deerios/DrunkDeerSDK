using System.Buffers.Binary;

namespace DrunkDeer.ProtocolAnalyzer.Capture;

/// <summary>
/// Pure .NET reader for <c>.pcap</c> and <c>.pcapng</c> files (no Npcap required).
/// Yields raw packet data with link type and timestamp for each captured frame.
/// </summary>
public static class PcapFileReader
{
    /// <summary>Link-type value assigned to USBPcap captures (Windows, DLT_USBPCAP = 249).</summary>
    public const uint LinkTypeUsbPcap = 249;

    public static IEnumerable<(uint LinkType, long TimestampUs, byte[] Data)> Read(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        Span<byte> peek = stackalloc byte[4];
        if (stream.Read(peek) < 4) yield break;
        stream.Seek(0, SeekOrigin.Begin);

        uint magic = BinaryPrimitives.ReadUInt32LittleEndian(peek);
        var source = magic == 0x0A0D0D0A ? ReadPcapng(stream) : ReadPcap(stream);
        foreach (var p in source) yield return p;
    }

    /// <summary>
    /// Reads a PCAP stream (not seekable, e.g. a pipe from USBPcapCMD.exe stdout).
    /// Only classic PCAP format is supported; PCAPNG requires seeking and is not expected here.
    /// </summary>
    public static IEnumerable<(uint LinkType, long TimestampUs, byte[] Data)> ReadStream(Stream stream)
    {
        foreach (var p in ReadPcap(stream)) yield return p;
    }

    // ── PCAP ──────────────────────────────────────────────────────────────────

    static IEnumerable<(uint, long, byte[])> ReadPcap(Stream s)
    {
        var h = new byte[24];
        s.ReadExactly(h);
        uint magic = BinaryPrimitives.ReadUInt32LittleEndian(h);

        bool le = magic == 0xa1b2c3d4 || magic == 0xa1b23c4d;
        bool ns = magic == 0xa1b23c4d || magic == 0x4d3cb2a1;
        if (magic != 0xa1b2c3d4 && magic != 0xd4c3b2a1 && magic != 0xa1b23c4d && magic != 0x4d3cb2a1)
            throw new InvalidDataException($"Unrecognised PCAP magic 0x{magic:X8}.");

        uint lt = le ? BinaryPrimitives.ReadUInt32LittleEndian(h.AsSpan(20))
                     : BinaryPrimitives.ReadUInt32BigEndian(h.AsSpan(20));

        var rec = new byte[16];
        while (s.Read(rec, 0, 16) == 16)
        {
            uint sec  = le ? BinaryPrimitives.ReadUInt32LittleEndian(rec)             : BinaryPrimitives.ReadUInt32BigEndian(rec);
            uint frac = le ? BinaryPrimitives.ReadUInt32LittleEndian(rec.AsSpan(4))   : BinaryPrimitives.ReadUInt32BigEndian(rec.AsSpan(4));
            uint len  = le ? BinaryPrimitives.ReadUInt32LittleEndian(rec.AsSpan(8))   : BinaryPrimitives.ReadUInt32BigEndian(rec.AsSpan(8));
            long ts   = ns ? (long)sec * 1_000_000 + frac / 1000 : (long)sec * 1_000_000 + frac;

            var data = new byte[len];
            s.ReadExactly(data);
            yield return (lt, ts, data);
        }
    }

    // ── PCAPNG ────────────────────────────────────────────────────────────────
    // Assumes LE byte order (standard for all Windows/USBPcap captures).

    static IEnumerable<(uint, long, byte[])> ReadPcapng(Stream s)
    {
        bool le = true;
        var ltList   = new List<uint>();
        var tpuList  = new List<double>(); // ticks-per-microsecond per interface

        var hdr = new byte[8];
        while (s.Read(hdr, 0, 8) == 8)
        {
            uint bt = le ? BinaryPrimitives.ReadUInt32LittleEndian(hdr)           : BinaryPrimitives.ReadUInt32BigEndian(hdr);
            uint bl = le ? BinaryPrimitives.ReadUInt32LittleEndian(hdr.AsSpan(4)) : BinaryPrimitives.ReadUInt32BigEndian(hdr.AsSpan(4));

            if (bl < 12) throw new InvalidDataException($"PCAPNG block length {bl} is too small.");

            var body     = new byte[bl - 12];
            var trailing = new byte[4];
            s.ReadExactly(body);
            s.ReadExactly(trailing);

            switch (bt)
            {
                case 0x0A0D0D0A: // Section Header Block
                    le = BinaryPrimitives.ReadUInt32LittleEndian(body) == 0x1A2B3C4D;
                    ltList.Clear();
                    tpuList.Clear();
                    break;

                case 1: // Interface Description Block
                    ltList.Add(le ? BinaryPrimitives.ReadUInt16LittleEndian(body.AsSpan(0, 2))
                                  : BinaryPrimitives.ReadUInt16BigEndian(body.AsSpan(0, 2)));
                    tpuList.Add(ParseIfTsResol(body, optOffset: 8, le));
                    break;

                case 6: // Enhanced Packet Block
                {
                    uint iid = le ? BinaryPrimitives.ReadUInt32LittleEndian(body)          : BinaryPrimitives.ReadUInt32BigEndian(body);
                    uint thi = le ? BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(4)) : BinaryPrimitives.ReadUInt32BigEndian(body.AsSpan(4));
                    uint tlo = le ? BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(8)) : BinaryPrimitives.ReadUInt32BigEndian(body.AsSpan(8));
                    uint cap = le ? BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(12)): BinaryPrimitives.ReadUInt32BigEndian(body.AsSpan(12));

                    long ticks = ((long)thi << 32) | tlo;
                    double tpu = iid < (uint)tpuList.Count ? tpuList[(int)iid] : 1.0;
                    long ts    = (long)(ticks / tpu);

                    uint linkType = iid < (uint)ltList.Count ? ltList[(int)iid] : 0;

                    int avail = Math.Min((int)cap, body.Length - 20);
                    if (avail > 0)
                        yield return (linkType, ts, body.AsSpan(20, avail).ToArray());
                    break;
                }
                // All other block types (SPB, NRB, ISB, DSB, etc.) are ignored.
            }
        }
    }

    // Parses the if_tsresol (option code 9) from IDB options to get ticks-per-microsecond.
    // Default: 10^-6 s/tick -> 1.0 tick/µs.
    static double ParseIfTsResol(byte[] body, int optOffset, bool le)
    {
        while (optOffset + 4 <= body.Length)
        {
            ushort code = le ? BinaryPrimitives.ReadUInt16LittleEndian(body.AsSpan(optOffset, 2))
                             : BinaryPrimitives.ReadUInt16BigEndian(body.AsSpan(optOffset, 2));
            ushort len  = le ? BinaryPrimitives.ReadUInt16LittleEndian(body.AsSpan(optOffset + 2, 2))
                             : BinaryPrimitives.ReadUInt16BigEndian(body.AsSpan(optOffset + 2, 2));
            if (code == 0) break; // end of options

            if (code == 9 && len >= 1 && optOffset + 4 < body.Length)
            {
                byte r = body[optOffset + 4];
                // bit 7=0: base-10 (10^r s/tick); bit 7=1: base-2 (2^(r&0x7F) s/tick)
                double tps = (r & 0x80) == 0 ? Math.Pow(10, r & 0x7F) : Math.Pow(2, r & 0x7F);
                return tps / 1_000_000.0;
            }
            optOffset += 4 + ((len + 3) & ~3); // options are 32-bit padded
        }
        return 1.0;
    }
}
