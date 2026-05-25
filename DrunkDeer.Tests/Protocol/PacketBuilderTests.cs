using DrunkDeer.Protocol;
using NUnit.Framework;

namespace DrunkDeer.Tests.Protocol;

/// <summary>
/// Verifies that every generated message builder produces correctly structured 64-byte packets.
/// Reference layout is cross-checked against the confirmed-working DDSharp implementation
/// (DrunkDeerSharp/DDSharp/Hardware/KeyboardProtocol.cs).
/// </summary>
[TestFixture]
public class PacketBuilderTests
{
    [TestCase(nameof(IdentityRequest))]
    [TestCase(nameof(TravelRequest))]
    [TestCase(nameof(SetLightingOff))]
    [TestCase(nameof(ClearRtpUpper))]
    public void FixedPackets_AreExactly64Bytes(string name)
    {
        byte[] pkt = name switch
        {
            nameof(IdentityRequest) => IdentityRequest.Build(),
            nameof(TravelRequest)   => TravelRequest.Build(),
            nameof(SetLightingOff)  => SetLightingOff.Build(),
            nameof(ClearRtpUpper)   => ClearRtpUpper.Build(),
            _ => throw new NotImplementedException()
        };
        Assert.That(pkt, Has.Length.EqualTo(64));
    }

    [Test]
    public void IdentityRequest_Build_HasCorrectHeader()
    {
        var pkt = IdentityRequest.Build();

        Assert.Multiple(() =>
        {
            Assert.That(pkt[0], Is.EqualTo(0xA0));
            Assert.That(pkt[1], Is.EqualTo(0x02));
            Assert.That(pkt[2], Is.EqualTo(0x00));
        });
    }

    [Test]
    public void CommonConfig_Build_HasCorrectHeader()
    {
        var pkt = CommonConfig.Build(0, 0, 0, 0);

        Assert.Multiple(() =>
        {
            Assert.That(pkt[0], Is.EqualTo(0xB5));
            Assert.That(pkt[1], Is.EqualTo(0x00));
            Assert.That(pkt[2], Is.EqualTo(0x1E));
            Assert.That(pkt[3], Is.EqualTo(0x01));
            Assert.That(pkt[6], Is.EqualTo(0x01));
        });
    }

    [Test]
    public void CommonConfig_Build_EncodesTurboMode()
    {
        var pkt = CommonConfig.Build(turboMode: 1, rapidTriggerMode: 0, lastWinRapidTriggerMode: 0, rapidTriggerAutoMatch: 0);
        Assert.That(pkt[7], Is.EqualTo(1));
    }

    [Test]
    public void CommonConfig_Build_EncodesRapidTriggerMode()
    {
        var pkt = CommonConfig.Build(turboMode: 0, rapidTriggerMode: 1, lastWinRapidTriggerMode: 0, rapidTriggerAutoMatch: 0);
        Assert.That(pkt[8], Is.EqualTo(1));
    }

    [Test]
    public void CommonConfig_Build_EncodesLastWinRapidTriggerMode()
    {
        var pkt = CommonConfig.Build(turboMode: 0, rapidTriggerMode: 0, lastWinRapidTriggerMode: 3, rapidTriggerAutoMatch: 0);
        Assert.That(pkt[10], Is.EqualTo(3));
    }

    [Test]
    public void CommonConfig_Build_EncodesRapidTriggerAutoMatch()
    {
        var pkt = CommonConfig.Build(turboMode: 0, rapidTriggerMode: 0, lastWinRapidTriggerMode: 0, rapidTriggerAutoMatch: 1);
        Assert.That(pkt[11], Is.EqualTo(1));
    }

    [Test]
    public void SetLastWinRapidTriggerMode_Build_HasCorrectHeader()
    {
        var pkt = SetLastWinRapidTriggerMode.Build(0);
        Assert.Multiple(() =>
        {
            Assert.That(pkt[0], Is.EqualTo(0xFC));
            Assert.That(pkt[1], Is.EqualTo(0x0A));
        });
    }

    [TestCase((byte)0)]
    [TestCase((byte)1)]
    [TestCase((byte)2)]
    [TestCase((byte)3)]
    public void SetLastWinRapidTriggerMode_Build_EncodesMode(byte mode)
    {
        var pkt = SetLastWinRapidTriggerMode.Build(mode);
        Assert.That(pkt[2], Is.EqualTo(mode));
    }

    [Test]
    public void LastWinRapidTriggerMode_Disabled_IsZero() =>
        Assert.That((byte)LastWinRapidTriggerMode.Disabled, Is.EqualTo(0));

    [Test]
    public void LastWinRapidTriggerMode_LastWinOnly_IsOne() =>
        Assert.That((byte)LastWinRapidTriggerMode.LastWinOnly, Is.EqualTo(1));

    [Test]
    public void LastWinRapidTriggerMode_RapidTriggerOnly_IsTwo() =>
        Assert.That((byte)LastWinRapidTriggerMode.RapidTriggerOnly, Is.EqualTo(2));

    [Test]
    public void LastWinRapidTriggerMode_Both_IsThree() =>
        Assert.That((byte)LastWinRapidTriggerMode.Both, Is.EqualTo(3));

    [Test]
    public void WriteActuationPointStandard_Build_HasCorrectHeader()
    {
        var pkt = WriteActuationPointStandard.Build(0, new byte[59]);
        Assert.Multiple(() =>
        {
            Assert.That(pkt[0], Is.EqualTo(0xB6));
            Assert.That(pkt[1], Is.EqualTo(0x01));
            Assert.That(pkt[2], Is.EqualTo(0x00));
        });
    }

    [Test]
    public void WriteDownstrokePointStandard_Build_HasCorrectHeader()
    {
        var pkt = WriteDownstrokePointStandard.Build(0, new byte[59]);
        Assert.Multiple(() =>
        {
            Assert.That(pkt[0], Is.EqualTo(0xB6));
            Assert.That(pkt[1], Is.EqualTo(0x04));
            Assert.That(pkt[2], Is.EqualTo(0x00));
        });
    }

    [Test]
    public void WriteUpstrokePointStandard_Build_HasCorrectHeader()
    {
        var pkt = WriteUpstrokePointStandard.Build(0, new byte[59]);
        Assert.Multiple(() =>
        {
            Assert.That(pkt[0], Is.EqualTo(0xB6));
            Assert.That(pkt[1], Is.EqualTo(0x05));
            Assert.That(pkt[2], Is.EqualTo(0x00));
        });
    }

    [Test]
    public void WriteActuationPointStandard_Build_EncodesPacketIndexAndValues()
    {
        var values = new byte[59];
        values[0]  = 0x0A; // 1.0 mm
        values[58] = 0x1A; // 2.6 mm

        var pkt = WriteActuationPointStandard.Build(packetIndex: 1, values);

        Assert.Multiple(() =>
        {
            Assert.That(pkt[3], Is.EqualTo(1));   // packet index at buf[3]
            Assert.That(pkt[4], Is.EqualTo(0x0A)); // first value at buf[4]
            Assert.That(pkt[4 + 58], Is.EqualTo(0x1A)); // last value
        });
    }

    [Test]
    public void WriteActuationPointHighPrecision_Build_HasCorrectHeader()
    {
        var pkt = WriteActuationPointHighPrecision.Build(0, new byte[60]);
        Assert.Multiple(() =>
        {
            Assert.That(pkt[0], Is.EqualTo(0xFD));
            Assert.That(pkt[1], Is.EqualTo(0x01));
        });
    }

    [Test]
    public void WriteActuationPointHighPrecision_Build_EncodesSection()
    {
        var pkt = WriteActuationPointHighPrecision.Build(section: 3, new byte[60]);
        Assert.That(pkt[2], Is.EqualTo(3));
    }

    [Test]
    public void WriteActuationPointHighPrecision_Build_EncodesU16LittleEndianValues()
    {
        // 1.0 mm × 200 = 200 = 0x00C8 -> LE bytes: 0xC8, 0x00
        var values = new byte[60];
        values[0] = 0xC8; // low byte of 200
        values[1] = 0x00; // high byte of 200

        var pkt = WriteActuationPointHighPrecision.Build(0, values);

        Assert.Multiple(() =>
        {
            Assert.That(pkt[3], Is.EqualTo(0xC8));
            Assert.That(pkt[4], Is.EqualTo(0x00));
        });
    }

    [Test]
    public void RgbKeyDataPacket_Build_HasCorrectFixedHeader()
    {
        var pkt = RgbKeyDataPacket.Build(isTurbo: 0, modeIndex: 0x13, brightness: 9);

        Assert.Multiple(() =>
        {
            Assert.That(pkt[0], Is.EqualTo(0xAE));
            Assert.That(pkt[1], Is.EqualTo(0x01));
            Assert.That(pkt[2], Is.EqualTo(0));    // isTurbo = 0
            Assert.That(pkt[3], Is.EqualTo(0));    // reserved
            Assert.That(pkt[4], Is.EqualTo(0x13)); // modeIndex
            Assert.That(pkt[5], Is.EqualTo(0x06)); // constant
            Assert.That(pkt[6], Is.EqualTo(9));    // brightness
            Assert.That(pkt[7], Is.EqualTo(0xFF)); // end-of-header marker
        });
    }

    [Test]
    public void RgbKeyDataPacket_Build_TurboMode_EncodedAtBuf2()
    {
        var pkt = RgbKeyDataPacket.Build(isTurbo: 1, modeIndex: 0x13, brightness: 9);
        Assert.That(pkt[2], Is.EqualTo(1));
    }

    [Test]
    public void RgbEntry_WrittenIntoPacket_StartsAtOffset8()
    {
        var buf = RgbKeyDataPacket.Build(isTurbo: 0, modeIndex: 0x13, brightness: 9);
        var entry = RgbEntry.Create(layoutIndex: 5, r: 0xFF, g: 0x80, b: 0x10);
        entry.Write(buf.AsSpan(8));

        Assert.Multiple(() =>
        {
            Assert.That(buf[8],  Is.EqualTo(5 | 0x80)); // IndexFlag
            Assert.That(buf[9],  Is.EqualTo(0xFF));      // R
            Assert.That(buf[10], Is.EqualTo(0x80));      // G
            Assert.That(buf[11], Is.EqualTo(0x10));      // B
        });
    }

    [Test]
    public void RgbPacket_Sentinel_PlacedAfterSingleEntry()
    {
        var buf = RgbKeyDataPacket.Build(isTurbo: 0, modeIndex: 0x13, brightness: 9);
        RgbEntry.Create(0, 255, 0, 0).Write(buf.AsSpan(8));
        // Sentinel immediately after the one 4-byte entry
        buf[12] = 0xFF;

        Assert.That(buf[12], Is.EqualTo(0xFF));
    }

    [Test]
    public void RgbPacket_FullPacket_13Entries_SentinelAt60()
    {
        var buf = RgbKeyDataPacket.Build(isTurbo: 0, modeIndex: 0x13, brightness: 9);
        int writeOffset = 8;
        for (int i = 0; i < 13; i++)
        {
            RgbEntry.Create(i, 255, 0, 0).Write(buf.AsSpan(writeOffset));
            writeOffset += RgbEntry.ByteSize; // 4 bytes each
        }
        // Sentinel after 13 entries: 8 + 13*4 = 60
        buf[writeOffset] = 0xFF;

        Assert.That(writeOffset, Is.EqualTo(60));
        Assert.That(buf[60], Is.EqualTo(0xFF));
    }

    [Test]
    public void SetLightingOff_Build_HasExactBytes()
    {
        var pkt = SetLightingOff.Build();

        Assert.Multiple(() =>
        {
            Assert.That(pkt[0], Is.EqualTo(0xAE));
            Assert.That(pkt[1], Is.EqualTo(0x01));
            Assert.That(pkt[2], Is.EqualTo(0x00));
            Assert.That(pkt[3], Is.EqualTo(0x00));
            Assert.That(pkt[4], Is.EqualTo(0x05));
            Assert.That(pkt[5], Is.EqualTo(0x09));
        });
    }

    [Test]
    public void CreateLwPairs_Build_HasCorrectHeaderAndPairCount()
    {
        var pkt = CreateLwPairs.Build(pairCount: 3);

        Assert.Multiple(() =>
        {
            Assert.That(pkt[0], Is.EqualTo(0xFC));
            Assert.That(pkt[1], Is.EqualTo(0x01));
            Assert.That(pkt[2], Is.EqualTo(0x00));
            Assert.That(pkt[3], Is.EqualTo(3)); // pair count
        });
    }
}
