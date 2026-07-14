using DrunkDeer.Cli.Infrastructure;
using DrunkDeer.Protocol;
using NUnit.Framework;

namespace DrunkDeer.Cli.Tests;

[TestFixture]
public class KeyArgParserTests
{
	[Test]
	public void Wasd_Group_Expands()
	{
		Assert.That(KeyArgParser.Parse("wasd"), Is.EquivalentTo(new[] { DDKey.W, DDKey.A, DDKey.S, DDKey.D }));
	}

	[Test]
	public void CommaList_WithAliasesAndLetters()
	{
		var keys = KeyArgParser.Parse("esc,space,q");
		Assert.That(keys, Is.EquivalentTo(new[] { DDKey.Escape, DDKey.Space, DDKey.Q }));
	}

	[Test]
	public void FnRange_IsInclusive()
	{
		var keys = KeyArgParser.Parse("F1-F12");
		Assert.That(keys, Has.Length.EqualTo(12));
		Assert.That(keys, Does.Contain(DDKey.F1).And.Contain(DDKey.F12));
	}

	[Test]
	public void Digits_MapToNumberRow()
	{
		Assert.That(KeyArgParser.Parse("1"), Is.EquivalentTo(new[] { DDKey.D1 }));
	}

	[Test]
	public void Duplicates_AreCollapsed_OrderPreserved()
	{
		var keys = KeyArgParser.Parse("w,w,a");
		Assert.That(keys, Is.EqualTo(new[] { DDKey.W, DDKey.A }));
	}

	[Test]
	public void UnknownKey_Throws()
	{
		Assert.That(() => KeyArgParser.Parse("nope"), Throws.TypeOf<CliException>());
	}
}

[TestFixture]
public class ColorParserTests
{
	[Test]
	public void HexWithHash()
	{
		var c = ColorParser.Parse("#FF8000");
		Assert.That((c.R, c.G, c.B), Is.EqualTo(((byte)255, (byte)128, (byte)0)));
	}

	[Test]
	public void HexWithoutHash()
	{
		var c = ColorParser.Parse("0064FF");
		Assert.That((c.R, c.G, c.B), Is.EqualTo(((byte)0, (byte)100, (byte)255)));
	}

	[Test]
	public void NamedColor()
	{
		var c = ColorParser.Parse("orange");
		Assert.That((c.R, c.G, c.B), Is.EqualTo(((byte)255, (byte)140, (byte)0)));
	}

	[Test]
	public void Invalid_Throws()
	{
		Assert.That(() => ColorParser.Parse("blurple"), Throws.TypeOf<CliException>());
	}
}
