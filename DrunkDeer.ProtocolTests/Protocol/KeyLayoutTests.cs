using DrunkDeer.Protocol;
using NUnit.Framework;

namespace DrunkDeer.ProtocolTests.Protocol;

/// <summary>
/// API-5 regression coverage: X60 Future must not silently inherit A75's layout/RGB tables,
/// and an unrecognised model slug must fail loudly rather than falling back to A75.
/// </summary>
[TestFixture]
public class KeyLayoutTests
{
	[Test]
	public void GetLayout_X60Future_DiffersFromA75()
	{
		var x60Layout = KeyLayout.GetLayout(ModelSlugs.X60Future);
		var a75Layout = KeyLayout.GetLayout(ModelSlugs.A75);
		Assert.That(x60Layout, Is.Not.EqualTo(a75Layout));
	}

	[Test]
	public void GetLayout_X60Future_MatchesG60()
	{
		Assert.That(KeyLayout.GetLayout(ModelSlugs.X60Future), Is.EqualTo(KeyLayout.GetLayout(ModelSlugs.G60)));
	}

	[Test]
	public void GetRgbIndices_X60Future_DiffersFromA75()
	{
		var x60Rgb = KeyLayout.GetRgbIndices(ModelSlugs.X60Future, "ansi");
		var a75Rgb = KeyLayout.GetRgbIndices(ModelSlugs.A75, "ansi");
		Assert.That(x60Rgb, Is.Not.EqualTo(a75Rgb));
	}

	[Test]
	public void GetLayout_UnknownSlug_Throws() =>
		Assert.Throws<NotSupportedException>(() => KeyLayout.GetLayout("not_a_real_model"));

	[Test]
	public void GetRgbIndices_UnknownSlug_Throws() =>
		Assert.Throws<NotSupportedException>(() => KeyLayout.GetRgbIndices("not_a_real_model", "ansi"));
}
