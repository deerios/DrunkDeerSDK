namespace DrunkDeer.Protocol;

/// <summary>Marker interface: the connected model supports the FuncBlock extended gateway (0x55/0x05-0x06).</summary>
public interface IHasFuncBlock : IModelMarker { }

/// <summary>Marker interface: the connected model uses HighPrecision (0xFD × 200, 0.005 mm) depth encoding (A75 Ultra, A75 Master, X60 Future).</summary>
public interface IHasHighPrecision : IHasFuncBlock { }

/// <summary>Marker interface: the connected model supports Turbo mode via the FuncBlock gateway.</summary>
public interface IHasTurboMode : IHasFuncBlock { }

/// <summary>Marker interface: the connected model has a dedicated logo LED zone.</summary>
public interface IHasLogoLight : IHasFuncBlock { }

/// <summary>Marker interface: the connected model has a dedicated side LED strip zone.</summary>
public interface IHasSideLight : IHasFuncBlock { }
