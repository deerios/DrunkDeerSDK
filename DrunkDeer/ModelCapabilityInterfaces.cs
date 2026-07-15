namespace DrunkDeer.Protocol;

/// <summary>Marker interface: the connected model supports the FuncBlock extended gateway (0x55/0x05-0x06).</summary>
public interface IHasFuncBlock : IModelMarker { }

/// <summary>Marker interface: the connected model uses HighPrecision (0xFD × 200, 0.005 mm) depth encoding (A75 Ultra, A75 Master, X60 Future).</summary>
public interface IHasHighPrecision : IHasFuncBlock { }

/// <summary>Marker interface: the connected model supports Turbo mode.</summary>
/// <remarks>
/// Deliberately not an <see cref="IHasFuncBlock"/>: Turbo rides on the CommonConfig packet, not
/// the gateway. The base A75 has Turbo and no gateway, so tying the two together handed every
/// A75 a compile-time programmable surface its firmware refuses at runtime.
/// </remarks>
public interface IHasTurboMode : IModelMarker { }

/// <summary>Marker interface: the connected model has a dedicated logo LED zone.</summary>
public interface IHasLogoLight : IHasFuncBlock { }

/// <summary>Marker interface: the connected model has a dedicated side LED strip zone.</summary>
public interface IHasSideLight : IHasFuncBlock { }
