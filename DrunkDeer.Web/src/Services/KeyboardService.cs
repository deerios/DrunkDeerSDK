using DrunkDeer.Protocol;
using DrunkDeer.Simulation;
using DrunkDeer.Web.Transport;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace DrunkDeer.Web.Services;

/// <summary>
/// Owns the single async <see cref="KeyboardSession"/> for the page and drives the
/// <see cref="KeyboardStore"/> from it. Components call the async methods here to connect,
/// disconnect, and (later) configure; they read live per-key travel via <see cref="SnapshotHeights"/>.
/// </summary>
/// <remarks>
/// Everything runs on the WASM main thread — there is no background thread to marshal off of —
/// but the session's async API is used throughout so the same code survives a future desktop
/// (BlazorWebView) reuse or WASM multithreading. Only the async transport surface is touched:
/// the sync API would throw on a WebHID connection.
/// </remarks>
public sealed class KeyboardService : IAsyncDisposable
{
    private readonly KeyboardStore _store;
    private readonly IJSRuntime _js;
    private readonly DiagnosticsLog _diagnostics;
    private readonly ILoggerFactory _loggerFactory;
    private KeyboardSession? _session;
    private SimulatedKeyboardConnection? _sim;
    private WebHidKeyboardConnection? _webhid;
    private IJSObjectReference? _module;

    public KeyboardService(KeyboardStore store, IJSRuntime js, DiagnosticsLog diagnostics, ILoggerFactory loggerFactory)
    {
        _store = store;
        _js = js;
        _diagnostics = diagnostics;
        _loggerFactory = loggerFactory;
    }

    /// <summary>
    /// Whether this browser can talk to USB devices at all: WebHID is Chromium-only and needs a
    /// secure context. Loads the interop module as a side effect, which is the point — the module
    /// has to already be warm when <see cref="ConnectWebHidAsync"/> runs, because Chrome only
    /// opens the device picker while the user's click still counts as a gesture.
    /// </summary>
    public async Task<bool> IsWebHidSupportedAsync()
    {
        try
        {
            var module = await LoadModuleAsync().ConfigureAwait(false);
            return await module.InvokeAsync<bool>("isSupported").ConfigureAwait(false);
        }
        catch
        {
            return false;
        }
    }

    private async ValueTask<IJSObjectReference> LoadModuleAsync() =>
        _module ??= await _js.InvokeAsync<IJSObjectReference>("import", "./js/webhid.js").ConfigureAwait(false);

    /// <summary>The live session, or <see langword="null"/> when disconnected. Read-only for components.</summary>
    public KeyboardSession? Session => _session;

    public bool IsConnected => _session is not null;

    /// <summary>
    /// Connects to a fully simulated keyboard (an A75 by default) and starts polling. No hardware
    /// or browser HID support required — this is how the deployed site is explorable and how
    /// development happens without the one physical board.
    /// </summary>
    public async Task ConnectDemoAsync(CancellationToken ct = default)
    {
        if (_session is not null) await DisconnectAsync().ConfigureAwait(false);

        _store.SetConnecting(demo: true);
        try
        {
            _sim = new SimulatedKeyboardConnection { IdleJitter = true };
            // Opened through the async factory even though the simulator offers both surfaces, so
            // demo mode runs the same async-only session shape a real WebHID board does — and its
            // traffic reaches the diagnostics page by the same path.
            var session = KeyboardSession.OpenAsyncConnection(
                new TracingKeyboardConnection(_sim, _diagnostics), _loggerFactory);
            HookSession(session);
            await session.StartPollingAsync(ct).ConfigureAwait(false);
            _session = session;
            PublishConnected(demo: true);
        }
        catch (Exception ex)
        {
            _sim = null;
            _store.SetFaulted($"Demo mode failed to start: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Connects to a real keyboard over WebHID: prompts the user to pick a device, runs the
    /// identity handshake, and starts polling.
    /// </summary>
    /// <remarks>
    /// Call this straight from a click handler and don't await anything slow first — Chrome only
    /// shows the device picker while the click still counts as a user gesture.
    /// </remarks>
    /// <returns>
    /// <see langword="true"/> if a keyboard was connected; <see langword="false"/> if the user
    /// dismissed the picker without choosing, which is a normal thing to do and not an error.
    /// </returns>
    public async Task<bool> ConnectWebHidAsync(CancellationToken ct = default)
    {
        if (_session is not null) await DisconnectAsync().ConfigureAwait(false);

        var module = await LoadModuleAsync().ConfigureAwait(false);

        _store.SetConnecting(demo: false);
        WebHidKeyboardConnection? connection = null;
        try
        {
            connection = await WebHidKeyboardConnection.RequestAsync(module, _diagnostics, ct).ConfigureAwait(false);
            if (connection is null)
            {
                _store.SetDisconnected();
                return false;
            }

            connection.Disconnected += OnHardwareUnplugged;
            var session = KeyboardSession.OpenAsyncConnection(
                new TracingKeyboardConnection(connection, _diagnostics), _loggerFactory);
            HookSession(session);
            await session.StartPollingAsync(ct).ConfigureAwait(false);
            _session = session;
            _webhid = connection;
            PublishConnected(demo: false);
            return true;
        }
        catch (Exception ex)
        {
            if (connection is not null)
            {
                connection.Disconnected -= OnHardwareUnplugged;
                await connection.DisposeAsync().ConfigureAwait(false);
            }
            _webhid = null;
            _store.SetFaulted(ex.Message);
            throw;
        }
    }

    // The browser tells us the moment the cable is pulled. Without this the session would only
    // find out by way of its reads quietly timing out forever, leaving the UI claiming a
    // connection that no longer exists.
    private void OnHardwareUnplugged()
    {
        var session = _session;
        _session = null;
        DetachWebHid();
        ResetShadowState();

        if (session is not null)
        {
            UnhookSession(session);
            // This arrives on a JS event callback, which can't be awaited — and the teardown has
            // to happen regardless, to stop the poll loop reading a device that's gone.
            _ = TearDownAsync(session);
        }

        _store.SetDisconnected("The keyboard was unplugged.");
    }

    private static async Task TearDownAsync(KeyboardSession session)
    {
        try { await session.DisposeAsync().ConfigureAwait(false); }
        catch { /* the device is already gone; there's nothing left to close cleanly */ }
    }

    private void DetachWebHid()
    {
        if (_webhid is null) return;
        _webhid.Disconnected -= OnHardwareUnplugged;
        _webhid = null;
    }

    public async Task DisconnectAsync()
    {
        var session = _session;
        _session = null;
        _sim = null;
        DetachWebHid();
        ResetShadowState();
        if (session is not null)
        {
            UnhookSession(session);
            await session.DisposeAsync().ConfigureAwait(false);
        }
        _store.SetDisconnected();
    }

    /// <summary>
    /// The latest per-key travel in millimetres (index = firmware slot), or an empty array when
    /// disconnected. The keyboard view reads this each animation frame; it is cheap and allocation-free
    /// on the session side after the first call.
    /// </summary>
    public float[] SnapshotHeights() => _session?.GetAllKeyHeightsMm() ?? [];

    /// <summary>Demo-only: drive a key's travel so the UI shows movement without a real press.</summary>
    public void DemoPressKey(int slot, float mm) => _sim?.SetKeyTravelMm(slot, mm);

    /// <summary>Demo-only: release every simulated key.</summary>
    public void DemoReleaseAll() => _sim?.ReleaseAll();

    // ── Actuation ────────────────────────────────────────────────────────────

    /// <summary>The depth range this board accepts, in mm. Falls back to a sane range when disconnected.</summary>
    public float MinDepthMm => _session?.MinDepthMm ?? 0.1f;

    public float MaxDepthMm => _session?.MaxDepthMm ?? 4.0f;

    /// <summary>Whether rapid trigger is on. Unlike the depth profiles, this is read from the board's own handshake.</summary>
    public bool RapidTriggerEnabled => _session?.RapidTriggerEnabled ?? false;

    /// <summary>
    /// Whether this session has written a full depth profile yet, which is what makes the
    /// in-memory profile trustworthy — see <see cref="ApplyActuationAsync"/>.
    /// </summary>
    public bool DepthsAreKnown { get; private set; }

    /// <summary>The session's current actuation point per key, in mm. Only meaningful once <see cref="DepthsAreKnown"/>.</summary>
    public IReadOnlyDictionary<DDKey, float> GetActuationProfile() =>
        _session?.GetActuationProfile() ?? new Dictionary<DDKey, float>();

    /// <summary>Raised when the actuation points change, so the on-screen board can move its markers.</summary>
    public event Action? ActuationChanged;

    /// <summary>
    /// A depth the user is currently dialling in but has not applied, or <see langword="null"/>
    /// when nothing is being edited. Applies to the selected keys only.
    /// </summary>
    /// <remarks>
    /// The panel publishes this so the board can show where the actuation point is heading while
    /// the slider moves. It is deliberately not part of the session's profile: nothing has been
    /// written, and the board draws it differently for exactly that reason.
    /// </remarks>
    public float? ActuationPreviewMm { get; private set; }

    public void SetActuationPreview(float? depthMm)
    {
        if (Nullable.Equals(ActuationPreviewMm, depthMm)) return;
        ActuationPreviewMm = depthMm;
        ActuationChanged?.Invoke();
    }

    /// <summary>
    /// Sets the actuation point of <paramref name="keys"/> to <paramref name="depthMm"/>.
    /// </summary>
    /// <remarks>
    /// The firmware has no "set just these keys" command — every actuation write sends the whole
    /// board — so what happens to the keys the user did *not* select matters:
    /// <list type="bullet">
    /// <item>The first write of a session passes <paramref name="baselineMm"/> as the profile
    /// default, so every unselected key is set to it. The session starts with a made-up 2.0 mm
    /// per key rather than whatever the board actually holds (the A75 can't report its settings
    /// back), so leaving those keys "as-is" would silently write that guess to hardware. Making
    /// the baseline explicit means the value that lands on the other keys is one the user chose.</item>
    /// <item>Afterwards the profile default is left at zero, which the SDK reads as "leave keys
    /// not listed at their current value" — by then the session's profile is a true record of
    /// what it wrote, so unselected keys can safely be preserved.</item>
    /// </list>
    /// </remarks>
    public async Task ApplyActuationAsync(float depthMm, IReadOnlyCollection<DDKey> keys, float baselineMm, CancellationToken ct = default)
    {
        var session = _session ?? throw new InvalidOperationException("Not connected.");
        if (keys.Count == 0) throw new ArgumentException("At least one key must be selected.", nameof(keys));

        var actuation = new KeyDepthProfileBuilder()
            .Default(DepthsAreKnown ? 0f : baselineMm)
            .Keys(keys, depthMm)
            .Build();

        await session.ApplyProfileAsync(new KeyboardProfile { Actuation = actuation }, ct).ConfigureAwait(false);
        DepthsAreKnown = true;
        // The profile is now a real record of the board, so what the markers show stops being a
        // guess — and the preview has become the written value.
        ActuationPreviewMm = null;
        ActuationChanged?.Invoke();
    }

    /// <summary>Raised when rapid trigger is switched, so the on-screen board can badge its keys.</summary>
    public event Action? RapidTriggerChanged;

    /// <summary>Turns rapid trigger on or off. A whole-board setting, so it needs no selection.</summary>
    public async Task SetRapidTriggerAsync(bool enabled, bool autoMatch = false, CancellationToken ct = default)
    {
        var session = _session ?? throw new InvalidOperationException("Not connected.");
        if (enabled) await session.EnableRapidTriggerAsync(autoMatch, ct).ConfigureAwait(false);
        else await session.DisableRapidTriggerAsync(ct).ConfigureAwait(false);
        RapidTriggerChanged?.Invoke();
    }

    // ── Lighting ─────────────────────────────────────────────────────────────

    /// <summary>Raised after a lighting write, so the on-screen board can repaint its colours.</summary>
    public event Action? LightingChanged;

    /// <summary>
    /// Whether this session has written a colour to every key yet, which is what makes the
    /// in-memory colour profile trustworthy — see <see cref="ApplyLightingAsync"/>.
    /// </summary>
    public bool ColorsAreKnown { get; private set; }

    /// <summary>
    /// The firmware animation currently running, or <see langword="null"/> when the board is
    /// showing the per-key colours instead. The two are mutually exclusive: the per-key colour
    /// stream selects the firmware's custom mode, and picking an animation replaces it.
    /// </summary>
    public LightingMode? ActiveMode { get; private set; }

    /// <summary>Whether the backlight was switched off. The per-key colours survive it.</summary>
    public bool BacklightOff { get; private set; }

    /// <summary>The colour this session last wrote to each key, indexed by firmware slot.</summary>
    public IReadOnlyList<(int Slot, byte R, byte G, byte B)> SnapshotColors()
    {
        var session = _session;
        if (session is null) return [];
        return [.. session.Layout.Select(k =>
        {
            var (r, g, b) = session.GetKeyColor(k.Key);
            return (k.SlotIndex, r, g, b);
        })];
    }

    /// <summary>
    /// Sets the colour of <paramref name="keys"/>, leaving every other key at
    /// <paramref name="background"/> on the first write of a session.
    /// </summary>
    /// <remarks>
    /// Lighting has the same whole-board problem as actuation, one step worse. Every colour write
    /// sends the entire board from the session's in-memory profile, and unlike the actuation
    /// profile that one is never seeded at connect — it starts black. So a per-key colour write on
    /// a fresh session would switch every key the user did not select off.
    /// <para>
    /// The first write therefore goes out as a theme, whose base colour is an explicit background
    /// the user chose; afterwards the session's profile is a true record of what it wrote, so
    /// per-key writes can safely preserve the rest. The board can report its live colours back,
    /// but only through a blocking read this host can't make, so asking is not an option here.
    /// </para>
    /// </remarks>
    public async Task ApplyLightingAsync(
        RgbColor color, IReadOnlyCollection<DDKey> keys, RgbColor background,
        byte backgroundBrightness, byte brightness, CancellationToken ct = default)
    {
        var session = _session ?? throw new InvalidOperationException("Not connected.");
        if (keys.Count == 0) throw new ArgumentException("At least one key must be selected.", nameof(keys));

        if (ColorsAreKnown)
        {
            await session.SetKeyColorAsync(color.R, color.G, color.B, [.. keys], brightness, ct).ConfigureAwait(false);
        }
        else
        {
            var theme = new KeyboardThemeBuilder()
                .Base(background)
                .BaseBrightness(backgroundBrightness)
                .Brightness(brightness)
                .Keys(keys, color.R, color.G, color.B)
                .Build();
            await session.ApplyProfileAsync(new KeyboardProfile { Theme = theme }, ct).ConfigureAwait(false);
        }

        ColorsAreKnown = true;
        LastBrightness = brightness;
        // Sending per-key colours is what puts the board back into its custom mode, lit.
        ActiveMode = null;
        BacklightOff = false;
        LightingChanged?.Invoke();
    }

    /// <summary>
    /// Starts a built-in firmware animation. Whole-board, so it needs no selection — and it
    /// replaces the per-key colours on screen until the next colour write.
    /// </summary>
    public async Task SetLightingModeAsync(LightingMode mode, byte brightness, byte speed, CancellationToken ct = default)
    {
        var session = _session ?? throw new InvalidOperationException("Not connected.");
        await session.SetLightingModeAsync(mode, brightness, speed, ct).ConfigureAwait(false);
        LastBrightness = brightness;
        ActiveMode = mode;
        BacklightOff = false;
        LightingChanged?.Invoke();
    }

    /// <summary>Turns the backlight off. The per-key colours are kept and restored by the next colour write.</summary>
    public async Task TurnLightingOffAsync(CancellationToken ct = default)
    {
        var session = _session ?? throw new InvalidOperationException("Not connected.");
        await session.DisableLightingAsync(ct).ConfigureAwait(false);
        ActiveMode = null;
        BacklightOff = true;
        LightingChanged?.Invoke();
    }

    // ── Profiles ─────────────────────────────────────────────────────────────

    /// <summary>
    /// The brightness this session last sent. The firmware carries one brightness for the whole
    /// board and the session doesn't keep it — every write passes it — so capturing a profile
    /// needs it remembered here.
    /// </summary>
    public byte LastBrightness { get; private set; } = 9;

    /// <summary>
    /// A snapshot of the settings this session knows to be true, ready to serialise.
    /// </summary>
    /// <remarks>
    /// Only what the session actually knows goes in, which is why the two "known" flags gate it.
    /// The A75 can't report its settings back, so before this session has written a whole board
    /// the in-memory profile is the SDK's invented seed — 2.0 mm per key, and black — and
    /// capturing that would save a guess as though the user had chosen it. Rapid trigger is the
    /// exception: the board reports it in the identity handshake, so it is always real.
    /// <para>
    /// Depths and colours are captured as "the value most keys share, plus the exceptions", so the
    /// default is never zero. That keeps the JSON small and readable, and it matters on apply:
    /// the SDK reads a zero default as "leave the keys I didn't list alone", which would make a
    /// saved profile land differently depending on what the session it was applied to had already
    /// written. A real default writes the whole board and lands the same every time.
    /// </para>
    /// </remarks>
    public KeyboardProfile CaptureProfile()
    {
        var session = _session ?? throw new InvalidOperationException("Not connected.");

        var profile = new KeyboardProfile { RapidTrigger = session.RapidTriggerEnabled };
        if (DepthsAreKnown) profile.Actuation = CaptureDepths(session.GetActuationProfile());
        if (ColorsAreKnown) profile.Theme = CaptureTheme(session);
        return profile;
    }

    private static KeyDepthProfile? CaptureDepths(IReadOnlyDictionary<DDKey, float> depths)
    {
        if (depths.Count == 0) return null;

        float baseline = Modal(depths.Values);
        var builder = new KeyDepthProfileBuilder().Default(baseline);
        foreach (var (key, depthMm) in depths)
            if (depthMm != baseline) builder.Key(key, depthMm);
        return builder.Build();
    }

    private KeyboardTheme? CaptureTheme(KeyboardSession session)
    {
        var colors = session.Layout
            .Select(k => (k.Key, Color: session.GetKeyColor(k.Key)))
            .ToList();
        if (colors.Count == 0) return null;

        var baseColor = Modal(colors.Select(c => c.Color));
        var overrides = colors
            .Where(c => c.Color != baseColor)
            .ToDictionary(
                c => c.Key.ToString(),
                c => new KeyColor { R = c.Color.R, G = c.Color.G, B = c.Color.B });

        return new KeyboardTheme
        {
            BaseColor  = new RgbColor(baseColor.R, baseColor.G, baseColor.B),
            Brightness = LastBrightness,
            // Deliberately not set: the session stores colours with the background's brightness
            // already scaled into them, so setting it here would dim the background a second time.
            BaseBrightness = null,
            Keys = overrides.Count > 0 ? overrides : null,
        };
    }

    /// <summary>The value that occurs most often — the cheapest whole-board default to write.</summary>
    private static T Modal<T>(IEnumerable<T> values) where T : notnull =>
        values.GroupBy(v => v)
              .OrderByDescending(g => g.Count())
              .First().Key;

    /// <summary>
    /// Writes a saved profile to the board and re-synchronises what this session claims to know.
    /// </summary>
    /// <remarks>
    /// A captured profile carries a real default and a base colour, so applying one writes every
    /// key — which is what finally makes the session's picture of the board true rather than
    /// inherited from the SDK's seed. That is why applying a profile is the one operation that can
    /// promote both "known" flags on its own.
    /// </remarks>
    public async Task ApplyProfileAsync(KeyboardProfile profile, CancellationToken ct = default)
    {
        var session = _session ?? throw new InvalidOperationException("Not connected.");

        await session.ApplyProfileAsync(profile, ct).ConfigureAwait(false);

        // Only a non-zero default fills the whole board. A zero one preserves whatever the session
        // already held, so it can't turn a guess into knowledge.
        if (profile.Actuation is { Default: not 0f }) DepthsAreKnown = true;
        if (profile.Theme is not null)
        {
            ColorsAreKnown = true;
            LastBrightness = profile.Theme.Brightness;
            // Per-key colours are what select the firmware's custom mode, so any animation is over.
            ActiveMode = null;
            BacklightOff = false;
        }

        ActuationPreviewMm = null;
        ActuationChanged?.Invoke();
        LightingChanged?.Invoke();
    }

    private void HookSession(KeyboardSession session)
    {
        session.Disconnected += OnSessionDisconnected;
    }

    private void UnhookSession(KeyboardSession session)
    {
        session.Disconnected -= OnSessionDisconnected;
    }

    private void OnSessionDisconnected(object? sender, EventArgs e)
    {
        _session = null;
        _sim = null;
        DetachWebHid();
        ResetShadowState();
        _store.SetDisconnected("The keyboard was disconnected.");
    }

    // What this session knew about the board's settings dies with the session: the next one
    // starts from the SDK's invented defaults again, not from anything the hardware reported.
    private void ResetShadowState()
    {
        DepthsAreKnown = false;
        ColorsAreKnown = false;
        ActuationPreviewMm = null;
        ActiveMode = null;
        BacklightOff = false;
        LastBrightness = 9;
    }

    private void PublishConnected(bool demo)
    {
        var s = _session!;
        bool unverified = !string.Equals(s.Model.Slug, ModelSlugs.A75, StringComparison.OrdinalIgnoreCase);
        _store.SetConnected(s.Model.Name, s.Variant, s.FirmwareVersion, demo, unverified);
    }

    public async ValueTask DisposeAsync()
    {
        if (_session is not null)
            await DisconnectAsync().ConfigureAwait(false);

        if (_module is not null)
        {
            try { await _module.DisposeAsync().ConfigureAwait(false); }
            catch (JSDisconnectedException) { /* the page is already gone */ }
            _module = null;
        }
    }
}
