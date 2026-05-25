using DrunkDeer.Protocol;
using NUnit.Framework;

namespace DrunkDeer.Tests.Protocol;

[TestFixture]
public class UserKeyFactoryTests
{
    // ── UserKey.FromHid ───────────────────────────────────────────────────────

    [Test]
    public void FromHid_SetsTypeToKeyboard()
    {
        var key = UserKey.FromHid(0x04);
        Assert.That(key.Type, Is.EqualTo(UserKeyType.Keyboard));
    }

    [Test]
    public void FromHid_SetsUsageCode()
    {
        var key = UserKey.FromHid(usageCode: 0x28); // Enter
        Assert.That(key.Param2, Is.EqualTo(0x28));
    }

    [Test]
    public void FromHid_DefaultModifiers_AreZero()
    {
        var key = UserKey.FromHid(0x04);
        Assert.That(key.Param1, Is.EqualTo(0));
    }

    [Test]
    public void FromHid_AcceptsKeyModifiers_Flags()
    {
        var key = UserKey.FromHid(0x04, KeyModifiers.LeftCtrl | KeyModifiers.LeftShift);
        Assert.That(key.Param1, Is.EqualTo((byte)(KeyModifiers.LeftCtrl | KeyModifiers.LeftShift)));
    }

    [Test]
    public void FromHid_AllModifiers_RoundTrip()
    {
        var mods = KeyModifiers.LeftCtrl | KeyModifiers.RightAlt | KeyModifiers.LeftWin;
        var key = UserKey.FromHid(0x04, mods);
        Assert.That((KeyModifiers)key.Param1, Is.EqualTo(mods));
    }

    // ── UserKey.FromMouseButton ───────────────────────────────────────────────

    [Test]
    public void FromMouseButton_SetsType()
    {
        var key = UserKey.FromMouseButton(MouseButtons.Left);
        Assert.That(key.Type, Is.EqualTo(UserKeyType.MouseButton));
    }

    [Test]
    public void FromMouseButton_SingleButton_Bitmask()
    {
        Assert.Multiple(() =>
        {
            Assert.That(UserKey.FromMouseButton(MouseButtons.Left).Param1,    Is.EqualTo(1));
            Assert.That(UserKey.FromMouseButton(MouseButtons.Right).Param1,   Is.EqualTo(2));
            Assert.That(UserKey.FromMouseButton(MouseButtons.Middle).Param1,  Is.EqualTo(4));
            Assert.That(UserKey.FromMouseButton(MouseButtons.Forward).Param1, Is.EqualTo(8));
            Assert.That(UserKey.FromMouseButton(MouseButtons.Back).Param1,    Is.EqualTo(16));
        });
    }

    [Test]
    public void FromMouseButton_CombinedButtons()
    {
        var key = UserKey.FromMouseButton(MouseButtons.Left | MouseButtons.Right);
        Assert.That(key.Param1, Is.EqualTo(3));
    }

    // ── UserKey.FromMouseScroll ───────────────────────────────────────────────

    [Test]
    public void FromMouseScroll_SetsType()
    {
        var key = UserKey.FromMouseScroll(1);
        Assert.That(key.Type, Is.EqualTo(UserKeyType.MouseScroll));
    }

    [Test]
    public void FromMouseScroll_PositiveTick_EncodesAsOne()
    {
        var key = UserKey.FromMouseScroll(1);
        Assert.That(key.Param2, Is.EqualTo(1));
    }

    [Test]
    public void FromMouseScroll_NegativeTick_EncodesAs255()
    {
        var key = UserKey.FromMouseScroll(-1);
        Assert.That(key.Param2, Is.EqualTo(255));
    }

    [Test]
    public void FromMouseScroll_ZeroTicks_EncodesAsZero()
    {
        var key = UserKey.FromMouseScroll(0);
        Assert.That(key.Param2, Is.EqualTo(0));
    }

    [Test]
    public void FromMouseScroll_LargePositive_ClampedTo127()
    {
        var key = UserKey.FromMouseScroll(999);
        Assert.That(key.Param2, Is.EqualTo(127));
    }

    [Test]
    public void FromMouseScroll_LargeNegative_ClampedTo129()
    {
        var key = UserKey.FromMouseScroll(-999);
        Assert.That(key.Param2, Is.EqualTo((byte)(256 - 127)));
    }

    // ── UserKey.FromMultimedia ────────────────────────────────────────────────

    [Test]
    public void FromMultimedia_SetsType()
    {
        var key = UserKey.FromMultimedia(0xE9);
        Assert.That(key.Type, Is.EqualTo(UserKeyType.Multimedia));
    }

    [Test]
    public void FromMultimedia_SetsUsageCode()
    {
        var key = UserKey.FromMultimedia(0xE9); // Volume Up
        Assert.That(key.Param1, Is.EqualTo(0xE9));
    }

    [Test]
    public void FromMultimedia_DefaultUsagePage_IsZero()
    {
        var key = UserKey.FromMultimedia(0xB5);
        Assert.That(key.Param2, Is.EqualTo(0));
    }

    [Test]
    public void FromMultimedia_ExplicitUsagePage()
    {
        var key = UserKey.FromMultimedia(0xB5, usagePage: 0x01);
        Assert.That(key.Param2, Is.EqualTo(0x01));
    }

    // ── UserKey.FromSystem ────────────────────────────────────────────────────

    [Test]
    public void FromSystem_SetsType()
    {
        var key = UserKey.FromSystem(SystemControl.Power);
        Assert.That(key.Type, Is.EqualTo(UserKeyType.System));
    }

    [TestCase(SystemControl.Power)]
    [TestCase(SystemControl.Sleep)]
    [TestCase(SystemControl.Wake)]
    public void FromSystem_SetsControl(SystemControl control)
    {
        var key = UserKey.FromSystem(control);
        Assert.That(key.Param1, Is.EqualTo((byte)control));
    }

    // ── UserKey.FromMacro ─────────────────────────────────────────────────────

    [Test]
    public void FromMacro_SetsType()
    {
        var key = UserKey.FromMacro(0);
        Assert.That(key.Type, Is.EqualTo(UserKeyType.Macro));
    }

    [TestCase((byte)0)]
    [TestCase((byte)7)]
    [TestCase((byte)31)]
    public void FromMacro_SetsSlotIndex(byte slot)
    {
        var key = UserKey.FromMacro(slot);
        Assert.That(key.Param1, Is.EqualTo(slot));
    }

    // ── UserKey.FromDynamicKeystroke ───────────────────────────────────────────────────────

    [Test]
    public void FromDynamicKeystroke_SetsType()
    {
        var key = UserKey.FromDynamicKeystroke(0);
        Assert.That(key.Type, Is.EqualTo(UserKeyType.DynamicKeystroke));
    }

    [TestCase((byte)0)]
    [TestCase((byte)15)]
    [TestCase((byte)31)]
    public void FromDynamicKeystroke_SetsSlotIndex(byte slot)
    {
        var key = UserKey.FromDynamicKeystroke(slot);
        Assert.That(key.Param1, Is.EqualTo(slot));
    }

    // ── UserKey.FromToggle ────────────────────────────────────────────────────

    [Test]
    public void FromToggle_SetsType()
    {
        var key = UserKey.FromToggle(0);
        Assert.That(key.Type, Is.EqualTo(UserKeyType.Toggle));
    }

    [TestCase((byte)0)]
    [TestCase((byte)31)]
    public void FromToggle_SetsSlotIndex(byte slot)
    {
        var key = UserKey.FromToggle(slot);
        Assert.That(key.Param1, Is.EqualTo(slot));
    }

    // ── UserKey.FromMultiTap ──────────────────────────────────────────────────

    [Test]
    public void FromMultiTap_SetsType()
    {
        var key = UserKey.FromMultiTap(0);
        Assert.That(key.Type, Is.EqualTo(UserKeyType.MultiTap));
    }

    [TestCase((byte)0)]
    [TestCase((byte)31)]
    public void FromMultiTap_SetsSlotIndex(byte slot)
    {
        var key = UserKey.FromMultiTap(slot);
        Assert.That(key.Param1, Is.EqualTo(slot));
    }

    // ── UserKey.Disabled ──────────────────────────────────────────────────────

    [Test]
    public void Disabled_HasNoneType()
    {
        Assert.That(UserKey.Disabled.Type, Is.EqualTo(UserKeyType.None));
    }

    // ── KeyTriggerConfig.FromMm ───────────────────────────────────────────────

    [Test]
    public void KeyTriggerConfig_FromMm_ActuationRoundsToNearestUnit()
    {
        var cfg = KeyTriggerConfig.FromMm(2.0f, 0.2f, 0.2f);
        Assert.That(cfg.Actuation, Is.EqualTo(200));
    }

    [Test]
    public void KeyTriggerConfig_FromMm_RtPressRoundsToNearestUnit()
    {
        var cfg = KeyTriggerConfig.FromMm(2.0f, 0.3f, 0.2f);
        Assert.That(cfg.RtPress, Is.EqualTo(30));
    }

    [Test]
    public void KeyTriggerConfig_FromMm_RtReleaseRoundsToNearestUnit()
    {
        var cfg = KeyTriggerConfig.FromMm(2.0f, 0.2f, 0.5f);
        Assert.That(cfg.RtRelease, Is.EqualTo(50));
    }

    [Test]
    public void KeyTriggerConfig_FromMm_PreservesDefaultOtherFields()
    {
        var cfg = KeyTriggerConfig.FromMm(2.0f, 0.2f, 0.2f);
        Assert.Multiple(() =>
        {
            Assert.That(cfg.KeyMode,  Is.EqualTo(KeyTriggerConfig.Default.KeyMode));
            Assert.That(cfg.Priority, Is.EqualTo(KeyTriggerConfig.Default.Priority));
            Assert.That(cfg.PressDeadzone,   Is.EqualTo(KeyTriggerConfig.Default.PressDeadzone));
            Assert.That(cfg.ReleaseDeadzone, Is.EqualTo(KeyTriggerConfig.Default.ReleaseDeadzone));
        });
    }

    [Test]
    public void KeyTriggerConfig_WithMm_PreservesNonDepthFields()
    {
        var original = new KeyTriggerConfig
        {
            SwitchType      = 2,
            KeyMode         = 1,
            Priority        = 3,
            Actuation       = 50,
            RtPress         = 10,
            RtRelease       = 10,
            PressDeadzone   = 5,
            ReleaseDeadzone = 7,
        };
        var updated = original.WithMm(1.5f, 0.2f, 0.3f);

        Assert.Multiple(() =>
        {
            Assert.That(updated.SwitchType,      Is.EqualTo(2));
            Assert.That(updated.Priority,        Is.EqualTo(3));
            Assert.That(updated.PressDeadzone,   Is.EqualTo(5));
            Assert.That(updated.ReleaseDeadzone, Is.EqualTo(7));
            Assert.That(updated.Actuation,       Is.EqualTo(150)); // 1.5 × 100
            Assert.That(updated.RtPress,         Is.EqualTo(20));  // 0.2 × 100
            Assert.That(updated.RtRelease,       Is.EqualTo(30));  // 0.3 × 100
        });
    }

    // ── DynamicKeystrokeEntry.WithPointsMm ───────────────────────────────────

    [Test]
    public void DynamicKeystrokeEntry_WithPointsMm_ConvertsToFirmwareUnits()
    {
        var dks = new DynamicKeystrokeEntry().WithPointsMm(1.0f, 2.5f, 2.5f, 1.0f);
        Assert.Multiple(() =>
        {
            Assert.That(dks.Points[0], Is.EqualTo(10)); // 1.0 × 10
            Assert.That(dks.Points[1], Is.EqualTo(25)); // 2.5 × 10
            Assert.That(dks.Points[2], Is.EqualTo(25));
            Assert.That(dks.Points[3], Is.EqualTo(10));
        });
    }

    [Test]
    public void DynamicKeystrokeEntry_WithPointsMm_PreservesActions()
    {
        var action = new DynamicKeystrokeAction { Key = UserKey.FromHid(0x04), DownStart = 1 };
        var original = new DynamicKeystrokeEntry { Actions = [action, default, default, default] };
        var updated = original.WithPointsMm(1.0f, 2.0f, 3.0f, 4.0f);

        Assert.That(updated.Actions[0].Key.Param2, Is.EqualTo(0x04));
    }

    [Test]
    public void DynamicKeystrokeEntry_WithPointsMm_DoesNotMutateOriginal()
    {
        var original = new DynamicKeystrokeEntry();
        var updated = original.WithPointsMm(1.0f, 2.0f, 3.0f, 4.0f);

        Assert.That(original.Points[0], Is.EqualTo(10)); // default unchanged
    }
}
