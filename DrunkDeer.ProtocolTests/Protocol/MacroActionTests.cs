using DrunkDeer.Protocol;
using NUnit.Framework;

namespace DrunkDeer.ProtocolTests.Protocol;

/// <summary>
/// PROTO-5 regression coverage: MacroAction.EncodeBlock must reject slot data that doesn't fit
/// the 2048-byte wire block up front, rather than throwing mid-encode from a span slice after
/// the block has already been partially written.
/// </summary>
[TestFixture]
public class MacroActionTests
{
	private static MacroAction[]?[] EmptySlots() => new MacroAction[]?[MacroAction.SlotCount];

	[Test]
	public void EncodeBlock_FitsExactly_DoesNotThrow()
	{
		// Header (64) + 4-byte pad + N actions x 4 bytes <= 2048 => N <= 495.
		var slots = EmptySlots();
		slots[0] = CreateActions(495);
		Assert.DoesNotThrow(() => MacroAction.EncodeBlock(slots));
	}

	[Test]
	public void EncodeBlock_OneActionOverCapacity_ThrowsArgumentException()
	{
		var slots = EmptySlots();
		slots[0] = CreateActions(496);
		var ex = Assert.Throws<ArgumentException>(() => MacroAction.EncodeBlock(slots));
		Assert.That(ex!.Message, Does.Contain("2048"));
	}

	[Test]
	public void EncodeBlock_OverCapacity_AcrossMultipleSlots_ThrowsArgumentException()
	{
		var slots = EmptySlots();
		for (int i = 0; i < 10; i++)
			slots[i] = CreateActions(50); // 500 actions total, over the 495 cap
		Assert.Throws<ArgumentException>(() => MacroAction.EncodeBlock(slots));
	}

	private static MacroAction[] CreateActions(int count)
	{
		var actions = new MacroAction[count];
		for (int i = 0; i < count; i++)
			actions[i] = MacroAction.KeyDown(0x04);
		return actions;
	}
}
