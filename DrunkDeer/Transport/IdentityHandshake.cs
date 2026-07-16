namespace DrunkDeer.Protocol;

/// <summary>
/// What the identity handshake learns about a keyboard: everything a connection needs to
/// report through <see cref="IKeyboardConnectionInfo"/>.
/// </summary>
public sealed record IdentityResult(
	ModelInfo Model,
	string Variant,
	byte FirmwareVersion,
	byte InitialTurboValue,
	byte InitialRapidTriggerEnabled,
	byte InitialLastWinValue,
	byte InitialRapidTriggerAutoMatch);

/// <summary>
/// The identity handshake, shared by every transport that opens a keyboard.
/// </summary>
/// <remarks>
/// The retry loop itself stays with each transport, because the synchronous and asynchronous
/// paths drive their I/O differently. What lives here is what a response <em>means</em>: when
/// one counts as complete, and how to turn it into a <see cref="IdentityResult"/>. Those are the
/// parts that would quietly drift if each transport re-derived them.
/// </remarks>
public static class IdentityHandshake
{
	/// <summary>
	/// The shortest response <see cref="Interpret"/> can read. <see cref="IdentityResponse.Matches"/>
	/// only checks the 3-byte header, but the accessors reach byte 32
	/// (<see cref="IdentityResponse.GetLastWinReplace"/>), so a header-matching but truncated
	/// report has to be rejected and retried rather than indexed past its end.
	/// </summary>
	public const int MinResponseLength = 33;

	/// <summary>How many times to ask before giving up on a candidate device.</summary>
	/// <remarks>
	/// A false-positive VID/PID match (a third-party keyboard sharing a known pair) is skipped by
	/// the caller and costs this budget, so it stays short: 8 x 500 ms is enough headroom for a
	/// genuine DrunkDeer that's slow to answer without turning one wrong candidate into a long stall.
	/// </remarks>
	public const int Attempts = 8;

	/// <summary>How long to wait for each attempt's response, in milliseconds.</summary>
	public const int AttemptTimeoutMs = 500;

	/// <summary>Builds the request to send on each attempt.</summary>
	public static byte[] BuildRequest() => IdentityRequest.Build();

	/// <summary>
	/// Whether <paramref name="response"/> is a complete identity response that
	/// <see cref="Interpret"/> can read.
	/// </summary>
	public static bool IsComplete(ReadOnlySpan<byte> response) =>
		IdentityResponse.Matches(response) && response.Length >= MinResponseLength;

	/// <summary>
	/// Resolves a complete identity response into the connected keyboard's model and settings.
	/// </summary>
	/// <exception cref="InvalidOperationException">
	/// The response is incomplete, or its identity bytes aren't a model this SDK knows.
	/// </exception>
	public static IdentityResult Interpret(ReadOnlySpan<byte> response)
	{
		if (!IsComplete(response))
			throw new InvalidOperationException(
				$"Not a complete identity response ({response.Length} bytes; at least {MinResponseLength} needed).");

		var id = IdentityResponse.GetModel(response);

		var resolved = ModelRegistry.Resolve(id[0], id[1], id[2])
			?? throw new InvalidOperationException(
				$"Unknown model identity: 0x{id[0]:X2} 0x{id[1]:X2} 0x{id[2]:X2}");

		var info = ModelRegistry.GetInfo(resolved.Slug)
			?? throw new InvalidOperationException($"No ModelInfo for slug '{resolved.Slug}'.");

		return new IdentityResult(
			info,
			resolved.Variant,
			IdentityResponse.GetFirmwareVersion(response),
			IdentityResponse.GetTurboValue(response),
			IdentityResponse.GetRapidTriggerEnabled(response),
			IdentityResponse.GetLastWinValue(response),
			IdentityResponse.GetRapidTriggerAutoMatch(response));
	}

	/// <summary>
	/// Describes what came back when no complete response did, for error messages.
	/// </summary>
	public static string DescribeFailure(byte[]? lastReceived) =>
		lastReceived is null
			? "no packets received (timeout)"
			: $"last packet: [{string.Join(", ", lastReceived.Take(8).Select(b => $"0x{b:X2}"))}]";
}
