namespace DrunkDeer.Protocol;

/// <summary>
/// Physical placement and identity of one key on a keyboard, expressed in KLE
/// 1u key units (origin top-left; x grows right, y grows down; one unit is one
/// 1u keycap). Returned in order by <see cref="KeyboardSession.Layout"/>.
/// </summary>
/// <remarks>
/// <see cref="SlotIndex"/> is the firmware key index used for actuation and
/// lighting - identical to what <see cref="KeyboardSession.GetKeyIndex"/> returns
/// for <see cref="Key"/>. Most keys are a single rectangle; <see cref="Secondary"/>
/// carries the extra leg of a non-rectangular key such as an ISO Enter.
/// </remarks>
public sealed record KeyInfo(
	DDKey Key,
	int SlotIndex,
	string Legend,
	float X,
	float Y,
	float W,
	float H,
	KeyRect? Secondary = null);

/// <summary>
/// A rectangular region in KLE 1u key units, used for the extra leg of a
/// non-rectangular key via <see cref="KeyInfo.Secondary"/>.
/// </summary>
public readonly record struct KeyRect(float X, float Y, float W, float H);
