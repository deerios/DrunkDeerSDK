using DrunkDeer.FeatureTests.Fakes;
using DrunkDeer.Protocol;
using NUnit.Framework;

namespace DrunkDeer.FeatureTests.Features;

[TestFixture]
public class MacroTests
{
	private FakeKeyboardConnection _fake = null!;
	private KeyboardSession _session = null!;

	[SetUp]
	public void SetUp()
	{
		// Macro slot read/write goes through the FuncBlock gateway, which requires Kun or
		// HighPrecision. G65 m1 is always Kun-precision.
		_fake    = new FakeKeyboardConnection(ModelRegistry.GetInfo(ModelSlugs.G65M1));
		_session = new KeyboardSession(_fake);
	}

	[TearDown]
	public void TearDown() => _session.Dispose();

	[Test]
	public void WriteMacroSlots_StandardPrecisionA75_Throws()
	{
		using var fake    = new FakeKeyboardConnection(); // default A75, fw 1 -> Standard precision
		using var session = new KeyboardSession(fake);
		Assert.Throws<NotSupportedException>(() =>
			session.WriteMacroSlots(new MacroAction[MacroAction.SlotCount][]));
	}

	private const int MacroBytes = MacroAction.BlockSize; // 2048
	private const int MacroChunks = (MacroBytes + 55) / 56; // 37
	private const byte MacroReadCmd = 0x0C;
	private const byte MacroWriteCmd = 0x0D;

	// ── MacroAction encode/decode roundtrip ────────────────────────────────────

	[Test]
	public void EncodeBlock_EmptySlotsWriteSentinel()
	{
		var slots = new MacroAction[MacroAction.SlotCount][];
		var block = MacroAction.EncodeBlock(slots!);
		// Every header entry should be the empty sentinel 0x0040
		for (int i = 0; i < MacroAction.SlotCount; i++)
		{
			ushort ptr = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(
				block.AsSpan(i * 2));
			Assert.That(ptr, Is.EqualTo(0x0040), $"slot {i}");
		}
	}

	[Test]
	public void EncodeDecodeBlock_Roundtrip_PreservesActions()
	{
		var slots = new MacroAction[MacroAction.SlotCount][];
		slots[0] = [
			MacroAction.KeyDown(0x04, 50),   // 'a' down, 50 ms
            MacroAction.KeyUp(0x04),          // 'a' up
        ];
		slots[5] = [
			MacroAction.MouseDown(0, 30),     // left-click down, 30 ms
            MacroAction.MouseUp(0),           // left-click up
        ];

		var decoded = MacroAction.DecodeBlock(MacroAction.EncodeBlock(slots));

		Assert.Multiple(() =>
		{
			Assert.That(decoded[0], Has.Length.EqualTo(2));
			Assert.That(decoded[0][0].EventType, Is.EqualTo(MacroEventType.KeyDown));
			Assert.That(decoded[0][0].Code, Is.EqualTo(0x04));
			Assert.That(decoded[0][0].DelayAfterMs, Is.EqualTo(50));
			Assert.That(decoded[0][1].EventType, Is.EqualTo(MacroEventType.KeyUp));
			Assert.That(decoded[0][1].Code, Is.EqualTo(0x04));

			Assert.That(decoded[1], Is.Empty); // slot 1 was empty

			Assert.That(decoded[5][0].EventType, Is.EqualTo(MacroEventType.MouseDown));
			Assert.That(decoded[5][0].Code, Is.EqualTo(0));
			Assert.That(decoded[5][1].EventType, Is.EqualTo(MacroEventType.MouseUp));
		});
	}

	[Test]
	public void EncodeDecodeBlock_ModifierKey_RoundtripViaHidCode()
	{
		var slots = new MacroAction[MacroAction.SlotCount][];
		slots[0] = [
			MacroAction.KeyDown(0xE0),  // Left Ctrl
            MacroAction.KeyDown(0x04),  // 'a'
            MacroAction.KeyUp(0x04),
			MacroAction.KeyUp(0xE0),
		];

		var decoded = MacroAction.DecodeBlock(MacroAction.EncodeBlock(slots));

		Assert.Multiple(() =>
		{
			Assert.That(decoded[0][0].EventType, Is.EqualTo(MacroEventType.KeyDown));
			Assert.That(decoded[0][0].Code, Is.EqualTo(0xE0)); // Left Ctrl roundtripped
			Assert.That(decoded[0][1].Code, Is.EqualTo(0x04));
		});
	}

	// ── WriteMacroSlots ────────────────────────────────────────────────────────

	[Test]
	public void WriteMacroSlots_UsesSubcommand0x0D()
	{
		_fake.EnqueueGatewayWriteAcks(MacroBytes);
		_session.WriteMacroSlots(new MacroAction[MacroAction.SlotCount][]);
		Assert.That(_fake.GatewayWritePackets(MacroWriteCmd), Is.Not.Empty);
	}

	[Test]
	public void WriteMacroSlots_SendsCorrectChunkCount()
	{
		_fake.EnqueueGatewayWriteAcks(MacroBytes);
		_session.WriteMacroSlots(new MacroAction[MacroAction.SlotCount][]);
		Assert.That(_fake.GatewayWritePackets(MacroWriteCmd), Has.Count.EqualTo(MacroChunks));
	}

	[Test]
	public void WriteMacroSlots_Profile1_AddressOffset()
	{
		_fake.EnqueueGatewayWriteAcks(MacroBytes);
		_session.WriteMacroSlots(new MacroAction[MacroAction.SlotCount][], profileIndex: 1);
		var first = _fake.GatewayWritePackets(MacroWriteCmd)[0];
		ushort addr = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(first.AsSpan(5));
		Assert.That(addr, Is.EqualTo(MacroBytes)); // 2048
	}

	[Test]
	public void WriteMacroSlots_WrongLength_Throws()
	{
		Assert.Throws<ArgumentException>(() =>
			_session.WriteMacroSlots(new MacroAction[16][]));
	}

	// ── ReadMacroSlots ─────────────────────────────────────────────────────────

	[Test]
	public void ReadMacroSlots_Returns32Slots()
	{
		_fake.EnqueueGatewayRead(new byte[MacroBytes]);
		var slots = _session.ReadMacroSlots();
		Assert.That(slots, Has.Length.EqualTo(32));
	}

	[Test]
	public void ReadMacroSlots_UsesSubcommand0x0C()
	{
		_fake.EnqueueGatewayRead(new byte[MacroBytes]);
		_session.ReadMacroSlots();
		var sent = _fake.SentPackets;
		Assert.That(sent.Any(p => p.Length > 1 && p[0] == 0x55 && p[1] == MacroReadCmd), Is.True);
	}

	[Test]
	public void ReadMacroSlots_EmptyBlock_AllSlotsEmpty()
	{
		// All-zero block -> every pointer is 0 -> empty
		_fake.EnqueueGatewayRead(new byte[MacroBytes]);
		var slots = _session.ReadMacroSlots();
		Assert.That(slots.All(s => s.Length == 0), Is.True);
	}

	[Test]
	public void ReadMacroSlots_DecodesPopulatedSlot()
	{
		var slots = new MacroAction[MacroAction.SlotCount][];
		slots[0] = [MacroAction.KeyDown(0x04, 10), MacroAction.KeyUp(0x04)];
		var block = MacroAction.EncodeBlock(slots);

		_fake.EnqueueGatewayRead(block);
		var read = _session.ReadMacroSlots();
		Assert.Multiple(() =>
		{
			Assert.That(read[0], Has.Length.EqualTo(2));
			Assert.That(read[0][0].Code, Is.EqualTo(0x04));
		});
	}

	// ── SetMacroSlot ──────────────────────────────────────────────────────────

	[Test]
	public void SetMacroSlot_ReplacesSlotAndWrites()
	{
		// SetMacroSlot reads the full block then writes it back
		_fake.EnqueueGatewayRead(new byte[MacroBytes]);
		_fake.EnqueueGatewayWriteAcks(MacroBytes);

		_session.SetMacroSlot(3, [MacroAction.KeyDown(0x28)]);

		var written = _fake.ReassembleGatewayWriteData(MacroWriteCmd);
		// Slot 3 pointer (bytes 6..7 of header) should be non-zero after write
		ushort ptr = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(written.AsSpan(6));
		Assert.That(ptr, Is.Not.EqualTo(0x0040));
	}

	[Test]
	public void SetMacroSlot_InvalidIndex_Throws()
	{
		Assert.Throws<ArgumentOutOfRangeException>(() =>
			_session.SetMacroSlot(32, []));
	}
}
