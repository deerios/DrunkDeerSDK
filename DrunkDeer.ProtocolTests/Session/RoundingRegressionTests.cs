using DrunkDeer.Protocol;
using NUnit.Framework;

namespace DrunkDeer.ProtocolTests.Session;

/// <summary>
/// Midpoint-rounding regression tests for the mm-to-raw conversions that live outside
/// <see cref="KeyboardSession"/> (API-6). All firmware unit conversions must round
/// half away from zero, not half-to-even (the .NET <see cref="System.Math.Round(double)"/>
/// default), since values like 0.005 mm are valid, minimal, non-zero user inputs.
/// </summary>
[TestFixture]
public class RoundingRegressionTests
{
	// KeyTriggerConfig.MmToRaw: 1 unit = 0.01 mm, so raw = Round(mm x 100), clamped to [1, 512].
	[TestCase(0.005f, 1)] // 0.005 x 100 = 0.5 -> rounds up to 1, not down to 0 (banker's rounding)
	[TestCase(0.015f, 2)] // 0.015 x 100 = 1.5 -> rounds up to 2, not down to 2 by chance-of-even
	[TestCase(0.025f, 3)] // 0.025 x 100 = 2.5 -> rounds up to 3, not down to 2 (banker's rounding)
	public void KeyTriggerConfig_FromMm_RoundsHalfAwayFromZero(float mm, int expectedRaw)
	{
		var config = KeyTriggerConfig.FromMm(actuationMm: mm, rtPressMm: mm, rtReleaseMm: mm);
		Assert.That(config.Actuation, Is.EqualTo(expectedRaw));
		Assert.That(config.RtPress, Is.EqualTo(expectedRaw));
		Assert.That(config.RtRelease, Is.EqualTo(expectedRaw));
	}

	// DynamicKeystrokeEntry.MmToUnit: 1 unit = 0.1 mm, so raw = Round(mm x 10), clamped to [0, 255].
	[TestCase(0.05f, (byte)1)] // 0.05 x 10 = 0.5 -> rounds up to 1, not down to 0 (banker's rounding)
	[TestCase(0.15f, (byte)2)] // 0.15 x 10 = 1.5 -> rounds up to 2
	[TestCase(0.25f, (byte)3)] // 0.25 x 10 = 2.5 -> rounds up to 3, not down to 2 (banker's rounding)
	public void DynamicKeystrokeEntry_WithPointsMm_RoundsHalfAwayFromZero(float mm, byte expectedUnit)
	{
		var entry = new DynamicKeystrokeEntry().WithPointsMm(mm, mm, mm, mm);
		Assert.That(entry.Points, Is.EqualTo(new[] { expectedUnit, expectedUnit, expectedUnit, expectedUnit }));
	}
}
