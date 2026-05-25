using DrunkDeer.FeatureTests.Fakes;
using DrunkDeer.Protocol;
using NUnit.Framework;

namespace DrunkDeer.FeatureTests.Features;

[TestFixture]
public class KeyMapTests
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

	// 128 keys × 3 bytes = 384 bytes -> ceil(384/56) = 7 chunks
	private const int KeyMapBytes = 128 * 3;
	private const int KeyMapChunks = 7; // ceil(384 / 56)
	private const byte ReadUserCmd = 0x08;
	private const byte ReadDefCmd = 0x07;
	private const byte WriteCmd = 0x09;

	// ── WriteKeyMap ───────────────────────────────────────────────────────────

	[Test]
	public void WriteKeyMap_SendsCorrectGatewaySubcommand()
	{
		_fake.EnqueueGatewayWriteAcks(KeyMapBytes);
		_session.WriteKeyMap(new UserKey[128]);
		var chunks = _fake.GatewayWritePackets(WriteCmd);
		Assert.That(chunks, Is.Not.Empty);
		Assert.That(chunks[0][1], Is.EqualTo(WriteCmd));
	}

	[Test]
	public void WriteKeyMap_SendsCorrectNumberOfChunks()
	{
		_fake.EnqueueGatewayWriteAcks(KeyMapBytes);
		_session.WriteKeyMap(new UserKey[128]);
		Assert.That(_fake.GatewayWritePackets(WriteCmd), Has.Count.EqualTo(KeyMapChunks));
	}

	[Test]
	public void WriteKeyMap_FirstKeyEncodedInFirstChunk()
	{
		_fake.EnqueueGatewayWriteAcks(KeyMapBytes);
		var keys = new UserKey[128];
		keys[0] = UserKey.FromHid(0x28); // Enter
		_session.WriteKeyMap(keys);
		var data = _fake.ReassembleGatewayWriteData(WriteCmd);
		Assert.Multiple(() =>
		{
			Assert.That(data[0], Is.EqualTo(0x10)); // Type = Keyboard
			Assert.That(data[1], Is.EqualTo(0x00)); // Param1 = modifiers (none)
			Assert.That(data[2], Is.EqualTo(0x28)); // Param2 = HID Enter
		});
	}

	[Test]
	public void WriteKeyMap_LastKeyEncodedAtCorrectOffset()
	{
		_fake.EnqueueGatewayWriteAcks(KeyMapBytes);
		var keys = new UserKey[128];
		keys[127] = UserKey.FromHid(0x04, KeyModifiers.LeftCtrl);
		_session.WriteKeyMap(keys);
		var data = _fake.ReassembleGatewayWriteData(WriteCmd);
		int offset = 127 * 3;
		Assert.Multiple(() =>
		{
			Assert.That(data[offset], Is.EqualTo(0x10)); // Type
			Assert.That(data[offset + 1], Is.EqualTo(0x01)); // LeftCtrl modifier
			Assert.That(data[offset + 2], Is.EqualTo(0x04)); // HID 'a'
		});
	}

	[Test]
	public void WriteKeyMap_WrongLength_Throws()
	{
		Assert.Throws<ArgumentException>(() => _session.WriteKeyMap(new UserKey[64]));
	}

	[Test]
	public void WriteKeyMap_Layer1_AddressOffset512()
	{
		_fake.EnqueueGatewayWriteAcks(KeyMapBytes);
		_session.WriteKeyMap(new UserKey[128], layerIndex: 1);
		var firstChunk = _fake.GatewayWritePackets(WriteCmd)[0];
		// Address = 2048 × profile(0) + 512 × layer(1) + 0 = 0x0200 = 512
		ushort addr = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(firstChunk.AsSpan(5));
		Assert.That(addr, Is.EqualTo(512));
	}

	// ── ReadKeyMap ────────────────────────────────────────────────────────────

	[Test]
	public void ReadKeyMap_DecodesFirstKeyCorrectly()
	{
		var raw = new byte[KeyMapBytes];
		raw[0] = 0x10; // Type = Keyboard
		raw[1] = 0x00; // modifiers
		raw[2] = 0x28; // Enter
		_fake.EnqueueGatewayRead(raw);

		var keys = _session.ReadKeyMap();
		Assert.Multiple(() =>
		{
			Assert.That(keys[0].Type, Is.EqualTo(0x10));
			Assert.That(keys[0].Param2, Is.EqualTo(0x28));
		});
	}

	[Test]
	public void ReadKeyMap_DecodesLastKeyCorrectly()
	{
		var raw = new byte[KeyMapBytes];
		raw[127 * 3]     = 0x20; // Type = MouseButton
		raw[127 * 3 + 1] = 0x01; // Left button
		raw[127 * 3 + 2] = 0x00;
		_fake.EnqueueGatewayRead(raw);

		var keys = _session.ReadKeyMap();
		Assert.Multiple(() =>
		{
			Assert.That(keys[127].Type, Is.EqualTo(0x20));
			Assert.That(keys[127].Param1, Is.EqualTo(0x01));
		});
	}

	[Test]
	public void ReadKeyMap_UsesSubcommand0x08()
	{
		_fake.EnqueueGatewayRead(new byte[KeyMapBytes]);
		_session.ReadKeyMap();
		var chunks = _fake.SentPackets.Where(p => p[0] == 0x55 && p[1] == ReadUserCmd).ToList();
		Assert.That(chunks, Is.Not.Empty);
	}

	[Test]
	public void ReadDefaultKeyMap_UsesSubcommand0x07()
	{
		_fake.EnqueueGatewayRead(new byte[KeyMapBytes]);
		_session.ReadDefaultKeyMap();
		var chunks = _fake.SentPackets.Where(p => p[0] == 0x55 && p[1] == ReadDefCmd).ToList();
		Assert.That(chunks, Is.Not.Empty);
	}

	// ── SetKey (single-key write) ─────────────────────────────────────────────

	[Test]
	public void SetKey_ByIndex_SendsSingleThreeByteChunk()
	{
		_fake.EnqueueGatewayWriteAcks(3);
		_session.SetKey(0, UserKey.FromHid(0x28));
		var chunks = _fake.GatewayWritePackets(WriteCmd);
		Assert.That(chunks, Has.Count.EqualTo(1));
	}

	[Test]
	public void SetKey_ByIndex_EncodesKeyDataCorrectly()
	{
		_fake.EnqueueGatewayWriteAcks(3);
		_session.SetKey(5, UserKey.FromMouseButton(MouseButtons.Right));
		var data = _fake.ReassembleGatewayWriteData(WriteCmd);
		Assert.Multiple(() =>
		{
			Assert.That(data[0], Is.EqualTo(0x20)); // MouseButton type
			Assert.That(data[1], Is.EqualTo(0x02)); // Right button
		});
	}

	[Test]
	public void SetKey_ByIndex_AddressIncludesKeyOffset()
	{
		_fake.EnqueueGatewayWriteAcks(3);
		_session.SetKey(keyIndex: 10, UserKey.FromHid(0x04));
		var chunk = _fake.GatewayWritePackets(WriteCmd)[0];
		ushort addr = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(chunk.AsSpan(5));
		Assert.That(addr, Is.EqualTo(10 * 3)); // offset = 30
	}

	[Test]
	public void SetKey_ByDDKey_ResolvesIndexCorrectly()
	{
		_fake.EnqueueGatewayWriteAcks(3);
		int spaceIdx = _session.GetKeyIndex(DDKey.Space);
		_session.SetKey(DDKey.Space, UserKey.FromHid(0x2C));
		var chunk = _fake.GatewayWritePackets(WriteCmd)[0];
		ushort addr = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(chunk.AsSpan(5));
		Assert.That(addr, Is.EqualTo((ushort)(spaceIdx * 3)));
	}

	[Test]
	public void SetKey_InvalidLayerIndex_Throws()
	{
		Assert.Throws<ArgumentOutOfRangeException>(() =>
			_session.SetKey(0, UserKey.Disabled, layerIndex: 4));
	}
}
