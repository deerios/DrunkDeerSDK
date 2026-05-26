namespace DrunkDeer.Protocol;

/// <summary>Marker interface: the connected model supports the FuncBlock extended gateway (0x55/0x05-0x06).</summary>
public interface IHasFuncBlock { }

/// <summary>Marker interface: the connected model uses FD × 200 high-precision depth encoding (A75 Ultra, A75 Master, X60 Future).</summary>
public interface IHasHighPrecision : IHasFuncBlock { }

/// <summary>Marker interface: the connected model supports Berserk (Turbo) mode via the FuncBlock gateway.</summary>
public interface IHasBerserkMode : IHasFuncBlock { }

/// <summary>Marker interface: the connected model has a dedicated logo LED zone.</summary>
public interface IHasLogoLight : IHasFuncBlock { }

/// <summary>Marker interface: the connected model has a dedicated side LED strip zone.</summary>
public interface IHasSideLight : IHasFuncBlock { }
