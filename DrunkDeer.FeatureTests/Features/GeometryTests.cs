using DrunkDeer.FeatureTests.Fakes;
using DrunkDeer.Protocol;
using NUnit.Framework;

namespace DrunkDeer.FeatureTests.Features;

/// <summary>
/// Coverage for the public physical-key geometry (§3.3): every A75 ANSI KeyInfo must
/// resolve to the same firmware slot the session's own DDKey map returns, the geometry
/// must cover exactly the physically-lit keys, and board dimensions must be derived.
/// </summary>
[TestFixture]
public class GeometryTests
{
	private static KeyboardSession OpenA75Ansi() => new(new FakeKeyboardConnection());

	[Test]
	public void A75Ansi_HasLayout_AndIsNonEmpty()
	{
		using var session = OpenA75Ansi();
		Assert.That(session.HasLayout, Is.True);
		Assert.That(session.Layout, Is.Not.Empty);
	}

	[Test]
	public void A75Ansi_EverySlotIndex_MatchesGetKeyIndex()
	{
		using var session = OpenA75Ansi();
		foreach (var info in session.Layout)
			Assert.That(info.SlotIndex, Is.EqualTo(session.GetKeyIndex(info.Key)),
				$"Geometry slot for {info.Key} disagrees with the firmware key map.");
	}

	[Test]
	public void A75Ansi_KeyCount_Matches82PhysicalKeys()
	{
		using var session = OpenA75Ansi();
		// A75 ANSI is an 82-key board (== the number of lit LEDs).
		Assert.That(session.Layout, Has.Count.EqualTo(82));
		Assert.That(session.Layout, Has.Count.EqualTo(session.LightingKeyCount));
	}

	[Test]
	public void A75Ansi_SlotIndicesAndKeys_AreUnique()
	{
		using var session = OpenA75Ansi();
		var slots = session.Layout.Select(k => k.SlotIndex).ToList();
		var keys  = session.Layout.Select(k => k.Key).ToList();
		Assert.That(slots.Distinct().Count(), Is.EqualTo(slots.Count), "Duplicate slot index in geometry.");
		Assert.That(keys.Distinct().Count(),  Is.EqualTo(keys.Count),  "Duplicate DDKey in geometry.");
	}

	[Test]
	public void A75Ansi_BoardDimensions_MatchLargestKeyExtent()
	{
		using var session = OpenA75Ansi();
		float expectedW = session.Layout.Max(k => k.X + k.W);
		float expectedH = session.Layout.Max(k => k.Y + k.H);
		Assert.That(session.BoardWidth,  Is.EqualTo(expectedW).Within(0.001f));
		Assert.That(session.BoardHeight, Is.EqualTo(expectedH).Within(0.001f));
		// Sanity: a 75% board is ~16u wide and 6 rows tall.
		Assert.That(session.BoardWidth,  Is.EqualTo(16.25f).Within(0.001f));
		Assert.That(session.BoardHeight, Is.EqualTo(6f).Within(0.001f));
	}

	[Test]
	public void A75Ansi_AllKeys_FitWithinBoard()
	{
		using var session = OpenA75Ansi();
		foreach (var k in session.Layout)
		{
			Assert.That(k.X, Is.GreaterThanOrEqualTo(0f));
			Assert.That(k.Y, Is.GreaterThanOrEqualTo(0f));
			Assert.That(k.X + k.W, Is.LessThanOrEqualTo(session.BoardWidth + 0.001f));
			Assert.That(k.Y + k.H, Is.LessThanOrEqualTo(session.BoardHeight + 0.001f));
		}
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
