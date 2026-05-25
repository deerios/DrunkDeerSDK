using DrunkDeer.FeatureTests.Fakes;
using DrunkDeer.Protocol;
using NUnit.Framework;

namespace DrunkDeer.FeatureTests.Features;

[TestFixture]
public class KeyTriggerTests
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

	// 128 keys × 8 bytes = 1024 bytes -> ceil(1024/56) = 19 chunks
	private const int TriggerBytes = 128 * 8;
	private const int TriggerChunks = 19;
	private const byte ReadCmd = 0xA0;
	private const byte WriteCmd = 0xA1;

	// ── WriteKeyTriggers ──────────────────────────────────────────────────────

	[Test]
	public void WriteKeyTriggers_SendsCorrectGatewaySubcommand()
	{
		_fake.EnqueueGatewayWriteAcks(TriggerBytes);
		_session.WriteKeyTriggers(Enumerable.Repeat(KeyTriggerConfig.Default, 128).ToArray());
		Assert.That(_fake.GatewayWritePackets(WriteCmd), Is.Not.Empty);
	}

	[Test]
	public void WriteKeyTriggers_SendsCorrectChunkCount()
	{
		_fake.EnqueueGatewayWriteAcks(TriggerBytes);
		_session.WriteKeyTriggers(Enumerable.Repeat(KeyTriggerConfig.Default, 128).ToArray());
		Assert.That(_fake.GatewayWritePackets(WriteCmd), Has.Count.EqualTo(TriggerChunks));
	}

	[Test]
	public void WriteKeyTriggers_FirstKeyEncodedCorrectly()
	{
		_fake.EnqueueGatewayWriteAcks(TriggerBytes);
		var configs = Enumerable.Repeat(KeyTriggerConfig.Default, 128).ToArray();
		// Actuation = 50 raw units
		configs[0] = configs[0] with { Actuation = 50, KeyMode = 1 };
		_session.WriteKeyTriggers(configs);

		var data = _fake.ReassembleGatewayWriteData(WriteCmd);
		// Byte [0]: 0xA0 | switchType(0) = 0xA0
		Assert.That(data[0], Is.EqualTo(0xA0));
		// Byte [1]: priority(0)<<4 | keyMode(1) = 0x01
		Assert.That(data[1], Is.EqualTo(0x01));
		// Bytes [2..3]: LE9 actuation stored = 50-1 = 49 = 0x31
		Assert.That(data[2], Is.EqualTo(49)); // low 8 bits of (50-1)
		Assert.That(data[3] & 0x01, Is.EqualTo(0)); // bit 9 = 0 (49 < 256)
	}

	[Test]
	public void WriteKeyTriggers_WrongLength_Throws()
	{
		Assert.Throws<ArgumentException>(() =>
			_session.WriteKeyTriggers(new KeyTriggerConfig[64]));
	}

	// ── ReadKeyTriggers ───────────────────────────────────────────────────────

	[Test]
	public void ReadKeyTriggers_UsesSubcommand0xA0()
	{
		_fake.EnqueueGatewayRead(new byte[TriggerBytes]);
		_session.ReadKeyTriggers();
		var reads = _fake.SentPackets.Where(p => p[0] == 0x55 && p[1] == ReadCmd).ToList();
		Assert.That(reads, Is.Not.Empty);
	}

	[Test]
	public void ReadKeyTriggers_Returns128Configs()
	{
		_fake.EnqueueGatewayRead(new byte[TriggerBytes]);
		var configs = _session.ReadKeyTriggers();
		Assert.That(configs, Has.Length.EqualTo(128));
	}

	[Test]
	public void ReadKeyTriggers_DecodesActuationCorrectly()
	{
		// Build a raw block where key 0 has actuation = 75 (stored = 74 = 0x4A)
		var raw = new byte[TriggerBytes];
		// byte[0]: 0xA0 (switch type 0), byte[1]: 0x10 (priority=1, keyMode=0)
		raw[0] = 0xA0;
		raw[1] = 0x00; // keyMode=0, priority=0
		raw[2] = 74;   // actuation stored = 75-1 = 74
		raw[3] = 0x00; // bit 9 = 0
		_fake.EnqueueGatewayRead(raw);

		var configs = _session.ReadKeyTriggers();
		Assert.That(configs[0].Actuation, Is.EqualTo(75));
	}

	[Test]
	public void ReadKeyTriggers_DecodesSwitchTypeCorrectly()
	{
		var raw = new byte[TriggerBytes];
		raw[0] = 0xA5; // 0xA0 | switchType=5
		_fake.EnqueueGatewayRead(raw);

		var configs = _session.ReadKeyTriggers();
		Assert.That(configs[0].SwitchType, Is.EqualTo(5));
	}

	// ── SetKeyTrigger (single-key write) ──────────────────────────────────────

	[Test]
	public void SetKeyTrigger_ByIndex_SendsSingleEightByteChunk()
	{
		_fake.EnqueueGatewayWriteAcks(8);
		_session.SetKeyTrigger(0, KeyTriggerConfig.Default);
		Assert.That(_fake.GatewayWritePackets(WriteCmd), Has.Count.EqualTo(1));
	}

	[Test]
	public void SetKeyTrigger_ByIndex_AddressIncludesKeyOffset()
	{
		_fake.EnqueueGatewayWriteAcks(8);
		_session.SetKeyTrigger(keyIndex: 5, KeyTriggerConfig.Default);
		var chunk = _fake.GatewayWritePackets(WriteCmd)[0];
		ushort addr = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(chunk.AsSpan(5));
		Assert.That(addr, Is.EqualTo(5 * 8)); // 40
	}

	[Test]
	public void SetKeyTrigger_ByDDKey_ResolvesIndexCorrectly()
	{
		_fake.EnqueueGatewayWriteAcks(8);
		int wIdx = _session.GetKeyIndex(DDKey.W);
		_session.SetKeyTrigger(DDKey.W, KeyTriggerConfig.Default);
		var chunk = _fake.GatewayWritePackets(WriteCmd)[0];
		ushort addr = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(chunk.AsSpan(5));
		Assert.That(addr, Is.EqualTo((ushort)(wIdx * 8)));
	}

	[Test]
	public void SetKeyTrigger_InvalidIndex_Throws()
	{
		Assert.Throws<ArgumentOutOfRangeException>(() =>
			_session.SetKeyTrigger(128, KeyTriggerConfig.Default));
	}
}
