using DrunkDeer.FeatureTests.Fakes;
using DrunkDeer.Protocol;
using NUnit.Framework;

namespace DrunkDeer.FeatureTests.Features;

/// <summary>
/// Pins the async FuncBlock twins against their synchronous originals by comparing the actual
/// bytes each one puts on the wire. The async half is a hand-written port of the sync half, so
/// asserting "it produced a packet" would not catch the failure that matters: a twin that talks
/// to the keyboard slightly differently from the method it claims to mirror. A wrong sub-command,
/// address, checksum, or a dropped field would all pass a shape-only test and corrupt a real
/// keyboard's flash.
/// </summary>
/// <remarks>
/// G65 m1 is always Kun-precision, so it has the FuncBlock gateway without LogoLight or SideLight.
/// Each case runs the sync method against one fake and the async twin against another, primed
/// identically, then compares <see cref="FakeKeyboardConnection.SentPackets"/> byte for byte.
/// </remarks>
[TestFixture]
public class AsyncFuncBlockParityTests
{
	/// <summary>
	/// A function block with every byte distinct and non-zero, used to prime the read half of a
	/// read-modify-write cycle.
	/// </summary>
	/// <remarks>
	/// Priming with <c>new byte[64]</c> silently defeats this whole fixture. Several FuncBlock
	/// fields write 0 for their "on" state — <c>LightSingleColor = true</c> stores 0x00, since the
	/// flag is inverted on the wire — so against an all-zero block, setting them is indistinguishable
	/// from not setting them at all, and a twin that drops the field entirely still produces
	/// identical bytes. A distinct non-zero seed makes every field write observable.
	/// </remarks>
	private static byte[] SeededBlock()
	{
		var block = new byte[64];
		for (int i = 0; i < block.Length; i++)
			block[i] = (byte)(0xA1 + i * 7); // wraps, but stays non-zero across the block
		return block;
	}

	private static (FakeKeyboardConnection fake, KeyboardSession session) NewSession(string? slug = null) =>
		NewSessionFor(slug ?? ModelSlugs.G65M1);

	private static (FakeKeyboardConnection fake, KeyboardSession session) NewSessionFor(string slug)
	{
		var fake = new FakeKeyboardConnection(ModelRegistry.GetInfo(slug));
		return (fake, new KeyboardSession(fake));
	}

	/// <summary>
	/// Runs a sync action and its async twin against separately primed fakes and asserts the two
	/// wire traces are identical. <paramref name="prime"/> re-enqueues the same responses for each
	/// side, since both consume the queue.
	/// </summary>
	private static async Task AssertWireParityAsync(
		Action<FakeKeyboardConnection, KeyboardSession> prime,
		Action<KeyboardSession> sync,
		Func<KeyboardSession, Task> async,
		string? slug = null)
	{
		var (syncFake, syncSession) = NewSession(slug);
		using (syncSession)
		{
			prime(syncFake, syncSession);
			sync(syncSession);
		}

		var (asyncFake, asyncSession) = NewSession(slug);
		using (asyncSession)
		{
			prime(asyncFake, asyncSession);
			await async(asyncSession);
		}

		Assert.That(asyncFake.SentPackets, Has.Count.EqualTo(syncFake.SentPackets.Count),
			"async twin sent a different number of packets than the sync original");
		for (int i = 0; i < syncFake.SentPackets.Count; i++)
		{
			Assert.That(asyncFake.SentPackets[i], Is.EqualTo(syncFake.SentPackets[i]),
				$"packet {i} differs between the sync method and its async twin");
		}
	}

	// ── Function block settings ───────────────────────────────────────────────

	[Test]
	public Task SetKeyboardModeAsync_MatchesSyncWireTraffic() => AssertWireParityAsync(
		(f, _) => f.EnqueueFuncBlockCycle(SeededBlock()),
		s => s.SetKeyboardMode(KeyboardMode.Mac),
		s => s.SetKeyboardModeAsync(KeyboardMode.Mac));

	[Test]
	public Task SetReportRateAsync_MatchesSyncWireTraffic() => AssertWireParityAsync(
		(f, _) => f.EnqueueFuncBlockCycle(SeededBlock()),
		s => s.SetReportRate(ReportRate.Hz1000),
		s => s.SetReportRateAsync(ReportRate.Hz1000));

	[Test]
	public Task SetDebounceAsync_MatchesSyncWireTraffic() => AssertWireParityAsync(
		(f, _) => f.EnqueueFuncBlockCycle(SeededBlock()),
		s => s.SetDebounce(5),
		s => s.SetDebounceAsync(5));

	[Test]
	public Task SetStabilityModeAsync_MatchesSyncWireTraffic() => AssertWireParityAsync(
		(f, _) => f.EnqueueFuncBlockCycle(SeededBlock()),
		s => s.SetStabilityMode(2),
		s => s.SetStabilityModeAsync(2));

	[Test]
	public Task SetTickRateAsync_MatchesSyncWireTraffic() => AssertWireParityAsync(
		(f, _) => f.EnqueueFuncBlockCycle(SeededBlock()),
		s => s.SetTickRate(11),
		s => s.SetTickRateAsync(11));

	[Test]
	public Task ConfigureKeyLocksAsync_MatchesSyncWireTraffic() => AssertWireParityAsync(
		(f, _) => f.EnqueueFuncBlockCycle(SeededBlock()),
		s => s.ConfigureKeyLocks(winLock: true, altF4Lock: false),
		s => s.ConfigureKeyLocksAsync(winLock: true, altF4Lock: false));

	// Guards the two fields the sync original sets that a naive port drops: the single-colour
	// flag rides along with the RGB bytes.
	[Test]
	public Task SetLightPresetColorAsync_MatchesSyncWireTraffic() => AssertWireParityAsync(
		(f, _) => f.EnqueueFuncBlockCycle(SeededBlock()),
		s => s.SetLightPresetColor(0x30, 0x60, 0xC0),
		s => s.SetLightPresetColorAsync(0x30, 0x60, 0xC0));

	[Test]
	public Task SetLightPresetAsync_MatchesSyncWireTraffic() => AssertWireParityAsync(
		(f, _) => f.EnqueueFuncBlockCycle(SeededBlock()),
		s => s.SetLightPreset(LightPreset.Ripple, brightness: 7, speed: 3),
		s => s.SetLightPresetAsync(LightPreset.Ripple, brightness: 7, speed: 3));

	[Test]
	public Task SetLightCustomAsync_MatchesSyncWireTraffic() => AssertWireParityAsync(
		(f, _) => f.EnqueueFuncBlockCycle(SeededBlock()),
		s => s.SetLightCustom(),
		s => s.SetLightCustomAsync());

	// ── Zone lighting (needs models that actually have the zones) ──────────────

	[Test]
	public Task SetLogoLightColorAsync_MatchesSyncWireTraffic() => AssertWireParityAsync(
		(f, _) => f.EnqueueFuncBlockCycle(SeededBlock()),
		s => s.SetLogoLightColor(0x10, 0x20, 0x30),
		s => s.SetLogoLightColorAsync(0x10, 0x20, 0x30),
		slug: ModelSlugs.A75Ultra);

	[Test]
	public Task SetSideLightColorAsync_MatchesSyncWireTraffic() => AssertWireParityAsync(
		(f, _) => f.EnqueueFuncBlockCycle(SeededBlock()),
		s => s.SetSideLightColor(0x10, 0x20, 0x30),
		s => s.SetSideLightColorAsync(0x10, 0x20, 0x30),
		slug: ModelSlugs.X60Future);

	// ── Key map ───────────────────────────────────────────────────────────────

	[Test]
	public Task SetKeyAsync_MatchesSyncWireTraffic() => AssertWireParityAsync(
		(f, _) => f.EnqueueGatewayWriteAcks(3),
		s => s.SetKey(12, new UserKey { Type = 1, Param1 = 2, Param2 = 3 }, layerIndex: 2, profileIndex: 1),
		s => s.SetKeyAsync(12, new UserKey { Type = 1, Param1 = 2, Param2 = 3 }, layerIndex: 2, profileIndex: 1));

	[Test]
	public Task ReadKeyMapAsync_MatchesSyncWireTraffic() => AssertWireParityAsync(
		(f, _) => f.EnqueueGatewayRead(new byte[128 * 3]),
		s => s.ReadKeyMap(layerIndex: 1, profileIndex: 2),
		async s => await s.ReadKeyMapAsync(layerIndex: 1, profileIndex: 2));

	[Test]
	public Task ReadDefaultKeyMapAsync_MatchesSyncWireTraffic() => AssertWireParityAsync(
		(f, _) => f.EnqueueGatewayRead(new byte[128 * 3]),
		s => s.ReadDefaultKeyMap(layerIndex: 1),
		async s => await s.ReadDefaultKeyMapAsync(layerIndex: 1));

	[Test]
	public Task WriteKeyMapAsync_MatchesSyncWireTraffic()
	{
		var keys = new UserKey[128];
		for (int i = 0; i < keys.Length; i++)
			keys[i] = new UserKey { Type = (byte)(i % 7), Param1 = (byte)i, Param2 = 0 };
		return AssertWireParityAsync(
			(f, _) => f.EnqueueGatewayWriteAcks(128 * 3),
			s => s.WriteKeyMap(keys),
			s => s.WriteKeyMapAsync(keys));
	}

	// ── Per-key triggers ──────────────────────────────────────────────────────

	[Test]
	public Task ReadKeyTriggersAsync_MatchesSyncWireTraffic() => AssertWireParityAsync(
		(f, _) => f.EnqueueGatewayRead(new byte[1024]),
		s => s.ReadKeyTriggers(profileIndex: 1),
		async s => await s.ReadKeyTriggersAsync(profileIndex: 1));

	[Test]
	public Task SetKeyTriggerAsync_MatchesSyncWireTraffic() => AssertWireParityAsync(
		(f, _) => f.EnqueueGatewayWriteAcks(8),
		s => s.SetKeyTrigger(9, new KeyTriggerConfig(), profileIndex: 2),
		s => s.SetKeyTriggerAsync(9, new KeyTriggerConfig(), profileIndex: 2));

	// ── Dynamic Keystroke / Multi-Tap / Toggle ────────────────────────────────

	[Test]
	public Task ReadDynamicKeystrokeEntriesAsync_MatchesSyncWireTraffic() => AssertWireParityAsync(
		(f, _) => f.EnqueueGatewayRead(new byte[768]),
		s => s.ReadDynamicKeystrokeEntries(profileIndex: 3),
		async s => await s.ReadDynamicKeystrokeEntriesAsync(profileIndex: 3));

	[Test]
	public Task SetDynamicKeystrokeEntryAsync_MatchesSyncWireTraffic() => AssertWireParityAsync(
		(f, _) => f.EnqueueGatewayWriteAcks(DynamicKeystrokeEntry.ByteSize),
		s => s.SetDynamicKeystrokeEntry(4, new DynamicKeystrokeEntry()),
		s => s.SetDynamicKeystrokeEntryAsync(4, new DynamicKeystrokeEntry()));

	[Test]
	public Task ReadMultiTapEntriesAsync_MatchesSyncWireTraffic() => AssertWireParityAsync(
		(f, _) => f.EnqueueGatewayRead(new byte[MultiTapEntry.SlotCount * MultiTapEntry.ByteSize]),
		s => s.ReadMultiTapEntries(),
		async s => await s.ReadMultiTapEntriesAsync());

	[Test]
	public Task SetMultiTapEntryAsync_MatchesSyncWireTraffic() => AssertWireParityAsync(
		(f, _) => f.EnqueueGatewayWriteAcks(MultiTapEntry.ByteSize),
		s => s.SetMultiTapEntry(6, new MultiTapEntry(), profileIndex: 1),
		s => s.SetMultiTapEntryAsync(6, new MultiTapEntry(), profileIndex: 1));

	[Test]
	public Task ReadToggleKeyEntriesAsync_MatchesSyncWireTraffic() => AssertWireParityAsync(
		(f, _) => f.EnqueueGatewayRead(new byte[32 * 3]),
		s => s.ReadToggleKeyEntries(),
		async s => await s.ReadToggleKeyEntriesAsync());

	[Test]
	public Task SetToggleKeyEntryAsync_MatchesSyncWireTraffic() => AssertWireParityAsync(
		(f, _) => f.EnqueueGatewayWriteAcks(3),
		s => s.SetToggleKeyEntry(7, new UserKey { Type = 1, Param1 = 2, Param2 = 3 }),
		s => s.SetToggleKeyEntryAsync(7, new UserKey { Type = 1, Param1 = 2, Param2 = 3 }));

	// ── Macros ────────────────────────────────────────────────────────────────

	[Test]
	public Task ReadMacroSlotsAsync_MatchesSyncWireTraffic() => AssertWireParityAsync(
		(f, _) => f.EnqueueGatewayRead(new byte[MacroAction.BlockSize]),
		s => s.ReadMacroSlots(),
		async s => await s.ReadMacroSlotsAsync());

	// ── Profile slots ─────────────────────────────────────────────────────────

	[Test]
	public Task SwitchProfileAsync_MatchesSyncWireTraffic() => AssertWireParityAsync(
		(f, _) => f.EnqueueGatewayWriteAcks(1),
		s => s.SwitchProfile(2),
		s => s.SwitchProfileAsync(2));

	[Test]
	public Task GetCurrentProfileAsync_MatchesSyncWireTraffic() => AssertWireParityAsync(
		(f, _) => f.EnqueueGatewayRead(new byte[32]),
		s => s.GetCurrentProfile(),
		async s => await s.GetCurrentProfileAsync());

	[Test]
	public Task ReadFuncBlockAsync_MatchesSyncWireTraffic() => AssertWireParityAsync(
		(f, _) => f.EnqueueGatewayRead(new byte[64]),
		s => s.ReadFuncBlock(profileIndex: 1),
		async s => await s.ReadFuncBlockAsync(profileIndex: 1));

	[Test]
	public Task ReadStoredColorsAsync_MatchesSyncWireTraffic() => AssertWireParityAsync(
		(f, _) => f.EnqueueGatewayRead(new byte[128 * 3]),
		s => s.ReadStoredColors(profileIndex: 1),
		async s => await s.ReadStoredColorsAsync(profileIndex: 1));

	[Test]
	public Task WriteStoredColorsAsync_MatchesSyncWireTraffic()
	{
		var colors = new (byte R, byte G, byte B)[128];
		for (int i = 0; i < colors.Length; i++)
			colors[i] = ((byte)(i * 2), (byte)(255 - i), (byte)(i | 0x40));
		return AssertWireParityAsync(
			(f, _) => f.EnqueueGatewayWriteAcks(128 * 3),
			s => s.WriteStoredColors(colors, profileIndex: 1),
			s => s.WriteStoredColorsAsync(colors, profileIndex: 1));
	}

	[Test]
	public Task SaveLightingToProfileAsync_MatchesSyncWireTraffic() => AssertWireParityAsync(
		(f, _) => f.EnqueueGatewayWriteAcks(128 * 3),
		s => s.SaveLightingToProfile(profileIndex: 2),
		s => s.SaveLightingToProfileAsync(profileIndex: 2));

	[Test]
	public Task ReadLiveColorsAsync_MatchesSyncWireTraffic() => AssertWireParityAsync(
		(f, _) => f.EnqueueGatewayRead(new byte[128 * 3]),
		s => s.ReadLiveColors(),
		async s => await s.ReadLiveColorsAsync());

	// Reads the stored block, then pushes it live, so this covers both halves of the twin: the
	// gateway read needs its data and the RGB push that follows needs its own 0xAE ACKs.
	[Test]
	public Task LoadLightingFromProfileAsync_MatchesSyncWireTraffic()
	{
		var stored = new byte[128 * 3];
		for (int i = 0; i < stored.Length; i++) stored[i] = (byte)(i * 5 + 1);
		return AssertWireParityAsync(
			(f, s) =>
			{
				f.EnqueueGatewayRead(stored);
				// The live push that follows needs one 0xAE ACK per RGB packet, and RGB goes out
				// 13 keys at a time. Read off the session so it tracks the model, not a constant.
				f.EnqueueAcks(0xAE, (s.LightingKeyCount + 12) / 13);
			},
			s => s.LoadLightingFromProfile(profileIndex: 1, brightness: 6),
			s => s.LoadLightingFromProfileAsync(profileIndex: 1, brightness: 6));
	}

	// ── Maintenance (bare 0x55 sub-commands, not gateway transfers) ────────────

	[Test]
	public Task StartCalibrationAsync_MatchesSyncWireTraffic() => AssertWireParityAsync(
		(_, _) => { },
		s => s.StartCalibration(),
		s => s.StartCalibrationAsync());

	[Test]
	public Task EndCalibrationAsync_MatchesSyncWireTraffic() => AssertWireParityAsync(
		(_, _) => { },
		s => s.EndCalibration(),
		s => s.EndCalibrationAsync());

	[Test]
	public Task ResetAsync_MatchesSyncWireTraffic() => AssertWireParityAsync(
		(_, _) => { },
		s => s.Reset(),
		s => s.ResetAsync());

	[Test]
	public Task StartFastTransferModeAsync_MatchesSyncWireTraffic() => AssertWireParityAsync(
		(_, _) => { },
		s => s.StartFastTransferMode(),
		s => s.StartFastTransferModeAsync());

	// The stop path is guarded by an "am I in fast mode?" flag on both halves; starting first is
	// what makes it send anything at all.
	[Test]
	public Task StopFastTransferModeAsync_MatchesSyncWireTraffic() => AssertWireParityAsync(
		(_, _) => { },
		s => { s.StartFastTransferMode(); s.StopFastTransferMode(); },
		async s => { await s.StartFastTransferModeAsync(); await s.StopFastTransferModeAsync(); });

	[Test]
	public async Task StopFastTransferModeAsync_WithoutStart_SendsNothing()
	{
		var (fake, session) = NewSession();
		using (session)
			await session.StopFastTransferModeAsync();

		Assert.That(fake.SentPackets, Is.Empty,
			"stopping fast transfer without starting it should be a no-op, as on the sync path");
	}

	// ── Gate behaviour ────────────────────────────────────────────────────────

	/// <summary>
	/// The read-modify-write twins hold the wire gate across both halves. If the gate were taken
	/// again inside that window (e.g. by reusing the public twins to do the read and the write)
	/// the non-reentrant semaphore would deadlock, so this pins that it completes.
	/// </summary>
	[Test]
	public async Task MutatingTwin_HoldsGateAcrossReadModifyWrite_WithoutDeadlocking()
	{
		var (fake, session) = NewSession();
		using (session)
		{
			fake.EnqueueFuncBlockCycle();
			var write  = session.SetDebounceAsync(3);
			var winner = await Task.WhenAny(write, Task.Delay(TimeSpan.FromSeconds(5)));

			Assert.That(winner, Is.SameAs(write),
				"SetDebounceAsync did not finish within 5s - the wire gate deadlocked");
			await write; // surface any fault rather than leaving it observed-but-unthrown
		}
	}
}
