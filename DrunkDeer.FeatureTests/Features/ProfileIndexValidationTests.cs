using DrunkDeer.FeatureTests.Fakes;
using DrunkDeer.Protocol;
using NUnit.Framework;

namespace DrunkDeer.FeatureTests.Features;

/// <summary>
/// API-1 regression coverage: every 0x55-gateway accessor that takes a profileIndex must
/// validate it before computing a flash address, so an out-of-range index throws instead of
/// silently wrapping (ushort overflow) into another profile's flash region.
/// </summary>
[TestFixture]
public class ProfileIndexValidationTests
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

	private static void AssertThrowsAndSendsNothing(FakeKeyboardConnection fake, TestDelegate action)
	{
		Assert.Throws<ArgumentOutOfRangeException>(action);
		Assert.That(fake.SentPackets, Is.Empty);
	}

	[TestCase(4)]
	[TestCase(-1)]
	public void ReadKeyTriggers_InvalidProfileIndex_Throws(int profileIndex) =>
		AssertThrowsAndSendsNothing(_fake, () => _session.ReadKeyTriggers(profileIndex));

	[TestCase(4)]
	[TestCase(-1)]
	public void WriteKeyTriggers_InvalidProfileIndex_Throws(int profileIndex) =>
		AssertThrowsAndSendsNothing(_fake,
			() => _session.WriteKeyTriggers(new KeyTriggerConfig[128], profileIndex));

	[TestCase(4)]
	[TestCase(-1)]
	public void SetKeyTrigger_InvalidProfileIndex_Throws(int profileIndex) =>
		AssertThrowsAndSendsNothing(_fake,
			() => _session.SetKeyTrigger(0, KeyTriggerConfig.Default, profileIndex));

	[TestCase(4)]
	[TestCase(-1)]
	public void ReadKeyMap_InvalidProfileIndex_Throws(int profileIndex) =>
		AssertThrowsAndSendsNothing(_fake, () => _session.ReadKeyMap(profileIndex: profileIndex));

	[TestCase(4)]
	[TestCase(-1)]
	public void WriteKeyMap_InvalidProfileIndex_Throws(int profileIndex) =>
		AssertThrowsAndSendsNothing(_fake,
			() => _session.WriteKeyMap(new UserKey[128], profileIndex: profileIndex));

	[TestCase(4)]
	[TestCase(-1)]
	public void SetKey_InvalidProfileIndex_Throws(int profileIndex) =>
		AssertThrowsAndSendsNothing(_fake,
			() => _session.SetKey(0, new UserKey(), profileIndex: profileIndex));

	[TestCase(4)]
	[TestCase(-1)]
	public void ReadDynamicKeystrokeEntries_InvalidProfileIndex_Throws(int profileIndex) =>
		AssertThrowsAndSendsNothing(_fake, () => _session.ReadDynamicKeystrokeEntries(profileIndex));

	[TestCase(4)]
	[TestCase(-1)]
	public void SetDynamicKeystrokeEntry_InvalidProfileIndex_Throws(int profileIndex) =>
		AssertThrowsAndSendsNothing(_fake,
			() => _session.SetDynamicKeystrokeEntry(0, new DynamicKeystrokeEntry(), profileIndex));

	[TestCase(4)]
	[TestCase(-1)]
	public void ReadMultiTapEntries_InvalidProfileIndex_Throws(int profileIndex) =>
		AssertThrowsAndSendsNothing(_fake, () => _session.ReadMultiTapEntries(profileIndex));

	[TestCase(4)]
	[TestCase(-1)]
	public void SetMultiTapEntry_InvalidProfileIndex_Throws(int profileIndex) =>
		AssertThrowsAndSendsNothing(_fake,
			() => _session.SetMultiTapEntry(0, new MultiTapEntry(), profileIndex));

	[TestCase(4)]
	[TestCase(-1)]
	public void ReadToggleKeyEntries_InvalidProfileIndex_Throws(int profileIndex) =>
		AssertThrowsAndSendsNothing(_fake, () => _session.ReadToggleKeyEntries(profileIndex));

	[TestCase(4)]
	[TestCase(-1)]
	public void SetToggleKeyEntry_InvalidProfileIndex_Throws(int profileIndex) =>
		AssertThrowsAndSendsNothing(_fake,
			() => _session.SetToggleKeyEntry(0, new UserKey(), profileIndex));

	[TestCase(4)]
	[TestCase(-1)]
	public void ReadMacroSlots_InvalidProfileIndex_Throws(int profileIndex) =>
		AssertThrowsAndSendsNothing(_fake, () => _session.ReadMacroSlots(profileIndex));

	[TestCase(4)]
	[TestCase(-1)]
	public void SetMacroSlot_InvalidProfileIndex_Throws(int profileIndex) =>
		AssertThrowsAndSendsNothing(_fake, () => _session.SetMacroSlot(0, [], profileIndex));

	[TestCase(4)]
	[TestCase(-1)]
	public void ReadStoredColors_InvalidProfileIndex_Throws(int profileIndex) =>
		AssertThrowsAndSendsNothing(_fake, () => _session.ReadStoredColors(profileIndex));

	[TestCase(4)]
	[TestCase(-1)]
	public void WriteStoredColors_InvalidProfileIndex_Throws(int profileIndex) =>
		AssertThrowsAndSendsNothing(_fake,
			() => _session.WriteStoredColors(new (byte, byte, byte)[128], profileIndex));

	[TestCase(4)]
	[TestCase(-1)]
	public void SaveLightingToProfile_InvalidProfileIndex_Throws(int profileIndex) =>
		AssertThrowsAndSendsNothing(_fake, () => _session.SaveLightingToProfile(profileIndex));

	[TestCase(4)]
	[TestCase(-1)]
	public void LoadLightingFromProfile_InvalidProfileIndex_Throws(int profileIndex) =>
		AssertThrowsAndSendsNothing(_fake, () => _session.LoadLightingFromProfile(profileIndex));

	[TestCase(4)]
	[TestCase(-1)]
	public void PullFullProfile_InvalidProfileIndex_Throws(int profileIndex) =>
		AssertThrowsAndSendsNothing(_fake, () => _session.PullFullProfile(profileIndex));

	[TestCase(4)]
	[TestCase(-1)]
	public void PushFullProfile_InvalidProfileIndex_Throws(int profileIndex) =>
		AssertThrowsAndSendsNothing(_fake, () => _session.PushFullProfile(new FullProfileData(), profileIndex));

	// FuncBlock-gated accessors: use an A75 Ultra fake so HasFuncBlock is true and profile-index
	// validation (which now runs before the FuncBlock capability check) is what actually fires.
	[TestCase(4)]
	[TestCase(-1)]
	public void ReadFuncBlock_InvalidProfileIndex_Throws(int profileIndex)
	{
		var fake = new FakeKeyboardConnection(ModelRegistry.GetInfo(ModelSlugs.A75Ultra));
		using var session = new KeyboardSession(fake);
		AssertThrowsAndSendsNothing(fake, () => session.ReadFuncBlock(profileIndex));
	}

	[TestCase(4)]
	[TestCase(-1)]
	public void WriteFuncBlock_InvalidProfileIndex_Throws(int profileIndex)
	{
		var fake = new FakeKeyboardConnection(ModelRegistry.GetInfo(ModelSlugs.A75Ultra));
		using var session = new KeyboardSession(fake);
		AssertThrowsAndSendsNothing(fake, () => session.WriteFuncBlock(new KeyboardFuncBlock(), profileIndex));
	}

	[TestCase(4)]
	[TestCase(-1)]
	public void CaptureProfile_InvalidProfileIndex_Throws(int profileIndex) =>
		AssertThrowsAndSendsNothing(_fake, () => _session.CaptureProfile(profileIndex));

	[TestCase(4)]
	[TestCase(-1)]
	public void SwitchProfile_InvalidProfileIndex_Throws(int profileIndex) =>
		AssertThrowsAndSendsNothing(_fake, () => _session.SwitchProfile(profileIndex));
}
