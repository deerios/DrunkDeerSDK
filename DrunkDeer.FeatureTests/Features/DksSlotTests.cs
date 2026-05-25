using DrunkDeer.FeatureTests.Fakes;
using DrunkDeer.Protocol;
using NUnit.Framework;

namespace DrunkDeer.FeatureTests.Features;

[TestFixture]
public class DksSlotTests
{
	private FakeKeyboardConnection _fake = null!;
	private KeyboardSession _session = null!;

	[SetUp]
	public void SetUp()
	{
		_fake    = new FakeKeyboardConnection();
		_session = new KeyboardSession(_fake);
	}

	[TearDown]
	public void TearDown() => _session.Dispose();

	private const int DksBytes = 768;
	private const int DksChunks = 14;
	private const byte DksWriteCmd = 0xA3;

	private const int MtBytes = 192;
	private const byte MtWriteCmd = 0xA5;

	private const int TglBytes = 96;
	private const byte TglWriteCmd = 0xA7;

	[Test]
	public void WriteDynamicKeystrokeEntries_UsesSubcommand0xA3()
	{
		_fake.EnqueueGatewayWriteAcks(DksBytes);
		_session.WriteDynamicKeystrokeEntries(Enumerable.Range(0, 32).Select(_ => new DynamicKeystrokeEntry()).ToArray());
		Assert.That(_fake.GatewayWritePackets(DksWriteCmd), Is.Not.Empty);
	}

	[Test]
	public void WriteDynamicKeystrokeEntries_SendsCorrectChunkCount()
	{
		_fake.EnqueueGatewayWriteAcks(DksBytes);
		_session.WriteDynamicKeystrokeEntries(Enumerable.Range(0, 32).Select(_ => new DynamicKeystrokeEntry()).ToArray());
		Assert.That(_fake.GatewayWritePackets(DksWriteCmd), Has.Count.EqualTo(DksChunks));
	}

	[Test]
	public void WriteDynamicKeystrokeEntries_WrongLength_Throws()
	{
		Assert.Throws<ArgumentException>(() => _session.WriteDynamicKeystrokeEntries(new DynamicKeystrokeEntry[16]));
	}

	[Test]
	public void ReadDynamicKeystrokeEntries_Returns32Entries()
	{
		_fake.EnqueueGatewayRead(new byte[DksBytes]);
		var entries = _session.ReadDynamicKeystrokeEntries();
		Assert.That(entries, Has.Length.EqualTo(32));
	}

	[Test]
	public void ReadDynamicKeystrokeEntries_DecodesPointsCorrectly()
	{
		var raw = new byte[DksBytes];
		raw[0] = 10; raw[1] = 30; raw[2] = 30; raw[3] = 10;
		_fake.EnqueueGatewayRead(raw);
		var entries = _session.ReadDynamicKeystrokeEntries();
		Assert.That(entries[0].Points, Is.EqualTo(new byte[] { 10, 30, 30, 10 }));
	}

	[Test]
	public void SetDynamicKeystrokeEntry_SendsSingleChunkForSlot0()
	{
		_fake.EnqueueGatewayWriteAcks(DynamicKeystrokeEntry.ByteSize);
		_session.SetDynamicKeystrokeEntry(0, new DynamicKeystrokeEntry());
		Assert.That(_fake.GatewayWritePackets(DksWriteCmd), Has.Count.EqualTo(1));
	}

	[Test]
	public void SetDynamicKeystrokeEntry_AddressIncludesSlotOffset()
	{
		_fake.EnqueueGatewayWriteAcks(DynamicKeystrokeEntry.ByteSize);
		_session.SetDynamicKeystrokeEntry(slotIndex: 2, new DynamicKeystrokeEntry());
		var chunk = _fake.GatewayWritePackets(DksWriteCmd)[0];
		ushort addr = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(chunk.AsSpan(5));
		Assert.That(addr, Is.EqualTo(2 * DynamicKeystrokeEntry.ByteSize)); // 48
	}

	[Test]
	public void SetDynamicKeystrokeEntry_InvalidSlot_Throws()
	{
		Assert.Throws<ArgumentOutOfRangeException>(() =>
			_session.SetDynamicKeystrokeEntry(32, new DynamicKeystrokeEntry()));
	}

	[Test]
	public void WriteMultiTapEntries_UsesSubcommand0xA5()
	{
		_fake.EnqueueGatewayWriteAcks(MtBytes);
		_session.WriteMultiTapEntries(new MultiTapEntry[32]);
		Assert.That(_fake.GatewayWritePackets(MtWriteCmd), Is.Not.Empty);
	}

	[Test]
	public void WriteMultiTapEntries_EncodesClickAndDownKey()
	{
		_fake.EnqueueGatewayWriteAcks(MtBytes);
		var entries = new MultiTapEntry[32];
		entries[0] = new MultiTapEntry
		{
			ClickKey = UserKey.FromHid(0x04),   // 'a'
			DownKey  = UserKey.FromHid(0x28),   // Enter
		};
		_session.WriteMultiTapEntries(entries);
		var data = _fake.ReassembleGatewayWriteData(MtWriteCmd);
		Assert.Multiple(() =>
		{
			Assert.That(data[0], Is.EqualTo(0x10)); // ClickKey.Type = Keyboard
			Assert.That(data[2], Is.EqualTo(0x04)); // ClickKey.Param2 = 'a'
			Assert.That(data[3], Is.EqualTo(0x10)); // DownKey.Type = Keyboard
			Assert.That(data[5], Is.EqualTo(0x28)); // DownKey.Param2 = Enter
		});
	}

	[Test]
	public void ReadMultiTapEntries_Returns32Entries()
	{
		_fake.EnqueueGatewayRead(new byte[MtBytes]);
		var entries = _session.ReadMultiTapEntries();
		Assert.That(entries, Has.Length.EqualTo(32));
	}

	[Test]
	public void ReadMultiTapEntries_DecodesFirstEntryCorrectly()
	{
		var raw = new byte[MtBytes];
		raw[0] = 0x10; raw[1] = 0x00; raw[2] = 0x04; // ClickKey: Keyboard, 'a'
		raw[3] = 0x10; raw[4] = 0x00; raw[5] = 0x28; // DownKey:  Keyboard, Enter
		_fake.EnqueueGatewayRead(raw);
		var entries = _session.ReadMultiTapEntries();
		Assert.Multiple(() =>
		{
			Assert.That(entries[0].ClickKey.Param2, Is.EqualTo(0x04));
			Assert.That(entries[0].DownKey.Param2, Is.EqualTo(0x28));
		});
	}

	[Test]
	public void SetMultiTapEntry_SendsSingleChunk()
	{
		_fake.EnqueueGatewayWriteAcks(MultiTapEntry.ByteSize);
		_session.SetMultiTapEntry(0, new MultiTapEntry());
		Assert.That(_fake.GatewayWritePackets(MtWriteCmd), Has.Count.EqualTo(1));
	}

	[Test]
	public void WriteToggleKeyEntries_UsesSubcommand0xA7()
	{
		_fake.EnqueueGatewayWriteAcks(TglBytes);
		_session.WriteToggleKeyEntries(new UserKey[32]);
		Assert.That(_fake.GatewayWritePackets(TglWriteCmd), Is.Not.Empty);
	}

	[Test]
	public void WriteToggleKeyEntries_EncodesFirstSlot()
	{
		_fake.EnqueueGatewayWriteAcks(TglBytes);
		var entries = new UserKey[32];
		entries[0] = UserKey.FromHid(0x39); // Caps Lock
		_session.WriteToggleKeyEntries(entries);
		var data = _fake.ReassembleGatewayWriteData(TglWriteCmd);
		Assert.Multiple(() =>
		{
			Assert.That(data[0], Is.EqualTo(0x10));
			Assert.That(data[2], Is.EqualTo(0x39));
		});
	}

	[Test]
	public void ReadToggleKeyEntries_Returns32Entries()
	{
		_fake.EnqueueGatewayRead(new byte[TglBytes]);
		var entries = _session.ReadToggleKeyEntries();
		Assert.That(entries, Has.Length.EqualTo(32));
	}

	[Test]
	public void SetToggleKeyEntry_SendsSingleThreeByteChunk()
	{
		_fake.EnqueueGatewayWriteAcks(3);
		_session.SetToggleKeyEntry(0, UserKey.FromHid(0x39));
		Assert.That(_fake.GatewayWritePackets(TglWriteCmd), Has.Count.EqualTo(1));
	}

	[Test]
	public void SetToggleKeyEntry_InvalidSlot_Throws()
	{
		Assert.Throws<ArgumentOutOfRangeException>(() =>
			_session.SetToggleKeyEntry(32, UserKey.Disabled));
	}
}
