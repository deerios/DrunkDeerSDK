using System.Buffers.Binary;
using DrunkDeer.Protocol;
using DrunkDeer.Simulation;
using NUnit.Framework;

namespace DrunkDeer.FeatureTests.Features;

/// <summary>
/// Coverage for <see cref="SimulatedKeyboardConnection"/> (PLAN-GAP-8): it must synthesise travel
/// frames the poll loop can decode and acknowledge every config write, so the CLI/web can run and
/// demo without hardware.
/// </summary>
[TestFixture]
public class SimulatedKeyboardConnectionTests
{
	[Test]
	public void Standard_StagesThreeB7PacketsReflectingTravel()
	{
		var sim = new SimulatedKeyboardConnection(); // A75, standard precision
		Assert.That(sim.KeyCount, Is.EqualTo(127));
		sim.SetKeyTravelMm(0, 2.0f); // raw = 2.0 mm * 10 = 20

		sim.Send(TravelRequest.Build());

		var p0 = sim.ReceiveCommand();
		Assert.That(p0, Is.Not.Null);
		Assert.That(p0![0], Is.EqualTo(0xB7));
		Assert.That(p0[3], Is.EqualTo(0));   // packet index
		Assert.That(p0[4], Is.EqualTo(20));  // slot 0 travel byte

		Assert.That(sim.ReceiveCommand(), Is.Not.Null); // packet 1
		Assert.That(sim.ReceiveCommand(), Is.Not.Null); // packet 2
		Assert.That(sim.ReceiveCommand(), Is.Null, "A standard frame is exactly 3 packets.");
	}

	[Test]
	public void HighPrecision_StagesFiveFdPacketsReflectingTravel()
	{
		var sim = new SimulatedKeyboardConnection(ModelRegistry.GetInfo(ModelSlugs.A75Ultra));
		Assert.That(sim.KeyCount, Is.EqualTo(126));
		sim.SetKeyTravelMm(0, 2.0f); // HP raw = 2.0 mm * 200 = 400

		sim.Send(TravelRequest.Build());

		var p0 = sim.ReceiveCommand();
		Assert.That(p0, Is.Not.Null);
		Assert.That(p0![0], Is.EqualTo(0xFD));
		Assert.That(p0[1], Is.EqualTo(0x06));
		Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(p0.AsSpan(2)), Is.EqualTo(400));

		for (int i = 0; i < 4; i++)
			Assert.That(sim.ReceiveCommand(), Is.Not.Null); // sections 1..4
		Assert.That(sim.ReceiveCommand(), Is.Null, "A high-precision frame is exactly 5 packets.");
	}

	[Test]
	public void SetKeyTravelRaw_OutOfRange_Throws()
	{
		var sim = new SimulatedKeyboardConnection();
		Assert.Throws<ArgumentOutOfRangeException>(() => sim.SetKeyTravelRaw(sim.KeyCount, 10));
	}

	[Test]
	public void FlushReadBuffer_DiscardsStagedFrame()
	{
		var sim = new SimulatedKeyboardConnection();
		sim.Send(TravelRequest.Build());
		sim.FlushReadBuffer();
		Assert.That(sim.ReceiveCommand(), Is.Null);
	}

	[TestCase((byte)0xB6, (byte)0xB6)] // standard key-point write
	[TestCase((byte)0xFD, (byte)0xFD)] // high-precision key-point write
	[TestCase((byte)0xAE, (byte)0xAE)] // lighting
	[TestCase((byte)0xB5, (byte)0xB5)] // common config
	[TestCase((byte)0x55, (byte)0xAA)] // extended gateway read/write
	public void SendAndReceive_ReturnsMatchingAck(byte requestHeader, byte expectedAck)
	{
		var sim = new SimulatedKeyboardConnection();
		var req = new byte[64];
		req[0] = requestHeader;

		var resp = sim.SendAndReceive(req);

		Assert.That(resp, Is.Not.Null);
		Assert.That(resp![0], Is.EqualTo(expectedAck));
	}

	[Test]
	public void Session_PollLoop_ReportsSimulatedPress()
	{
		using var sim = new SimulatedKeyboardConnection();
		using var session = new KeyboardSession(sim);
		int wSlot = session.GetKeyIndex(DDKey.W);
		sim.SetKeyTravelMm(wSlot, 2.0f);

		session.StartPolling();
		try
		{
			var deadline = DateTime.UtcNow.AddSeconds(5);
			while (session.GetKeyHeightMm(DDKey.W) <= 0 && DateTime.UtcNow < deadline)
				Thread.Sleep(5);
			Assert.That(session.GetKeyHeightMm(DDKey.W), Is.EqualTo(2.0f).Within(0.05f));
		}
		finally
		{
			session.StopPolling();
		}
	}

	[Test]
	public void Session_ConfigWrites_DoNotThrowOverSimulator()
	{
		using var sim = new SimulatedKeyboardConnection();
		using var session = new KeyboardSession(sim);

		Assert.DoesNotThrow(() => session.SetActuationPoint(1.5f));
		Assert.DoesNotThrow(() => session.SetKeyColor(DDKey.Escape, 0xFF, 0x00, 0x00));
		Assert.DoesNotThrow(() =>
			session.SetCommonConfig(true, true, LastWinRapidTriggerMode.Both, false));
	}
}
