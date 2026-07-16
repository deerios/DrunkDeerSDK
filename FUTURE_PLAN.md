# FUTURE_PLAN — DrunkDeer CLI + Web UI

> **Where the code went.** Both consumers now exist and neither is described by this file's layout
> sections any more. The CLI shipped as `deerkb` in `DrunkDeer.Cli/`. The web app shipped and then
> moved to its own repository — <https://github.com/deerios/DrunkDeerWeb> (`~/Projects/DrunkDeerWeb`)
> — where it consumes this SDK as the `DrunkDeerSDK` NuGet package rather than a ProjectReference.
> Everything below is kept as the reasoning that produced them, so read it as history: any path
> starting `DrunkDeer.Web/` is now a path in that other repository.

Plan for two new consumers of the DrunkDeer SDK:

1. **`deer`** — a modern, git-style CLI built on `System.CommandLine`.
2. **DrunkDeer.Web** — a Blazor WebAssembly app that talks to the keyboard directly
   from the browser via WebHID, re-using the C# SDK, featuring a live on-screen
   keyboard with per-key actuation bars and RGB rendering.

Both consumers share one hard constraint: **they discover the keyboard model at
runtime**, while the SDK's full API is currently only reachable through
compile-time typed sessions (`KeyboardSession<A75Ultra>`). That, plus the
sync-only transport, means there is a mandatory "Phase 0" of SDK work before
either app can be built properly. It is small compared to the apps themselves
and benefits every future consumer.

---

## 1. Current SDK reality (what the plan is grounded in)

- `KeyboardSession` (untyped) publicly exposes only the universal surface:
  polling/events, key heights, actuation/downstroke/upstroke writes, live RGB,
  rapid trigger / turbo / last-win, `ApplyProfile`.
- Everything programmable (`IHasFuncBlock`: keymaps, macros, DKS, multi-tap,
  toggle keys, profile slots, debounce, report rate, presets, calibration,
  reset) is **`internal`** on `KeyboardSession` and only surfaced through
  generic extension methods constrained on marker interfaces
  ([KeyboardSessionExtensions.cs](DrunkDeer/KeyboardSessionExtensions.cs)).
  External code that doesn't know the model at compile time **cannot call any
  of it**.
- `IKeyboardConnection` is a **synchronous, blocking** seam
  (`Receive(timeoutMs)`), and the poll loop runs blocking reads on a
  `Task.Run` thread. This is fine on desktop, fatal on single-threaded WASM.
- `KeyLayout` is `internal`, maps firmware slot index → token name
  (`"SWUNG"`, `"VIRGUE"`, …), and has **no physical geometry** (key x/y,
  widths, ISO/ANSI shapes). An on-screen keyboard cannot be drawn from it.
- `KeyboardDiscoverer.KnownIdentifiers` (the VID/PID list) is `private` —
  the web app needs those pairs for WebHID `requestDevice` filters.
- Session shadow state (`_rgbProfile`, `_actuationProfile`, …) has no public
  read access; a UI can't render "current" colors/depths without tracking
  every write itself.
- Threading contract: all events fire on the poll thread; config methods
  require polling stopped (`EnsureNotPolling`).
- Only the **A75** is human-verified hardware. The A75's output report 4
  physically holds **63 payload bytes**, not 64; `HidTransport.Send` drops
  trailing zero padding only ([HidTransport.cs:43](DrunkDeer/Transport/HidTransport.cs#L43)).
- Per-key depth writes are always **full-profile writes**: on models without
  read-back, the first per-key write in a fresh session rewrites every other
  key to the SDK's 2.0 mm default (documented in
  [KeyboardSession.cs:812](DrunkDeer/KeyboardSession.cs#L812-L819)). A
  one-shot CLI process is exactly the "fresh session" worst case.

---

## 2. Bugs & gaps found during this planning pass

Declared here as requested; the fixes are folded into Phase 0.

| ID | Severity | Finding |
|---|---|---|
| PLAN-BUG-1 | Medium | ✅ **Fixed.** **Connection leak on session-constructor throw.** `KeyboardSession<TModel>.OpenFirst()` opens a `KeyboardConnection`, then the base ctor can throw (`KeyLayout.GetLayout` → `NotSupportedException` for a slug without a layout case) or the derived ctor throws `DrunkDeerModelMismatchException` ([KeyboardSession.Typed.cs:26](DrunkDeer/KeyboardSession.Typed.cs#L26-L30)). Neither path disposes the just-opened connection, so the hidraw streams stay open until GC — and the keyboard reports "in use by another process" to the next opener. `KeyboardConnection.Open` itself disposes carefully on handshake failure; the sessions don't. — *Base ctor now wraps its work in try/catch-dispose; the typed ctor disposes before throwing mismatch. Regression tests in [SessionLifecycleFixTests.cs](DrunkDeer.FeatureTests/Features/SessionLifecycleFixTests.cs) and [TypedSessionModelCheckTests.cs](DrunkDeer.FeatureTests/Features/TypedSessionModelCheckTests.cs).* |
| PLAN-BUG-2 | Medium | ✅ **Fixed.** **Typed `OpenFirst` doesn't scan for a matching model.** With two different keyboards attached, `KeyboardSession<A75Ultra>.OpenFirst()` binds whichever device completes the handshake first and throws mismatch if it's the A75 — it never tries the next candidate. `KeyboardDiscoverer.OpenFirst` has retry-next-candidate logic for handshake failures but nothing filters by expected slug. — *Added `KeyboardDiscoverer.OpenAll()` (lazy, yields each handshaking connection, caller-owned); typed `OpenFirst` scans it, disposes mismatched candidates, and only throws when none match (message lists what was found). Discovery-dependent, so no hardware-free unit test.* |
| PLAN-BUG-3 | Low | ✅ **Fixed.** **`StartPolling` leaks the previous CTS** when the prior loop exited on its own (disconnect): [KeyboardSession.cs:486](DrunkDeer/KeyboardSession.cs#L486-L495) sees `IsCompleted == true`, then overwrites `_pollCts` without disposing the old one. — *`StartPolling` now disposes the stale CTS before overwriting; regression test in [SessionLifecycleFixTests.cs](DrunkDeer.FeatureTests/Features/SessionLifecycleFixTests.cs).* |
| PLAN-GAP-4 | Blocker (CLI/Web) | Programmable API unreachable at runtime (internal-only, typed-extension gated) — see §1. |
| PLAN-GAP-5 | Blocker (Web) | `IKeyboardConnection` is sync/blocking; no async transport or async session surface exists. WASM cannot block its only thread. |
| PLAN-GAP-6 | Blocker (Web) | No public layout/geometry API; `KeyLayout` internal, no physical key positions. |
| PLAN-GAP-7 | Minor | ✅ **Fixed.** VID/PID discovery pairs private; web needs them for WebHID filters. Should be codegen'd public from `models.yaml`. — *Codegen now emits `ModelRegistry.DiscoveryPairs` (public `(int Vid, int Pid)[]`) from the discovery section of models.yaml; `KeyboardDiscoverer` consumes it, dropping the private list and its "kept in sync" comment-contract. Test in [ModelRegistryTests.cs](DrunkDeer.ProtocolTests/Protocol/ModelRegistryTests.cs).* |
| PLAN-GAP-8 | Minor | ✅ **Fixed.** No public read of session shadow state (current RGB per key, depth profiles) for UI initial render. — *Added `GetKeyColor`/`GetActuationProfile`/`GetDownstrokeProfile`/`GetUpstrokeProfile` ([ShadowStateReadbackTests.cs](DrunkDeer.FeatureTests/Features/ShadowStateReadbackTests.cs)) and shipped `DrunkDeer.Simulation.SimulatedKeyboardConnection` — a hardware-free connection that synthesises poll-loop travel frames (standard + high precision) and ACKs every config write ([SimulatedKeyboardConnectionTests.cs](DrunkDeer.FeatureTests/Features/SimulatedKeyboardConnectionTests.cs)). Note: implements the sync `IKeyboardConnection`; the async twin waits on GAP-5.* |
| PLAN-NOTE-9 | Design constraint | Fresh-session per-key writes clobber unknown state on non-HighPrecision models (§1 last bullet). Not an SDK bug — firmware has no partial write — but the CLI must mitigate (state cache, §4.6) and the web UI must warn before its first per-key write. |

Known limitations that shape but don't block this plan: the data stream is
opened but never drained (poll loop is command-stream only), chunked writes
(`WriteFuncBlockChunk`/`WriteKeyTriggerChunk`/`WriteMacroChunk`) would still
overflow a 63-byte report — moot on A75 (not programmable), unverified on
programmable models; and `RestoreFactorySettings` stays `internal`/unexposed.

---

## 3. Phase 0 — SDK enablement work (prerequisite for both apps)

### 3.1 Async transport + async session surface (fixes PLAN-GAP-5)

- New `IKeyboardConnectionAsync` (`ValueTask SendAsync(byte[], CancellationToken)`,
  `ValueTask<byte[]?> ReceiveCommandAsync(int timeoutMs, CancellationToken)`, …)
  alongside the existing sync interface.
- Make the session core async-first internally: one **single-threaded async
  command pump** owns the connection; sync public methods stay as thin
  blocking wrappers for desktop (safe there), async twins
  (`SetActuationPointAsync`, `StartPollingAsync`, …) are added for WASM.
- **Design win worth taking:** once a single pump owns the wire, config
  commands can be *queued between poll frames* instead of requiring
  `StopPolling()` first. `EnsureNotPolling` friction disappears for the web
  UI (live slider drags while the heatmap keeps running). Keep
  `EnsureNotPolling` semantics on the sync path; relax on the async path.
- Desktop `KeyboardConnection` implements both interfaces (async over
  HidSharp's stream with `Task.Run` per read is acceptable there).
- This is the largest, riskiest Phase 0 item — architecture-heavy, touches
  the poll loop. Assign to Opus (§7).

### 3.2 Runtime capability facades (fixes PLAN-GAP-4)

- Public capability interfaces mirroring the extension groups:
  `IProgrammableKeyboardFeatures` (FuncBlock surface),
  `IHighPrecisionFeatures`, `ILogoLightFeatures`, `ISideLightFeatures`.
- Untyped `KeyboardSession` gains `bool Supports(Capabilities)` plus
  `TryGetFeatures<TFeatures>(out TFeatures?)` / `GetFeatures<TFeatures>()`
  (throws a clear `DrunkDeerCapabilityException` naming the model). The
  implementation just runtime-checks `Model.Capabilities` and forwards to the
  existing internal methods — no protocol changes.
- Generate the facade plumbing from the same YAML capability data so it can't
  drift from the marker interfaces (extend `Templates/Models.sbn` or a new
  template; regen via `dotnet run --project DrunkDeer.CodeGen/DrunkDeer.CodeGen.csproj`;
  CI drift check already covers it).
- The typed extension methods become one-line forwards to the facades —
  IntelliSense gating for compile-time users is preserved.

### 3.3 Public layout + physical geometry (fixes PLAN-GAP-6)

- New `protocol/geometry/*.yaml`: per model+variant, an ordered list of keys:
  `{ slot: <firmware index>, key: <DDKey>, legend: "Esc", x, y, w, h }` in
  key units (1u grid, KLE conventions; ISO Enter as the usual two-rect or
  path special case). Codegen → `KeyGeometry.g.cs`.
- Public API: `session.Layout` → `IReadOnlyList<KeyInfo>` with
  `{ DDKey Key, int SlotIndex, string Legend, float X, float Y, float W, float H }`
  plus board width/height. Make `KeyLayout`'s name/index mapping public
  through the same type; keep the raw internal arrays as-is.
- One data source then serves: web on-screen keyboard, CLI `--keys` name
  parsing and `deer watch` TUI rendering, and future per-model docs.
- Start with **A75 ANSI + ISO** (verified hardware), then G75/G65/G60 marked
  `// TODO: verify via capture` per existing convention.
- Mechanical, well-specified — Sonnet task (§7).

### 3.4 Discovery & selection improvements (fixes PLAN-BUG-1/2, PLAN-GAP-7)

- Wrap both session constructors' post-open work in try/catch-dispose (or a
  private static factory) so a throwing ctor never leaks the connection.
- `KeyboardDiscoverer.OpenAll()` / `Open(Func<candidate,bool>)`, and typed
  `KeyboardSession<TModel>.OpenFirst()` skips handshaked devices whose slug
  doesn't match `TModel.Slug` (keep trying candidates; only throw mismatch if
  a match never appears — include what *was* found in the message).
- Expose device identity (path/serial) so `deer --device <serial|path>` can
  target one of several keyboards.
- Codegen a public `ModelRegistry.DiscoveryPairs` (VID/PID list) from
  `models.yaml`; `KeyboardDiscoverer` consumes it instead of its private copy
  (removes today's "kept in sync with models.yaml" comment-contract).
- Fix PLAN-BUG-3 while in the area (dispose stale CTS in `StartPolling`).

### 3.5 State read-back & simulator (fixes PLAN-GAP-8, enables hardware-free dev)

- Public getters for shadow state: `GetKeyColor(DDKey)`, `GetCurrentTheme()`,
  `GetActuationProfile()` etc. (documented as "what this session last wrote /
  seeded defaults", not hardware truth on non-HP models).
- `SimulatedKeyboardConnection` (in a new `DrunkDeer.Testing` or shipped in
  the SDK under a `Simulation` namespace): implements the async interface,
  synthesizes plausible travel frames (idle noise + scripted/interactive key
  presses), accepts all config writes. Distinct from `FakeKeyboardConnection`
  (which is a strict response *queue* for tests, not a simulator). Purpose:
  web/CLI development and demo mode without hardware — critical since only
  one A75 exists to test against, and subagents have none.

**Phase 0 sizing:** 3.1 = L, 3.2 = M, 3.3 = M (data entry heavy), 3.4 = S, 3.5 = S/M.

---

### 4.5 Architecture

```
DrunkDeer.Cli/
  Program.cs               (root command wiring only)
  Infrastructure/
    SessionFactory.cs      (device selection, ILoggerFactory from -v, IKeyboardConnection injection seam)
    OutputWriter.cs        (human vs --json rendering; every handler returns a result model)
    Confirmation.cs        (flash-write prompts, --yes, non-TTY ⇒ require --yes)
    StateCache.cs          (§4.6)
    KeyArgParser.cs        (DDKey names, ranges "F1-F12", groups "wasd", from geometry legends)
  Commands/
    <one file per command group; handlers are thin — parse → call SDK → shape result>
DrunkDeer.Cli.Tests/       (handlers run against FakeKeyboardConnection / SimulatedKeyboardConnection;
                            snapshot tests over --json output and --help text)
```

Design rules: handlers never `Console.Write` directly (all output through
`OutputWriter` so `--json` is total); every command that writes flash goes
through `Confirmation`; capability checks produce exit code 4 with a message
naming the model *and* the models that do support the feature.

### 4.6 The state-cache problem (PLAN-NOTE-9 — important)

A one-shot `deer actuation set 0.2 --keys W,A,S,D` on a non-HighPrecision
board would silently reset every other key to 2.0 mm. Mitigation:

- Per-keyboard state cache at `~/.config/deer/state/<serial>.json` holding the
  last full depth profiles this CLI wrote.
- On per-key writes: HP models → read back from hardware and merge (truth);
  others → merge over the cache; **no cache present → refuse** with an
  explanation and the escape hatches (`--all-others <mm>` to set the rest
  explicitly, or run `deer actuation set <uniform>` once to establish state).
- `deer state show|clear` for transparency. Cache is advisory; `--json`
  output marks values as `"source": "cache" | "device"`.

### 4.7 Use cases the design must serve

- **Gaming setup script**: `deer rt on && deer actuation set 0.2 --keys wasd && deer light set --map fps.json` (fish/bash scriptable, exit codes reliable).
- **Backup/restore**: `deer profile capture -o backup.json` → later `deer profile apply backup.json`.
- **Diagnostics**: `deer watch` to verify a flaky switch; `deer info --json` in bug reports.
- **Fleet/scripted** (streamers, LAN setups): `--device` + `--json` + `--yes` make it automatable; JSON-lines from `watch --raw` feeds OBS overlays.

---

## 5. The Web UI — Blazor WebAssembly + WebHID

### 5.1 Hosting model decision

**Standalone Blazor WebAssembly** (no server): the keyboard is attached to the
*user's* machine, so the HID access must happen in *their* browser — exactly
what WebHID provides and precisely why Blazor Server (HID would run on the
web server) is wrong here. Ship as a static PWA on GitHub Pages; works
offline after first load. .NET 10 `[JSImport]/[JSExport]` interop (no
`IJSUnmarshalledRuntime` — that API is gone).

Reality checks to state on the landing page: WebHID is **Chromium-only**
(Chrome/Edge/Opera; no Firefox/Safari) and requires a secure context (HTTPS
or localhost). Feature-detect `navigator.hid` and show a friendly
unsupported-browser page. DrunkDeer's own official configurator is a WebHID
web app, so the vendor command interface is known to pass Chrome's HID
blocklist (the *typing* interface — usage page 1, usage 6 — is blocked as a
protected keyboard collection; we never need it).

### 5.2 WebHID transport (`WebHidKeyboardConnection : IKeyboardConnectionAsync`)

- JS module `webhid.js` + C# interop wrapper. `requestDevice` filters come
  from `ModelRegistry.DiscoveryPairs` (§3.4).
- Chrome returns one `HIDDevice` per HID interface of the granted physical
  device. Pick the command interface by mirroring `IsCommandInterface`:
  a top-level **vendor-defined usage page** collection whose output *and*
  input report 4 sizes are ≥ 63 bytes (compute from
  `collections[].outputReports[].items`).
- **Report-ID asymmetry (gotcha):** hidraw reads include the report-ID byte
  (the SDK strips `buf[0]`); WebHID gives `inputreport` events already
  stripped, and `sendReport(reportId, data)` takes the ID separately. The JS
  layer must therefore: send with `reportId=4` and payload only; on receive,
  hand the SDK the raw `event.data` bytes **without** stripping anything.
  Do the strip/prepend adaptation in exactly one place and unit-test it.
- **63-byte capacity rule:** replicate `HidTransport.Send`'s
  zero-padding-only truncation (drop trailing zero bytes beyond the device's
  report size; throw if non-zero payload would be dropped). The A75's report
  4 holds 63 bytes; this rule is what keeps the handshake working.
- Input reports arrive as events → push into a bounded
  `Channel<byte[]>`; `ReceiveCommandAsync` reads with timeout via
  `CancellationTokenSource.CancelAfter`. `FlushReadBuffer` = drain channel.
- No `serialNumber` exists in WebHID, so the desktop data-stream binding
  heuristic is impossible — **command-stream polling only**, which is already
  the SDK's fallback path (and the data stream is never drained today anyway).
- Disconnect: subscribe `navigator.hid` `disconnect` events → surface as the
  session's `Disconnected` event.

### 5.3 App structure

```
DrunkDeer.Web/
  wwwroot/js/webhid.js         (ES module; the only JS in the app)
  Transport/                   (WebHidKeyboardConnection + interop)
  Services/
    KeyboardService.cs         (connect/disconnect lifecycle, owns the session, exposes state)
    KeyboardStore.cs           (UI state: colors, depths, selection; event → store → components)
    DemoService.cs             (SimulatedKeyboardConnection-backed demo mode)
  Components/
    Keyboard/                  (§5.4: KeyboardView, KeyCap, GlowLayer, DepthBar)
    Panels/                    (ActuationPanel, LightingPanel, TriggerPanel, …)
    Shared/                    (layout, nav, capability-gate wrapper component)
  Pages/
    Connect.razor              (browser check, requestDevice button, demo-mode entry)
    Dashboard.razor            (live keyboard + quick stats)
    Actuation.razor  Lighting.razor  Keymap.razor  Macros.razor  Profiles.razor
    Settings.razor   Diagnostics.razor (TX/RX log, poll stats, report descriptor dump)
```

- **Component library:** recommend **MudBlazor** — professional out of the
  box, themable via a single palette object (CSS variables under the hood),
  and it ships the sliders, dialogs, and crucially the **color picker** this
  app needs. The keyboard visual itself stays 100% bespoke (plain
  HTML/CSS/SVG in scoped `.razor.css`) so styling it is never fighting a
  framework. Alternative if zero dependencies preferred: hand-rolled + CSS
  custom properties; costs ~a week extra in form controls.
- **Capability gating in UI:** one `<RequiresCapability Capability="…">`
  wrapper that renders its child panel, a "not supported on {model}" card, or
  a demo watermark. Pages don't hand-roll checks.
- **Unverified-model banner:** anything but A75 gets a persistent "protocol
  unverified on this model — flash writes at your own risk" banner, and flash
  writes get a confirm dialog listing exactly what will be written (mirrors
  the repo's flash-write safety convention).

### 5.4 The on-screen keyboard (the centerpiece)

Data: geometry from `session.Layout` (§3.3) — the component is model-agnostic
and renders whatever board is connected.

Rendering — layered HTML/CSS per key inside one absolutely-positioned board
(no canvas; DOM+CSS keeps styling trivial and 60fps is comfortably achievable
for ≤ 128 keys when animation bypasses the Blazor render tree, below):

```
.board                 (position:relative; scales px-per-u from container width)
└─ .key (per KeyInfo, absolute at x/y/w/h)
   ├─ .glow      z:0   bottom-anchored radial gradient, blur; color: var(--kc); opacity/scale: var(--glow)
   ├─ .cap       z:1   dark keycap face, subtle top-light border (the glow "leaks" around it from below)
   │   └─ .legend      color: var(--kc)          ← letter takes the key's RGB color
   └─ .depth-bar z:2   right-edge vertical track; fill height: var(--depth);
                       tick mark at var(--act) (actuation point); fill turns accent color when pressed
```

- Per-key visual state lives entirely in **CSS custom properties**
  (`--kc`, `--depth`, `--act`, `--glow`) on the `.key` element.
- **Hot path bypasses Blazor rendering:** the session's `Polled` event writes
  heights into a `float[]`; a `requestAnimationFrame` loop in `webhid.js`
  (fed via `[JSImport]` batch call, or reading a shared buffer) patches only
  the CSS variables of keys whose value changed. Blazor renders *structure*
  (on connect/model change/selection); it never re-renders 80 components at
  poll rate. This is the single most important performance decision in the app.
  Poll (~200 Hz) → coalesce to rAF (60 fps).
- Colors change rarely (user edits) → those go through normal Blazor
  state → `--kc`.
- ISO Enter / stepped caps handled as geometry special cases (two-rect or
  SVG path clip).
- The keyboard is also the app's **selection surface**: click/drag-marquee/
  shift-click keys, `wasd`-style group presets; the active side panel
  (actuation, lighting, keymap…) edits the selection. One component, several
  overlay modes (depth bars prominent in actuation mode, glow prominent in
  lighting mode, remap labels in keymap mode).
- Accessibility & polish: keys are buttons with `aria-pressed`, focus ring,
  `prefers-reduced-motion` disables glow animation, `contain: strict` on
  keys, only `transform`/`opacity` animate.

### 5.5 Sessions, threading, and state

- One `KeyboardService` singleton owns the async session; components
  subscribe to the store, never to session events directly. In WASM
  everything is on the main sync context — but keep `InvokeAsync` discipline
  anyway so the code survives future WASM threading or a desktop
  (BlazorWebView/MAUI) reuse of the same components.
- Config writes go through the async pump (§3.1) — no stop/start polling
  dance in the UI; a busy indicator per panel while a write is in flight.
- Demo mode (SimulatedKeyboardConnection) is first-class: the deployed site
  is fully explorable without hardware, which is also how most development
  and all component tests (bUnit) run.

---

## 6. Repo, CI, and delivery

- New projects added to `DrunkDeer/DrunkDeer.slnx`: `DrunkDeer.Cli`,
  `DrunkDeer.Cli.Tests`, `DrunkDeer.Web`, `DrunkDeer.Web.Tests` (bUnit),
  optionally `DrunkDeer.Testing` (simulator, if not folded into the SDK).
- CI additions: build/test the new projects in the existing workflow; codegen
  drift check already covers new templates; add a GH Pages publish job for
  `DrunkDeer.Web` (on tag or main), and `dotnet pack` artifact for the CLI tool.
- Docs: README gets short CLI + Web sections; wiki pages "CLI" and "Web UI";
  `deer` help text is the CLI's real spec — keep it excellent.

### Phasing

| Phase | Contents | Size |
|---|---|---|
| 0 | §3 SDK work (3.4 + 3.2 first — they unblock the CLI without waiting for async; 3.1 + 3.3 + 3.5 next) | L |
| 1 | CLI core: infrastructure, `devices/info/watch`, actuation/rt/turbo/light, profile capture/apply, tests | M/L |
| 2 | CLI programmable groups (keymap/macro/dks/profile slots/config) + state cache + completions + packaging | M |
| 3 | Web foundation: WebHID transport, connect flow, KeyboardService/store, demo mode, app shell | M/L |
| 4 | On-screen keyboard component + actuation & lighting panels (the demo-able milestone) | L |
| 5 | Web programmable editors (keymap, macros, profiles), diagnostics page, PWA/Pages deploy, polish | M/L |

Phases 1–2 (CLI) and 3–5 (Web) can run in parallel once Phase 0 lands.
Hardware verification note: everything is developed against the simulator and
`FakeKeyboardConnection`; only A75-verifiable behavior gets manually confirmed
on the real board before release. Programmable-model features ship flagged
"unverified hardware" until captures exist.

---

## 7. Subagent playbook (Opus / Sonnet prompts)

### 7.1 Routing guidance

| Task type | Model | Why |
|---|---|---|
| §3.1 async transport/session refactor | **Opus** | Cross-cutting, concurrency, easy to subtly break the poll loop |
| §3.2 capability facades + codegen template | **Opus** (design) then Sonnet (mechanical regen fixes) | Template + generator changes must stay in lockstep |
| §3.3 geometry YAML data entry + codegen | **Sonnet** | Well-specified, mechanical, verifiable against layout arrays |
| §3.4 discovery fixes (PLAN-BUG-1/2/3) | **Sonnet** | Small, sharply specified diffs with tests |
| CLI infrastructure + first command groups | **Opus** first (sets the pattern), then **Sonnet** per command group | Command groups become cookie-cutter after the pattern exists |
| WebHID JS transport | **Opus** | Interop + protocol edge cases (report IDs, 63-byte rule) |
| Keyboard component (rendering/perf) | **Opus** | The rAF/CSS-var bypass must be designed, not accreted |
| Panels, pages, bUnit tests, docs | **Sonnet** | Pattern-following once the store/component idioms exist |

### 7.2 Shared preamble (paste at the top of every subagent prompt)

```text
Repo: /home/addi/Projects/DrunkDeerSDK — .NET 10 SDK for DrunkDeer analog HID keyboards.
Ground rules:
- Build: dotnet build DrunkDeer/DrunkDeer.slnx --nologo
  Test:  dotnet test DrunkDeer/DrunkDeer.slnx --nologo   (all tests must stay green)
- NEVER hand-edit DrunkDeer/Generated/*.g.cs. Change protocol/*.yaml and/or
  DrunkDeer/Templates/*.sbn, then regenerate:
  dotnet run --project DrunkDeer.CodeGen/DrunkDeer.CodeGen.csproj
  and commit YAML/template + regenerated output together (CI enforces drift).
- Indentation is file-local: KeyboardSession.cs and most of the library use TABS;
  KeyboardSessionExtensions.cs and generated code use 4 spaces. Match the file.
- Comments: plain English for developers. No audit IDs, no meta-narration.
  Unfixed issues: "// Known limitation: <behavior> + <evidence a fix needs>".
- Flash-write safety: anything writing keyboard flash must validate all indices
  first and be tested against FakeKeyboardConnection — never speculatively on
  real hardware. FakeKeyboardConnection is a response QUEUE (enqueue counts must
  match chunk math exactly) — prefer its higher-level helpers.
- Only the A75 is human-verified hardware. Its HID output report 4 holds 63
  payload bytes (not 64); HidTransport.Send drops trailing ZERO padding only.
  Preserve that behavior in any transport work.
- Shell is fish: quote globs (--include='*.cs'); prefer ripgrep.
- Wire-format knowledge belongs in protocol/*.yaml comments, not C# comments.
```

### 7.3 Ready-to-use task prompts

**Opus — async transport & session pump (§3.1):**

```text
<shared preamble>
Task: introduce an async transport seam and an async command pump in the DrunkDeer SDK
so it can run on Blazor WebAssembly (single-threaded; blocking reads are fatal).
1. Add IKeyboardConnectionAsync (SendAsync / SendAndReceiveAsync / ReceiveCommandAsync /
   FlushReadBufferAsync, CancellationToken-first) next to IKeyboardConnection.
2. Refactor KeyboardSession so ONE async pump owns all wire I/O. Keep the existing
   public sync API working identically on desktop (sync wrappers may block there).
   Add async twins for: StartPollingAsync/StopPollingAsync and all public config
   methods. Preserve the documented threading contract for the sync path
   (events on poll thread; EnsureNotPolling on sync config methods).
3. On the async path, allow config commands to be queued between poll frames
   instead of requiring polling to stop. Document the new contract in the README
   Threading section.
4. KeyboardConnection implements both interfaces. FakeKeyboardConnection gets an
   async twin or adapter so ALL existing FeatureTests also run against the async path.
Do not change any wire bytes: ProtocolTests must pass unmodified. Poll-loop
behavior (frame collection, resync-on-desync, dropped-frame flush) must be
preserved — port it, don't redesign it.
Acceptance: full test suite green; new tests covering (a) async poll loop via fake,
(b) config-between-frames interleaving, (c) cancellation of a pending ReceiveAsync.
```

**Sonnet — geometry data + public layout API (§3.3):**

```text
<shared preamble>
Task: add physical key geometry and a public layout API.
1. Create protocol/geometry/a75.yaml (ansi + iso variants): for every non-empty
   slot in KeyLayout.LayoutA75 (DrunkDeer/Keys/KeyLayout.cs), one entry
   { slot, key (DDKey name), legend, x, y, w, h } in key units, KLE conventions
   (Esc at 0,0; standard 75% layout; ISO Enter special-cased).
2. Extend the codegen (Generator.cs + a new Templates/Geometry.sbn) to emit
   Generated/KeyGeometry.g.cs. Remember: adding YAML fields means updating BOTH
   Generator.cs and the template.
3. Public API: KeyboardSession.Layout returning IReadOnlyList<KeyInfo>
   { DDKey Key; int SlotIndex; string Legend; float X, Y, W, H } plus
   BoardWidth/BoardHeight, sourced from the generated data by Model.Slug+Variant.
4. Tests (DrunkDeer.ProtocolTests style): every geometry slot maps to a non-empty
   layout token; no overlapping key rectangles; every DDKey in the session's
   key-index map appears exactly once in the geometry.
Do NOT invent geometry for other models yet. Verify slot indices against the
layout array comments (e.g. 14=Delete, 36=Home, 98=ArrowUp).
```

**Sonnet — discovery fixes (§3.4, PLAN-BUG-1/2/3):**

```text
<shared preamble>
Task: three small fixes with tests, one commit each.
1. KeyboardSession and KeyboardSession<TModel> constructors can throw after the
   KeyboardConnection was opened (unknown-layout slug; model mismatch in
   KeyboardSession.Typed.cs) — the connection leaks. Ensure every ctor throw path
   disposes the connection it was handed when the session itself opened it
   (OpenFirst paths). Test: FakeKeyboardConnection with a mismatched slug →
   assert Dispose was called.
2. KeyboardSession<TModel>.OpenFirst binds the first device that completes the
   handshake even if its slug doesn't match TModel — with two keyboards attached
   the wrong one wins and it throws instead of trying the next candidate. Add
   candidate iteration that skips slug mismatches (dispose skipped connections!)
   and only throws if no candidate matches; the error must list what WAS found.
3. StartPolling overwrites _pollCts without disposing it when the previous loop
   exited on its own. Dispose the stale CTS.
Match each file's existing indentation (tabs in KeyboardSession.cs).
```

**Opus — CLI skeleton (§4, sets the pattern):**

```text
<shared preamble>
Task: create DrunkDeer.Cli (net10.0, PackAsTool, ToolCommandName=deer) and
DrunkDeer.Cli.Tests; add both to DrunkDeer/DrunkDeer.slnx.
CRITICAL: use System.CommandLine 2.0 CURRENT API — Command/Option<T>/Argument<T>,
command.SetAction(parseResult => ...), parseResult.GetValue(option). The old
beta4 SetHandler/binder API does not compile; do not emit it. Check the restored
package version and compile early.
Deliver: Program.cs root wiring; global options (--device --json --yes --quiet
--verbose --timeout); Infrastructure/{SessionFactory,OutputWriter,Confirmation,
KeyArgParser}; commands: devices, info, actuation get/set, light set/mode/off,
rt on/off, turbo on/off. Handlers return result models; OutputWriter renders
human (Spectre.Console) or --json. Exit codes: 0 ok, 1 failure, 2 usage,
3 no device, 4 capability unsupported, 5 aborted, 6 busy/handshake.
SessionFactory takes an injectable connection factory so tests run handlers
against FakeKeyboardConnection end-to-end (assert on JSON output + exit code).
Per-key depth writes: refuse on non-HighPrecision models without cached state
(see FUTURE_PLAN.md §4.6) — implement the refusal + --all-others escape hatch;
the state cache itself is a later task.
```

**Opus — WebHID transport (§5.2):**

```text
<shared preamble>
Task: DrunkDeer.Web (standalone Blazor WASM, net10.0) with a working WebHID
transport implementing IKeyboardConnectionAsync (from the async SDK work).
wwwroot/js/webhid.js (ES module, [JSImport]/[JSExport] interop):
- requestDevice with VID/PID filters from ModelRegistry.DiscoveryPairs.
- Select the command interface: vendor-defined top-level usage page collection
  with input AND output report 4 payload sizes >= 63 (compute from
  collections[].{input,output}Reports[].items).
- sendReport(4, payload): payload EXCLUDES the report-ID byte. Replicate
  HidTransport.Send's rule: if payload exceeds the device's report size, drop
  trailing bytes ONLY if all zero, else throw. (A75 report 4 = 63 bytes.)
- inputreport events arrive WITHOUT the report-ID byte — pass bytes through
  unmodified (the desktop hidraw path strips buf[0]; WebHID is pre-stripped;
  do NOT strip again). Push into a bounded Channel<byte[]>;
  ReceiveCommandAsync = channel read with timeout. FlushReadBuffer = drain.
- navigator.hid disconnect events → connection-level Disconnected callback.
No serialNumber exists in WebHID: command-stream polling only (SDK fallback).
Acceptance: Connect page performing the identity handshake against a real A75
(maintainer will manually test); unit tests for the 63-byte rule and report-ID
pass-through using a scripted fake JS layer; unsupported-browser page when
navigator.hid is absent.
```

**Opus — on-screen keyboard component (§5.4):**

```text
<shared preamble>
Task: the live keyboard component for DrunkDeer.Web. Read FUTURE_PLAN.md §5.4
first — the layered key anatomy (glow/cap/legend/depth-bar), CSS-custom-property
state (--kc --depth --act --glow), and the requirement that per-frame updates
BYPASS Blazor rendering (Polled event → float[] → one JS batch call → rAF loop
patches CSS vars on changed keys only). Blazor renders structure and selection
only. Geometry comes from session.Layout; the component must render any model.
Include: selection surface (click/shift/drag-marquee), overlay modes
(actuation/lighting/keymap), ISO Enter special case, prefers-reduced-motion,
demo mode driven by SimulatedKeyboardConnection so it runs without hardware.
Acceptance: bUnit tests for structure/selection; a Diagnostics counter proving
steady-state renders/sec ≈ 0 while depth animation runs at 60fps in demo mode.
```

**Sonnet — panel/page template (repeat per panel):**

```text
<shared preamble>
Task: implement the <Actuation|Lighting|Keymap|...> panel in DrunkDeer.Web
following the existing pattern (see Components/Panels/ and KeyboardStore).
Read FUTURE_PLAN.md §5.3/§5.4. Rules: subscribe to KeyboardStore, never to
session events directly; all writes via KeyboardService async methods; wrap
capability-gated UI in <RequiresCapability>; flash writes need the confirm
dialog listing exactly what will be written; non-A75 models show the
unverified-hardware banner. Add bUnit tests mirroring the existing panel tests.
```

---

## 8. Open questions (decide before the relevant phase, none block Phase 0)

1. CLI command name `deer` — confirm, or prefer `drunkdeer` as primary?
2. MudBlazor vs. fully hand-rolled UI chrome (recommendation: MudBlazor, §5.3).
3. Web deploy target: GitHub Pages under the repo, or a dedicated domain
   (affects PWA scope/base href)?
