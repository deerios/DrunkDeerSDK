namespace DrunkDeer.Protocol;

/// <summary>
/// Report-payload rules that every transport has to follow, kept in one place so the
/// desktop (hidraw) and browser (WebHID) paths can't drift apart on them.
/// </summary>
/// <remarks>
/// The two transports disagree about the report-ID byte - hidraw carries it inline as byte 0
/// of every buffer, while WebHID passes it as a separate argument and hands back input reports
/// with it already removed - so prepending and stripping stay local to each transport. What
/// they must agree on is how much payload fits in a report, which is what lives here.
/// </remarks>
public static class HidReportPacket
{
	/// <summary>The HID report ID DrunkDeer keyboards use for the command stream.</summary>
	public const byte CommandReportId = 0x04;

	/// <summary>
	/// The number of payload bytes an interface must accept before this SDK will talk to it.
	/// Reports 4 on these keyboards carry 63 payload bytes.
	/// </summary>
	public const int MinCommandCapacity = 63;

	/// <summary>
	/// Returns how many bytes of <paramref name="packet"/> to send on a device whose report
	/// holds <paramref name="capacity"/> payload bytes.
	/// </summary>
	/// <remarks>
	/// Report 4 on some keyboards (e.g. the A75) only has room for 63 payload bytes, one short of
	/// this SDK's uniform 64-byte packet size. All-zero codegen padding (protocol/*.yaml's
	/// padding_to: 64) can safely drop its last byte, since nothing reads it back; real payload
	/// data reaching that far cannot be dropped without corrupting the message, so that case
	/// throws instead. Truncating blindly here is what would silently break the handshake.
	/// </remarks>
	/// <exception cref="ArgumentException">
	/// A byte beyond <paramref name="capacity"/> is non-zero, so it is payload rather than padding.
	/// </exception>
	public static int FitToCapacity(ReadOnlySpan<byte> packet, int capacity, string? paramName = null)
	{
		if (capacity <= 0)
			throw new ArgumentOutOfRangeException(nameof(capacity), capacity,
				"A report capacity of zero or less can't carry any payload.");

		if (packet.Length <= capacity)
			return packet.Length;

		for (int i = capacity; i < packet.Length; i++)
		{
			if (packet[i] != 0)
				throw new ArgumentException(
					$"Packet is {packet.Length} bytes, but this device's report only has room for " +
					$"{capacity} bytes, and byte {i} is non-zero payload, not padding, so it can't " +
					"be safely dropped.", paramName ?? nameof(packet));
		}

		return capacity;
	}
}
