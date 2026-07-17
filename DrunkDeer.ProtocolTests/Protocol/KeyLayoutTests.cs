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

	/// <summary>
	/// The X60 Future used to borrow LayoutG60, and a test here used to pin that. It was
	/// wrong: the X60 is a 64-key board whose Esc is slot 0 (the G60 puts Esc at 21) and
	/// which has a Del and an arrow cluster the G60 has no tokens for at all. It now has
	/// its own table, so what is worth pinning is that it does NOT match the G60's.
	/// </summary>
	[Test]
	public void GetLayout_X60Future_DiffersFromG60() =>
		Assert.That(KeyLayout.GetLayout(ModelSlugs.X60Future),
			Is.Not.EqualTo(KeyLayout.GetLayout(ModelSlugs.G60)));

	[Test]
	public void GetLayout_X60Future_HasTheKeysTheBoardActuallyHas()
	{
		var x60 = KeyLayout.GetLayout(ModelSlugs.X60Future);
		Assert.Multiple(() =>
		{
			Assert.That(x60[0], Is.EqualTo("ESC"), "Esc is slot 0, not the G60's slot 21");
			Assert.That(x60[21], Is.Empty, "the X60 has no backtick key");
			Assert.That(x60[14], Is.EqualTo("DELETE"), "Del sits right of ArrowUp on the shift row");
			Assert.That(x60[98], Is.EqualTo("ARR_UP"));
			Assert.That(x60[119], Is.EqualTo("ARR_L"));
			Assert.That(x60[120], Is.EqualTo("ARR_DW"));
			Assert.That(x60[121], Is.EqualTo("ARR_R"));
			Assert.That(x60[117], Is.Empty, "the X60 has no Menu/Fn2 key");
			Assert.That(x60[118], Is.Empty, "the X60 has no right Ctrl");
		});
	}

	[Test]
	public void GetRgbIndices_X60Future_Lights64Keys() =>
		Assert.That(KeyLayout.GetRgbIndices(ModelSlugs.X60Future, "ansi"), Has.Length.EqualTo(64));

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
