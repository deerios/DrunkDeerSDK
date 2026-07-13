using DrunkDeer.Protocol;
using NUnit.Framework;

namespace DrunkDeer.ProtocolTests.Protocol;

/// <summary>
/// LOW-3 regression coverage: DDKey.RightCtrl must be addressable by name, since CTRL_R appears
/// in the A75 and G60 layout tables.
/// </summary>
[TestFixture]
public class DDKeyTests
{
	[TestCase(ModelSlugs.A75)]
	[TestCase(ModelSlugs.G60)]
	public void RightCtrl_IsMappedInLayout(string modelSlug)
	{
		var layout = KeyLayout.GetLayout(modelSlug);
		var map = KeyLayout.BuildIndexMap(layout);
		Assert.That(map.ContainsKey(DDKey.RightCtrl), Is.True);
		Assert.That(layout[map[DDKey.RightCtrl]], Is.EqualTo("CTRL_R"));
	}
}
