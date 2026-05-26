using DrunkDeer.FeatureTests.Fakes;
using DrunkDeer.Protocol;
using NUnit.Framework;

namespace DrunkDeer.FeatureTests.Features;

[TestFixture]
public class ProfileManagementTests
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

	// Sub-command byte positions within 0x55 gateway packets
	private const byte SwitchProfileCmd = 0x0E;
	private const byte BaseBlockReadCmd = 0x04;
	private const byte FuncBlockReadCmd = 0x05;
	private const byte FuncBlockWriteCmd = 0x06;
	private const byte KeyMapWriteCmd = 0x09;
	private const byte KeyTriggerWriteCmd = 0xA1;
	private const byte DksWriteCmd = 0xA3;
	private const byte MtWriteCmd = 0xA5;
	private const byte TglWriteCmd = 0xA7;
	private const byte MacroWriteCmd = 0x0D;

	// Region sizes (bytes per profile)
	private const int FuncBlockSize = 64;
	private const int KeyTriggerSize = 1024;  // 128 × 8
	private const int KeyMapSize = 384;   // 128 × 3 per layer
	private const int KeyMapLayerCount = 4;
	private const int DksSize = 768;
	private const int MtSize = 256;
	private const int TglSize = 128;
	private const int MacroSize = MacroAction.BlockSize; // 2048

	// ── Helpers ────────────────────────────────────────────────────────────────

	/// <summary>Queues all gateway reads for a single PullFullProfile call (profile 0).</summary>
	private void EnqueueFullProfileRead()
	{
		_fake.EnqueueGatewayRead(new byte[FuncBlockSize]);
		_fake.EnqueueGatewayRead(new byte[KeyTriggerSize]);
		for (int l = 0; l < KeyMapLayerCount; l++)
			_fake.EnqueueGatewayRead(new byte[KeyMapSize]);
		_fake.EnqueueGatewayRead(new byte[DksSize]);
		_fake.EnqueueGatewayRead(new byte[MtSize]);
		_fake.EnqueueGatewayRead(new byte[TglSize]);
		_fake.EnqueueGatewayRead(new byte[MacroSize]);
	}

	/// <summary>Queues all gateway write ACKs for a single PushFullProfile call (all sections).</summary>
	private void EnqueueFullProfileWriteAcks()
	{
		_fake.EnqueueGatewayWriteAcks(FuncBlockSize);
		_fake.EnqueueGatewayWriteAcks(KeyTriggerSize);
		for (int l = 0; l < KeyMapLayerCount; l++)
			_fake.EnqueueGatewayWriteAcks(KeyMapSize);
		_fake.EnqueueGatewayWriteAcks(DksSize);
		_fake.EnqueueGatewayWriteAcks(MtSize);
		_fake.EnqueueGatewayWriteAcks(TglSize);
		_fake.EnqueueGatewayWriteAcks(MacroSize);
	}

	private static FullProfileData AllSectionsPopulated() => new()
	{
		FuncBlock    = new KeyboardFuncBlock(),
		KeyTriggers  = new KeyTriggerConfig[128],
		KeyMapLayers = Enumerable.Range(0, KeyMapLayerCount)
								 .Select(_ => new UserKey[128])
								 .ToArray(),
		DynamicKeystrokeEntries   = Enumerable.Range(0, DynamicKeystrokeEntry.SlotCount).Select(_ => new DynamicKeystrokeEntry()).ToArray(),
		MultiTapEntries           = new MultiTapEntry[MultiTapEntry.SlotCount],
		ToggleKeyEntries   = new UserKey[32],
		MacroSlots   = new MacroAction[MacroAction.SlotCount][],
	};

	// ── SwitchProfile ──────────────────────────────────────────────────────────

	[Test]
	public void SwitchProfile_UsesSubcommand0x0E()
	{
		_fake.EnqueueGatewayWriteAcks(1);
		_session.SwitchProfile(0);
		Assert.That(_fake.GatewayWritePackets(SwitchProfileCmd), Is.Not.Empty);
	}

	[Test]
	public void SwitchProfile_WritesProfileIndex0()
	{
		_fake.EnqueueGatewayWriteAcks(1);
		_session.SwitchProfile(0);
		var data = _fake.ReassembleGatewayWriteData(SwitchProfileCmd);
		Assert.That(data[0], Is.EqualTo(0));
	}

	[Test]
	public void SwitchProfile_WritesProfileIndex2()
	{
		_fake.EnqueueGatewayWriteAcks(1);
		_session.SwitchProfile(2);
		var data = _fake.ReassembleGatewayWriteData(SwitchProfileCmd);
		Assert.That(data[0], Is.EqualTo(2));
	}

	// ── GetCurrentProfile ──────────────────────────────────────────────────────

	[Test]
	public void GetCurrentProfile_UsesSubcommand0x04()
	{
		_fake.EnqueueGatewayRead(new byte[32]);
		_session.GetCurrentProfile();
		Assert.That(
			_fake.SentPackets.Any(p => p.Length > 1 && p[0] == 0x55 && p[1] == BaseBlockReadCmd),
			Is.True);
	}

	[Test]
	public void GetCurrentProfile_ReturnsFirstByteOfResponse()
	{
		var response = new byte[32];
		response[0] = 3;
		_fake.EnqueueGatewayRead(response);
		Assert.That(_session.GetCurrentProfile(), Is.EqualTo(3));
	}

	// ── PullFullProfile ────────────────────────────────────────────────────────

	[Test]
	public void PullFullProfile_AllSectionsPopulated()
	{
		EnqueueFullProfileRead();
		var data = _session.PullFullProfile();
		Assert.Multiple(() =>
		{
			Assert.That(data.FuncBlock, Is.Not.Null);
			Assert.That(data.KeyTriggers, Is.Not.Null.And.Length.EqualTo(128));
			Assert.That(data.KeyMapLayers, Is.Not.Null.And.Length.EqualTo(KeyMapLayerCount));
			Assert.That(data.DynamicKeystrokeEntries, Is.Not.Null);
			Assert.That(data.MultiTapEntries, Is.Not.Null);
			Assert.That(data.ToggleKeyEntries, Is.Not.Null);
			Assert.That(data.MacroSlots, Is.Not.Null.And.Length.EqualTo(MacroAction.SlotCount));
		});
	}

	[Test]
	public void PullFullProfile_ReadsFuncBlockAtProfileOffset()
	{
		EnqueueFullProfileRead();
		_session.PullFullProfile(profileIndex: 1);
		// FuncBlock sub-cmd 0x05; address for profile 1 = 64 × 1 = 64 = 0x0040
		var readPackets = _fake.SentPackets
			.Where(p => p.Length > 1 && p[0] == 0x55 && p[1] == FuncBlockReadCmd)
			.ToList();
		Assert.That(readPackets, Is.Not.Empty);
		ushort addr = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(readPackets[0].AsSpan(5));
		Assert.That(addr, Is.EqualTo(64)); // profileIndex 1 -> addr = 64 × 1
	}

	[Test]
	public void PullFullProfile_MacroSlotsCount()
	{
		EnqueueFullProfileRead();
		var data = _session.PullFullProfile();
		Assert.That(data.MacroSlots, Has.Length.EqualTo(MacroAction.SlotCount));
	}

	// ── PushFullProfile ────────────────────────────────────────────────────────

	[Test]
	public void PushFullProfile_NullSections_NoWritesForThem()
	{
		// Only FuncBlock is set; all other sections are null.
		_fake.EnqueueGatewayWriteAcks(FuncBlockSize);
		_session.PushFullProfile(new FullProfileData { FuncBlock = new KeyboardFuncBlock() });

		Assert.Multiple(() =>
		{
			Assert.That(_fake.GatewayWritePackets(FuncBlockWriteCmd), Is.Not.Empty, "FuncBlock");
			Assert.That(_fake.GatewayWritePackets(KeyTriggerWriteCmd), Is.Empty, "KeyTriggers");
			Assert.That(_fake.GatewayWritePackets(KeyMapWriteCmd), Is.Empty, "KeyMap");
			Assert.That(_fake.GatewayWritePackets(DksWriteCmd), Is.Empty, "Dks");
			Assert.That(_fake.GatewayWritePackets(MtWriteCmd), Is.Empty, "Mt");
			Assert.That(_fake.GatewayWritePackets(TglWriteCmd), Is.Empty, "Tgl");
			Assert.That(_fake.GatewayWritePackets(MacroWriteCmd), Is.Empty, "Macro");
		});
	}

	[Test]
	public void PushFullProfile_AllSections_AllWriteCommandsSent()
	{
		EnqueueFullProfileWriteAcks();
		_session.PushFullProfile(AllSectionsPopulated());

		Assert.Multiple(() =>
		{
			Assert.That(_fake.GatewayWritePackets(FuncBlockWriteCmd), Is.Not.Empty, "FuncBlock");
			Assert.That(_fake.GatewayWritePackets(KeyTriggerWriteCmd), Is.Not.Empty, "KeyTriggers");
			Assert.That(_fake.GatewayWritePackets(KeyMapWriteCmd), Is.Not.Empty, "KeyMap");
			Assert.That(_fake.GatewayWritePackets(DksWriteCmd), Is.Not.Empty, "Dks");
			Assert.That(_fake.GatewayWritePackets(MtWriteCmd), Is.Not.Empty, "Mt");
			Assert.That(_fake.GatewayWritePackets(TglWriteCmd), Is.Not.Empty, "Tgl");
			Assert.That(_fake.GatewayWritePackets(MacroWriteCmd), Is.Not.Empty, "Macro");
		});
	}

	[Test]
	public void PushFullProfile_KeyTriggersOnly_OnlyKeyTriggerWritten()
	{
		_fake.EnqueueGatewayWriteAcks(KeyTriggerSize);
		_session.PushFullProfile(new FullProfileData { KeyTriggers = new KeyTriggerConfig[128] });

		Assert.Multiple(() =>
		{
			Assert.That(_fake.GatewayWritePackets(KeyTriggerWriteCmd), Is.Not.Empty, "KeyTriggers");
			Assert.That(_fake.GatewayWritePackets(FuncBlockWriteCmd), Is.Empty, "FuncBlock");
			Assert.That(_fake.GatewayWritePackets(MacroWriteCmd), Is.Empty, "Macro");
		});
	}

	[Test]
	public void PushFullProfile_MacroSlotsOnly_CorrectChunkCount()
	{
		_fake.EnqueueGatewayWriteAcks(MacroSize);
		_session.PushFullProfile(new FullProfileData
		{
			MacroSlots = new MacroAction[MacroAction.SlotCount][],
		});
		int expectedChunks = (MacroSize + 55) / 56; // 37
		Assert.That(_fake.GatewayWritePackets(MacroWriteCmd), Has.Count.EqualTo(expectedChunks));
	}

	// ── ProfileCount ───────────────────────────────────────────────────────────

	[Test]
	public void ProfileCount_IsFour() =>
		Assert.That(KeyboardSession.ProfileCount, Is.EqualTo(4));

	// ── SwitchProfile validation ───────────────────────────────────────────────

	[Test]
	public void SwitchProfile_IndexEqualToProfileCount_Throws() =>
		Assert.Throws<ArgumentOutOfRangeException>(() => _session.SwitchProfile(KeyboardSession.ProfileCount));

	[Test]
	public void SwitchProfile_NegativeIndex_Throws() =>
		Assert.Throws<ArgumentOutOfRangeException>(() => _session.SwitchProfile(-1));

	// ── CaptureProfile ────────────────────────────────────────────────────────

	private const byte KeyTriggerReadCmd = 0xA0;

	/// <summary>Queues the reads CaptureProfile needs: one FuncBlock + one KeyTrigger block.</summary>
	private void EnqueueCaptureProfileReads(byte[]? funcBlock = null)
	{
		_fake.EnqueueGatewayRead(funcBlock ?? new byte[FuncBlockSize]);
		_fake.EnqueueGatewayRead(new byte[KeyTriggerSize]);
	}

	[Test]
	public void CaptureProfile_InvalidIndex_Throws() =>
		Assert.Throws<ArgumentOutOfRangeException>(() => _session.CaptureProfile(KeyboardSession.ProfileCount));

	[Test]
	public void CaptureProfile_AllZeroTriggers_ReturnsUniformDepths()
	{
		EnqueueCaptureProfileReads();
		var profile = _session.CaptureProfile();
		Assert.Multiple(() =>
		{
			Assert.That(profile.ActuationMm, Is.Not.Null, "uniform actuation");
			Assert.That(profile.PerKeyActuationMm, Is.Null, "no per-key actuation");
			Assert.That(profile.DownstrokeMm, Is.Not.Null, "uniform downstroke");
			Assert.That(profile.PerKeyDownstrokeMm, Is.Null, "no per-key downstroke");
			Assert.That(profile.UpstrokeMm, Is.Not.Null, "uniform upstroke");
			Assert.That(profile.PerKeyUpstrokeMm, Is.Null, "no per-key upstroke");
		});
	}

	[Test]
	public void CaptureProfile_ReadsBothFuncBlockAndKeyTriggers()
	{
		EnqueueCaptureProfileReads();
		_session.CaptureProfile();
		Assert.Multiple(() =>
		{
			Assert.That(
				_fake.SentPackets.Any(p => p.Length > 1 && p[0] == 0x55 && p[1] == FuncBlockReadCmd),
				Is.True, "FuncBlock read");
			Assert.That(
				_fake.SentPackets.Any(p => p.Length > 1 && p[0] == 0x55 && p[1] == KeyTriggerReadCmd),
				Is.True, "KeyTrigger read");
		});
	}

	[Test]
	public void CaptureProfile_SingleColorTheme_CapturedWhenEffectActive()
	{
		var func = new byte[FuncBlockSize];
		func[8]  = 2;    // LightEffect = preset 2
		func[12] = 0;    // LightSingleColor = true (0 = single-colour on)
		func[9]  = 7;    // LightBrightness
		func[14] = 0xFF; // R
		func[15] = 0x80; // G
		func[16] = 0x10; // B

		EnqueueCaptureProfileReads(func);
		var profile = _session.CaptureProfile();

		Assert.That(profile.Theme, Is.Not.Null, "theme captured");
		Assert.Multiple(() =>
		{
			Assert.That(profile.Theme!.BaseColor.R, Is.EqualTo(0xFF));
			Assert.That(profile.Theme!.BaseColor.G, Is.EqualTo(0x80));
			Assert.That(profile.Theme!.BaseColor.B, Is.EqualTo(0x10));
			Assert.That(profile.Theme!.Brightness, Is.EqualTo(7));
		});
	}

	[Test]
	public void CaptureProfile_NoTheme_WhenMultiColorPreset()
	{
		var func = new byte[FuncBlockSize];
		func[8]  = 2;    // LightEffect = preset 2
		func[12] = 1;    // LightSingleColor = false (non-zero = multi-colour)

		EnqueueCaptureProfileReads(func);
		var profile = _session.CaptureProfile();

		Assert.That(profile.Theme, Is.Null, "no theme for multi-colour preset");
	}

	[Test]
	public void CaptureProfile_NoTheme_WhenCustomRgbMode()
	{
		var func = new byte[FuncBlockSize];
		func[8]  = 0;    // LightEffect = 0 (custom per-key RGB)
		func[12] = 0;    // LightSingleColor = true (irrelevant since effect is 0)

		EnqueueCaptureProfileReads(func);
		var profile = _session.CaptureProfile();

		Assert.That(profile.Theme, Is.Null, "no theme for custom RGB mode");
	}

	// ── CopyProfile ───────────────────────────────────────────────────────────

	[Test]
	public void CopyProfile_SameSlot_SendsNoPackets()
	{
		_session.CopyProfile(0, 0);
		Assert.That(_fake.SentPackets, Is.Empty);
	}

	[Test]
	public void CopyProfile_InvalidFromSlot_Throws() =>
		Assert.Throws<ArgumentOutOfRangeException>(() => _session.CopyProfile(KeyboardSession.ProfileCount, 0));

	[Test]
	public void CopyProfile_InvalidToSlot_Throws() =>
		Assert.Throws<ArgumentOutOfRangeException>(() => _session.CopyProfile(0, KeyboardSession.ProfileCount));

	[Test]
	public void CopyProfile_PullsThenPushesAllSections()
	{
		EnqueueFullProfileRead();
		EnqueueFullProfileWriteAcks();
		_session.CopyProfile(0, 1);

		// After pull + push, write commands for every section must be present.
		Assert.Multiple(() =>
		{
			Assert.That(_fake.GatewayWritePackets(FuncBlockWriteCmd), Is.Not.Empty, "FuncBlock");
			Assert.That(_fake.GatewayWritePackets(KeyTriggerWriteCmd), Is.Not.Empty, "KeyTriggers");
			Assert.That(_fake.GatewayWritePackets(KeyMapWriteCmd), Is.Not.Empty, "KeyMap");
			Assert.That(_fake.GatewayWritePackets(DksWriteCmd), Is.Not.Empty, "Dks");
			Assert.That(_fake.GatewayWritePackets(MtWriteCmd), Is.Not.Empty, "Mt");
			Assert.That(_fake.GatewayWritePackets(TglWriteCmd), Is.Not.Empty, "Tgl");
			Assert.That(_fake.GatewayWritePackets(MacroWriteCmd), Is.Not.Empty, "Macro");
		});
	}

	[Test]
	public void CopyProfile_PushesToTargetProfileOffset()
	{
		EnqueueFullProfileRead();
		EnqueueFullProfileWriteAcks();
		_session.CopyProfile(fromSlot: 0, toSlot: 2);

		// FuncBlock for profile 2 lives at addr 64 × 2 = 128 = 0x0080.
		var funcWrites = _fake.GatewayWritePackets(FuncBlockWriteCmd);
		Assert.That(funcWrites, Is.Not.Empty);
		ushort addr = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(
			funcWrites[0].AsSpan(5));
		Assert.That(addr, Is.EqualTo(128)); // profile 2 -> 64 × 2
	}
}
