using DrunkDeer.Protocol;
using NUnit.Framework;

namespace DrunkDeer.ProtocolTests.Protocol;

/// <summary>
/// Guards the codegen'd <see cref="ModelRegistry.DiscoveryPairs"/> (from the discovery section of
/// protocol/models.yaml). Catches regressions such as a VID/PID above 0x7FFF being sign-extended
/// during YAML parsing, or a pair being dropped from the generated table.
/// </summary>
[TestFixture]
public class ModelRegistryTests
{
	// Mirrors protocol/models.yaml discovery.interfaces, flattened in declaration order.
	private static readonly (int Vid, int Pid)[] Expected =
	[
		(0x352D, 0x2382), (0x352D, 0x2383), (0x352D, 0x2384),
		(0x352D, 0x2386), (0x352D, 0x2387), (0x352D, 0x2391),
		(0x05AC, 0x024F),
		(0x04D9, 0x2A08),
		(0x1A85, 0xFC4F),
	];

	[Test]
	public void DiscoveryPairs_MatchModelsYaml()
	{
		Assert.That(ModelRegistry.DiscoveryPairs, Is.EqualTo(Expected));
	}

	[Test]
	public void DiscoveryPairs_AreAll16BitUnsigned()
	{
		foreach (var (vid, pid) in ModelRegistry.DiscoveryPairs)
		{
			Assert.That(vid, Is.InRange(0, 0xFFFF), $"VID 0x{vid:X} is not a 16-bit unsigned value.");
			Assert.That(pid, Is.InRange(0, 0xFFFF), $"PID 0x{pid:X} is not a 16-bit unsigned value.");
		}
	}
}
