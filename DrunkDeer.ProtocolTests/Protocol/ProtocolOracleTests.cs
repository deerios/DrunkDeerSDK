using DrunkDeer.Protocol;
using NUnit.Framework;

namespace DrunkDeer.ProtocolTests.Protocol;

/// <summary>
/// Pins the packet classifier that names traffic for the capture analyzer and the web app's
/// diagnostics timeline. Most of these feed it packets straight from the generated builders
/// rather than hand-written bytes: that is what makes them a drift guard. If a message's header
/// changes in the protocol YAML, the builder follows it and the oracle does not, so the pairing
/// breaks here rather than in a log that quietly mislabels every packet it sees.
/// </summary>
[TestFixture]
public class ProtocolOracleTests
{
    static OracleResult Out(byte[] packet) => ProtocolOracle.Classify(packet, PacketDirection.HostToDevice);
    static OracleResult In(byte[] packet) => ProtocolOracle.Classify(packet, PacketDirection.DeviceToHost);

    [Test]
    public void TravelRequest_IsNamed_AndExpectsATravelResponse()
    {
        var result = Out(TravelRequest.Build());

        Assert.That(result.MessageName, Is.EqualTo("TravelRequest"));
        Assert.That(result.ExpectedResponseName, Is.EqualTo("TravelResponse"));
        Assert.That(result.ExpectedResponseByte0, Is.EqualTo(0xB7));
    }

    [Test]
    public void IdentityRequest_IsNamed_AndExpectsAnIdentityResponse()
    {
        var result = Out(IdentityRequest.Build());

        Assert.That(result.MessageName, Is.EqualTo("IdentityRequest"));
        Assert.That(result.ExpectedResponseByte0, Is.EqualTo(0xA0));
    }

    // Identity and key remap both open with 0xA0 0x02 and are told apart by byte 2 alone. A
    // regression here would classify every remap write as a handshake.
    [Test]
    public void KeyRemapPacket_IsNotMistakenForAnIdentityRequest()
    {
        var result = Out(KeyRemapPacket.Build(packetNumber: 0, layerGroup: 0));

        Assert.That(result.MessageName, Is.EqualTo("KeyRemapPacket"));
    }

    // The 0xB6 sub-commands share a first byte with the travel request the poll loop sends
    // hundreds of times a second, so telling them apart is what keeps a config write legible.
    [Test]
    public void ActuationWrite_IsNotMistakenForATravelRequest()
    {
        var result = Out(WriteActuationPointStandard.Build(packetIndex: 0, keyValues: new byte[59]));

        Assert.That(result.MessageName, Is.EqualTo("WriteActuationPointStandard"));
        Assert.That(result.ExpectedResponseName, Is.EqualTo("WriteKeyPointAcknowledgeStandard"));
    }

    [Test]
    public void CommonConfig_IsNamed()
    {
        var packet = CommonConfig.Build(turboMode: 0, rapidTriggerMode: 1, lastWinRapidTriggerMode: 0, rapidTriggerAutoMatch: 0);

        Assert.That(Out(packet).MessageName, Is.EqualTo("CommonConfig"));
    }

    [Test]
    public void IdentityResponse_ExtractsTheFirmwareVersion()
    {
        var packet = new byte[64];
        packet[0] = 0xA0; packet[1] = 0x02; packet[2] = 0x00;
        packet[7] = 42;

        var result = In(packet);

        Assert.That(result.MessageName, Is.EqualTo("IdentityResponse"));
        Assert.That(result.StructuralOk, Is.True);
        var firmware = result.Fields.Single(f => f.Name == "firmware_version");
        Assert.That(firmware.Value, Is.EqualTo("42"));
        Assert.That(firmware.FirmwareSensitive, Is.True);
    }

    [Test]
    public void IdentityResponse_TooShortToHoldItsFields_IsFlaggedNotRead()
    {
        var packet = new byte[] { 0xA0, 0x02, 0x00, 0x00 };

        var result = In(packet);

        Assert.That(result.MessageName, Is.EqualTo("IdentityResponse"));
        Assert.That(result.StructuralOk, Is.False);
        Assert.That(result.StructuralFailures, Is.Not.Empty);
        Assert.That(result.Fields, Is.Empty);
    }

    [Test]
    public void TravelResponse_ExtractsItsPacketIndex()
    {
        var packet = new byte[64];
        packet[0] = 0xB7;
        packet[3] = 2;

        var result = In(packet);

        Assert.That(result.MessageName, Is.EqualTo("TravelResponse"));
        Assert.That(result.StructuralOk, Is.True);
        Assert.That(result.Fields.Single(f => f.Name == "packet_index").Value, Is.EqualTo("2"));
    }

    [Test]
    public void TravelResponse_WithAnIndexOutsideTheFrame_IsFlagged()
    {
        var packet = new byte[64];
        packet[0] = 0xB7;
        packet[3] = 9;

        var result = In(packet);

        Assert.That(result.StructuralOk, Is.False);
        Assert.That(result.StructuralFailures.Single(), Does.Contain("9"));
    }

    [Test]
    public void TheSameByte_MeansDifferentThings_InEachDirection()
    {
        var packet = new byte[64];
        packet[0] = 0xB5;

        Assert.That(Out(packet).MessageName, Is.EqualTo("CommonConfig"));
        Assert.That(In(packet).MessageName, Is.EqualTo("CommonConfigAcknowledge"));
    }

    [Test]
    public void AnUnrecognisedPacket_IsNamedByItsCommandByte_NotSilentlyDropped()
    {
        var result = Out([0x42, 0x00]);

        Assert.That(result.MessageName, Is.EqualTo("Unknown(0x42)"));
        Assert.That(result.ExpectedResponseByte0, Is.Null);
    }

    [Test]
    public void AnEmptyPacket_IsReportedRatherThanThrowing()
    {
        var result = In([]);

        Assert.That(result.MessageName, Is.EqualTo("Empty"));
        Assert.That(result.StructuralOk, Is.False);
    }

    [Test]
    public void ValidateSequence_AcceptsTheResponseTheCommandAskedFor()
    {
        var request = Out(TravelRequest.Build());
        var response = new byte[64];
        response[0] = 0xB7;

        Assert.That(ProtocolOracle.ValidateSequence(request, In(response), response), Is.Null);
    }

    [Test]
    public void ValidateSequence_ReportsTheWrongResponse_NamingBothSides()
    {
        var request = Out(TravelRequest.Build());
        var response = new byte[64];
        response[0] = 0xB5;

        var failure = ProtocolOracle.ValidateSequence(request, In(response), response);

        Assert.That(failure, Is.Not.Null);
        Assert.That(failure, Does.Contain("TravelResponse").And.Contain("0xB5"));
    }

    // A command with no known reply must not have every following packet reported as a mismatch.
    [Test]
    public void ValidateSequence_StaysQuiet_ForCommandsWithNoExpectedResponse()
    {
        var request = Out(ClearRtpUpper.Build());
        var anything = new byte[64];
        anything[0] = 0xB7;

        Assert.That(request.ExpectedResponseByte0, Is.Null);
        Assert.That(ProtocolOracle.ValidateSequence(request, In(anything), anything), Is.Null);
    }
}
