using System.ComponentModel.DataAnnotations;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Buffers.Binary;
using System.Diagnostics;

namespace DrunkDeer.Protocol;

/// <summary>USB polling rate reported to the host.</summary>
public enum ReportRate : byte
{
	/// <summary>125 Hz - 8 ms latency.</summary>
	Hz125 = 0,
	/// <summary>250 Hz - 4 ms latency.</summary>
	Hz250 = 1,
	/// <summary>500 Hz - 2 ms latency.</summary>
	Hz500 = 2,
	/// <summary>1000 Hz - 1 ms latency.</summary>
	Hz1000 = 3,
}

/// <summary>Operating-system compatibility mode for modifier key behaviour.</summary>
public enum KeyboardMode : byte
{
	/// <summary>Standard Windows / Linux mode.</summary>
	Windows = 0,
	/// <summary>
	/// Mac mode: Cmd and Option keys are remapped to match macOS conventions.
	/// </summary>
	Mac = 1,
}

/// <summary>
/// Built-in firmware lighting animation preset for <see cref="KeyboardSessionExtensions.SetLightPreset"/>,
/// <see cref="KeyboardSessionExtensions.SetLogoLightPreset"/>, and
/// <see cref="KeyboardSessionExtensions.SetSideLightPreset"/>.
/// </summary>
/// <remarks>
/// <see cref="Custom"/> (byte 0) switches to per-key custom RGB mode.
/// Values 1 and above are the firmware's sequential preset indices, inferred from firmware
/// locale strings. Exact byte assignments should be verified via USB capture if
/// precise control is required.
/// </remarks>
public enum LightPreset : byte
{
    /// <summary>Per-key custom RGB mode. Colours set via SetLighting/SetKeyColor take effect.</summary>
    Custom = 0,
    RotateMarquee = 1,
    WaveSpectrum = 2,
    SurfToTheRight = 3,
    Breath = 4,
    CenterSurfing = 5,
    Spectrum = 6,
    Ripple = 7,
    AlwaysLight = 8,
    LightByPress = 9,
    SerpentineToTheCentre = 10,
    LaserKey = 11,
    GlowingFish = 12,
    SurfingCross = 13,
    Heart = 14,
    Traffic = 15,
    GluttonousSnake = 16,
    Raindrops = 17,
    Stars = 18,
    SurfingDown = 19,
    RepeatSurfing = 20,
    RandomFountain = 21,
    DanceOfDemons = 22,
}

/// <summary>
/// A pair of keys that compete under Last Win: whichever was most recently pressed takes priority.
/// </summary>
public readonly record struct LastWinPair(DDKey A, DDKey B);

/// <summary>
/// Key-point encoding precision determined at connect time from the model's capabilities
/// and firmware version.
/// </summary>
public enum PrecisionMode : byte
{
	/// <summary>B6 × 10 (0.1 mm/unit). Standard RAESHA V1 switch.</summary>
	Standard = 0,
	/// <summary>B6 × 100 (0.01 mm/unit). Kun switch (firmware-gated or always-Kun).</summary>
	Kun = 1,
	/// <summary>HighPrecision (0xFD × 200, 0.005 mm/unit, u16le). A75 Ultra, A75 Master, X60 Future.</summary>
	HighPrecision = 2,
}

/// <summary>
/// Controls which Last Win and Rapid Trigger features are simultaneously active.
/// Pass to <see cref="KeyboardSession.SetLastWinRapidTriggerMode"/> or as the
/// <c>lastWinRapidTriggerMode</c> argument of <see cref="KeyboardSession.SetCommonConfig"/>.
/// </summary>
public enum LastWinRapidTriggerMode : byte
{
	/// <summary>Both Last Win and Rapid Trigger are disabled.</summary>
	Disabled = 0,
	/// <summary>Only Last Win is active; Rapid Trigger is disabled.</summary>
	LastWinOnly = 1,
	/// <summary>Only Rapid Trigger is active; Last Win is disabled.</summary>
	RapidTriggerOnly = 2,
	/// <summary>Both Last Win and Rapid Trigger are active.</summary>
	Both = 3,
}

/// <summary>
/// AE-path lighting animation mode codes for <see cref="KeyboardSession.SetLightingMode"/>.
/// All 18 values confirmed by USB packet capture (sequential clicks through the firmware UI).
/// </summary>
/// <remarks>
/// "Turbo mode light" and "Custom light" require per-key colour data sent via
/// <see cref="KeyboardSession.SetLighting"/> — they do not use this enum.
/// To turn off all lighting use <see cref="KeyboardSession.DisableLighting"/>.
/// </remarks>
public enum LightingMode : byte
{
	/// <summary>Rotate Marquee animation.</summary>
	RotateMarquee = 0x01,
	/// <summary>Always-on solid colour.</summary>
	AlwaysLight = 0x02,
	/// <summary>Colour spectrum cycle.</summary>
	Spectrum = 0x03,
	/// <summary>Breathing pulse animation.</summary>
	Breath = 0x04,
	/// <summary>Lights up keys only while they are held.</summary>
	LightByPress = 0x05,
	/// <summary>Static starfield effect.</summary>
	Stars = 0x06,
	/// <summary>Wave spectrum animation.</summary>
	WaveSpectrum = 0x07,
	/// <summary>Centre-out surfing wave.</summary>
	CenterSurfing = 0x08,
	/// <summary>Top-down surfing wave.</summary>
	SurfingDown = 0x09,
	/// <summary>Ripple on keypress animation.</summary>
	Ripple = 0x0A,
	/// <summary>Glowing fish swimming animation.</summary>
	GlowingFish = 0x0B,
	/// <summary>Colourful fountain animation.</summary>
	ColorfulFountain = 0x0C,
	/// <summary>Traffic-light colour cycling.</summary>
	Traffic = 0x0D,
	/// <summary>Gluttonous snake game animation.</summary>
	GluttonousSnake = 0x0E,
	/// <summary>Repeat surfing wave.</summary>
	RepeatSurfing = 0x0F,
	/// <summary>Cross-shaped surfing pattern.</summary>
	SurfingCross = 0x10,
	/// <summary>Laser key scan animation.</summary>
	LaserKey = 0x11,
	/// <summary>Random fountain bursts.</summary>
	RandomFountain = 0x12,
}

/// <summary>
/// High-level wrapper around <see cref="KeyboardConnection"/> that runs a background
/// poll loop and raises typed events for key travel changes, presses, and releases.
/// Also exposes configuration methods for actuation points, lighting, and global options.
/// </summary>
public class KeyboardSession : IDisposable
{
	private readonly ILogger _log;

	// Firmware always addresses 127 key slots (indices 0-126) across three B7 packets.
	private const int KeyCount = 127;

	// Packet 0 = keys 0-58, Packet 1 = keys 59-117, Packet 2 = keys 118-126.
	private static readonly int[] PacketBase = [0, 59, 118];
	private static readonly int[] PacketCount = [59, 59, 9];

	// High-precision: 5 sections, 30 keys each (last section has 6).
	private const int HpKeyCount = 126;
	private static readonly int[] HpSectionBase = [0, 30, 60, 90, 120];
	private static readonly int[] HpSectionSizes = [30, 30, 30, 30, 6];

	private readonly IKeyboardConnection _connection;
	private readonly PrecisionMode _precisionMode;
	private readonly short[] _heights = new short[KeyCount];
	private readonly bool[] _pressed = new bool[KeyCount];

	private Task? _pollTask;
	private CancellationTokenSource? _pollCts;
	private bool _inFastMode;
	private int _disposedFlag;
	private bool IsDisposed => Volatile.Read(ref _disposedFlag) != 0;

	// Per-model layout and key-mapping data (initialised in constructor from KeyLayout)
	private readonly int[] _rgbIndices;
	private readonly IReadOnlyDictionary<DDKey, int> _keyIndexMap;

	// Stateful key-point profiles - used by DDKey depth-setter overloads so a
	// per-key change can be sent as a complete packet with all other keys unchanged.
	private readonly float[] _actuationProfile;
	private readonly float[] _downstrokeProfile;
	private readonly float[] _upstrokeProfile;

	// Stateful RGB profile - used by SetKeyColor to preserve other keys' colours.
	private readonly (byte R, byte G, byte B)[] _rgbProfile = new (byte, byte, byte)[128];

	// Stateful global feature flags - mirrored from the keyboard and kept in sync by
	// Enable/Disable helpers so SendCommonConfig can always rebuild a complete packet.
	private bool _turboEnabled;
	private bool _rapidTriggerEnabled;
	private LastWinRapidTriggerMode _lastWinRtMode;
	private bool _rapidTriggerAutoMatch;

	/// <summary>Model metadata resolved during the identity handshake.</summary>
	public ModelInfo Model { get; }
	/// <summary>Variant string (e.g. "ansi", "iso") resolved during the identity handshake.</summary>
	public string Variant { get; }
	/// <summary>Firmware version byte returned by the keyboard.</summary>
	public byte FirmwareVersion { get; }

	/// <summary>
	/// Key travel depth in mm that fires <see cref="KeyDown"/>. Default: 1.0 mm.
	/// </summary>
	public float PressThresholdMm { get; set; } = 1.0f;

	/// <summary>
	/// Key travel depth in mm below which <see cref="KeyUp"/> fires. Default: 0.5 mm.
	/// </summary>
	public float ReleaseThresholdMm { get; set; } = 0.5f;

	/// <summary>Sets <see cref="PressThresholdMm"/> and <see cref="ReleaseThresholdMm"/> in one call.</summary>
	public void SetThresholds(float pressThresholdMm, float releaseThresholdMm)
	{
		PressThresholdMm   = pressThresholdMm;
		ReleaseThresholdMm = releaseThresholdMm;
	}

	/// <summary>
	/// Total number of addressable key slots for the connected model.
	/// HighPrecision models: 126. All other models: 127.
	/// </summary>
	public int TotalKeyCount => _precisionMode == PrecisionMode.HighPrecision ? HpKeyCount : KeyCount;

	/// <summary>
	/// Number of physically illuminated keys for the connected model.
	/// Equal to the number of entries in the model's RGB index array.
	/// Use this as the upper bound when building per-key colour arrays.
	/// </summary>
	public int LightingKeyCount => _rgbIndices.Length;

	/// <summary>
	/// <see langword="true"/> while the background poll loop is running.
	/// Configuration methods throw when this is <see langword="true"/>.
	/// </summary>
	public bool IsPolling => _pollTask is { IsCompleted: false };

	/// <summary>Active precision mode determined at connect time from capabilities and firmware version.</summary>
	public PrecisionMode PrecisionMode => _precisionMode;

	/// <summary>
	/// <see langword="true"/> if the connected keyboard uses HighPrecision (0xFD, 0.005 mm) depth
	/// encoding (A75 Ultra, A75 Master, X60).
	/// </summary>
	public bool IsHighPrecision => _precisionMode == PrecisionMode.HighPrecision;

	internal bool HasLogoLight => (Model.Capabilities & Capabilities.LogoLight) != 0;

	internal bool HasSideLight => (Model.Capabilities & Capabilities.SideLight) != 0;

	/// <summary>
	/// <see langword="true"/> if the connected keyboard persists Turbo mode via the FuncBlock gateway.
	/// HighPrecision models (A75 Ultra, A75 Master, X60 Future) and always-KunPrecision
	/// models (G65 m1/m2/m3, G60 v600) support this feature.
	/// Standard-precision models with firmware-gated Kun do not because old firmware does
	/// not respond to the FuncBlock gateway (0x55/0x05).
	/// </summary>
	internal bool HasTurboMode => (Model.Capabilities & Capabilities.TurboMode) != 0;

	/// <summary>
	/// <see langword="true"/> when the FuncBlock gateway (0x55/0x05 read, 0x06 write) is
	/// supported by the connected keyboard. Requires <see cref="PrecisionMode"/> to be
	/// <see cref="PrecisionMode.Kun"/> or <see cref="PrecisionMode.HighPrecision"/>. Standard-precision
	/// models running below their Kun firmware threshold do not respond to these sub-commands.
	/// </summary>
	internal bool HasFuncBlock => _precisionMode != PrecisionMode.Standard;

	/// <summary>
	/// Raised when the background poll loop detects that the keyboard has been disconnected.
	/// The session is no longer usable after this event fires; dispose it and reconnect.
	/// </summary>
	public event EventHandler? Disconnected;

	/// <summary>
	/// Effective minimum actuation/downstroke/upstroke depth in mm for this connection.
	/// Accounts for the active precision mode (standard vs Kun vs HighPrecision).
	/// </summary>
	public float MinDepthMm { get; }

	/// <summary>
	/// Effective maximum actuation/downstroke/upstroke depth in mm for this connection.
	/// Accounts for the active precision mode (standard vs Kun vs HighPrecision).
	/// </summary>
	public float MaxDepthMm { get; }

	/// <summary>Whether Rapid Trigger is currently enabled on the keyboard.</summary>
	public bool RapidTriggerEnabled => _rapidTriggerEnabled;

	/// <summary>Whether Turbo mode is currently enabled on the keyboard.</summary>
	public bool TurboEnabled => _turboEnabled;

	// mm-to-raw scale for the B7/0xFD travel stream: Standard=10, Kun=100, HighPrecision=200.
	private float HeightScale => _precisionMode switch
	{
		PrecisionMode.HighPrecision => 200f,
		PrecisionMode.Kun => 100f,
		_ => 10f,
	};

	/// <summary>
	/// Returns the last-polled travel depth for <paramref name="keyIndex"/> in millimetres.
	/// Resolution: 0.1 mm (Standard), 0.01 mm (Kun), 0.005 mm (HighPrecision).
	/// Returns 0 when the key is at rest or has not yet been polled.
	/// </summary>
	public float GetKeyHeightMm(int keyIndex)
	{
		if ((uint)keyIndex >= (uint)TotalKeyCount)
			throw new ArgumentOutOfRangeException(nameof(keyIndex),
				$"Key index {keyIndex} is out of range [0, {TotalKeyCount - 1}].");
		return _heights[keyIndex] / HeightScale;
	}

	/// <summary>
	/// Returns the last-polled travel depth for <paramref name="key"/> in millimetres.
	/// Throws if <paramref name="key"/> is not present on this keyboard model.
	/// </summary>
	public float GetKeyHeightMm(DDKey key) => GetKeyHeightMm(GetKeyIndex(key));

	/// <summary>Returns the set of <see cref="DDKey"/> values present on this keyboard model.</summary>
	public IEnumerable<DDKey> GetKeys() => _keyIndexMap.Keys;

	/// <summary>
	/// Returns a snapshot of all key travel depths in millimetres, indexed by layout key index.
	/// Length equals <see cref="TotalKeyCount"/>. Keys at rest return 0.
	/// </summary>
	public float[] GetAllKeyHeightsMm()
	{
		float scale = HeightScale;
		var mm = new float[TotalKeyCount];
		for (int i = 0; i < TotalKeyCount; i++)
			mm[i] = _heights[i] / scale;
		return mm;
	}

	/// <summary>
	/// Returns the last-polled travel depth for every key present on this model,
	/// keyed by <see cref="DDKey"/>. Keys at rest return 0.
	/// </summary>
	public IReadOnlyDictionary<DDKey, float> GetAllKeyHeightsMmByKey()
	{
		float scale = HeightScale;
		var dict = new Dictionary<DDKey, float>(_keyIndexMap.Count);
		foreach (var (key, idx) in _keyIndexMap)
			dict[key] = _heights[idx] / scale;
		return dict;
	}

	/// <summary>
	/// Converts a per-index depth array (as returned by <see cref="ReadActuationPoint"/> etc.)
	/// into a <see cref="DDKey"/>-keyed dictionary using this session's key layout.
	/// </summary>
	internal IReadOnlyDictionary<DDKey, float> ToKeyDictionary(float[] values)
	{
		var dict = new Dictionary<DDKey, float>(_keyIndexMap.Count);
		foreach (var (key, idx) in _keyIndexMap)
			dict[key] = values[idx];
		return dict;
	}

	/// <summary>
	/// Returns <see langword="true"/> if <paramref name="keyIndex"/> is currently considered
	/// pressed (travel has crossed <see cref="PressThresholdMm"/> and not yet recovered).
	/// </summary>
	public bool IsKeyPressed(int keyIndex)
	{
		if ((uint)keyIndex >= (uint)TotalKeyCount)
			throw new ArgumentOutOfRangeException(nameof(keyIndex),
				$"Key index {keyIndex} is out of range [0, {TotalKeyCount - 1}].");
		return _pressed[keyIndex];
	}

	/// <summary>
	/// Returns <see langword="true"/> if <paramref name="key"/> is currently pressed.
	/// Throws if <paramref name="key"/> is not present on this keyboard model.
	/// </summary>
	public bool IsKeyPressed(DDKey key) => IsKeyPressed(GetKeyIndex(key));

	/// <summary>
	/// Returns the layout index (0-based grid position) for <paramref name="key"/> on the
	/// connected keyboard model. The same index is used for actuation profiles, RGB lighting,
	/// and Last Win pairs.
	/// </summary>
	/// <exception cref="ArgumentException">
	/// Thrown when <paramref name="key"/> is not present on this keyboard model.
	/// </exception>
	public int GetKeyIndex(DDKey key)
	{
		if (!_keyIndexMap.TryGetValue(key, out int index))
			throw new ArgumentException(
				$"Key {key} is not present on this keyboard model ({Model.Name}).", nameof(key));
		return index;
	}

	/// <summary>
	/// Tries to resolve the layout index for <paramref name="key"/> on the connected model.
	/// Returns <see langword="false"/> when the key is not present on this model.
	/// </summary>
	public bool TryGetKeyIndex(DDKey key, out int index) =>
		_keyIndexMap.TryGetValue(key, out index);

	/// <summary>Returns <see langword="true"/> if <paramref name="key"/> exists on the connected model.</summary>
	public bool IsKeyPresent(DDKey key) => _keyIndexMap.ContainsKey(key);

	/// <summary>Fired whenever any key's travel depth changes between two consecutive polls.</summary>
	public event EventHandler<KeyHeightChangedEventArgs>? KeyHeightChanged;

	/// <summary>Fired when a key's travel crosses <see cref="PressThresholdMm"/> on the way down.</summary>
	public event EventHandler<KeyEventArgs>? KeyDown;

	/// <summary>Fired when a key's travel drops below <see cref="ReleaseThresholdMm"/> after being pressed.</summary>
	public event EventHandler<KeyEventArgs>? KeyUp;

	/// <summary>Fired after a complete press-release cycle, at the same time as <see cref="KeyUp"/>.</summary>
	public event EventHandler<KeyEventArgs>? KeyPressed;

	/// <summary>Fired once per complete poll cycle, after all key events for that cycle.</summary>
	public event EventHandler<PolledEventArgs>? Polled;

	private static PrecisionMode DeterminePrecisionMode(ModelInfo model, byte firmwareVersion)
	{
		if ((model.Capabilities & Capabilities.HighPrecision) != 0) return PrecisionMode.HighPrecision;
		if ((model.Capabilities & Capabilities.KunPrecision)  != 0) return PrecisionMode.Kun;
		if (model.KunPrecisionMinFirmware is byte threshold && firmwareVersion >= threshold)
			return PrecisionMode.Kun;
		return PrecisionMode.Standard;
	}

	internal KeyboardSession(IKeyboardConnection connection, ILoggerFactory? loggerFactory = null)
	{
		_log           = (ILogger?)loggerFactory?.CreateLogger<KeyboardSession>() ?? NullLogger.Instance;
		_connection    = connection;
		Model          = connection.Model;
		Variant        = connection.Variant;
		FirmwareVersion = connection.FirmwareVersion;
		_precisionMode = DeterminePrecisionMode(Model, FirmwareVersion);
		MinDepthMm     = Model.MinDepthMm;
		MaxDepthMm     = _precisionMode == PrecisionMode.Kun ? Model.KunMaxDepthMm : Model.MaxDepthMm;

		var layout = KeyLayout.GetLayout(Model.Slug);
		_rgbIndices      = KeyLayout.GetRgbIndices(Model.Slug, Variant);
		_keyIndexMap     = KeyLayout.BuildIndexMap(layout);

		int profileSize = TotalKeyCount;
		_actuationProfile = new float[profileSize];
		Array.Fill(_actuationProfile, 2.0f);
		// Firmware default RtPress/RtRelease is 25 raw units (0.01 mm/unit) = 0.25 mm
		// (KeyTriggerConfig.Default). Seeding with 0 would make the first per-key
		// downstroke/upstroke call fail ValidateDepthMm for every other key still at its
		// unset default, since MinDepthMm is 0.2 mm.
		_downstrokeProfile = new float[profileSize];
		Array.Fill(_downstrokeProfile, 0.25f);
		_upstrokeProfile   = new float[profileSize];
		Array.Fill(_upstrokeProfile, 0.25f);

		_turboEnabled          = connection.InitialTurboValue != 0;
		_rapidTriggerEnabled   = connection.InitialRapidTriggerEnabled != 0;
		_lastWinRtMode         = (LastWinRapidTriggerMode)connection.InitialLastWinValue;
		_rapidTriggerAutoMatch = connection.InitialRapidTriggerAutoMatch != 0;
	}

	/// <summary>
	/// Opens the first detected DrunkDeer keyboard, performs the identity handshake,
	/// and returns a ready-to-use <see cref="KeyboardSession"/>.
	/// </summary>
	public static KeyboardSession OpenFirst(ILoggerFactory? loggerFactory = null) =>
		new(KeyboardDiscoverer.OpenFirst(loggerFactory), loggerFactory);

	/// <summary>Starts the background poll loop. Calling while already polling is a no-op.</summary>
	public void StartPolling(CancellationToken cancellationToken = default)
	{
		if (_pollTask is { IsCompleted: false })
		{
			_log.LogWarning("StartPolling called while already polling");
			return;
		}

		_log.LogInformation("Starting poll loop (PressThresholdMm={P}, ReleaseThresholdMm={R}, PrecisionMode={PM})",
			PressThresholdMm, ReleaseThresholdMm, _precisionMode);
		_pollCts  = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		_pollTask = Task.Run(() => PollLoop(_pollCts.Token), _pollCts.Token);
	}

	/// <summary>
	/// Signals the polling loop to stop and waits up to two seconds for it to exit.
	/// </summary>
	/// <returns>
	/// <see langword="true"/> if the loop stopped within the timeout; <see langword="false"/> if
	/// it's still running (rare - the inner receive loop can legitimately take up to
	/// 10 retries x 200 ms plus dispatch time). On <see langword="false"/>, <see cref="IsPolling"/>
	/// may still report <see langword="true"/> and a caller that immediately invokes a config
	/// method will still hit <c>EnsureNotPolling</c>'s guard.
	/// </returns>
	public bool StopPolling()
	{
		_log.LogInformation("Stopping poll loop…");
		_pollCts?.Cancel();

		bool completed;
		try
		{
			completed = _pollTask?.Wait(TimeSpan.FromSeconds(2)) ?? true;
		}
		catch (AggregateException ex)
		{
			// The loop itself now swallows and logs handler exceptions (POLL-2), so a fault here
			// means something genuinely unexpected happened in PollLoop. The task is done either
			// way, so treat it as stopped rather than leaving the caller to catch this themselves.
			_log.LogError(ex.Flatten(), "Poll loop task faulted while stopping.");
			completed = true;
		}

		if (completed)
		{
			_pollCts?.Dispose();
			_pollCts  = null;
			_pollTask = null;
			_log.LogInformation("Poll loop stopped.");
		}
		else
		{
			_log.LogWarning("Poll loop did not stop within the 2s timeout; it may still be running.");
		}

		return completed;
	}

	private void PollLoop(CancellationToken ct)
	{
		_log.LogInformation("PollLoop started (HasDataStream={DS}, PrecisionMode={PM}).",
			_connection.HasDataStream, _precisionMode);
		_connection.FlushReadBuffer();

		var request = TravelRequest.Build();
		int frameCount = _precisionMode == PrecisionMode.HighPrecision ? 5 : 3;
		var packets = new byte[frameCount][];
		var gotPkt = new bool[frameCount];
		long lastTicks = Stopwatch.GetTimestamp();
		long totalFrames = 0;
		long droppedFrames = 0;

		while (!ct.IsCancellationRequested)
		{
			try { _connection.Send(request); }
			catch (Exception ex)
			{
				// If StopPolling timed out and Dispose() went ahead anyway, _connection.Dispose()
				// yanks the streams out from under this still-running loop, and the resulting
				// exception here is expected teardown, not a real disconnect - don't raise a
				// misleading Disconnected event on an already-disposed session.
				if (!IsDisposed)
				{
					_log.LogWarning(ex, "PollLoop: send failed - keyboard disconnected.");
					Disconnected?.Invoke(this, EventArgs.Empty);
				}
				return;
			}

			Array.Clear(gotPkt, 0, frameCount);
			int retries = 0;

			while (!AllReceived(gotPkt) && retries < 10 && !ct.IsCancellationRequested)
			{
				var buf = _connection.ReceiveCommand(200);
				if (buf is null) { retries++; continue; }

				if (_precisionMode == PrecisionMode.HighPrecision)
				{
					if (!KeyTravelHighPrecision.Matches(buf)) { retries++; continue; }
					int slot = FirstEmpty(gotPkt);
					if (slot < 0)
					{
						// HP packets carry no section id, so an extra packet for an already-full frame
						// means we're desynced (e.g. a stale packet from a prior dropped frame). Restart
						// frame collection from a clean buffer rather than keeping the misaligned state.
						_log.LogDebug("PollLoop: unexpected extra HP packet, resyncing.");
						Array.Clear(gotPkt, 0, frameCount);
						_connection.FlushReadBuffer();
						retries++;
						continue;
					}
					packets[slot] = buf;
					gotPkt[slot]  = true;
				}
				else
				{
					if (!TravelResponse.Matches(buf))
					{
						_log.LogDebug("PollLoop: non-B7 during frame collection (cmd=0x{C:X2}), retry {R}",
							buf[0], retries);
						retries++;
						continue;
					}
					int idx = TravelResponse.GetPacketIndex(buf);
					if ((uint)idx > 2)
					{
						_log.LogDebug("PollLoop: B7 bad index {Idx}, retry {R}", idx, retries);
						retries++;
						continue;
					}
					packets[idx] = buf;
					gotPkt[idx]  = true;
				}
			}

			if (!AllReceived(gotPkt))
			{
				droppedFrames++;
				_log.LogDebug("PollLoop: dropped frame #{F} (retries={R})",
					totalFrames + droppedFrames, retries);
				// Any packets still in flight for this timed-out request belong to a frame we're
				// abandoning. Without this flush, stale packets arrive first on the next request
				// and get assigned to the wrong slots by FirstEmpty - since HP packets carry no
				// section id, that misassignment never self-corrects and persists for the session.
				_connection.FlushReadBuffer();
				continue;
			}

			long now = Stopwatch.GetTimestamp();
			var elapsed = TimeSpan.FromSeconds((double)(now - lastTicks) / Stopwatch.Frequency);
			lastTicks    = now;
			totalFrames++;

			_log.LogTrace("PollLoop: frame #{F} elapsed={Ms:F2}ms (dropped={D})",
				totalFrames, elapsed.TotalMilliseconds, droppedFrames);

			try
			{
				if (_precisionMode == PrecisionMode.HighPrecision)
					DispatchFrameHighPrecision(packets, elapsed);
				else
					DispatchFrame(packets, elapsed);
			}
			catch (Exception ex)
			{
				// DispatchFrame*/UpdateKeyState invoke user-supplied event handlers
				// (KeyDown/KeyUp/KeyPressed/KeyHeightChanged/Polled). A handler that throws must
				// not fault this background task - callers only observe that via the unrelated
				// AggregateException surfacing later from StopPolling's Wait, which is confusing
				// and stops polling with no clear signal. Log and keep polling instead.
				_log.LogError(ex, "PollLoop: unhandled exception from an event handler; continuing to poll.");
			}
		}

		_log.LogInformation("PollLoop exiting. frames={F} dropped={D}", totalFrames, droppedFrames);
	}

	private void DispatchFrame(byte[][] packets, TimeSpan elapsed)
	{
		int pressRaw = (int)Math.Round(PressThresholdMm   * HeightScale);
		int releaseRaw = (int)Math.Round(ReleaseThresholdMm * HeightScale);

		for (int pkt = 0; pkt < 3; pkt++)
		{
			var travel = TravelResponse.GetTravel(packets[pkt]);
			int baseIdx = PacketBase[pkt];
			int count = PacketCount[pkt];

			for (int x = 0; x < count; x++)
			{
				int key = baseIdx + x;
				short h = travel[x];
				short prev = _heights[key];
				if (h == prev) continue;

				_log.LogTrace("Height[{I:000}] {Prev} -> {H}  (press={Ps} thresh={PT}/{RT})",
					key, prev, h, _pressed[key], pressRaw, releaseRaw);
				UpdateKeyState(key, prev, h, pressRaw, releaseRaw);
			}
		}

		Polled?.Invoke(this, new PolledEventArgs(elapsed));
	}

	private void DispatchFrameHighPrecision(byte[][] packets, TimeSpan elapsed)
	{
		// 1 HP unit = 0.005 mm, so mm × 200 = raw
		int pressRaw   = (int)Math.Round(PressThresholdMm   * HeightScale);
		int releaseRaw = (int)Math.Round(ReleaseThresholdMm * HeightScale);

		for (int section = 0; section < 5; section++)
		{
			var vals = KeyTravelHighPrecision.GetKeyValues(packets[section]);
			int baseKey = HpSectionBase[section];
			int count = HpSectionSizes[section];

			for (int x = 0; x < count; x++)
			{
				int key = baseKey + x;
				ushort raw = BinaryPrimitives.ReadUInt16LittleEndian(vals.Slice(x * 2));
				short h = (short)(raw < 40 ? 0 : raw);
				short prev = _heights[key];
				if (h == prev) continue;

				UpdateKeyState(key, prev, h, pressRaw, releaseRaw);
			}
		}

		Polled?.Invoke(this, new PolledEventArgs(elapsed));
	}

	private void UpdateKeyState(int key, short prev, short h, int pressRaw, int releaseRaw)
	{
		_heights[key] = h;
		KeyHeightChanged?.Invoke(this, new KeyHeightChangedEventArgs(key, prev, h));

		if (!_pressed[key] && h >= pressRaw)
		{
			_pressed[key] = true;
			_log.LogDebug("KeyDown  idx={I:000} h={H}", key, h);
			KeyDown?.Invoke(this, new KeyEventArgs(key, h));
		}
		else if (_pressed[key] && h < releaseRaw)
		{
			_pressed[key] = false;
			_log.LogDebug("KeyUp    idx={I:000} h={H}", key, h);
			var args = new KeyEventArgs(key, h);
			KeyUp?.Invoke(this, args);
			KeyPressed?.Invoke(this, args);
		}
	}

	private static bool AllReceived(bool[] flags)
	{
		foreach (var f in flags) if (!f) return false;
		return true;
	}

	private static int FirstEmpty(bool[] flags)
	{
		for (int i = 0; i < flags.Length; i++)
			if (!flags[i]) return i;
		return -1;
	}

	private void EnsureNotPolling()
	{
		if (_pollTask is { IsCompleted: false })
			throw new InvalidOperationException(
				"Stop polling before issuing configuration commands.");
	}

	private void EnsureHasLogoLight()
	{
		if (!HasLogoLight)
			throw new NotSupportedException(
				$"{Model.Name} does not have a logo LED zone.");
	}

	private void EnsureHasSideLight()
	{
		if (!HasSideLight)
			throw new NotSupportedException(
				$"{Model.Name} does not have a side LED zone.");
	}

	private void EnsureHasFuncBlock()
	{
		if (!HasFuncBlock)
			throw new NotSupportedException(
				$"{Model.Name} (fw {FirmwareVersion}, {_precisionMode} precision) does not support " +
				$"FuncBlock operations (0x55/0x05-0x06). " +
				$"Check {nameof(HasFuncBlock)} before calling this method.");
	}


	/// <summary>Sets the actuation point for all keys to a uniform depth.</summary>
	/// <param name="depthMm">
	/// Actuation depth in mm. Must be within [<see cref="MinDepthMm"/>, <see cref="MaxDepthMm"/>].
	/// </param>
	public void SetActuationPoint(float depthMm)
	{
		EnsureNotPolling();
		ValidateDepthMm(depthMm);
		Array.Fill(_actuationProfile, depthMm);
		SetKeyPointUniform(depthMm,
			(idx, vals) => WriteActuationPointStandard.Build(idx, vals),
			(sec, data) => WriteActuationPointHighPrecision.Build((byte)sec, data));
	}

	/// <summary>Sets per-key actuation depths.</summary>
	/// <param name="depthsMm">
	/// One depth (mm) per key. Length must equal <see cref="TotalKeyCount"/>.
	/// </param>
	internal void SetActuationPoint(float[] depthsMm)
	{
		EnsureNotPolling();
		SetKeyPointPerKey(depthsMm,
			(idx, vals) => WriteActuationPointStandard.Build(idx, vals),
			(sec, data) => WriteActuationPointHighPrecision.Build((byte)sec, data));
		depthsMm.CopyTo(_actuationProfile, 0);
	}

	/// <summary>
	/// Sets the actuation point for specific keys, leaving all others unchanged.
	/// Keys not present on this model are silently skipped.
	/// </summary>
	/// <remarks>
	/// This is always a full-profile write: the firmware has no "set just these keys" command,
	/// so every other key is rewritten to whatever this session's in-memory shadow currently
	/// holds for it - which starts at an SDK-chosen default (2.0 mm), not whatever the keyboard
	/// was actually last configured to (e.g. via the official app), on Standard-precision models
	/// with no read-back. The first call in a session will therefore clobber every other key's
	/// actuation point to that default. Call <see cref="SetActuationPoints"/> with a full
	/// <see cref="KeyDepthProfile"/> first if you need to preserve specific existing values.
	/// </remarks>
	/// <param name="depthMm">Depth in mm. Must be within [<see cref="MinDepthMm"/>, <see cref="MaxDepthMm"/>].</param>
	/// <param name="keys">One or more keys to update.</param>
	/// <example>
	/// <code>session.SetActuationPoint(0.2f, DDKey.W, DDKey.A, DDKey.S, DDKey.D);</code>
	/// </example>
	public void SetActuationPoint(float depthMm, params DDKey[] keys)
	{
		EnsureNotPolling();
		if (keys.Length == 0)
			throw new ArgumentException("At least one key must be specified.", nameof(keys));
		foreach (var key in keys)
		{
			if (!TryGetKeyIndex(key, out int idx))
			{
				_log.LogWarning("SetActuationPoint: key {Key} not present on {Model}; skipped.", key, Model.Name);
				continue;
			}
			ValidateDepthMm(depthMm, idx);
			_actuationProfile[idx] = depthMm;
		}
		SetKeyPointPerKey(_actuationProfile,
			(idx, vals) => WriteActuationPointStandard.Build(idx, vals),
			(sec, data) => WriteActuationPointHighPrecision.Build((byte)sec, data));
	}

	/// <summary>Sets the downstroke point for all keys to a uniform depth.</summary>
	public void SetDownstrokePoint(float depthMm)
	{
		EnsureNotPolling();
		ValidateDepthMm(depthMm);
		Array.Fill(_downstrokeProfile, depthMm);
		SetKeyPointUniform(depthMm,
			(idx, vals) => WriteDownstrokePointStandard.Build(idx, vals),
			(sec, data) => WriteDownstrokePointHighPrecision.Build((byte)sec, data));
	}

	/// <summary>Sets per-key downstroke depths.</summary>
	/// <param name="depthsMm">One depth (mm) per key. Length must equal <see cref="TotalKeyCount"/>.</param>
	internal void SetDownstrokePoint(float[] depthsMm)
	{
		EnsureNotPolling();
		SetKeyPointPerKey(depthsMm,
			(idx, vals) => WriteDownstrokePointStandard.Build(idx, vals),
			(sec, data) => WriteDownstrokePointHighPrecision.Build((byte)sec, data));
		depthsMm.CopyTo(_downstrokeProfile, 0);
	}

	/// <summary>
	/// Sets the downstroke point for specific keys, leaving all others unchanged.
	/// Keys not present on this model are silently skipped.
	/// </summary>
	/// <remarks>
	/// This is always a full-profile write: the firmware has no "set just these keys" command,
	/// so every other key is rewritten to whatever this session's in-memory shadow currently
	/// holds for it - which starts at an SDK-chosen default (0.25 mm), not whatever the keyboard
	/// was actually last configured to, on Standard-precision models with no read-back. See
	/// <see cref="SetActuationPoint(float, DDKey[])"/>'s remarks for the same caveat.
	/// </remarks>
	public void SetDownstrokePoint(float depthMm, params DDKey[] keys)
	{
		EnsureNotPolling();
		if (keys.Length == 0)
			throw new ArgumentException("At least one key must be specified.", nameof(keys));
		foreach (var key in keys)
		{
			if (!TryGetKeyIndex(key, out int idx))
			{
				_log.LogWarning("SetDownstrokePoint: key {Key} not present on {Model}; skipped.", key, Model.Name);
				continue;
			}
			ValidateDepthMm(depthMm, idx);
			_downstrokeProfile[idx] = depthMm;
		}
		SetKeyPointPerKey(_downstrokeProfile,
			(idx, vals) => WriteDownstrokePointStandard.Build(idx, vals),
			(sec, data) => WriteDownstrokePointHighPrecision.Build((byte)sec, data));
	}

	/// <summary>Sets the upstroke point for all keys to a uniform depth.</summary>
	public void SetUpstrokePoint(float depthMm)
	{
		EnsureNotPolling();
		ValidateDepthMm(depthMm);
		Array.Fill(_upstrokeProfile, depthMm);
		SetKeyPointUniform(depthMm,
			(idx, vals) => WriteUpstrokePointStandard.Build(idx, vals),
			(sec, data) => WriteUpstrokePointHighPrecision.Build((byte)sec, data));
	}

	/// <summary>Sets per-key upstroke depths.</summary>
	/// <param name="depthsMm">One depth (mm) per key. Length must equal <see cref="TotalKeyCount"/>.</param>
	internal void SetUpstrokePoint(float[] depthsMm)
	{
		EnsureNotPolling();
		SetKeyPointPerKey(depthsMm,
			(idx, vals) => WriteUpstrokePointStandard.Build(idx, vals),
			(sec, data) => WriteUpstrokePointHighPrecision.Build((byte)sec, data));
		depthsMm.CopyTo(_upstrokeProfile, 0);
	}

	/// <summary>
	/// Sets the upstroke point for specific keys, leaving all others unchanged.
	/// Keys not present on this model are silently skipped.
	/// </summary>
	/// <remarks>
	/// This is always a full-profile write: the firmware has no "set just these keys" command,
	/// so every other key is rewritten to whatever this session's in-memory shadow currently
	/// holds for it - which starts at an SDK-chosen default (0.25 mm), not whatever the keyboard
	/// was actually last configured to, on Standard-precision models with no read-back. See
	/// <see cref="SetActuationPoint(float, DDKey[])"/>'s remarks for the same caveat.
	/// </remarks>
	public void SetUpstrokePoint(float depthMm, params DDKey[] keys)
	{
		EnsureNotPolling();
		if (keys.Length == 0)
			throw new ArgumentException("At least one key must be specified.", nameof(keys));
		foreach (var key in keys)
		{
			if (!TryGetKeyIndex(key, out int idx))
			{
				_log.LogWarning("SetUpstrokePoint: key {Key} not present on {Model}; skipped.", key, Model.Name);
				continue;
			}
			ValidateDepthMm(depthMm, idx);
			_upstrokeProfile[idx] = depthMm;
		}
		SetKeyPointPerKey(_upstrokeProfile,
			(idx, vals) => WriteUpstrokePointStandard.Build(idx, vals),
			(sec, data) => WriteUpstrokePointHighPrecision.Build((byte)sec, data));
	}

	/// <summary>Sets per-key actuation depths from a <see cref="KeyDepthProfile"/>.</summary>
	public void SetActuationPoints(KeyDepthProfile profile) =>
		SetActuationPoint(BuildDepthArray(profile, _actuationProfile));

	/// <summary>Sets per-key downstroke depths from a <see cref="KeyDepthProfile"/>.</summary>
	public void SetDownstrokePoints(KeyDepthProfile profile) =>
		SetDownstrokePoint(BuildDepthArray(profile, _downstrokeProfile));

	/// <summary>Sets per-key upstroke depths from a <see cref="KeyDepthProfile"/>.</summary>
	public void SetUpstrokePoints(KeyDepthProfile profile) =>
		SetUpstrokePoint(BuildDepthArray(profile, _upstrokeProfile));

	/// <summary>
	/// Expands a <see cref="KeyDepthProfile"/> into a per-key depth array. A zero/unset
	/// <see cref="KeyDepthProfile.Default"/> means "leave keys not listed in
	/// <see cref="KeyDepthProfile.Keys"/> at their current session value" - it does not mean
	/// "set them to 0", which would be below <see cref="MinDepthMm"/> and fail validation
	/// (this is what made CaptureProfile -> ApplyProfile round-trips throw for non-uniform
	/// depths, since CaptureProfile always emits Default: 0 for a non-uniform profile).
	/// </summary>
	private float[] BuildDepthArray(KeyDepthProfile profile, float[] currentProfile, [CallerMemberName] string callerName = "")
	{
		var depths = new float[TotalKeyCount];
		if (profile.Default != 0f)
			Array.Fill(depths, profile.Default);
		else
			currentProfile.CopyTo(depths, 0);
		if (profile.Keys != null)
		{
			foreach (var (name, depthMm) in profile.Keys)
			{
				if (!Enum.TryParse<DDKey>(name, ignoreCase: true, out var key) || !TryGetKeyIndex(key, out int idx))
				{
					_log.LogWarning("{Method}: unknown key '{Key}'; skipped.", callerName, name);
					continue;
				}
				depths[idx] = depthMm;
			}
		}
		return depths;
	}

	/// <summary>
	/// Reads the actuation point profile and returns per-key depths in mm.
	/// Only supported on HighPrecision models.
	/// </summary>
	internal float[] ReadActuationPoint()
	{
		if (_precisionMode != PrecisionMode.HighPrecision)
			throw new NotSupportedException(
				"Actuation point read-back is only supported on high-precision models.");
		return ReadKeyPointMm(
			DrunkDeer.Protocol.ReadActuationPointHighPrecision.Build(),
			ActuationPointResponseHighPrecision.Matches,
			ActuationPointResponseHighPrecision.GetSection,
			ActuationPointResponseHighPrecision.GetKeyValues);
	}

	/// <summary>
	/// Reads the downstroke point profile and returns per-key depths in mm.
	/// Only supported on HighPrecision models.
	/// </summary>
	internal float[] ReadDownstrokePoint()
	{
		if (_precisionMode != PrecisionMode.HighPrecision)
			throw new NotSupportedException(
				"Downstroke point read-back is only supported on high-precision models.");
		return ReadKeyPointMm(
			DrunkDeer.Protocol.ReadDownstrokePointHighPrecision.Build(),
			DownstrokePointResponseHighPrecision.Matches,
			DownstrokePointResponseHighPrecision.GetSection,
			DownstrokePointResponseHighPrecision.GetKeyValues);
	}

	/// <summary>
	/// Reads the upstroke point profile and returns per-key depths in mm.
	/// Only supported on HighPrecision models.
	/// </summary>
	internal float[] ReadUpstrokePoint()
	{
		if (_precisionMode != PrecisionMode.HighPrecision)
			throw new NotSupportedException(
				"Upstroke point read-back is only supported on high-precision models.");
		return ReadKeyPointMm(
			DrunkDeer.Protocol.ReadUpstrokePointHighPrecision.Build(),
			UpstrokePointResponseHighPrecision.Matches,
			UpstrokePointResponseHighPrecision.GetSection,
			UpstrokePointResponseHighPrecision.GetKeyValues);
	}

	private const float SafeMaxDepthMm = 3.3f;

	private void ValidateDepthMm(float mm, int keyIndex = -1)
	{
		if (mm < MinDepthMm || mm > MaxDepthMm)
		{
			var location = keyIndex >= 0 ? $" for key {keyIndex}" : "";
			throw new ArgumentOutOfRangeException(
				nameof(mm),
				$"Depth {mm:F3} mm{location} is outside the valid range " +
				$"[{MinDepthMm:F2}, {MaxDepthMm:F2}] mm.");
		}
		if (mm > SafeMaxDepthMm)
		{
			var location = keyIndex >= 0 ? $" (key {keyIndex})" : "";
			_log.LogWarning(
				"Depth {Mm:F3} mm{Location} exceeds {Safe} mm - depths above this threshold may cause " +
				"the firmware to emit repeated keypress packets while a key is held down.",
				mm, location, SafeMaxDepthMm);
		}
	}

	internal static byte MmToStandardUnit(float mm) =>
		(byte)Math.Clamp((int)Math.Round(mm * 10f, MidpointRounding.AwayFromZero), 0, 255);

	internal static byte MmToKunUnit(float mm) =>
		(byte)Math.Clamp((int)Math.Round(mm * 100f, MidpointRounding.AwayFromZero), 0, 255);

	private byte MmToB6Unit(float mm) =>
		_precisionMode == PrecisionMode.Kun ? MmToKunUnit(mm) : MmToStandardUnit(mm);

	internal static ushort MmToHighPrecisionUnit(float mm) => (ushort)Math.Clamp((int)Math.Round(mm * 200, MidpointRounding.AwayFromZero), 0, 65535);

	private void SetKeyPointUniform(float depthMm,
		Func<byte, ReadOnlySpan<byte>, byte[]> buildStandard,
		Func<int, ReadOnlySpan<byte>, byte[]> buildHighPrecision)
	{
		if (_precisionMode == PrecisionMode.HighPrecision)
		{
			var raw = new ushort[HpKeyCount];
			Array.Fill(raw, MmToHighPrecisionUnit(depthMm));
			WriteKeyPointHighPrecision(raw, buildHighPrecision);
		}
		else
		{
			var raw = new byte[KeyCount];
			Array.Fill(raw, MmToB6Unit(depthMm));
			WriteKeyPointStandard(raw, buildStandard);
		}
	}

	private void SetKeyPointPerKey(ReadOnlySpan<float> depthsMm,
		Func<byte, ReadOnlySpan<byte>, byte[]> buildStandard,
		Func<int, ReadOnlySpan<byte>, byte[]> buildHighPrecision)
	{
		int expectedCount = _precisionMode == PrecisionMode.HighPrecision ? HpKeyCount : KeyCount;
		if (depthsMm.Length != expectedCount)
			throw new ArgumentException(
				$"Expected {expectedCount} depth values for this model, got {depthsMm.Length}.",
				nameof(depthsMm));

		for (int i = 0; i < depthsMm.Length; i++)
			ValidateDepthMm(depthsMm[i], i);

		if (_precisionMode == PrecisionMode.HighPrecision)
		{
			var raw = new ushort[HpKeyCount];
			for (int i = 0; i < HpKeyCount; i++)
				raw[i] = MmToHighPrecisionUnit(depthsMm[i]);
			WriteKeyPointHighPrecision(raw, buildHighPrecision);
		}
		else
		{
			var raw = new byte[KeyCount];
			for (int i = 0; i < KeyCount; i++)
				raw[i] = MmToB6Unit(depthsMm[i]);
			WriteKeyPointStandard(raw, buildStandard);
		}
	}

	private float[] ReadKeyPointMm(
		byte[] request,
		Func<ReadOnlySpan<byte>, bool> matches,
		Func<ReadOnlySpan<byte>, byte> getSection,
		Func<ReadOnlySpan<byte>, ReadOnlySpan<byte>> getValues)
	{
		EnsureNotPolling();
		var raw = ReadKeyPointRawHighPrecision(request, matches, getSection, getValues);
		var mm = new float[HpKeyCount];
		for (int i = 0; i < HpKeyCount; i++)
			mm[i] = raw[i] / HeightScale;
		return mm;
	}

	internal void WriteKeyPointStandard(ReadOnlySpan<byte> values,
		Func<byte, ReadOnlySpan<byte>, byte[]> build)
	{
		var normalized = new byte[KeyCount];
		values.Slice(0, Math.Min(values.Length, KeyCount)).CopyTo(normalized);

		for (int pkt = 0; pkt < 3; pkt++)
		{
			var slice = normalized.AsSpan(PacketBase[pkt], PacketCount[pkt]);
			var req = build((byte)pkt, slice);
			var resp = _connection.SendAndReceive(req);
			if (resp is null || !WriteKeyPointAcknowledgeStandard.Matches(resp))
				throw new InvalidOperationException(
					$"No ACK for standard key-point write packet {pkt}.");
		}
	}

	internal void WriteKeyPointHighPrecision(ReadOnlySpan<ushort> values,
		Func<int, ReadOnlySpan<byte>, byte[]> build)
	{
		var normalized = new ushort[HpKeyCount];
		values.Slice(0, Math.Min(values.Length, HpKeyCount)).CopyTo(normalized);
		var sectionBytes = new byte[60];

		// WriteKeyPointAcknowledgeHighPrecision.Matches accepts any 0xFD packet, which collides
		// with 0xFD 0x06 travel frames (TRN-4). If polling just stopped, a straggler travel
		// packet can still be sitting in the read buffer and get mistaken for this write's ACK,
		// letting the loop believe the write succeeded while the real ACK (or failure) is never
		// actually observed. Flush before the first send to clear anything left over.
		_connection.FlushReadBuffer();

		for (int sec = 0; sec < 5; sec++)
		{
			int baseKey = HpSectionBase[sec];
			int count = HpSectionSizes[sec];
			Array.Clear(sectionBytes, 0, sectionBytes.Length);
			for (int i = 0; i < count; i++)
				BinaryPrimitives.WriteUInt16LittleEndian(sectionBytes.AsSpan(i * 2), normalized[baseKey + i]);

			var req = build(sec, sectionBytes);
			var resp = _connection.SendAndReceive(req);
			if (resp is null || !WriteKeyPointAcknowledgeHighPrecision.Matches(resp))
				throw new InvalidOperationException(
					$"No ACK for high-precision key-point write section {sec}.");
		}
	}

	internal ushort[] ReadKeyPointRawHighPrecision(
		byte[] request,
		Func<ReadOnlySpan<byte>, bool> matches,
		Func<ReadOnlySpan<byte>, byte> getSection,
		Func<ReadOnlySpan<byte>, ReadOnlySpan<byte>> getValues)
	{
		var result = new ushort[HpKeyCount];
		var received = new bool[5];
		int got = 0;

		_connection.Send(request);

		int retries = 0;
		while (got < 5 && retries < 20)
		{
			var resp = _connection.ReceiveCommand(500);
			if (resp is null) { retries++; continue; }
			if (!matches(resp)) { retries++; continue; }

			int sec = getSection(resp);
			if ((uint)sec >= 5) { retries++; continue; }
			if (received[sec]) continue;

			var vals = getValues(resp);
			int baseKey = HpSectionBase[sec];
			int count = HpSectionSizes[sec];
			for (int i = 0; i < count; i++)
				result[baseKey + i] = BinaryPrimitives.ReadUInt16LittleEndian(vals.Slice(i * 2));

			received[sec] = true;
			got++;
		}

		if (got < 5)
			throw new InvalidOperationException(
				$"Incomplete high-precision key-point read: only {got}/5 sections received.");

		return result;
	}

	/// <summary>
	/// Sets per-key RGB lighting by invoking <paramref name="colorForKey"/> once per
	/// LED key. The callback receives the layout grid index (0-based key grid position)
	/// and returns <c>(R, G, B)</c> for that key. Use <see cref="GetKeyIndex"/> to
	/// map a <see cref="DDKey"/> to a grid index inside the callback.
	/// </summary>
	/// <param name="colorForKey">
	/// Callback that maps a grid index to an RGB colour. Called once per physical LED key.
	/// </param>
	/// <param name="brightness">
	/// Firmware brightness level (0-9, default 9 = maximum).
	/// </param>
	/// <example>
	/// <code>
	/// // Solid red at full brightness
	/// session.SetLighting(_ => (255, 0, 0));
	///
	/// // Highlight WASD in blue, everything else off
	/// var wasd = new[] { DDKey.W, DDKey.A, DDKey.S, DDKey.D }
	///     .Select(k => session.GetKeyIndex(k)).ToHashSet();
	/// session.SetLighting(gridIdx => wasd.Contains(gridIdx) ? (0, 0, 255) : (0, 0, 0));
	/// </code>
	/// </example>
	public void SetLighting(Func<int, (byte R, byte G, byte B)> colorForKey, [Range(0, 9)] byte brightness = 9)
	{
		EnsureNotPolling();
		for (int i = 0; i < _rgbIndices.Length; i++)
		{
			int gridPos = _rgbIndices[i];
			var (r, g, b) = colorForKey(gridPos);
			_rgbProfile[gridPos] = (r, g, b);
		}
		SendLightingPackets(BuildEntriesFromProfile(), brightness);
	}

	/// <summary>Sets every key to the same RGB colour.</summary>
	/// <param name="r">Red channel (0-255).</param>
	/// <param name="g">Green channel (0-255).</param>
	/// <param name="b">Blue channel (0-255).</param>
	/// <param name="brightness">Firmware brightness level (0-9, default 9 = maximum).</param>
	public void SetUniformLighting(byte r, byte g, byte b, [Range(0, 9)] byte brightness = 9)
	{
		EnsureNotPolling();
		SetLighting(_ => (r, g, b), brightness);
	}

	/// <inheritdoc cref="SetUniformLighting(byte, byte, byte, byte)"/>
	public void SetUniformLighting(RgbColor color, [Range(0, 9)] byte brightness = 9)
		=> SetUniformLighting(color.R, color.G, color.B, brightness);

	/// <summary>
	/// Sets the colour of a single key. All other keys keep their previously set colours.
	/// Turns on all RGB packets with the updated profile.
	/// </summary>
	/// <param name="key">The key to colour.</param>
	/// <param name="r">Red channel (0-255).</param>
	/// <param name="g">Green channel (0-255).</param>
	/// <param name="b">Blue channel (0-255).</param>
	/// <param name="brightness">Firmware brightness level (0-9, default 9 = maximum).</param>
	/// <exception cref="ArgumentException">Thrown when <paramref name="key"/> is not on this model.</exception>
	public void SetKeyColor(DDKey key, byte r, byte g, byte b, [Range(0, 9)] byte brightness = 9)
	{
		EnsureNotPolling();
		int gridIdx = GetKeyIndex(key);
		_rgbProfile[gridIdx] = (r, g, b);
		SendLightingPackets(BuildEntriesFromProfile(), brightness);
	}

	/// <inheritdoc cref="SetKeyColor(DDKey, byte, byte, byte, byte)"/>
	public void SetKeyColor(DDKey key, RgbColor color, [Range(0, 9)] byte brightness = 9)
		=> SetKeyColor(key, color.R, color.G, color.B, brightness);

	/// <summary>
	/// Sets the colour of one or more keys. All other keys keep their previously set colours.
	/// Keys not present on this model are silently skipped.
	/// </summary>
	/// <param name="r">Red channel (0-255).</param>
	/// <param name="g">Green channel (0-255).</param>
	/// <param name="b">Blue channel (0-255).</param>
	/// <param name="brightness">Firmware brightness level (0-9, default 9 = maximum).</param>
	/// <param name="keys">One or more keys to update.</param>
	public void SetKeyColor(byte r, byte g, byte b, [Range(0, 9)] byte brightness = 9, params DDKey[] keys)
	{
		EnsureNotPolling();
		if (keys.Length == 0)
			throw new ArgumentException("At least one key must be specified.", nameof(keys));
		foreach (var key in keys)
		{
			if (!TryGetKeyIndex(key, out int gridIdx))
			{
				_log.LogWarning("SetKeyColor: key {Key} not present on {Model}; skipped.", key, Model.Name);
				continue;
			}
			_rgbProfile[gridIdx] = (r, g, b);
		}
		SendLightingPackets(BuildEntriesFromProfile(), brightness);
	}

	/// <inheritdoc cref="SetKeyColor(byte, byte, byte, byte, DDKey[])"/>
	public void SetKeyColor(RgbColor color, [Range(0, 9)] byte brightness = 9, params DDKey[] keys)
		=> SetKeyColor(color.R, color.G, color.B, brightness, keys);

	/// <summary>
	/// Sets the colour of a single key by layout index. All other keys keep their previously
	/// set colours. Use <see cref="GetKeyIndex"/> to map a <see cref="DDKey"/> to an index.
	/// </summary>
	/// <param name="keyIndex">Layout index (0-based).</param>
	/// <param name="r">Red channel (0-255).</param>
	/// <param name="g">Green channel (0-255).</param>
	/// <param name="b">Blue channel (0-255).</param>
	/// <param name="brightness">Firmware brightness level (0-9, default 9 = maximum).</param>
	public void SetKeyColor(int keyIndex, byte r, byte g, byte b, [Range(0, 9)] byte brightness = 9)
	{
		EnsureNotPolling();
		if ((uint)keyIndex >= (uint)_rgbProfile.Length)
			throw new ArgumentOutOfRangeException(nameof(keyIndex),
				$"Key index {keyIndex} is out of range [0, {_rgbProfile.Length - 1}].");
		_rgbProfile[keyIndex] = (r, g, b);
		SendLightingPackets(BuildEntriesFromProfile(), brightness);
	}

	/// <inheritdoc cref="SetKeyColor(int, byte, byte, byte, byte)"/>
	public void SetKeyColor(int keyIndex, RgbColor color, [Range(0, 9)] byte brightness = 9)
		=> SetKeyColor(keyIndex, color.R, color.G, color.B, brightness);

	/// <summary>
	/// Activates a built-in firmware lighting animation by wire mode code.
	/// Takes effect immediately on all models (AE direct path, no FuncBlock required).
	/// </summary>
	/// <param name="modeCode">
	/// Firmware animation mode code. Use the <see cref="LightingMode"/> constants for
	/// known values. Modes that require per-key colour data (Turbo and Custom) must use
	/// <see cref="SetLighting"/> instead.
	/// </param>
	/// <param name="brightness">Brightness level 0–9. Default 9.</param>
	/// <param name="speed">Animation speed 0–9. Default 5.</param>
	public void SetLightingMode(LightingMode modeCode, [Range(0, 9)] byte brightness = 9, [Range(0, 9)] byte speed = 5)
	{
		EnsureNotPolling();
		ValidateBrightness(brightness);
		ValidateSpeed(speed);
		var resp = _connection.SendAndReceive(Protocol.SetLightingMode.Build(
			slot: 0, (byte)modeCode, brightness, speed, tail: 0));
		if (resp is null || !RgbAcknowledge.Matches(resp))
			throw new InvalidOperationException("No ACK for SetLightingMode.");
	}

	/// <summary>Turns off all key lighting.</summary>
	public void DisableLighting()
	{
		EnsureNotPolling();
		var resp = _connection.SendAndReceive(SetLightingOff.Build());
		if (resp is null || !RgbAcknowledge.Matches(resp))
			throw new InvalidOperationException("No ACK for SetLightingOff.");
	}

	/// <summary>
	/// Returns a snapshot of the current in-memory RGB profile, indexed by layout key index.
	/// This reflects the last colours sent via <see cref="SetLighting"/>, <see cref="SetKeyColor"/>,
	/// or <see cref="LoadLightingFromProfile"/>, not necessarily what the keyboard displays right now.
	/// </summary>
	public (byte R, byte G, byte B)[] GetCurrentColors()
	{
		var result = new (byte, byte, byte)[_rgbProfile.Length];
		_rgbProfile.CopyTo(result, 0);
		return result;
	}

	// Per-key stored color flash block - sub-cmd 0x0A (read) / 0x0B (write).
	// 512 bytes per profile at address 512 × profileIndex. First 384 bytes = 128 keys × 3 RGB.
	// Distinct from the live 0xAE push: this is the flash map used when effect = 0 (custom mode).
	private const int StoredColorStride = 512;
	private const int StoredColorKeyCount = 128;
	private const int StoredColorByteCount = StoredColorKeyCount * 3; // 384

	/// <summary>
	/// Reads the stored per-key RGB colours from the keyboard's flash for the specified profile.
	/// These are the colours the firmware uses when the lighting effect is set to custom (effect = 0).
	/// Returns 128 entries indexed by layout key index.
	/// </summary>
	/// <param name="profileIndex">Keyboard profile slot (0-based). Default: 0.</param>
	internal (byte R, byte G, byte B)[] ReadStoredColors(int profileIndex = 0)
	{
		EnsureNotPolling();
		ValidateProfileIndex(profileIndex);
		var raw = ReadExtendedGateway(0x0A, StoredColorStride * profileIndex, StoredColorByteCount);
		var result = new (byte, byte, byte)[StoredColorKeyCount];
		for (int i = 0; i < StoredColorKeyCount; i++)
			result[i] = (raw[i * 3], raw[i * 3 + 1], raw[i * 3 + 2]);
		return result;
	}

	/// <summary>
	/// Writes per-key RGB colours to the keyboard's flash for the specified profile.
	/// The keyboard uses these colours when the lighting effect is set to custom (effect = 0).
	/// Pass exactly 128 entries indexed by layout key index.
	/// </summary>
	/// <param name="colors">128 RGB entries indexed by layout key index.</param>
	/// <param name="profileIndex">Keyboard profile slot (0-based). Default: 0.</param>
	internal void WriteStoredColors((byte R, byte G, byte B)[] colors, int profileIndex = 0)
	{
		EnsureNotPolling();
		ValidateProfileIndex(profileIndex);
		if (colors.Length != StoredColorKeyCount)
			throw new ArgumentException(
				$"Expected {StoredColorKeyCount} color entries, got {colors.Length}.", nameof(colors));
		var raw = new byte[StoredColorByteCount];
		for (int i = 0; i < StoredColorKeyCount; i++)
		{
			raw[i * 3]     = colors[i].R;
			raw[i * 3 + 1] = colors[i].G;
			raw[i * 3 + 2] = colors[i].B;
		}
		WriteExtendedGateway(0x0B, StoredColorStride * profileIndex, raw);
	}

	/// <summary>
	/// Saves the current in-memory colour profile (the colours last set via
	/// <see cref="SetLighting"/> or <see cref="SetKeyColor"/>) to the keyboard's flash
	/// for the specified profile slot. After saving, the colours persist on the keyboard
	/// even after it is disconnected, and will be displayed when the lighting effect is
	/// set to custom (effect = 0) via <see cref="SetLightCustom"/>.
	/// </summary>
	/// <param name="profileIndex">Keyboard profile slot (0-based). Default: 0.</param>
	internal void SaveLightingToProfile(int profileIndex = 0)
	{
		EnsureNotPolling();
		ValidateProfileIndex(profileIndex);
		var raw = new byte[StoredColorByteCount];
		for (int i = 0; i < StoredColorKeyCount; i++)
		{
			var (r, g, b) = _rgbProfile[i];
			raw[i * 3]     = r;
			raw[i * 3 + 1] = g;
			raw[i * 3 + 2] = b;
		}
		WriteExtendedGateway(0x0B, StoredColorStride * profileIndex, raw);
	}

	/// <summary>
	/// Loads the stored per-key colours for the specified profile from flash into the
	/// in-memory profile and applies them live to the keyboard.
	/// </summary>
	/// <param name="profileIndex">Keyboard profile slot (0-based). Default: 0.</param>
	/// <param name="brightness">Firmware brightness level (0-9, default 9 = maximum).</param>
	internal void LoadLightingFromProfile(int profileIndex = 0, [Range(0, 9)] byte brightness = 9)
	{
		EnsureNotPolling();
		ValidateProfileIndex(profileIndex);
		var raw = ReadExtendedGateway(0x0A, StoredColorStride * profileIndex, StoredColorByteCount);
		for (int i = 0; i < StoredColorKeyCount; i++)
			_rgbProfile[i] = (raw[i * 3], raw[i * 3 + 1], raw[i * 3 + 2]);
		SendLightingPackets(BuildEntriesFromProfile(), brightness);
	}

	/// <summary>
	/// Reads the currently displayed per-key RGB colours from the keyboard regardless of
	/// the active lighting effect mode. Returns 128 entries indexed by layout key index.
	/// </summary>
	internal (byte R, byte G, byte B)[] ReadLiveColors()
	{
		EnsureNotPolling();
		var raw = ReadExtendedGateway(0xDE, 0, StoredColorByteCount);
		var result = new (byte, byte, byte)[StoredColorKeyCount];
		for (int i = 0; i < StoredColorKeyCount; i++)
			result[i] = (raw[i * 3], raw[i * 3 + 1], raw[i * 3 + 2]);
		return result;
	}

	private RgbEntry[] BuildEntriesFromProfile()
	{
		var entries = new RgbEntry[_rgbIndices.Length];
		for (int i = 0; i < _rgbIndices.Length; i++)
		{
			int gridPos = _rgbIndices[i];
			var (r, g, b) = _rgbProfile[gridPos];
			entries[i]    = RgbEntry.Create(gridPos, r, g, b);
		}
		return entries;
	}

	private void SendLightingPackets(ReadOnlySpan<RgbEntry> entries, byte brightness)
	{
		ValidateBrightness(brightness);
		const int EntriesPerPacket = 13;
		int packetCount = (entries.Length + EntriesPerPacket - 1) / EntriesPerPacket;

		for (int pkt = 0; pkt < packetCount; pkt++)
		{
			var buf = RgbKeyDataPacket.Build(isTurbo: 0, modeIndex: 0x13, brightness);

			int entryBase = pkt * EntriesPerPacket;
			int writeOffset = 8;

			for (int i = 0; i < EntriesPerPacket && entryBase + i < entries.Length; i++)
			{
				entries[entryBase + i].Write(buf.AsSpan(writeOffset));
				writeOffset += RgbEntry.ByteSize;
			}

			if (writeOffset < 64)
				buf[writeOffset] = 0xFF;

			var resp = _connection.SendAndReceive(buf);
			if (resp is null || !RgbAcknowledge.Matches(resp))
				throw new InvalidOperationException($"No ACK for RGB packet {pkt}.");
		}
	}

	/// <summary>
	/// Sends a CommonConfig (0xB5) packet that controls global Turbo, Rapid Trigger,
	/// Last Win / Rapid Trigger mode, and Rapid Trigger Auto Match in a single command.
	/// </summary>
	/// <param name="turboMode">Enable or disable Turbo mode globally.</param>
	/// <param name="rapidTriggerMode">Enable or disable Rapid Trigger globally.</param>
	/// <param name="lastWinRapidTriggerMode">Which combination of Last Win and Rapid Trigger is active.</param>
	/// <param name="rapidTriggerAutoMatch">
	/// When <see langword="true"/>, the Rapid Trigger release threshold automatically
	/// mirrors the press threshold (Auto Match).
	/// </param>
	public void SetCommonConfig(bool turboMode, bool rapidTriggerMode, LastWinRapidTriggerMode lastWinRapidTriggerMode, bool rapidTriggerAutoMatch)
	{
		EnsureNotPolling();
		SendCommonConfig(turboMode, rapidTriggerMode, lastWinRapidTriggerMode, rapidTriggerAutoMatch);
	}

	/// <summary>
	/// Sets which combination of Last Win and Rapid Trigger is active.
	/// </summary>
	public void SetLastWinRapidTriggerMode(LastWinRapidTriggerMode mode)
	{
		EnsureNotPolling();
		_lastWinRtMode = mode;
		_connection.Send(Protocol.SetLastWinRapidTriggerMode.Build((byte)mode));
	}

	/// <summary>Enables or disables the Last Win Replace feature.</summary>
	public void ConfigureLastWinReplace(bool enabled)
	{
		EnsureNotPolling();
		_connection.Send(SetLastWinReplace.Build(enabled ? (byte)1 : (byte)0));
	}

	/// <summary>
	/// Enables Rapid Trigger Auto Match: the release threshold automatically mirrors the press threshold.
	/// </summary>
	/// <param name="sensitivity">Firmware sensitivity level (1–254). Lower values are more sensitive. Default: 1.</param>
	public void EnableAutoMatch([Range(1, 254)] byte sensitivity = 1)
	{
		if (sensitivity is < 1 or > 254)
			throw new ArgumentOutOfRangeException(nameof(sensitivity), sensitivity,
				"Sensitivity must be 1–254.");
		EnsureNotPolling();
		_connection.Send(SetAutoMatchMode.Build(sensitivity));
		// Keep the mirror in sync: any subsequent SendCommonConfig() call (from EnableRapidTrigger,
		// DisableRapidTrigger, Enable/DisableTurboMode, ...) rebuilds its B5 packet from this flag,
		// and would otherwise silently revert auto-match back off on the keyboard.
		_rapidTriggerAutoMatch = true;
	}

	/// <summary>Disables Rapid Trigger Auto Match.</summary>
	public void DisableAutoMatch()
	{
		EnsureNotPolling();
		_connection.Send(SetAutoMatchMode.Build(255));
		_rapidTriggerAutoMatch = false;
	}

	/// <summary>
	/// Programs the Last Win pair table using raw layout indices.
	/// Each pair links two keys that compete under Last Win - whichever was pressed most recently wins.
	/// Pass each pair as <c>(keyA, keyB)</c> using layout indices (0-125). Maximum 14 pairs.
	/// </summary>
	/// <example>
	/// <code>session.ConfigureLastWinPairs((16, 26), (17, 27));</code>
	/// </example>
	public void ConfigureLastWinPairs(params (int keyA, int keyB)[] pairs)
	{
		EnsureNotPolling();
		if (pairs.Length > 14)
			throw new ArgumentException(
				$"Cannot configure {pairs.Length} last win pairs. The maximum allowed is 14.", nameof(pairs));

		foreach (var (keyA, keyB) in pairs)
		{
			if ((uint)keyA >= KeyMapKeyCount)
				throw new ArgumentOutOfRangeException(nameof(pairs),
					$"Layout index {keyA} must be in [0, {KeyMapKeyCount - 1}].");
			if ((uint)keyB >= KeyMapKeyCount)
				throw new ArgumentOutOfRangeException(nameof(pairs),
					$"Layout index {keyB} must be in [0, {KeyMapKeyCount - 1}].");
		}

		var buf = CreateLwPairs.Build((byte)pairs.Length);
		int offset = 4;
		foreach (var (keyA, keyB) in pairs)
		{
			var entry = new LastWinPairEntry((byte)keyA, (byte)keyB);
			entry.Write(buf.AsSpan(offset));
			offset += LastWinPairEntry.ByteSize;
		}
		_connection.Send(buf);
	}

	/// <summary>
	/// Programs the Last Win pair table using named keys.
	/// Each pair links two keys that compete under Last Win - whichever was pressed most recently wins.
	/// Maximum 14 pairs. Keys not present on this model throw <see cref="ArgumentException"/>.
	/// </summary>
	/// <example>
	/// <code>session.ConfigureLastWinPairs((DDKey.A, DDKey.D), (DDKey.Q, DDKey.E));</code>
	/// </example>
	public void ConfigureLastWinPairs(params (DDKey keyA, DDKey keyB)[] pairs)
	{
		EnsureNotPolling();
		if (pairs.Length > 14)
			throw new ArgumentException(
				$"Cannot configure {pairs.Length} last win pairs. The maximum allowed is 14.", nameof(pairs));

		var rawPairs = new (int, int)[pairs.Length];
		for (int i = 0; i < pairs.Length; i++)
			rawPairs[i] = (GetKeyIndex(pairs[i].keyA), GetKeyIndex(pairs[i].keyB));

		ConfigureLastWinPairs(rawPairs);
	}

	/// <inheritdoc cref="ConfigureLastWinPairs(ValueTuple{DDKey, DDKey}[])"/>
	/// <example>
	/// <code>session.ConfigureLastWinPairs(new LastWinPair(DDKey.A, DDKey.D), new LastWinPair(DDKey.W, DDKey.S));</code>
	/// </example>
	public void ConfigureLastWinPairs(params LastWinPair[] pairs)
	{
		EnsureNotPolling();
		if (pairs.Length > 14)
			throw new ArgumentException(
				$"Cannot configure {pairs.Length} last win pairs. The maximum allowed is 14.", nameof(pairs));

		var rawPairs = new (int, int)[pairs.Length];
		for (int i = 0; i < pairs.Length; i++)
			rawPairs[i] = (GetKeyIndex(pairs[i].A), GetKeyIndex(pairs[i].B));

		ConfigureLastWinPairs(rawPairs);
	}

	/// <summary>Enables Rapid Trigger globally.</summary>
	/// <param name="autoMatch">
	/// When <see langword="true"/>, the Rapid Trigger release threshold automatically mirrors
	/// the press threshold (Auto Match mode).
	/// </param>
	public void EnableRapidTrigger(bool autoMatch = false)
	{
		EnsureNotPolling();
		SendCommonConfig(_turboEnabled, true, _lastWinRtMode, autoMatch);
	}

	/// <summary>Disables Rapid Trigger globally.</summary>
	public void DisableRapidTrigger()
	{
		EnsureNotPolling();
		SendCommonConfig(_turboEnabled, false, _lastWinRtMode, _rapidTriggerAutoMatch);
	}

	/// <summary>Enables Turbo mode globally.</summary>
	public void EnableTurboMode()
	{
		EnsureNotPolling();
		SendCommonConfig(true, _rapidTriggerEnabled, _lastWinRtMode, _rapidTriggerAutoMatch);
		if (HasTurboMode) { var b = FetchFuncBlock(); b.TurboMode = true;  PushFuncBlock(b); }
	}

	/// <summary>Disables Turbo mode globally.</summary>
	public void DisableTurboMode()
	{
		EnsureNotPolling();
		SendCommonConfig(false, _rapidTriggerEnabled, _lastWinRtMode, _rapidTriggerAutoMatch);
		if (HasTurboMode) { var b = FetchFuncBlock(); b.TurboMode = false; PushFuncBlock(b); }
	}

	/// <summary>
	/// Applies a <see cref="KeyboardProfile"/> to the keyboard. Only non-<see langword="null"/>
	/// fields are written. When <see cref="KeyboardProfile.IsThemeOnly"/> is <see langword="true"/>
	/// only RGB lighting is updated.
	/// </summary>
	public void ApplyProfile(KeyboardProfile profile)
	{
		EnsureNotPolling();

		if (!profile.IsThemeOnly)
		{
			if (profile.Actuation != null)
				SetActuationPoint(BuildDepthArray(profile.Actuation, _actuationProfile));

			if (profile.Downstroke != null)
				SetDownstrokePoint(BuildDepthArray(profile.Downstroke, _downstrokeProfile));

			if (profile.Upstroke != null)
				SetUpstrokePoint(BuildDepthArray(profile.Upstroke, _upstrokeProfile));

			if (profile.RapidTrigger.HasValue)
			{
				SendCommonConfig(_turboEnabled, profile.RapidTrigger.Value, _lastWinRtMode,
					profile.RapidTriggerAutoMatch ?? _rapidTriggerAutoMatch);
			}
			else if (profile.RapidTriggerAutoMatch.HasValue)
			{
				SendCommonConfig(_turboEnabled, _rapidTriggerEnabled, _lastWinRtMode,
					profile.RapidTriggerAutoMatch.Value);
			}

			if (profile.TurboMode.HasValue)
			{
				SendCommonConfig(profile.TurboMode.Value, _rapidTriggerEnabled, _lastWinRtMode,
					_rapidTriggerAutoMatch);
			}
		}

		if (profile.Theme != null)
			ApplyTheme(profile.Theme);
	}

	private void ApplyTheme(KeyboardTheme theme)
	{
		var (br, bg, bb) = theme.BaseColor;
		for (int i = 0; i < _rgbIndices.Length; i++)
			_rgbProfile[_rgbIndices[i]] = (br, bg, bb);

		if (theme.Keys != null)
		{
			foreach (var (name, keyColor) in theme.Keys)
			{
				if (!Enum.TryParse<DDKey>(name, ignoreCase: true, out var key) || !TryGetKeyIndex(key, out int gridIdx))
				{
					_log.LogWarning("ApplyTheme: unknown key '{Key}'; skipped.", name);
					continue;
				}
				_rgbProfile[gridIdx] = (keyColor.R, keyColor.G, keyColor.B);
			}
		}

		SendLightingPackets(BuildEntriesFromProfile(), theme.Brightness);
	}

	/// <summary>
	/// Sends a CommonConfig (0xB5) packet built from the given values and, only once the
	/// keyboard ACKs it, commits those values to the session's mirrors. Building the packet from
	/// parameters (not the mirror fields directly) means a failed/timed-out send leaves the
	/// mirrors matching what the keyboard actually has.
	/// </summary>
	private void SendCommonConfig(bool turboMode, bool rapidTriggerMode,
		LastWinRapidTriggerMode lastWinRapidTriggerMode, bool rapidTriggerAutoMatch)
	{
		var resp = _connection.SendAndReceive(CommonConfig.Build(
			turboMode: turboMode ? (byte)1 : (byte)0,
			rapidTriggerMode: rapidTriggerMode ? (byte)1 : (byte)0,
			lastWinRapidTriggerMode: (byte)lastWinRapidTriggerMode,
			rapidTriggerAutoMatch: rapidTriggerAutoMatch ? (byte)1 : (byte)0));
		if (resp is null || !CommonConfigAcknowledge.Matches(resp))
			throw new InvalidOperationException("No ACK for CommonConfig.");

		_turboEnabled          = turboMode;
		_rapidTriggerEnabled   = rapidTriggerMode;
		_lastWinRtMode         = lastWinRapidTriggerMode;
		_rapidTriggerAutoMatch = rapidTriggerAutoMatch;
	}

	/// <summary>Switches the keyboard between Windows and Mac compatibility modes.</summary>
	internal void SetKeyboardMode(KeyboardMode mode)
	{
		EnsureNotPolling();
		var block = FetchFuncBlock();
		block.MacMode = (byte)mode;
		PushFuncBlock(block);
	}

	/// <summary>
	/// Sets the USB polling rate. Higher rates reduce input latency at the cost of
	/// marginally more host CPU time.
	/// </summary>
	internal void SetReportRate(ReportRate rate)
	{
		EnsureNotPolling();
		var block = FetchFuncBlock();
		block.ReportRate = rate;
		PushFuncBlock(block);
	}

	/// <summary>
	/// Sets the debounce level (0-7). Higher values add a longer settle window before
	/// a key event is registered, reducing chatter from worn or noisy switches.
	/// 0 = no added debounce.
	/// </summary>
	internal void SetDebounce(byte level)
	{
		if (level > 7)
			throw new ArgumentOutOfRangeException(nameof(level), "Debounce level must be 0-7.");
		EnsureNotPolling();
		var block = FetchFuncBlock();
		block.Debounce = level;
		PushFuncBlock(block);
	}

	/// <summary>
	/// Sets the contact stability mode level (0-3). Higher values increase stabilisation
	/// for rattling or bouncy switches. 0 = off.
	/// </summary>
	internal void SetStabilityMode(byte level)
	{
		if (level > 3)
			throw new ArgumentOutOfRangeException(nameof(level), "Stability mode must be 0-3.");
		EnsureNotPolling();
		var block = FetchFuncBlock();
		block.StabilityMode = level;
		PushFuncBlock(block);
	}

	/// <summary>
	/// Configures key-combination locks. Pass <see langword="null"/> for any parameter to
	/// leave that lock unchanged.
	/// </summary>
	/// <param name="winLock">Suppress the Windows key while gaming.</param>
	/// <param name="altTabLock">Suppress Alt+Tab.</param>
	/// <param name="altF4Lock">Suppress Alt+F4.</param>
	/// <example>
	/// <code>
	/// // Lock Win key, leave Alt combinations unchanged
	/// session.ConfigureKeyLocks(winLock: true);
	///
	/// // Unlock everything
	/// session.ConfigureKeyLocks(winLock: false, altTabLock: false, altF4Lock: false);
	/// </code>
	/// </example>
	internal void ConfigureKeyLocks(bool? winLock = null, bool? altTabLock = null, bool? altF4Lock = null)
	{
		EnsureNotPolling();
		var block = FetchFuncBlock();
		if (winLock.HasValue) block.WinLock    = winLock.Value;
		if (altTabLock.HasValue) block.AltTabLock = altTabLock.Value;
		if (altF4Lock.HasValue) block.AltF4Lock  = altF4Lock.Value;
		PushFuncBlock(block);
	}

	/// <summary>
	/// Activates a built-in firmware lighting animation. Use <see cref="SetLightCustom"/>
	/// to switch back to per-key RGB set via <see cref="SetLighting"/>.
	/// </summary>
	/// <param name="effect">Firmware animation preset. <see cref="LightPreset.Custom"/> switches back to per-key RGB.</param>
	/// <param name="brightness">Brightness 0-9. Default 9.</param>
	/// <param name="speed">Animation speed 0-9. Default 5.</param>
	internal void SetLightPreset(LightPreset effect, [Range(0, 9)] byte brightness = 9, [Range(0, 9)] byte speed = 5)
	{
		EnsureNotPolling();
		ValidateBrightness(brightness);
		ValidateSpeed(speed);
		var block = FetchFuncBlock();
		block.LightEffect     = (byte)effect;
		block.LightBrightness = brightness;
		block.LightSpeed      = speed;
		PushFuncBlock(block);
	}

	/// <summary>
	/// Switches lighting back to custom RGB mode so colours set via
	/// <see cref="SetLighting"/> or <see cref="SetKeyColor"/> take effect.
	/// </summary>
	internal void SetLightCustom()
	{
		EnsureNotPolling();
		var block = FetchFuncBlock();
		block.LightEffect = 0;
		PushFuncBlock(block);
	}

	/// <summary>
	/// Sets the single-colour tint used by the main key preset lighting effect.
	/// Automatically enables <see cref="KeyboardFuncBlock.LightSingleColor"/>.
	/// Has no effect when the effect index is 0 (custom RGB mode).
	/// </summary>
	internal void SetLightPresetColor(byte r, byte g, byte b)
	{
		EnsureNotPolling();
		var block = FetchFuncBlock();
		block.LightSingleColor = true;
		block.LightColorR = r;
		block.LightColorG = g;
		block.LightColorB = b;
		PushFuncBlock(block);
	}

	internal void SetLightPresetColor(RgbColor color) => SetLightPresetColor(color.R, color.G, color.B);

	/// <summary>
	/// Sets the firmware-internal sensor sampling tick rate (bits 4-7 of FuncBlock byte 4).
	/// This controls how frequently the firmware polls sensors for rapid-trigger evaluation.
	/// Values 0-15; higher = more frequent sampling.
	/// </summary>
	internal void SetTickRate(byte rate)
	{
		if (rate > 15)
			throw new ArgumentOutOfRangeException(nameof(rate), rate,
				"Tick rate must be 0–15.");
		EnsureNotPolling();
		var block = FetchFuncBlock();
		block.TickRate = rate;
		PushFuncBlock(block);
	}

	/// <summary>
	/// Activates a built-in firmware lighting animation on the logo light zone.
	/// Only supported on models with a dedicated logo LED (see <see cref="HasLogoLight"/>).
	/// </summary>
	/// <param name="effect">Firmware animation preset. <see cref="LightPreset.Custom"/> turns the logo light off.</param>
	/// <param name="brightness">Brightness 0-9. Default 9.</param>
	/// <param name="speed">Animation speed 0-9. Default 5.</param>
	/// <exception cref="NotSupportedException">Thrown when the connected model has no logo LED zone.</exception>
	internal void SetLogoLightPreset(LightPreset effect, [Range(0, 9)] byte brightness = 9, [Range(0, 9)] byte speed = 5)
	{
		EnsureNotPolling();
		ValidateBrightness(brightness);
		ValidateSpeed(speed);
		EnsureHasLogoLight();
		var block = FetchFuncBlock();
		block.LogoLightEffect     = (byte)effect;
		block.LogoLightBrightness = brightness;
		block.LogoLightSpeed      = speed;
		PushFuncBlock(block);
	}

	/// <summary>
	/// Turns off the logo light zone.
	/// Only supported on models with a dedicated logo LED (see <see cref="HasLogoLight"/>).
	/// </summary>
	/// <exception cref="NotSupportedException">Thrown when the connected model has no logo LED zone.</exception>
	internal void SetLogoLightOff()
	{
		EnsureNotPolling();
		EnsureHasLogoLight();
		var block = FetchFuncBlock();
		block.LogoLightEffect = 0;
		PushFuncBlock(block);
	}

	/// <summary>
	/// Sets the single-colour tint used by the logo light preset effect.
	/// Automatically enables <see cref="KeyboardFuncBlock.LogoLightSingleColor"/>.
	/// Only supported on models with a dedicated logo LED (see <see cref="HasLogoLight"/>).
	/// </summary>
	/// <exception cref="NotSupportedException">Thrown when the connected model has no logo LED zone.</exception>
	internal void SetLogoLightColor(byte r, byte g, byte b)
	{
		EnsureNotPolling();
		EnsureHasLogoLight();
		var block = FetchFuncBlock();
		block.LogoLightSingleColor = true;
		block.LogoLightColorR = r;
		block.LogoLightColorG = g;
		block.LogoLightColorB = b;
		PushFuncBlock(block);
	}

	internal void SetLogoLightColor(RgbColor color) => SetLogoLightColor(color.R, color.G, color.B);

	/// <summary>
	/// Activates a built-in firmware lighting animation on the side light zone.
	/// Only supported on models with a dedicated side LED strip (see <see cref="HasSideLight"/>).
	/// </summary>
	/// <param name="effect">Firmware animation preset. <see cref="LightPreset.Custom"/> turns the side light off.</param>
	/// <param name="brightness">Brightness 0-9. Default 9.</param>
	/// <param name="speed">Animation speed 0-9. Default 5.</param>
	/// <exception cref="NotSupportedException">Thrown when the connected model has no side LED zone.</exception>
	internal void SetSideLightPreset(LightPreset effect, [Range(0, 9)] byte brightness = 9, [Range(0, 9)] byte speed = 5)
	{
		EnsureNotPolling();
		ValidateBrightness(brightness);
		ValidateSpeed(speed);
		EnsureHasSideLight();
		var block = FetchFuncBlock();
		block.SideLightEffect     = (byte)effect;
		block.SideLightBrightness = brightness;
		block.SideLightSpeed      = speed;
		PushFuncBlock(block);
	}

	/// <summary>
	/// Turns off the side light zone.
	/// Only supported on models with a dedicated side LED strip (see <see cref="HasSideLight"/>).
	/// </summary>
	/// <exception cref="NotSupportedException">Thrown when the connected model has no side LED zone.</exception>
	internal void SetSideLightOff()
	{
		EnsureNotPolling();
		EnsureHasSideLight();
		var block = FetchFuncBlock();
		block.SideLightEffect = 0;
		PushFuncBlock(block);
	}

	/// <summary>
	/// Sets the single-colour tint used by the side light preset effect.
	/// Automatically enables <see cref="KeyboardFuncBlock.SideLightSingleColor"/>.
	/// Only supported on models with a dedicated side LED strip (see <see cref="HasSideLight"/>).
	/// </summary>
	/// <exception cref="NotSupportedException">Thrown when the connected model has no side LED zone.</exception>
	internal void SetSideLightColor(byte r, byte g, byte b)
	{
		EnsureNotPolling();
		EnsureHasSideLight();
		var block = FetchFuncBlock();
		block.SideLightSingleColor = true;
		block.SideLightColorR = r;
		block.SideLightColorG = g;
		block.SideLightColorB = b;
		PushFuncBlock(block);
	}

	internal void SetSideLightColor(RgbColor color) => SetSideLightColor(color.R, color.G, color.B);

	//
	// All 0x55 operations share the same envelope:
	//   Read:  [0x55, sub_cmd, 0x00, cs, len, addr_lo, addr_hi]
	//   Write: [0x55, sub_cmd, 0x00, cs, len, addr_lo, addr_hi, is_last, data...]
	//   Read  checksum = (addr_lo + addr_hi + len)                   & 0xFF
	//   Write checksum = (len + addr_lo + addr_hi + is_last + Σdata) & 0xFF

	// PROTO-1: the reply's echoed sub-command/address/length (bytes 1-7 of the 0xAA response,
	// currently unread/treated as reserved) are never compared against what was requested, so a
	// stale or reordered gateway response would reassemble into the wrong offset of `result`
	// with no error - subtle profile-data corruption on read. Fixing this requires a capture of
	// the real echo layout (which fields are at which offsets) to add to ExtendedGatewayResponse
	// in the protocol YAML and validate per chunk; left as unverified/undone here.
	private byte[] ReadExtendedGateway(byte subCmd, int baseAddr, int totalBytes)
	{
		EnsureHasFuncBlock();

		var result = new byte[totalBytes];
		int offset = 0, chunk = 0;
		while (offset < totalBytes)
		{
			int len = Math.Min(56, totalBytes - offset);
			ushort addr = checked((ushort)(baseAddr + offset));
			byte cs = (byte)((addr & 0xFF) + (addr >> 8) + len);
			var req = new byte[64];
			req[0] = 0x55; req[1] = subCmd; req[2] = 0x00;
			req[3] = cs; req[4] = (byte)len;
			BinaryPrimitives.WriteUInt16LittleEndian(req.AsSpan(5), addr);
			var resp = _connection.SendAndReceive(req);
			if (resp is null || !ExtendedGatewayResponse.Matches(resp))
				throw new InvalidOperationException(
					$"No response for 0x55/0x{subCmd:X2} chunk {chunk} (addr=0x{addr:X4}).");
			ExtendedGatewayResponse.GetData(resp)[..len].CopyTo(result.AsSpan(offset));
			offset += len;
			chunk++;
		}
		return result;
	}

	private void WriteExtendedGateway(byte subCmd, int baseAddr, ReadOnlySpan<byte> data)
	{
		EnsureHasFuncBlock();

		int offset = 0, chunk = 0;
		while (offset < data.Length)
		{
			int len = Math.Min(56, data.Length - offset);
			ushort addr = checked((ushort)(baseAddr + offset));
			byte isLast = (offset + len >= data.Length) ? (byte)1 : (byte)0;
			var slice = data.Slice(offset, len);
			byte cs = (byte)(len + (addr & 0xFF) + (addr >> 8) + isLast + SumBytes(slice));
			var req = new byte[64];
			req[0] = 0x55; req[1] = subCmd; req[2] = 0x00;
			req[3] = cs; req[4] = (byte)len;
			BinaryPrimitives.WriteUInt16LittleEndian(req.AsSpan(5), addr);
			req[7] = isLast;
			slice.CopyTo(req.AsSpan(8));
			var resp = _connection.SendAndReceive(req);
			if (resp is null || !ExtendedGatewayResponse.Matches(resp))
				throw new InvalidOperationException(
					$"No ACK for 0x55/0x{subCmd:X2} chunk {chunk} (addr=0x{addr:X4}).");
			offset += len;
			chunk++;
		}
	}

	private static void ValidateBrightness(byte brightness)
	{
		if (brightness > 9)
			throw new ArgumentOutOfRangeException(nameof(brightness), brightness,
				"Brightness must be 0–9.");
	}

	// PROTO-3: the YAML documents the wire range for light_speed/logo_light_speed/
	// side_light_speed as raw 0-4, *inverted* (0 = fastest, 4 = slowest), but this validates and
	// passes through 0-9 unchanged - so speed: 9 writes an out-of-range raw byte, and higher
	// "faster" numbers actually animate slower on the wire. Deciding the right fix (clamp/rescale
	// 0-9 -> 0-4 inverted here, or change the public unit to 0-4 and teach the generator an
	// inverted-range YAML annotation) needs confirming the 0-4 range against a capture first;
	// left unverified/undone here rather than guessing at the transform.
	private static void ValidateSpeed(byte speed)
	{
		if (speed > 9)
			throw new ArgumentOutOfRangeException(nameof(speed), speed,
				"Speed must be 0–9.");
	}

	private static byte SumBytes(ReadOnlySpan<byte> data)
	{
		byte sum = 0;
		foreach (var b in data) sum += b;
		return sum;
	}


	private KeyboardFuncBlock FetchFuncBlock(int profileIndex = 0)
	{
		ValidateProfileIndex(profileIndex);
		EnsureHasFuncBlock();
		var block = new KeyboardFuncBlock();
		ReadExtendedGateway(0x05, 64 * profileIndex, 64).CopyTo(block.RawBytes, 0);
		return block;
	}

	private void PushFuncBlock(KeyboardFuncBlock block, int profileIndex = 0)
	{
		ValidateProfileIndex(profileIndex);
		EnsureHasFuncBlock();
		WriteExtendedGateway(0x06, 64 * profileIndex, block.RawBytes);
	}


	private const int KeyTriggerStride = 1024; // 128 keys × 8 bytes per profile

	private byte[] FetchKeyTriggers(int profileIndex = 0)
	{
		ValidateProfileIndex(profileIndex);
		return ReadExtendedGateway(0xA0, KeyTriggerStride * profileIndex, KeyTriggerStride);
	}

	private void PushKeyTriggers(ReadOnlySpan<byte> data, int profileIndex = 0)
	{
		ValidateProfileIndex(profileIndex);
		WriteExtendedGateway(0xA1, KeyTriggerStride * profileIndex, data);
	}

	/// <summary>
	/// Reads all 128 per-key trigger configurations for the specified profile.
	/// </summary>
	/// <param name="profileIndex">Keyboard profile slot (0-based). Default: 0.</param>
	/// <returns>Array of 128 <see cref="KeyTriggerConfig"/> values, indexed by layout key index.</returns>
	internal KeyTriggerConfig[] ReadKeyTriggers(int profileIndex = 0)
	{
		EnsureNotPolling();
		var raw = FetchKeyTriggers(profileIndex);
		var result = new KeyTriggerConfig[128];
		for (int i = 0; i < 128; i++)
			result[i] = KeyTriggerConfig.Decode(raw.AsSpan(i * 8));
		return result;
	}

	/// <summary>
	/// Writes all 128 per-key trigger configurations for the specified profile.
	/// </summary>
	/// <param name="configs">Exactly 128 configs, indexed by layout key index.</param>
	/// <param name="profileIndex">Keyboard profile slot (0-based). Default: 0.</param>
	internal void WriteKeyTriggers(KeyTriggerConfig[] configs, int profileIndex = 0)
	{
		EnsureNotPolling();
		if (configs.Length != 128)
			throw new ArgumentException(
				$"Expected 128 key trigger configs, got {configs.Length}.", nameof(configs));
		var raw = new byte[KeyTriggerStride];
		for (int i = 0; i < 128; i++)
			KeyTriggerConfig.Encode(configs[i], raw.AsSpan(i * 8));
		PushKeyTriggers(raw, profileIndex);
	}

	/// <summary>
	/// Writes the trigger configuration for a single key by layout index.
	/// Sends only 8 bytes - does not require a full read-modify-write cycle.
	/// </summary>
	/// <param name="keyIndex">Layout index (0-127).</param>
	/// <param name="config">Trigger configuration to apply.</param>
	/// <param name="profileIndex">Keyboard profile slot (0-based). Default: 0.</param>
	internal void SetKeyTrigger(int keyIndex, KeyTriggerConfig config, int profileIndex = 0)
	{
		EnsureNotPolling();
		ValidateProfileIndex(profileIndex);
		if ((uint)keyIndex >= 128)
			throw new ArgumentOutOfRangeException(nameof(keyIndex),
				$"Key trigger index {keyIndex} must be in [0, 127].");
		var entry = new byte[8];
		KeyTriggerConfig.Encode(config, entry);
		WriteExtendedGateway(0xA1,
			KeyTriggerStride * profileIndex + 8 * keyIndex, entry);
	}

	/// <summary>
	/// Writes the trigger configuration for a single named key.
	/// Sends only 8 bytes - does not require a full read-modify-write cycle.
	/// </summary>
	/// <param name="key">The key to configure.</param>
	/// <param name="config">Trigger configuration to apply.</param>
	/// <param name="profileIndex">Keyboard profile slot (0-based). Default: 0.</param>
	/// <exception cref="ArgumentException">Thrown when <paramref name="key"/> is not on this model.</exception>
	internal void SetKeyTrigger(DDKey key, KeyTriggerConfig config, int profileIndex = 0) =>
		SetKeyTrigger(GetKeyIndex(key), config, profileIndex);

	//
	// Each profile contains 4 layers. Each layer contains 128 key slots × 3 bytes.
	// Address layout: 2048 × profileIndex + 512 × layerIndex + 3 × keyIndex.
	// Sub-commands: 0x07 = read default (factory) keys, 0x08 = read user keys,
	//               0x09 = write user keys.

	private const int KeyMapKeyCount = 128;
	private const int KeyMapLayerCount = 4;
	private const int KeyMapLayerStride = 512;
	private const int KeyMapProfileStride = KeyMapLayerStride * KeyMapLayerCount; // 2048

	private static int KeyMapAddr(int profileIndex, int layerIndex, int keyIndex = 0)
	{
		ValidateProfileIndex(profileIndex);
		return KeyMapProfileStride * profileIndex + KeyMapLayerStride * layerIndex + 3 * keyIndex;
	}

	private static UserKey[] DecodeUserKeyArray(byte[] raw, int count)
	{
		var result = new UserKey[count];
		for (int i = 0; i < count; i++)
			result[i] = new UserKey { Type = raw[i * 3], Param1 = raw[i * 3 + 1], Param2 = raw[i * 3 + 2] };
		return result;
	}

	private static void EncodeUserKeyArray(UserKey[] keys, byte[] dest, int count)
	{
		for (int i = 0; i < count; i++)
		{
			dest[i * 3]     = keys[i].Type;
			dest[i * 3 + 1] = keys[i].Param1;
			dest[i * 3 + 2] = keys[i].Param2;
		}
	}

	private static UserKey[] DecodeKeyMap(byte[] raw) =>
		DecodeUserKeyArray(raw, KeyMapKeyCount);

	private static void ValidateLayer(int layerIndex)
	{
		if ((uint)layerIndex >= KeyMapLayerCount)
			throw new ArgumentOutOfRangeException(nameof(layerIndex),
				$"Layer index {layerIndex} must be in [0, {KeyMapLayerCount - 1}].");
	}

	/// <summary>
	/// Reads the user-assigned key map for a layer.
	/// Returns 128 <see cref="UserKey"/> values indexed by layout key index.
	/// </summary>
	/// <param name="layerIndex">Layer (0 = base, 1 = Fn1, 2 = Fn2, 3 = Fn3). Default: 0.</param>
	/// <param name="profileIndex">Keyboard profile slot (0-based). Default: 0.</param>
	internal UserKey[] ReadKeyMap(int layerIndex = 0, int profileIndex = 0)
	{
		EnsureNotPolling();
		ValidateLayer(layerIndex);
		var raw = ReadExtendedGateway(0x07, KeyMapAddr(profileIndex, layerIndex), KeyMapKeyCount * 3);
		return DecodeKeyMap(raw);
	}

	/// <summary>
	/// Reads the factory-default key map for a layer.
	/// Returns 128 <see cref="UserKey"/> values indexed by layout key index.
	/// </summary>
	/// <param name="layerIndex">Layer (0 = base, 1 = Fn1, 2 = Fn2, 3 = Fn3). Default: 0.</param>
	/// <param name="profileIndex">Keyboard profile slot (0-based). Default: 0.</param>
	internal UserKey[] ReadDefaultKeyMap(int layerIndex = 0, int profileIndex = 0)
	{
		EnsureNotPolling();
		ValidateLayer(layerIndex);
		var raw = ReadExtendedGateway(0x08, KeyMapAddr(profileIndex, layerIndex), KeyMapKeyCount * 3);
		return DecodeKeyMap(raw);
	}

	/// <summary>
	/// Writes all 128 key assignments for a layer.
	/// </summary>
	/// <param name="keys">Exactly 128 entries indexed by layout key index.</param>
	/// <param name="layerIndex">Layer (0 = base, 1 = Fn1, 2 = Fn2, 3 = Fn3). Default: 0.</param>
	/// <param name="profileIndex">Keyboard profile slot (0-based). Default: 0.</param>
	internal void WriteKeyMap(UserKey[] keys, int layerIndex = 0, int profileIndex = 0)
	{
		EnsureNotPolling();
		ValidateLayer(layerIndex);
		if (keys.Length != KeyMapKeyCount)
			throw new ArgumentException(
				$"Expected {KeyMapKeyCount} key entries, got {keys.Length}.", nameof(keys));
		var raw = new byte[KeyMapKeyCount * 3];
		EncodeUserKeyArray(keys, raw, KeyMapKeyCount);
		WriteExtendedGateway(0x09, KeyMapAddr(profileIndex, layerIndex), raw);
	}

	/// <summary>
	/// Sets a single key assignment by layout index. Sends 3 bytes - does not require
	/// reading the full layer.
	/// </summary>
	/// <param name="keyIndex">Layout index (0-127).</param>
	/// <param name="key">The assignment to apply.</param>
	/// <param name="layerIndex">Layer (0 = base, 1 = Fn1, 2 = Fn2, 3 = Fn3). Default: 0.</param>
	/// <param name="profileIndex">Keyboard profile slot (0-based). Default: 0.</param>
	internal void SetKey(int keyIndex, UserKey key, int layerIndex = 0, int profileIndex = 0)
	{
		EnsureNotPolling();
		ValidateLayer(layerIndex);
		if ((uint)keyIndex >= KeyMapKeyCount)
			throw new ArgumentOutOfRangeException(nameof(keyIndex),
				$"Key index {keyIndex} must be in [0, {KeyMapKeyCount - 1}].");
		WriteExtendedGateway(0x09, KeyMapAddr(profileIndex, layerIndex, keyIndex),
			[key.Type, key.Param1, key.Param2]);
	}

	/// <summary>
	/// Sets a single key assignment by named key. Sends 3 bytes - does not require
	/// reading the full layer.
	/// </summary>
	/// <param name="key">The key to remap.</param>
	/// <param name="value">The assignment to apply.</param>
	/// <param name="layerIndex">Layer (0 = base, 1 = Fn1, 2 = Fn2, 3 = Fn3). Default: 0.</param>
	/// <param name="profileIndex">Keyboard profile slot (0-based). Default: 0.</param>
	/// <exception cref="ArgumentException">Thrown when <paramref name="key"/> is not on this model.</exception>
	internal void SetKey(DDKey key, UserKey value, int layerIndex = 0, int profileIndex = 0) =>
		SetKey(GetKeyIndex(key), value, layerIndex, profileIndex);

	private const int DksStride = 768;

	/// <summary>Reads all 32 Dynamic Keystroke slot configurations for the specified profile.</summary>
	/// <param name="profileIndex">Keyboard profile slot (0-based). Default: 0.</param>
	internal DynamicKeystrokeEntry[] ReadDynamicKeystrokeEntries(int profileIndex = 0)
	{
		EnsureNotPolling();
		ValidateProfileIndex(profileIndex);
		var raw = ReadExtendedGateway(0xA2, DksStride * profileIndex, DksStride);
		var result = new DynamicKeystrokeEntry[DynamicKeystrokeEntry.SlotCount];
		for (int i = 0; i < DynamicKeystrokeEntry.SlotCount; i++)
			result[i] = DynamicKeystrokeEntry.Decode(raw.AsSpan(i * DynamicKeystrokeEntry.ByteSize));
		return result;
	}

	/// <summary>Writes all 32 Dynamic Keystroke slot configurations for the specified profile.</summary>
	/// <param name="entries">Exactly 32 Dynamic Keystroke entries.</param>
	/// <param name="profileIndex">Keyboard profile slot (0-based). Default: 0.</param>
	internal void WriteDynamicKeystrokeEntries(DynamicKeystrokeEntry[] entries, int profileIndex = 0)
	{
		EnsureNotPolling();
		ValidateProfileIndex(profileIndex);
		if (entries.Length != DynamicKeystrokeEntry.SlotCount)
			throw new ArgumentException(
				$"Expected {DynamicKeystrokeEntry.SlotCount} DKS entries, got {entries.Length}.", nameof(entries));
		var raw = new byte[DksStride];
		for (int i = 0; i < DynamicKeystrokeEntry.SlotCount; i++)
			DynamicKeystrokeEntry.Encode(entries[i], raw.AsSpan(i * DynamicKeystrokeEntry.ByteSize));
		WriteExtendedGateway(0xA3, DksStride * profileIndex, raw);
	}

	/// <summary>
	/// Writes a single Dynamic Keystroke slot configuration. Sends 24 bytes - does not require
	/// reading the full DKS region.
	/// </summary>
	/// <param name="slotIndex">DKS slot (0-31).</param>
	/// <param name="entry">Configuration to apply.</param>
	/// <param name="profileIndex">Keyboard profile slot (0-based). Default: 0.</param>
	internal void SetDynamicKeystrokeEntry(int slotIndex, DynamicKeystrokeEntry entry, int profileIndex = 0)
	{
		EnsureNotPolling();
		ValidateProfileIndex(profileIndex);
		if ((uint)slotIndex >= DynamicKeystrokeEntry.SlotCount)
			throw new ArgumentOutOfRangeException(nameof(slotIndex),
				$"DKS slot index {slotIndex} must be in [0, {DynamicKeystrokeEntry.SlotCount - 1}].");
		var raw = new byte[DynamicKeystrokeEntry.ByteSize];
		DynamicKeystrokeEntry.Encode(entry, raw);
		WriteExtendedGateway(0xA3,
			DksStride * profileIndex + DynamicKeystrokeEntry.ByteSize * slotIndex, raw);
	}

	private const int MtStride = 256;

	/// <summary>Reads all 32 Multi-Tap slot configurations for the specified profile.</summary>
	/// <param name="profileIndex">Keyboard profile slot (0-based). Default: 0.</param>
	internal MultiTapEntry[] ReadMultiTapEntries(int profileIndex = 0)
	{
		EnsureNotPolling();
		ValidateProfileIndex(profileIndex);
		var raw = ReadExtendedGateway(0xA4, MtStride * profileIndex,
			MultiTapEntry.SlotCount * MultiTapEntry.ByteSize);
		var result = new MultiTapEntry[MultiTapEntry.SlotCount];
		for (int i = 0; i < MultiTapEntry.SlotCount; i++)
			result[i] = MultiTapEntry.Decode(raw.AsSpan(i * MultiTapEntry.ByteSize));
		return result;
	}

	/// <summary>Writes all 32 Multi-Tap slot configurations for the specified profile.</summary>
	/// <param name="entries">Exactly 32 Multi-Tap entries.</param>
	/// <param name="profileIndex">Keyboard profile slot (0-based). Default: 0.</param>
	internal void WriteMultiTapEntries(MultiTapEntry[] entries, int profileIndex = 0)
	{
		EnsureNotPolling();
		ValidateProfileIndex(profileIndex);
		if (entries.Length != MultiTapEntry.SlotCount)
			throw new ArgumentException(
				$"Expected {MultiTapEntry.SlotCount} MT entries, got {entries.Length}.", nameof(entries));
		var raw = new byte[MultiTapEntry.SlotCount * MultiTapEntry.ByteSize];
		for (int i = 0; i < MultiTapEntry.SlotCount; i++)
			MultiTapEntry.Encode(entries[i], raw.AsSpan(i * MultiTapEntry.ByteSize));
		WriteExtendedGateway(0xA5, MtStride * profileIndex, raw);
	}

	/// <summary>
	/// Writes a single Multi-Tap slot configuration. Sends 6 bytes - does not require
	/// reading the full MT region.
	/// </summary>
	/// <param name="slotIndex">MT slot (0-31).</param>
	/// <param name="entry">Configuration to apply.</param>
	/// <param name="profileIndex">Keyboard profile slot (0-based). Default: 0.</param>
	internal void SetMultiTapEntry(int slotIndex, MultiTapEntry entry, int profileIndex = 0)
	{
		EnsureNotPolling();
		ValidateProfileIndex(profileIndex);
		if ((uint)slotIndex >= MultiTapEntry.SlotCount)
			throw new ArgumentOutOfRangeException(nameof(slotIndex),
				$"MT slot index {slotIndex} must be in [0, {MultiTapEntry.SlotCount - 1}].");
		Span<byte> raw = stackalloc byte[MultiTapEntry.ByteSize];
		MultiTapEntry.Encode(entry, raw);
		WriteExtendedGateway(0xA5,
			MtStride * profileIndex + MultiTapEntry.ByteSize * slotIndex, raw);
	}

	private const int TglStride = 128;
	private const int TglSlotSize = 3;
	private const int TglSlotCount = 32;

	/// <summary>
	/// Reads all 32 Toggle slot configurations for the specified profile.
	/// Each returned <see cref="UserKey"/> is the key that toggles on/off when pressed.
	/// </summary>
	/// <param name="profileIndex">Keyboard profile slot (0-based). Default: 0.</param>
	internal UserKey[] ReadToggleKeyEntries(int profileIndex = 0)
	{
		EnsureNotPolling();
		ValidateProfileIndex(profileIndex);
		var raw = ReadExtendedGateway(0xA6, TglStride * profileIndex,
			TglSlotCount * TglSlotSize);
		return DecodeUserKeyArray(raw, TglSlotCount);
	}

	/// <summary>Writes all 32 Toggle slot configurations for the specified profile.</summary>
	/// <param name="entries">Exactly 32 entries; each is the key that toggles.</param>
	/// <param name="profileIndex">Keyboard profile slot (0-based). Default: 0.</param>
	internal void WriteToggleKeyEntries(UserKey[] entries, int profileIndex = 0)
	{
		EnsureNotPolling();
		ValidateProfileIndex(profileIndex);
		if (entries.Length != TglSlotCount)
			throw new ArgumentException(
				$"Expected {TglSlotCount} Toggle entries, got {entries.Length}.", nameof(entries));
		var raw = new byte[TglSlotCount * TglSlotSize];
		EncodeUserKeyArray(entries, raw, TglSlotCount);
		WriteExtendedGateway(0xA7, TglStride * profileIndex, raw);
	}

	/// <summary>
	/// Writes a single Toggle slot configuration. Sends 3 bytes - does not require
	/// reading the full Toggle region.
	/// </summary>
	/// <param name="slotIndex">Toggle slot (0-31).</param>
	/// <param name="entry">The key that toggles on/off when pressed.</param>
	/// <param name="profileIndex">Keyboard profile slot (0-based). Default: 0.</param>
	internal void SetToggleKeyEntry(int slotIndex, UserKey entry, int profileIndex = 0)
	{
		EnsureNotPolling();
		ValidateProfileIndex(profileIndex);
		if ((uint)slotIndex >= TglSlotCount)
			throw new ArgumentOutOfRangeException(nameof(slotIndex),
				$"Toggle slot index {slotIndex} must be in [0, {TglSlotCount - 1}].");
		WriteExtendedGateway(0xA7,
			TglStride * profileIndex + TglSlotSize * slotIndex,
			[entry.Type, entry.Param1, entry.Param2]);
	}

	private const int MacroStride = MacroAction.BlockSize;

	/// <summary>
	/// Reads all 32 macro slots for the specified profile.
	/// Empty slots are returned as empty arrays.
	/// </summary>
	/// <param name="profileIndex">Keyboard profile slot (0-based). Default: 0.</param>
	/// <returns>Array of 32 elements; each element is the action sequence for that slot.</returns>
	internal MacroAction[][] ReadMacroSlots(int profileIndex = 0)
	{
		EnsureNotPolling();
		ValidateProfileIndex(profileIndex);
		var raw = ReadExtendedGateway(0x0C, MacroStride * profileIndex, MacroStride);
		return MacroAction.DecodeBlock(raw);
	}

	/// <summary>
	/// Writes all 32 macro slots for the specified profile.
	/// </summary>
	/// <param name="slots">
	/// Exactly 32 elements. Null or empty arrays produce empty slot entries.
	/// </param>
	/// <param name="profileIndex">Keyboard profile slot (0-based). Default: 0.</param>
	internal void WriteMacroSlots(MacroAction[]?[] slots, int profileIndex = 0)
	{
		EnsureNotPolling();
		ValidateProfileIndex(profileIndex);
		if (slots.Length != MacroAction.SlotCount)
			throw new ArgumentException(
				$"Expected {MacroAction.SlotCount} macro slots, got {slots.Length}.", nameof(slots));
		var raw = MacroAction.EncodeBlock(slots);
		WriteExtendedGateway(0x0D, MacroStride * profileIndex, raw);
	}

	/// <summary>
	/// Writes a single macro slot. Reads the full block, replaces the slot, then writes back.
	/// </summary>
	/// <param name="slotIndex">Slot index (0-31).</param>
	/// <param name="actions">Action sequence for the slot. Pass an empty array to clear.</param>
	/// <param name="profileIndex">Keyboard profile slot (0-based). Default: 0.</param>
	internal void SetMacroSlot(int slotIndex, MacroAction[] actions, int profileIndex = 0)
	{
		EnsureNotPolling();
		if ((uint)slotIndex >= MacroAction.SlotCount)
			throw new ArgumentOutOfRangeException(nameof(slotIndex),
				$"Macro slot index {slotIndex} must be in [0, {MacroAction.SlotCount - 1}].");
		var slots = ReadMacroSlots(profileIndex);
		slots[slotIndex] = actions;
		WriteMacroSlots(slots, profileIndex);
	}

	private const int BaseBlockSize = 32;

	/// <summary>
	/// Number of profile slots the firmware supports on standard keyboard models.
	/// Profiles are zero-based: valid indices are <c>0</c> to <c>ProfileCount − 1</c>.
	/// </summary>
	public const int ProfileCount = 4;

	private static void ValidateProfileIndex(int profileIndex, [CallerMemberName] string caller = "")
	{
		if ((uint)profileIndex >= ProfileCount)
			throw new ArgumentOutOfRangeException(nameof(profileIndex),
				$"{caller}: profile index must be 0–{ProfileCount - 1}, got {profileIndex}.");
	}

	/// <summary>
	/// Tells the keyboard to switch to the specified profile immediately.
	/// The keyboard stores the active profile index in a global base block and
	/// the change takes effect at once on the hardware.
	/// </summary>
	/// <param name="profileIndex">Target profile (0-based, 0–<see cref="ProfileCount"/>−1).</param>
	internal void SwitchProfile(int profileIndex)
	{
		EnsureNotPolling();
		ValidateProfileIndex(profileIndex);
		WriteExtendedGateway(0x0E, 0, [(byte)profileIndex]);
	}

	/// <summary>
	/// Reads the currently active profile index from the keyboard.
	/// </summary>
	/// <returns>Zero-based profile index.</returns>
	internal int GetCurrentProfile()
	{
		EnsureNotPolling();
		var raw = ReadExtendedGateway(0x04, 0, BaseBlockSize);
		return raw[0];
	}

	/// <summary>
	/// Reads every sub-block for the specified profile and returns a
	/// <see cref="FullProfileData"/> snapshot containing all sections.
	/// </summary>
	/// <param name="profileIndex">Keyboard profile slot (0-based). Default: 0.</param>
	internal FullProfileData PullFullProfile(int profileIndex = 0)
	{
		EnsureNotPolling();
		ValidateProfileIndex(profileIndex);
		EnsureHasFuncBlock();

		var funcRaw = ReadExtendedGateway(0x05, 64 * profileIndex, 64);
		var funcBlock = new KeyboardFuncBlock();
		funcRaw.CopyTo(funcBlock.RawBytes, 0);

		var triggers = ReadKeyTriggers(profileIndex);

		var layers = new UserKey[KeyMapLayerCount][];
		for (int layer = 0; layer < KeyMapLayerCount; layer++)
			layers[layer] = ReadKeyMap(layer, profileIndex);

		var dks = ReadDynamicKeystrokeEntries(profileIndex);
		var mt = ReadMultiTapEntries(profileIndex);
		var tgl = ReadToggleKeyEntries(profileIndex);
		var mac = ReadMacroSlots(profileIndex);

		return new FullProfileData
		{
			FuncBlock                 = funcBlock,
			KeyTriggers               = triggers,
			KeyMapLayers              = layers,
			DynamicKeystrokeEntries   = dks,
			MultiTapEntries           = mt,
			ToggleKeyEntries                = tgl,
			MacroSlots                = mac,
		};
	}

	/// <summary>
	/// Writes all non-<see langword="null"/> sections of <paramref name="data"/> to the
	/// specified profile. Sections left <see langword="null"/> are not touched on the keyboard.
	/// </summary>
	/// <param name="data">Profile data to push.</param>
	/// <param name="profileIndex">Keyboard profile slot (0-based). Default: 0.</param>
	internal void PushFullProfile(FullProfileData data, int profileIndex = 0)
	{
		EnsureNotPolling();
		ValidateProfileIndex(profileIndex);

		if (data.FuncBlock != null)
			PushFuncBlock(data.FuncBlock, profileIndex);

		if (data.KeyTriggers != null)
			WriteKeyTriggers(data.KeyTriggers, profileIndex);

		if (data.KeyMapLayers != null)
		{
			for (int layer = 0; layer < KeyMapLayerCount; layer++)
			{
				if (data.KeyMapLayers.Length > layer && data.KeyMapLayers[layer] != null)
					WriteKeyMap(data.KeyMapLayers[layer]!, layer, profileIndex);
			}
		}

		if (data.DynamicKeystrokeEntries != null)
			WriteDynamicKeystrokeEntries(data.DynamicKeystrokeEntries, profileIndex);

		if (data.MultiTapEntries != null)
			WriteMultiTapEntries(data.MultiTapEntries, profileIndex);

		if (data.ToggleKeyEntries != null)
			WriteToggleKeyEntries(data.ToggleKeyEntries, profileIndex);

		if (data.MacroSlots != null)
			WriteMacroSlots(data.MacroSlots, profileIndex);
	}

	/// <summary>
	/// Reads the current state of the specified profile from the keyboard and returns a
	/// serialisable <see cref="KeyboardProfile"/> snapshot. Only data that maps cleanly into
	/// <see cref="KeyboardProfile"/> is captured:
	/// <list type="bullet">
	/// <item>Actuation, Rapid Trigger press/release depths - uniform or per-key.</item>
	/// <item>Rapid Trigger enabled, RT auto-match, and Turbo mode global flags (not profile-specific on firmware).</item>
	/// <item>Single-colour lighting theme when the profile uses a single-colour preset effect.</item>
	/// </list>
	/// </summary>
	/// <param name="profileIndex">Keyboard profile slot (0-based, 0–<see cref="ProfileCount"/>−1). Default: 0.</param>
	internal KeyboardProfile CaptureProfile(int profileIndex = 0)
	{
		EnsureNotPolling();
		ValidateProfileIndex(profileIndex);

		KeyboardFuncBlock? funcBlock = HasFuncBlock ? FetchFuncBlock(profileIndex) : null;

		KeyDepthProfile? actuation = null, downstroke = null, upstroke = null;

		if (HasFuncBlock)
		{
			var triggers   = ReadKeyTriggers(profileIndex);
			var indexToKey = _keyIndexMap.ToDictionary(kv => kv.Value, kv => kv.Key);

			var actuations  = new Dictionary<int, float>(_keyIndexMap.Count);
			var downstrokes = new Dictionary<int, float>(_keyIndexMap.Count);
			var upstrokes   = new Dictionary<int, float>(_keyIndexMap.Count);

			foreach (var (_, idx) in _keyIndexMap)
			{
				if (idx >= triggers.Length) continue;
				var t = triggers[idx];
				actuations[idx]  = t.Actuation / 100f;
				downstrokes[idx] = t.RtPress   / 100f;
				upstrokes[idx]   = t.RtRelease / 100f;
			}

			static bool IsUniform(Dictionary<int, float> map, out float value)
			{
				value = 0f;
				if (map.Count == 0) return true;
				value = map.Values.First();
				float first = value;
				return map.Values.All(v => MathF.Abs(v - first) < 0.05f);
			}

			Dictionary<string, float> BuildPerKeyMap(Dictionary<int, float> source)
			{
				var result = new Dictionary<string, float>(source.Count, StringComparer.OrdinalIgnoreCase);
				foreach (var (idx, mm) in source)
					if (indexToKey.TryGetValue(idx, out var key))
						result[key.ToString()] = mm;
				return result;
			}

			KeyDepthProfile ToDepthProfile(Dictionary<int, float> source, float uniform, bool isUniform) =>
				isUniform
					? new KeyDepthProfile { Default = uniform }
					: new KeyDepthProfile { Default = 0, Keys = BuildPerKeyMap(source) };

			bool uniformAct = IsUniform(actuations,  out float uActMm);
			bool uniformDs  = IsUniform(downstrokes, out float uDsMm);
			bool uniformUs  = IsUniform(upstrokes,   out float uUsMm);

			actuation  = ToDepthProfile(actuations,  uActMm, uniformAct);
			downstroke = ToDepthProfile(downstrokes, uDsMm,  uniformDs);
			upstroke   = ToDepthProfile(upstrokes,   uUsMm,  uniformUs);
		}

		KeyboardTheme? theme = null;
		if (funcBlock is { LightSingleColor: true, LightEffect: > 0 })
		{
			theme = new KeyboardTheme
			{
				BaseColor  = new RgbColor(funcBlock.LightColorR, funcBlock.LightColorG, funcBlock.LightColorB),
				Brightness = funcBlock.LightBrightness,
			};
		}

		return new KeyboardProfile
		{
			Actuation             = actuation,
			Downstroke            = downstroke,
			Upstroke              = upstroke,
			RapidTrigger          = _rapidTriggerEnabled,
			RapidTriggerAutoMatch = _rapidTriggerAutoMatch,
			TurboMode             = _turboEnabled,
			Theme                 = theme,
		};
	}

	/// <summary>
	/// Copies all profile data from <paramref name="fromSlot"/> to <paramref name="toSlot"/>
	/// by pulling the full profile and pushing it to the target slot.
	/// </summary>
	/// <param name="fromSlot">Source profile index (0-based, 0–<see cref="ProfileCount"/>−1).</param>
	/// <param name="toSlot">Destination profile index (0-based, 0–<see cref="ProfileCount"/>−1).</param>
	internal void CopyProfile(int fromSlot, int toSlot)
	{
		EnsureNotPolling();
		ValidateProfileIndex(fromSlot);
		ValidateProfileIndex(toSlot);
		if (fromSlot == toSlot) return;
		PushFullProfile(PullFullProfile(fromSlot), toSlot);
	}

	/// <summary>
	/// Reads the function configuration block for the specified profile.
	/// Modify the returned instance and pass it to <see cref="WriteFuncBlock"/> to apply changes.
	/// </summary>
	/// <param name="profileIndex">Keyboard profile slot (0-based). Default: 0.</param>
	internal KeyboardFuncBlock ReadFuncBlock(int profileIndex = 0)
	{
		EnsureNotPolling();
		return FetchFuncBlock(profileIndex);
	}

	/// <summary>
	/// Writes a function configuration block back to the specified profile.
	/// Use with <see cref="ReadFuncBlock"/> to read-modify-write the block.
	/// </summary>
	/// <param name="block">The block to write.</param>
	/// <param name="profileIndex">Keyboard profile slot (0-based). Default: 0.</param>
	internal void WriteFuncBlock(KeyboardFuncBlock block, int profileIndex = 0)
	{
		EnsureNotPolling();
		PushFuncBlock(block, profileIndex);
	}

	/// <summary>
	/// Enters fast-transfer mode, suspending normal key processing on the firmware.
	/// Call <see cref="StopFastTransferMode"/> after bulk write operations complete.
	/// Wire format: JS <c>startFastModel()</c> - defined but not called in the official app.
	/// </summary>
	internal void StartFastTransferMode()
	{
		EnsureNotPolling();
		EnsureHasFuncBlock();
		_connection.Send([0x55, 0x01]);
		_inFastMode = true;
	}

	/// <summary>
	/// Exits fast-transfer mode, resuming normal key processing on the firmware.
	/// Must be preceded by <see cref="StartFastTransferMode"/>. No-op if fast transfer mode is not active.
	/// </summary>
	internal void StopFastTransferMode()
	{
		EnsureNotPolling();
		EnsureHasFuncBlock();
		if (!_inFastMode)
		{
			_log.LogWarning("{Method} called without a preceding {Prereq}; nothing sent",
			nameof(StopFastTransferMode), nameof(StartFastTransferMode));
			return;
		}
		_connection.Send([0x55, 0x02]);
		_inFastMode = false;
	}

	/// <summary>
	/// Signals the keyboard to begin analog sensor calibration.
	/// Call <see cref="EndCalibration"/> when calibration is complete.
	/// The keyboard must not be polled during calibration.
	/// </summary>
	internal void StartCalibration()
	{
		EnsureNotPolling();
		EnsureHasFuncBlock();
		_connection.Send([0x55, 0xA8]);
	}

	/// <summary>
	/// Signals the keyboard to end analog sensor calibration.
	/// Must be preceded by <see cref="StartCalibration"/>.
	/// </summary>
	internal void EndCalibration()
	{
		EnsureNotPolling();
		EnsureHasFuncBlock();
		_connection.Send([0x55, 0xA9]);
	}

	/// <summary>
	/// Resets all keyboard settings to factory defaults.
	/// This operation is irreversible - all profiles, key maps, and lighting
	/// configurations stored on the keyboard are erased.
	/// </summary>
	/// <remarks>
	/// PROTO-4: the opcode <c>[0x06, 0x0F, 0xFF]</c> is unverified against a capture of the
	/// official app - no other message in this protocol starts with <c>0x06</c>. No response is
	/// checked either, so a failed or rejected request looks identical to success. Internal
	/// (not exposed publicly) until both are confirmed on real hardware.
	/// </remarks>
	internal void RestoreFactorySettings()
	{
		EnsureNotPolling();
		_connection.Send([0x06, 0x0F, 0xFF]);
	}

	/// <summary>
	/// Performs a soft reset (firmware reboot) of the keyboard without clearing settings.
	/// The USB connection will briefly drop and reappear; dispose this session and
	/// reconnect with <see cref="OpenFirst"/> after calling this method.
	/// </summary>
	internal void Reset()
	{
		EnsureNotPolling();
		EnsureHasFuncBlock();
		_connection.Send([0x55, 0xEE, 0, 0, 1, 0, 0, 0, 0xFF]);
	}

	public void Dispose()
	{
		// Interlocked, not a plain bool check-then-set: Dispose() can legitimately be called
		// concurrently (e.g. a using block on one thread racing an explicit Dispose() call on
		// another), and the old unsynchronized check let both callers past the guard.
		if (Interlocked.CompareExchange(ref _disposedFlag, 1, 0) != 0) return;

		try { StopPolling(); }
		catch (Exception ex) { _log.LogWarning(ex, "Dispose: StopPolling faulted."); }

		if (_inFastMode)
		{
			try
			{
				_connection.Send([0x55, 0x02]);
				_inFastMode = false;
			}
			catch (Exception ex) { _log.LogWarning(ex, "Dispose: EndFastMode send failed (keyboard may be disconnected)."); }
		}

		_connection.Dispose();
	}
}
