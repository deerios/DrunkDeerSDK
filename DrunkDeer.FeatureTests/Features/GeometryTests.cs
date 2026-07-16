using DrunkDeer.FeatureTests.Fakes;
using DrunkDeer.Protocol;
using NUnit.Framework;

namespace DrunkDeer.FeatureTests.Features;

/// <summary>
/// Coverage for the public physical-key geometry (§3.3): every KeyInfo must resolve to
/// the same firmware slot the session's own DDKey map returns, the geometry must cover
/// exactly the physically-lit keys, and board dimensions must be derived.
/// </summary>
/// <remarks>
/// The model-invariant checks run against every model+variant that ships geometry, so a
/// newly added board is held to them without anyone remembering to write its tests. The
/// per-model fixtures below pin the numbers that are specific to one board's shape.
/// </remarks>
[TestFixture]
public class GeometryTests
{
	private static KeyboardSession Open(string slug) =>
		new(new FakeKeyboardConnection(ModelRegistry.GetInfo(slug)!));

	private static KeyboardSession OpenA75Ansi() => Open(ModelSlugs.A75);

	/// <summary>
	/// Every model that ships geometry, including ones that share another board's via a slug
	/// alias -- an alias still has to satisfy the invariants against its own slot tables.
	/// Add a board here when it gains a YAML.
	/// </summary>
	private static readonly string[] ModelsWithGeometry =
		[ModelSlugs.A75, ModelSlugs.A75Ultra, ModelSlugs.G75, ModelSlugs.G65, ModelSlugs.G60];

	[TestCaseSource(nameof(ModelsWithGeometry))]
	public void HasLayout_AndIsNonEmpty(string slug)
	{
		using var session = Open(slug);
		Assert.That(session.HasLayout, Is.True);
		Assert.That(session.Layout, Is.Not.Empty);
	}

	[TestCaseSource(nameof(ModelsWithGeometry))]
	public void EverySlotIndex_MatchesGetKeyIndex(string slug)
	{
		using var session = Open(slug);
		foreach (var info in session.Layout)
			Assert.That(info.SlotIndex, Is.EqualTo(session.GetKeyIndex(info.Key)),
				$"Geometry slot for {info.Key} disagrees with the firmware key map.");
	}

	[TestCaseSource(nameof(ModelsWithGeometry))]
	public void KeyCount_MatchesLitKeyCount(string slug)
	{
		// Geometry must cover exactly the physically-lit keys: one KeyInfo per LED.
		using var session = Open(slug);
		Assert.That(session.Layout, Has.Count.EqualTo(session.LightingKeyCount));
	}

	[TestCaseSource(nameof(ModelsWithGeometry))]
	public void SlotIndicesAndKeys_AreUnique(string slug)
	{
		using var session = Open(slug);
		var slots = session.Layout.Select(k => k.SlotIndex).ToList();
		var keys  = session.Layout.Select(k => k.Key).ToList();
		Assert.That(slots.Distinct().Count(), Is.EqualTo(slots.Count), "Duplicate slot index in geometry.");
		Assert.That(keys.Distinct().Count(),  Is.EqualTo(keys.Count),  "Duplicate DDKey in geometry.");
	}

	[TestCaseSource(nameof(ModelsWithGeometry))]
	public void BoardDimensions_MatchLargestKeyExtent(string slug)
	{
		using var session = Open(slug);
		float expectedW = session.Layout.Max(k => k.X + k.W);
		float expectedH = session.Layout.Max(k => k.Y + k.H);
		Assert.That(session.BoardWidth,  Is.EqualTo(expectedW).Within(0.001f));
		Assert.That(session.BoardHeight, Is.EqualTo(expectedH).Within(0.001f));
	}

	[TestCaseSource(nameof(ModelsWithGeometry))]
	public void AllKeys_FitWithinBoard(string slug)
	{
		using var session = Open(slug);
		foreach (var k in session.Layout)
		{
			Assert.That(k.X, Is.GreaterThanOrEqualTo(0f));
			Assert.That(k.Y, Is.GreaterThanOrEqualTo(0f));
			Assert.That(k.X + k.W, Is.LessThanOrEqualTo(session.BoardWidth + 0.001f));
			Assert.That(k.Y + k.H, Is.LessThanOrEqualTo(session.BoardHeight + 0.001f));
		}
	}

	[Test]
	public void A75Ansi_KeyCount_Matches82PhysicalKeys()
	{
		using var session = OpenA75Ansi();
		// A75 ANSI is an 82-key board (== the number of lit LEDs).
		Assert.That(session.Layout, Has.Count.EqualTo(82));
	}

	[Test]
	public void A75Ansi_BoardDimensions_AreA75Shaped()
	{
		using var session = OpenA75Ansi();
		// A 75% board is ~16u wide and 6 rows tall. The A75's height runs to 6.5 rather than
		// a flat 6.0 because its alphanumeric block starts 0.25u below the function row and
		// its arrow cluster steps a further 0.25u below the bottom row.
		Assert.That(session.BoardWidth,  Is.EqualTo(16.25f).Within(0.001f));
		Assert.That(session.BoardHeight, Is.EqualTo(6.5f).Within(0.001f));
	}

	[Test]
	public void G75Ansi_KeyCount_Matches84PhysicalKeys()
	{
		using var session = Open(ModelSlugs.G75);
		Assert.That(session.Layout, Has.Count.EqualTo(84));
	}

	[Test]
	public void G75Ansi_BoardDimensions_AreAFlat16By6Grid()
	{
		using var session = Open(ModelSlugs.G75);
		// Unlike the A75, the G75 has no function-row gap and no stepped arrow cluster,
		// so it fills a flat 16x6 grid exactly.
		Assert.That(session.BoardWidth,  Is.EqualTo(16f).Within(0.001f));
		Assert.That(session.BoardHeight, Is.EqualTo(6f).Within(0.001f));
	}

	[Test]
	public void G65Ansi_IsAFlat16By5Grid_Of68Keys()
	{
		using var session = Open(ModelSlugs.G65);
		Assert.That(session.Layout,      Has.Count.EqualTo(68));
		Assert.That(session.BoardWidth,  Is.EqualTo(16f).Within(0.001f));
		Assert.That(session.BoardHeight, Is.EqualTo(5f).Within(0.001f));
	}

	[Test]
	public void G60Ansi_IsAFlat15By5Grid_Of61Keys()
	{
		using var session = Open(ModelSlugs.G60);
		Assert.That(session.Layout,      Has.Count.EqualTo(61));
		// No navigation column, so the G60 is a unit narrower than the G65.
		Assert.That(session.BoardWidth,  Is.EqualTo(15f).Within(0.001f));
		Assert.That(session.BoardHeight, Is.EqualTo(5f).Within(0.001f));
	}

	[Test]
	public void G75Ansi_FunctionRow_RunsPrintInsertDelete()
	{
		// The product drawing transposes Insert and Print; the hardware and LayoutG75 agree
		// on Print first. Pin the order so a regeneration cannot quietly reintroduce the swap.
		using var session = Open(ModelSlugs.G75);
		var byKey = session.Layout.ToDictionary(k => k.Key);
		Assert.That(byKey[DDKey.PrintScreen].X, Is.EqualTo(13f).Within(0.001f));
		Assert.That(byKey[DDKey.Insert].X,      Is.EqualTo(14f).Within(0.001f));
		Assert.That(byKey[DDKey.Delete].X,      Is.EqualTo(15f).Within(0.001f));
	}

	[Test]
	public void TryGetKeys_UnknownVariant_ReturnsFalseAndEmpty()
	{
		// A75 ISO geometry is intentionally not shipped yet.
		Assert.That(KeyGeometry.TryGetKeys(ModelSlugs.A75, "iso", out var keys), Is.False);
		Assert.That(keys, Is.Empty);
	}

	[Test]
	public void TryGetKeys_AnsiAlt_SharesAnsiGeometry()
	{
		Assert.That(KeyGeometry.TryGetKeys(ModelSlugs.A75, "ansi_alt", out var alt), Is.True);
		KeyGeometry.TryGetKeys(ModelSlugs.A75, "ansi", out var ansi);
		Assert.That(alt, Is.EqualTo(ansi));
	}
}
