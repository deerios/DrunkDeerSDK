using DrunkDeer.FeatureTests.Fakes;
using DrunkDeer.Protocol;
using NUnit.Framework;

namespace DrunkDeer.FeatureTests.Features;

/// <summary>
/// Tests for FuncBlock-backed methods: SetKeyboardMode, SetReportRate, SetDebounce,
/// SetStabilityMode, EnableBerserkMode, ConfigureKeyLocks, SetLightPreset, SetLightCustom.
/// Each method calls FetchFuncBlock (read 64 bytes -> 2 chunks) then PushFuncBlock
/// (write 64 bytes -> 2 chunks), so every test enqueues 4 responses via EnqueueFuncBlockCycle.
/// The written block data is at bytes [8..] of the write packets.
/// </summary>
[TestFixture]
public class FuncBlockTests
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

	/// <summary>
	/// Reassembles the 64-byte block pushed back to the keyboard from the 0x55/0x06 write chunks.
	/// </summary>
	private byte[] CaptureWrittenBlock()
	{
		var data = _fake.ReassembleGatewayWriteData(subCmd: 0x06);
		Assert.That(data, Has.Length.EqualTo(64), "FuncBlock push should write exactly 64 bytes");
		return data;
	}

	// ── SetKeyboardMode ───────────────────────────────────────────────────────

	[Test]
	public void SetKeyboardMode_Mac_WritesMacModeByteToOne()
	{
		_fake.EnqueueFuncBlockCycle();
		_session.SetKeyboardMode(KeyboardMode.Mac);
		var block = CaptureWrittenBlock();
		Assert.That(block[1] & 0x0F, Is.EqualTo(1)); // MacMode = low nibble of byte[1]
	}

	[Test]
	public void SetKeyboardMode_Windows_WritesMacModeByteToZero()
	{
		// Start with a block that has Mac mode set
		var existing = new byte[64];
		existing[1] = 0x01; // MacMode = 1
		_fake.EnqueueFuncBlockCycle(existing);
		_session.SetKeyboardMode(KeyboardMode.Windows);
		var block = CaptureWrittenBlock();
		Assert.That(block[1] & 0x0F, Is.EqualTo(0));
	}

	[Test]
	public void SetKeyboardMode_PreservesOtherFieldsInBlock()
	{
		var existing = new byte[64];
		existing[4] = 0x03; // ReportRate = Hz1000 (high nibble=0, low nibble=3)
		_fake.EnqueueFuncBlockCycle(existing);
		_session.SetKeyboardMode(KeyboardMode.Mac);
		var block = CaptureWrittenBlock();
		Assert.That(block[4] & 0x0F, Is.EqualTo(3)); // ReportRate unchanged
	}

	// ── SetReportRate ─────────────────────────────────────────────────────────

	[Test]
	public void SetReportRate_Hz1000_WritesRateByte3ToBlock()
	{
		_fake.EnqueueFuncBlockCycle();
		_session.SetReportRate(ReportRate.Hz1000);
		var block = CaptureWrittenBlock();
		Assert.That(block[4] & 0x0F, Is.EqualTo((byte)ReportRate.Hz1000));
	}

	[Test]
	public void SetReportRate_Hz500_WritesRateByte2ToBlock()
	{
		_fake.EnqueueFuncBlockCycle();
		_session.SetReportRate(ReportRate.Hz500);
		var block = CaptureWrittenBlock();
		Assert.That(block[4] & 0x0F, Is.EqualTo((byte)ReportRate.Hz500));
	}

	[Test]
	public void SetReportRate_Hz125_WritesRateByte0ToBlock()
	{
		_fake.EnqueueFuncBlockCycle();
		_session.SetReportRate(ReportRate.Hz125);
		var block = CaptureWrittenBlock();
		Assert.That(block[4] & 0x0F, Is.EqualTo((byte)ReportRate.Hz125));
	}

	// ── SetDebounce ───────────────────────────────────────────────────────────

	[Test]
	public void SetDebounce_Level3_WritesDebounceFieldToBlock()
	{
		_fake.EnqueueFuncBlockCycle();
		_session.SetDebounce(3);
		var block = CaptureWrittenBlock();
		Assert.That((block[7] >> 5) & 0x07, Is.EqualTo(3)); // bits 5–7 of byte[7]
	}

	[Test]
	public void SetDebounce_Level0_WritesZeroDebounce()
	{
		var existing = new byte[64];
		existing[7] = 0xE0; // all debounce bits set
		_fake.EnqueueFuncBlockCycle(existing);
		_session.SetDebounce(0);
		var block = CaptureWrittenBlock();
		Assert.That((block[7] >> 5) & 0x07, Is.EqualTo(0));
	}

	[Test]
	public void SetDebounce_AboveMax_Throws()
	{
		Assert.Throws<ArgumentOutOfRangeException>(() => _session.SetDebounce(8));
	}

	// ── SetStabilityMode ──────────────────────────────────────────────────────

	[Test]
	public void SetStabilityMode_Level2_WritesStabilityFieldToBlock()
	{
		_fake.EnqueueFuncBlockCycle();
		_session.SetStabilityMode(2);
		var block = CaptureWrittenBlock();
		Assert.That((block[7] >> 1) & 0x03, Is.EqualTo(2)); // bits 1–2 of byte[7]
	}

	[Test]
	public void SetStabilityMode_AboveMax_Throws()
	{
		Assert.Throws<ArgumentOutOfRangeException>(() => _session.SetStabilityMode(4));
	}

	// ── EnableBerserkMode / DisableBerserkMode ────────────────────────────────

	[Test]
	public void EnableBerserkMode_SetsBit0OfByte7()
	{
		_fake.EnqueueFuncBlockCycle();
		_session.EnableBerserkMode();
		var block = CaptureWrittenBlock();
		Assert.That(block[7] & 0x01, Is.EqualTo(1));
	}

	[Test]
	public void DisableBerserkMode_ClearsBit0OfByte7()
	{
		var existing = new byte[64];
		existing[7] = 0x01; // BerserkMode on
		_fake.EnqueueFuncBlockCycle(existing);
		_session.DisableBerserkMode();
		var block = CaptureWrittenBlock();
		Assert.That(block[7] & 0x01, Is.EqualTo(0));
	}

	// ── ConfigureKeyLocks ─────────────────────────────────────────────────────

	[Test]
	public void ConfigureKeyLocks_WinLock_SetsBit0OfByte6()
	{
		_fake.EnqueueFuncBlockCycle();
		_session.ConfigureKeyLocks(winLock: true);
		var block = CaptureWrittenBlock();
		Assert.That(block[6] & 0x01, Is.EqualTo(1));
	}

	[Test]
	public void ConfigureKeyLocks_AltTabLock_SetsBit1OfByte6()
	{
		_fake.EnqueueFuncBlockCycle();
		_session.ConfigureKeyLocks(altTabLock: true);
		var block = CaptureWrittenBlock();
		Assert.That(block[6] & 0x02, Is.EqualTo(2));
	}

	[Test]
	public void ConfigureKeyLocks_AltF4Lock_SetsBit2OfByte6()
	{
		_fake.EnqueueFuncBlockCycle();
		_session.ConfigureKeyLocks(altF4Lock: true);
		var block = CaptureWrittenBlock();
		Assert.That(block[6] & 0x04, Is.EqualTo(4));
	}

	[Test]
	public void ConfigureKeyLocks_NullParameter_LeavesExistingBitUnchanged()
	{
		var existing = new byte[64];
		existing[6] = 0x01; // WinLock on
		_fake.EnqueueFuncBlockCycle(existing);
		_session.ConfigureKeyLocks(winLock: null, altTabLock: true); // only AltTab changes
		var block = CaptureWrittenBlock();
		Assert.Multiple(() =>
		{
			Assert.That(block[6] & 0x01, Is.EqualTo(1)); // WinLock preserved
			Assert.That(block[6] & 0x02, Is.EqualTo(2)); // AltTab set
		});
	}

	// ── SetLightPreset / SetLightCustom ───────────────────────────────────────

	[Test]
	public void SetLightPreset_WritesEffectBrightnessSpeedToBlock()
	{
		_fake.EnqueueFuncBlockCycle();
		_session.SetLightPreset(effect: LightPreset.SurfToTheRight, brightness: 7, speed: 4);
		var block = CaptureWrittenBlock();
		Assert.Multiple(() =>
		{
			Assert.That(block[8], Is.EqualTo(3)); // LightEffect
			Assert.That(block[9], Is.EqualTo(7)); // LightBrightness
			Assert.That(block[10], Is.EqualTo(4)); // LightSpeed
		});
	}

	[Test]
	public void SetLightCustom_WritesEffectZeroToBlock()
	{
		var existing = new byte[64];
		existing[8] = 5; // LightEffect = 5
		_fake.EnqueueFuncBlockCycle(existing);
		_session.SetLightCustom();
		var block = CaptureWrittenBlock();
		Assert.That(block[8], Is.EqualTo(0)); // LightEffect cleared
	}

	// ── SetLightPresetColor ───────────────────────────────────────────────────

	[Test]
	public void SetLightPresetColor_WritesSingleColorFlagAndRgb()
	{
		_fake.EnqueueFuncBlockCycle();
		_session.SetLightPresetColor(r: 0xFF, g: 0x80, b: 0x10);
		var block = CaptureWrittenBlock();
		Assert.Multiple(() =>
		{
			Assert.That(block[12], Is.EqualTo(0x00)); // LightSingleColor: 0 = single-colour on
			Assert.That(block[14], Is.EqualTo(0xFF));     // R
			Assert.That(block[15], Is.EqualTo(0x80));     // G
			Assert.That(block[16], Is.EqualTo(0x10));     // B
		});
	}

	// ── SetTickRate ───────────────────────────────────────────────────────────

	[Test]
	public void SetTickRate_WritesRateToHighNibbleOfByte4()
	{
		_fake.EnqueueFuncBlockCycle();
		_session.SetTickRate(5);
		var block = CaptureWrittenBlock();
		Assert.That((block[4] >> 4) & 0x0F, Is.EqualTo(5));
	}

	[Test]
	public void SetTickRate_PreservesReportRateInLowNibble()
	{
		var existing = new byte[64];
		existing[4] = 0x03; // ReportRate = Hz1000
		_fake.EnqueueFuncBlockCycle(existing);
		_session.SetTickRate(7);
		var block = CaptureWrittenBlock();
		Assert.Multiple(() =>
		{
			Assert.That(block[4] & 0x0F, Is.EqualTo(3)); // ReportRate preserved
			Assert.That((block[4] >> 4) & 0x0F, Is.EqualTo(7)); // TickRate set
		});
	}

	// ── SetLogoLightPreset / SetLogoLightOff / SetLogoLightColor ─────────────

	[Test]
	public void SetLogoLightPreset_WritesEffectBrightnessSpeedToBytes24_26()
	{
		_fake.EnqueueFuncBlockCycle();
		_session.SetLogoLightPreset(effect: LightPreset.WaveSpectrum, brightness: 8, speed: 3);
		var block = CaptureWrittenBlock();
		Assert.Multiple(() =>
		{
			Assert.That(block[24], Is.EqualTo(2)); // LogoLightEffect
			Assert.That(block[25], Is.EqualTo(8)); // LogoLightBrightness
			Assert.That(block[26], Is.EqualTo(3)); // LogoLightSpeed
		});
	}

	[Test]
	public void SetLogoLightOff_WritesZeroEffectToByte24()
	{
		var existing = new byte[64];
		existing[24] = 3; // logo effect on
		_fake.EnqueueFuncBlockCycle(existing);
		_session.SetLogoLightOff();
		var block = CaptureWrittenBlock();
		Assert.That(block[24], Is.EqualTo(0));
	}

	[Test]
	public void SetLogoLightColor_WritesSingleColorFlagAndRgbToBytes27_31()
	{
		_fake.EnqueueFuncBlockCycle();
		_session.SetLogoLightColor(r: 0x10, g: 0x20, b: 0x30);
		var block = CaptureWrittenBlock();
		Assert.Multiple(() =>
		{
			Assert.That(block[27], Is.EqualTo(0x00)); // LogoLightSingleColor: 0 = single-colour on
			Assert.That(block[29], Is.EqualTo(0x10));     // R
			Assert.That(block[30], Is.EqualTo(0x20));     // G
			Assert.That(block[31], Is.EqualTo(0x30));     // B
		});
	}

	// ── SetSideLightPreset / SetSideLightOff / SetSideLightColor ─────────────

	[Test]
	public void SetSideLightPreset_WritesEffectBrightnessSpeedToBytes32_34()
	{
		_fake.EnqueueFuncBlockCycle();
		_session.SetSideLightPreset(effect: LightPreset.Breath, brightness: 6, speed: 2);
		var block = CaptureWrittenBlock();
		Assert.Multiple(() =>
		{
			Assert.That(block[32], Is.EqualTo(4)); // SideLightEffect
			Assert.That(block[33], Is.EqualTo(6)); // SideLightBrightness
			Assert.That(block[34], Is.EqualTo(2)); // SideLightSpeed
		});
	}

	[Test]
	public void SetSideLightOff_WritesZeroEffectToByte32()
	{
		var existing = new byte[64];
		existing[32] = 2; // side effect on
		_fake.EnqueueFuncBlockCycle(existing);
		_session.SetSideLightOff();
		var block = CaptureWrittenBlock();
		Assert.That(block[32], Is.EqualTo(0));
	}

	[Test]
	public void SetSideLightColor_WritesSingleColorFlagAndRgbToBytes35_39()
	{
		_fake.EnqueueFuncBlockCycle();
		_session.SetSideLightColor(r: 0xAA, g: 0xBB, b: 0xCC);
		var block = CaptureWrittenBlock();
		Assert.Multiple(() =>
		{
			Assert.That(block[35], Is.EqualTo(0x00)); // SideLightSingleColor: 0 = single-colour on
			Assert.That(block[37], Is.EqualTo(0xAA));     // R
			Assert.That(block[38], Is.EqualTo(0xBB));     // G
			Assert.That(block[39], Is.EqualTo(0xCC));     // B
		});
	}
}
