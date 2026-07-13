namespace DrunkDeer.Protocol;

/// <summary>
/// Thrown by <see cref="KeyboardSession{TModel}.OpenFirst"/> when the connected keyboard's
/// model doesn't match the <c>TModel</c> type argument. The phantom-type marker only unlocks
/// capability-gated extension methods at compile time - it doesn't make the connection itself
/// verify the hardware, so this check exists to catch that mismatch at connect time instead of
/// failing confusingly (or silently) later.
/// </summary>
public sealed class DrunkDeerModelMismatchException : Exception
{
	public DrunkDeerModelMismatchException() { }

	public DrunkDeerModelMismatchException(string? message) : base(message) { }

	public DrunkDeerModelMismatchException(string? message, Exception? innerException)
		: base(message, innerException) { }
}
