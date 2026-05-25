using DrunkDeer.Protocol;
using NUnit.Framework;

namespace DrunkDeer.Tests.Session;

/// <summary>
/// Tests for the internal mm-to-raw-unit conversion helpers on <see cref="KeyboardSession"/>.
/// Standard precision:     1 unit = 0.1 mm  -> multiply mm × 10
/// High precision:         1 unit = 0.005 mm -> multiply mm × 200
/// </summary>
[TestFixture]
public class DepthConversionTests
{
    [TestCase(1.0f,  (byte)10)]
    [TestCase(0.1f,  (byte)1)]
    [TestCase(2.0f,  (byte)20)]
    [TestCase(3.8f,  (byte)38)]
    [TestCase(0.5f,  (byte)5)]
    [TestCase(0.15f, (byte)2)] // rounds to nearest: 0.15 × 10 = 1.5 -> 2
    [TestCase(0.14f, (byte)1)] // rounds down: 0.14 × 10 = 1.4 -> 1
    public void MmToStandardUnit_ConvertsCorrectly(float mm, byte expected)
    {
        Assert.That(KeyboardSession.MmToStandardUnit(mm), Is.EqualTo(expected));
    }

    [Test]
    public void MmToStandardUnit_NegativeInput_ClampsToZero()
    {
        Assert.That(KeyboardSession.MmToStandardUnit(-1.0f), Is.EqualTo(0));
    }

    [Test]
    public void MmToStandardUnit_VeryLargeInput_ClampsTo255()
    {
        Assert.That(KeyboardSession.MmToStandardUnit(100f), Is.EqualTo(255));
    }

    [TestCase(1.0f,   (ushort)200)]
    [TestCase(0.005f, (ushort)1)]
    [TestCase(0.1f,   (ushort)20)]
    [TestCase(2.0f,   (ushort)400)]
    [TestCase(3.8f,   (ushort)760)]
    [TestCase(0.5f,   (ushort)100)]
    [TestCase(0.0075f,(ushort)2)] // rounds to nearest: 0.0075 × 200 = 1.5 -> 2
    [TestCase(0.0074f,(ushort)1)] // rounds down: 0.0074 × 200 = 1.48 -> 1
    public void MmToHighPrecisionUnit_ConvertsCorrectly(float mm, ushort expected)
    {
        Assert.That(KeyboardSession.MmToHighPrecisionUnit(mm), Is.EqualTo(expected));
    }

    [Test]
    public void MmToHighPrecisionUnit_NegativeInput_ClampsToZero()
    {
        Assert.That(KeyboardSession.MmToHighPrecisionUnit(-1.0f), Is.EqualTo(0));
    }

    [Test]
    public void MmToHighPrecisionUnit_VeryLargeInput_ClampsTo65535()
    {
        Assert.That(KeyboardSession.MmToHighPrecisionUnit(1000f), Is.EqualTo(65535));
    }

    [TestCase(1.0f)]
    [TestCase(0.5f)]
    [TestCase(2.0f)]
    [TestCase(3.8f)]
    public void StandardUnit_RoundTrip_WithinHalfUnit(float mm)
    {
        byte unit = KeyboardSession.MmToStandardUnit(mm);
        float back = unit / 10f;
        Assert.That(back, Is.EqualTo(mm).Within(0.05f)); // ±0.5 standard unit
    }

    [TestCase(1.0f)]
    [TestCase(0.5f)]
    [TestCase(2.0f)]
    [TestCase(3.8f)]
    public void HighPrecisionUnit_RoundTrip_WithinHalfUnit(float mm)
    {
        ushort unit = KeyboardSession.MmToHighPrecisionUnit(mm);
        float back = unit / 200f;
        Assert.That(back, Is.EqualTo(mm).Within(0.0025f)); // ±0.5 HP unit
    }
}
