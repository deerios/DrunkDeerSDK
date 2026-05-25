using DrunkDeer.Protocol;
using NUnit.Framework;

namespace DrunkDeer.Tests.Protocol;

[TestFixture]
public class StructTests
{

    [Test]
    public void RgbEntry_Create_SetsIndexFlagWithHighBit()
    {
        var entry = RgbEntry.Create(layoutIndex: 5, r: 0, g: 0, b: 0);
        Assert.That(entry.IndexFlag, Is.EqualTo(5 | 0x80));
    }

    [Test]
    public void RgbEntry_Create_LayoutIndex_Zero_HasHighBitOnly()
    {
        var entry = RgbEntry.Create(layoutIndex: 0, r: 0, g: 0, b: 0);
        Assert.That(entry.IndexFlag, Is.EqualTo(0x80));
    }

    [Test]
    public void RgbEntry_Create_MaxLayoutIndex_126()
    {
        var entry = RgbEntry.Create(layoutIndex: 126, r: 0, g: 0, b: 0);
        Assert.That(entry.IndexFlag, Is.EqualTo(126 | 0x80));
    }

    [Test]
    public void RgbEntry_Create_SetsRgbChannels()
    {
        var entry = RgbEntry.Create(layoutIndex: 0, r: 0xAA, g: 0xBB, b: 0xCC);

        Assert.Multiple(() =>
        {
            Assert.That(entry.R, Is.EqualTo(0xAA));
            Assert.That(entry.G, Is.EqualTo(0xBB));
            Assert.That(entry.B, Is.EqualTo(0xCC));
        });
    }

    [Test]
    public void RgbEntry_Write_ProducesCorrectBytes()
    {
        var entry = RgbEntry.Create(layoutIndex: 10, r: 0x11, g: 0x22, b: 0x33);
        var buf = new byte[4];
        entry.Write(buf);

        Assert.Multiple(() =>
        {
            Assert.That(buf[0], Is.EqualTo(10 | 0x80)); // IndexFlag
            Assert.That(buf[1], Is.EqualTo(0x11));       // R
            Assert.That(buf[2], Is.EqualTo(0x22));       // G
            Assert.That(buf[3], Is.EqualTo(0x33));       // B
        });
    }

    [Test]
    public void RgbEntry_Read_DecodesAllFields()
    {
        byte[] src = [0x8A, 0x11, 0x22, 0x33]; // IndexFlag = 10|0x80, R/G/B
        var entry = RgbEntry.Read(src);

        Assert.Multiple(() =>
        {
            Assert.That(entry.IndexFlag, Is.EqualTo(0x8A));
            Assert.That(entry.R,         Is.EqualTo(0x11));
            Assert.That(entry.G,         Is.EqualTo(0x22));
            Assert.That(entry.B,         Is.EqualTo(0x33));
        });
    }

    [Test]
    public void RgbEntry_WriteRead_RoundTrip()
    {
        var original = RgbEntry.Create(layoutIndex: 42, r: 200, g: 100, b: 50);
        var buf = new byte[4];
        original.Write(buf);
        var restored = RgbEntry.Read(buf);

        Assert.Multiple(() =>
        {
            Assert.That(restored.IndexFlag, Is.EqualTo(original.IndexFlag));
            Assert.That(restored.R,         Is.EqualTo(original.R));
            Assert.That(restored.G,         Is.EqualTo(original.G));
            Assert.That(restored.B,         Is.EqualTo(original.B));
        });
    }

    [Test]
    public void RgbEntry_ByteSize_Is4()
    {
        Assert.That(RgbEntry.ByteSize, Is.EqualTo(4));
    }

    [Test]
    public void LastWinPairEntry_Write_ProducesCorrectBytes()
    {
        var entry = new LastWinPairEntry(mainSlot: 16, triggerSlot: 26);
        var buf = new byte[4];
        entry.Write(buf);

        Assert.Multiple(() =>
        {
            Assert.That(buf[0], Is.EqualTo(16)); // mainSlot
            Assert.That(buf[1], Is.EqualTo(26)); // triggerSlot
            Assert.That(buf[2], Is.EqualTo(0));  // reserved
            Assert.That(buf[3], Is.EqualTo(0));  // reserved
        });
    }

    [Test]
    public void LastWinPairEntry_Read_DecodesSlots()
    {
        byte[] src = [0x10, 0x1A, 0x00, 0x00];
        var entry = LastWinPairEntry.Read(src);

        Assert.Multiple(() =>
        {
            Assert.That(entry.MainSlot,    Is.EqualTo(0x10));
            Assert.That(entry.TriggerSlot, Is.EqualTo(0x1A));
        });
    }

    [Test]
    public void LastWinPairEntry_WriteRead_RoundTrip()
    {
        var original = new LastWinPairEntry(mainSlot: 5, triggerSlot: 100);
        var buf = new byte[4];
        original.Write(buf);
        var restored = LastWinPairEntry.Read(buf);

        Assert.Multiple(() =>
        {
            Assert.That(restored.MainSlot,    Is.EqualTo(original.MainSlot));
            Assert.That(restored.TriggerSlot, Is.EqualTo(original.TriggerSlot));
        });
    }

    [Test]
    public void LastWinPairEntry_ByteSize_Is4()
    {
        Assert.That(LastWinPairEntry.ByteSize, Is.EqualTo(4));
    }

    [Test]
    public void LastWinPairEntry_ReservedBytes_AreZeroAfterWrite()
    {
        var entry = new LastWinPairEntry(mainSlot: 1, triggerSlot: 2);
        var buf = new byte[4];
        entry.Write(buf);

        Assert.That(buf[2], Is.EqualTo(0));
        Assert.That(buf[3], Is.EqualTo(0));
    }
}
