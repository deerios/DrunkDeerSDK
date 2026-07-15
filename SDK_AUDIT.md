# DrunkDeerSDK — Technical Audit & Fix Playbook

Audit date: 2026-07-13. Scope: everything under `DrunkDeerSDK/` (library, codegen, tests,
analyzer, CI). The KeyboardPiano app was reviewed separately in `REVIEW.md`.

Method: full read of the library source, generated code, and protocol YAML; solution build;
test-suite execution (after temporarily restoring the missing project references — reverted
afterwards, the repo is untouched); codegen drift check (`dotnet run --project
DrunkDeer.CodeGen` produces byte-identical `Generated/` output — no drift).

Line numbers reference the audited commit (master, clean tree at audit time). If lines have
shifted, search for the quoted identifiers.

---

## 0. Executive summary

The SDK's architecture is genuinely good: YAML protocol definitions → Scriban codegen →
thin typed wrappers, a capability/marker-interface system for per-model APIs, an
`IKeyboardConnection` seam for testing, and a serialisable profile layer. The protocol
knowledge encoded in the YAML comments is excellent.

But the repository is **not currently in a shippable state**:

- **The solution does not compile from a clean clone.** Every test project and the
  ProtocolAnalyzer lack a `<ProjectReference>` to the core library (BLD-1).
- Once made to compile, **90 of 151 feature tests fail** — the suite was never updated for
  the capability-gating system and has clearly never run in CI (BLD-2, BLD-3).
- The hidden tests caught a **real conversion bug** (banker's rounding, API-6).
- There are four bugs I'd class as **critical for users**: a flash-address wraparound that
  can corrupt arbitrary regions of keyboard flash (API-1), high-precision poll-frame
  misalignment that permanently attributes key travel to the wrong keys (POLL-1),
  `SetKeyboardMode` ignoring its argument (API-2), and X60 Future silently receiving the
  A75 layout tables (API-5).

Finding count: 4 critical · 8 high · 12 medium · 12 low/hygiene, plus a
verify-on-hardware list and an improvement roadmap.

| ID | Severity | One-liner |
|---|---|---|
| BLD-1 | Critical | Test/analyzer projects have no reference to the SDK → solution doesn't build |
| API-1 | Critical | `profileIndex` unvalidated in ~20 flash APIs → `ushort` address wraparound → flash corruption |
| POLL-1 | Critical | HP travel packets have no section id; dropped frame → keys permanently misassigned |
| API-2 | Critical | `SetKeyboardMode` always writes Mac mode regardless of argument |
| BLD-2 | High | 90/151 feature tests fail (fixtures use non-programmable model vs. capability gates) |
| API-3 | High | First per-key `SetDownstrokePoint`/`SetUpstrokePoint` call throws; `CaptureProfile`→`ApplyProfile` round-trip throws |
| API-4 | High | `KeyboardSession<TModel>.OpenFirst()` never verifies the connected model is `TModel` |
| API-5 | High | `KeyLayout` falls back to A75 tables for unknown slugs — X60 Future gets wrong layout & LED map |
| API-6 | High | Mm→raw conversions use banker's rounding (test-proven failure) |
| POLL-2 | High | Exception in a user event handler silently kills the poll loop; surfaces later from `StopPolling` |
| TRN-1 | High | Discoverer matches VID×PID cross-product → false positives (Apple/Holtek VIDs) + ~10 s hang per false device |
| TRN-2 | High | Data-stream scan can attach another physical keyboard's interface (multi-device) |
| API-7…12, TRN-3…4, PROTO-1…3 | Medium | See §3 |
| DOC/GEN/LOW-* | Low | See §4 |

---

## 1. Critical findings

### BLD-1 — Solution does not compile: missing project references
**Where:**
[DrunkDeer.FeatureTests.csproj](DrunkDeerSDK/DrunkDeer.FeatureTests/DrunkDeer.FeatureTests.csproj),
[DrunkDeer.ProtocolTests.csproj](DrunkDeerSDK/DrunkDeer.ProtocolTests/DrunkDeer.ProtocolTests.csproj),
[DrunkDeer.Tests.csproj](DrunkDeerSDK/DrunkDeer.Tests/DrunkDeer.Tests.csproj),
[DrunkDeer.ProtocolAnalyzer.csproj](DrunkDeerSDK/DrunkDeer.ProtocolAnalyzer/DrunkDeer.ProtocolAnalyzer.csproj)

**Evidence:** `dotnet build DrunkDeer/DrunkDeer.slnx` → 38 × CS0246/CS0234 (`KeyboardSession`,
`DrunkDeer.Protocol` not found). The core `DrunkDeer.csproj` builds fine alone. The git tree
is clean — this is the committed state, so tests have never been runnable from a clone, which
is how BLD-2 and API-6 went unnoticed.

**Fix (agent instructions):**
1. `dotnet add DrunkDeerSDK/DrunkDeer.FeatureTests/DrunkDeer.FeatureTests.csproj reference DrunkDeerSDK/DrunkDeer/DrunkDeer.csproj`
2. Same for `DrunkDeer.ProtocolTests` and `DrunkDeer.ProtocolAnalyzer`.
3. Delete `DrunkDeer.Tests/` entirely (see LOW-1 — it is a stale pre-rename duplicate of
   ProtocolTests, not in the `.slnx`), and remove its `InternalsVisibleTo` entry from
   [DrunkDeer.csproj:24](DrunkDeerSDK/DrunkDeer/DrunkDeer.csproj#L24).
4. Verify: `dotnet build DrunkDeerSDK/DrunkDeer/DrunkDeer.slnx` is clean, then run both test
   projects.
5. Add a CI workflow that runs build + tests on push/PR (the only workflow today is
   [release.yml](DrunkDeerSDK/.github/workflows/release.yml), which packs and publishes to
   NuGet **without ever running a test**). Also add the codegen-drift check:
   run the generator, then `git diff --exit-code DrunkDeer/Generated`.

### API-1 — Unvalidated `profileIndex` → 16-bit flash address wraparound
**Where:** [KeyboardSession.cs:2013-2017](DrunkDeerSDK/DrunkDeer/KeyboardSession.cs#L2013-L2017)
(`FetchKeyTriggers`/`PushKeyTriggers`), and every other 0x55 helper that computes
`(ushort)(stride * profileIndex …)`: `ReadKeyTriggers`, `WriteKeyTriggers`, `SetKeyTrigger`,
`ReadKeyMap`/`WriteKeyMap`/`SetKey` (`KeyMapAddr`,
[2092](DrunkDeerSDK/DrunkDeer/KeyboardSession.cs#L2092)), DKS
([2200-2246](DrunkDeerSDK/DrunkDeer/KeyboardSession.cs#L2200-L2246)), MultiTap, Toggle, Macro
([2346-2393](DrunkDeerSDK/DrunkDeer/KeyboardSession.cs#L2346-L2393)), stored colours
([1287-1355](DrunkDeerSDK/DrunkDeer/KeyboardSession.cs#L1287-L1355)),
`FetchFuncBlock`/`PushFuncBlock` ([1996-2008](DrunkDeerSDK/DrunkDeer/KeyboardSession.cs#L1996-L2008)).

**Problem:** `ValidateProfileIndex` exists
([2403](DrunkDeerSDK/DrunkDeer/KeyboardSession.cs#L2403)) but is only called by
`SwitchProfile`, `CaptureProfile`, and `CopyProfile`. Everything else accepts any `int`.
`WriteKeyTriggers(configs, profileIndex: 64)` computes `1024 * 64 = 65536`, which the
unchecked `(ushort)` cast wraps to **address 0** — the write lands on profile 0's trigger
region. Other values wrap into arbitrary offsets inside the region addressed by that
sub-command. These are **flash writes**; a bad index silently corrupts other profiles'
persistent data. Negative indices wrap too.

**Failure scenario:** caller iterates profiles with an off-by-one (`for i in 1..4` on a
4-profile board is enough: `i=4` → keymap addr `8192`, an undefined region for sub-cmd 0x09).

**Fix:** call `ValidateProfileIndex(profileIndex)` at the top of **every** public/internal
method that takes `profileIndex` (grep: `int profileIndex`). `ProfileCount` is `const 4`
([2401](DrunkDeerSDK/DrunkDeer/KeyboardSession.cs#L2401)); if some models have a different
slot count, move it into `models.yaml`/`ModelInfo` first and validate against
`Model.ProfileCount`. Also make `ReadExtendedGateway`/`WriteExtendedGateway`
([1921-1972](DrunkDeerSDK/DrunkDeer/KeyboardSession.cs#L1921-L1972)) compute the address in
`int`, and `checked`-cast to `ushort` so any future overflow throws instead of wrapping.
Add FeatureTests: each accessor with `profileIndex: 4` and `-1` must throw
`ArgumentOutOfRangeException` and send nothing (assert `fake.SentPackets` empty).

### POLL-1 — High-precision poll frames misassign sections after any dropped frame
**Where:** [KeyboardSession.cs:527-567](DrunkDeerSDK/DrunkDeer/KeyboardSession.cs#L527-L567)
(collection), [613-638](DrunkDeerSDK/DrunkDeer/KeyboardSession.cs#L613-L638) (dispatch);
packet shape: [Messages.g.cs:770-777](DrunkDeerSDK/DrunkDeer/Generated/Messages.g.cs#L770-L777).

**Problem:** `KeyTravelHighPrecision` (`0xFD 0x06`) carries **no section index** — unlike
B7 packets, which carry `packet_index` at byte 3. The loop therefore assigns HP packets to
slots in **arrival order** (`FirstEmpty`). When a frame times out mid-collection (say
sections 0-2 of 5 arrive), the loop `continue`s and re-sends the request **without flushing
the read buffer**. The stale sections 3-4 from the old request are then read first and land
in slots 0-1 of the next frame; `DispatchFrameHighPrecision` maps slot→key range positionally,
so keys 90-125's travel is written into keys 0-59's state. Because every subsequent frame
consumes exactly 5 packets from a queue that is now permanently offset, **the misalignment
persists for the rest of the session** — `KeyDown` fires for the wrong keys on every A75
Ultra / A75 Master / X60 Future whenever one frame drops (retry exhaustion, slow scheduler,
USB hiccup).

**Fix:** in `PollLoop`, when `!AllReceived(gotPkt)` (the dropped-frame branch at
[561](DrunkDeerSDK/DrunkDeer/KeyboardSession.cs#L561)), call `_connection.FlushReadBuffer()`
before `continue` — that is the minimal correct resync for both HP and B7 modes. Additionally
(belt-and-braces, HP only): on receiving an HP packet when `FirstEmpty` returns -1, flush and
restart the frame. Long-term: capture whether newer firmware echoes a section byte anywhere
in the 0xFD 0x06 payload (bytes 62-63 are currently unread) and extend the YAML if so.
Test: extend `FakeKeyboardConnection` to script a timeout mid-frame followed by stale
sections, and assert key indices stay correct after recovery.

### API-2 — `SetKeyboardMode` ignores its argument (always Mac)
**Where:** [KeyboardSession.cs:1664-1670](DrunkDeerSDK/DrunkDeer/KeyboardSession.cs#L1664-L1670)

```csharp
internal void SetKeyboardMode(KeyboardMode mode)
{
    EnsureNotPolling();
    var block = FetchFuncBlock();
    block.MacMode = (byte)KeyboardMode.Mac;   // ← should be (byte)mode
    PushFuncBlock(block);
}
```

`SetKeyboardMode(KeyboardMode.Windows)` switches the keyboard **to Mac mode** and there is
no way back through the SDK. The existing test
`SetKeyboardMode_Windows_WritesMacModeByteToZero`
([FuncBlockTests.cs:53](DrunkDeerSDK/DrunkDeer.FeatureTests/Features/FuncBlockTests.cs#L53))
would have caught this — it never ran (BLD-1/BLD-2).

**Fix:** `block.MacMode = (byte)mode;`. Then make the FuncBlock tests run against a
programmable model (BLD-2) and confirm this test passes.

---

## 2. High-severity findings

### BLD-2 — Feature-test suite is stale: 90/151 fail
**Evidence** (after restoring references): 84 × `NotSupportedException` — 78 of them
`"A75 (fw 1, Standard precision) does not support FuncBlock operations"`, 6 × logo/side-zone
gates; 2 × `"No ACK for CommonConfig"`; 1 × depth-range
(`SetActuationPoint_Uniform_Packet2CarriesKeys118To126` uses 3.8 mm against the 3.3 mm cap);
plus follow-on assertion failures. Root cause: every fixture does
`new FakeKeyboardConnection()` which defaults to **A75, fw 1 → Standard precision →
`HasFuncBlock == false`** ([FakeKeyboardConnection.cs:29-33](DrunkDeerSDK/DrunkDeer.FeatureTests/Fakes/FakeKeyboardConnection.cs#L29-L33)),
while all FuncBlock/lighting-zone methods now gate on precision/capabilities
([KeyboardSession.cs:280](DrunkDeerSDK/DrunkDeer/KeyboardSession.cs#L280),
[695-702](DrunkDeerSDK/DrunkDeer/KeyboardSession.cs#L695-L702)). The tests predate the gates.

**Fix (agent instructions):**
1. Give `FakeKeyboardConnection` a convenient way to impersonate programmable hardware,
   e.g. `FakeKeyboardConnection.ForModel(ModelSlugs.A75Ultra)` or an optional
   `firmwareVersion` ctor arg (fw ≥ 35 turns an A75 fake into Kun precision).
2. FuncBlock/KeyMap/KeyTrigger/DKS/MT/Toggle/Macro/Profile/logo tests → construct with
   `ModelRegistry.GetInfo(ModelSlugs.A75Ultra)!` (logo) / `X60Future` (side light) /
   `G65M1` (Kun-programmable, useful to cover the non-HP path).
3. `CommonConfigTests`: `EnableRapidTrigger`/`DisableTurboMode`/`SetCommonConfig` now require
   a `0xB5` ACK ([1652-1661](DrunkDeerSDK/DrunkDeer/KeyboardSession.cs#L1652-L1661)) —
   enqueue `fake.EnqueueAck(0xB5)`; `EnableTurboMode` on a TurboMode-capable model
   additionally performs a FuncBlock read-modify-write (`EnqueueFuncBlockCycle()`).
4. The 3.8 mm depth test: use a value within `[MinDepthMm, MaxDepthMm]` or assert the throw.
5. Keep at least one test per gated method asserting the `NotSupportedException` on a
   Standard-precision A75 — the gates themselves deserve coverage.
6. Target: 151/151 green, then wire into CI (BLD-1 step 5).

### API-3 — Per-key downstroke/upstroke setters throw on first use; captured profiles can't be re-applied
**Where:** ctor seeds [KeyboardSession.cs:457-461](DrunkDeerSDK/DrunkDeer/KeyboardSession.cs#L457-L461)
(`_actuationProfile` filled with 2.0f, but `_downstrokeProfile`/`_upstrokeProfile` left 0.0);
per-key setters [787-805](DrunkDeerSDK/DrunkDeer/KeyboardSession.cs#L787-L805) and
[833-851](DrunkDeerSDK/DrunkDeer/KeyboardSession.cs#L833-L851) validate the **whole** profile
array via `SetKeyPointPerKey` → `ValidateDepthMm(0.0)` → throws because `MinDepthMm = 0.2`.

**Failure scenarios:**
- `session.SetDownstrokePoint(0.3f, DDKey.W)` as the first downstroke call →
  `ArgumentOutOfRangeException: Depth 0.000 mm … for key 0`. Same for upstroke.
- `CaptureProfile` → `ApplyProfile` round-trip: for non-uniform depths `CaptureProfile`
  builds `KeyDepthProfile { Default = 0, Keys = … }`
  ([2565-2568](DrunkDeerSDK/DrunkDeer/KeyboardSession.cs#L2565-L2568));
  `BuildDepthArray` fills unmapped layout slots with that 0 default
  ([865-882](DrunkDeerSDK/DrunkDeer/KeyboardSession.cs#L865-L882)) → `ApplyProfile` throws.
  The README's flagship "save → load" workflow is broken for any per-key configuration.

**Fix:**
1. Seed all three profiles with model-appropriate defaults (actuation 2.0 already; use
   firmware defaults 0.25 mm for downstroke/upstroke — cross-check `KeyTriggerConfig.Default`
   25/100 = 0.25).
2. In `BuildDepthArray`, treat a 0/absent `Default` as "keep current session profile value
   for keys not listed" instead of writing 0 — pass the existing profile array in as the
   base. That fixes the round-trip without changing the JSON schema.
3. On HP models, consider seeding from `ReadActuationPoint()`/`ReadDownstrokePoint()`/
   `ReadUpstrokePoint()` at connect (read-back exists there), so "leave others unchanged"
   is actually true instead of "reset others to SDK defaults" (see API-11).
4. Tests: first-call per-key downstroke on a fresh session; capture→apply round-trip with
   mixed depths.

### API-4 — Typed sessions never verify the connected model
**Where:** [KeyboardSession.Typed.cs:25-26](DrunkDeerSDK/DrunkDeer/KeyboardSession.Typed.cs#L25-L26)

`KeyboardSession<A75Ultra>.OpenFirst()` opens **whatever keyboard is plugged in** and brands
it `A75Ultra`. With an A75 connected, every `IHasHighPrecision`/`IHasFuncBlock` extension is
visible in IntelliSense and fails only at runtime (`NotSupportedException` at best;
at worst semantic mismatches — HP read paths, `CaptureProfile` scaling). The phantom-type
design promises compile-time safety it doesn't deliver.

**Fix:**
1. Generate a slug on each marker type (e.g. `public interface IModelMarker { static abstract string Slug { get; } }`,
   markers implement it; or a `[ModelSlug("a75_ultra")]` attribute — both easy in
   `Templates/Models.sbn`).
2. Constrain `KeyboardSession<TModel> where TModel : IModelMarker`.
3. In `OpenFirst`, after the handshake compare `connection.Model.Slug` to `TModel`'s slug and
   throw `DrunkDeerModelMismatchException` naming both (message should tell the user to use
   `KeyboardSession.OpenFirst()` untyped or the right marker).
4. Add `KeyboardSession.Open(HidDevice)`-style overloads for multi-keyboard setups, and a
   `TryOpenFirst`.
5. Tests: fake connection advertising A75 + `KeyboardSession<A75Ultra>` ctor path must throw.
   (Requires routing the typed ctor through something fake-able; the internal ctor already
   accepts `IKeyboardConnection` — add the check there, keyed off `typeof(TModel)`.)

### API-5 — Unknown model slugs silently fall back to A75 layout/LED tables (X60 Future is wrong today)
**Where:** [KeyLayout.cs:177-193](DrunkDeerSDK/DrunkDeer/Keys/KeyLayout.cs#L177-L193)
(`_ => LayoutA75`), [199-214](DrunkDeerSDK/DrunkDeer/Keys/KeyLayout.cs#L199-L214)
(`_ => RgbA75`).

`x60_future` has no case in either switch, so a 60% keyboard gets the 75% A75 layout and its
84-LED RGB index table: `GetKeyIndex(DDKey.F5)` "works" and addresses a nonexistent key,
per-key lighting writes target wrong LEDs, `GetKeys()` lies. Any future model added to
`models.yaml` inherits the same silent wrongness.

**Fix:**
1. Add an explicit `ModelSlugs.X60Future` case. Until the real firmware slot map is captured,
   mapping it to `LayoutG60`/`RgbG60` is strictly closer than A75 — but flag it
   `// TODO: verify via capture` and note it in the README verification table.
2. Change both `default` arms to `throw new NotSupportedException($"No layout table for model '{modelSlug}'…")`
   so new models fail loudly at connect.
3. Better (roadmap GEN-1): move layouts into the protocol YAML and generate `KeyLayout`, so a
   model without layout data fails codegen, not runtime.

### API-6 — Banker's rounding in all mm→raw conversions (proven by the dormant test)
**Where:** [KeyboardSession.cs:954-963](DrunkDeerSDK/DrunkDeer/KeyboardSession.cs#L954-L963)

`Math.Round(double)` rounds half-to-even: `MmToKunUnit(0.005f)` → `Round(0.5)` → **0**, the
test expects 1 (`DepthConversionTests.cs:47`, fails). A 0.005 mm request becomes 0 (out of
the valid range the SDK itself enforces elsewhere), 0.025 mm → 2 not 3, etc. Same latent
issue in `MmToStandardUnit`, `MmToHighPrecisionUnit`, `KeyTriggerConfig.MmToRaw`
([KeyTriggerConfig.cs:73-74](DrunkDeerSDK/DrunkDeer/Profile/KeyTriggerConfig.cs#L73-L74)) and
`DynamicKeystrokeEntry.MmToUnit`
([DynamicKeystrokeEntry.cs:50-51](DrunkDeerSDK/DrunkDeer/Profile/DynamicKeystrokeEntry.cs#L50-L51)).

**Fix:** add `MidpointRounding.AwayFromZero` to every depth conversion (5 call sites; grep
`Math.Round`). Run `DrunkDeer.ProtocolTests` — the failing case goes green; add midpoint
cases for the other units.

### POLL-2 — A throwing user event handler silently kills the poll loop
**Where:** [KeyboardSession.cs:514-584](DrunkDeerSDK/DrunkDeer/KeyboardSession.cs#L514-L584)
(only `Send` is guarded), `UpdateKeyState`
([640-659](DrunkDeerSDK/DrunkDeer/KeyboardSession.cs#L640-L659)) invokes `KeyHeightChanged`/
`KeyDown`/`KeyUp`/`KeyPressed` unguarded on the poll thread.

**Failure scenario:** any exception in a subscriber propagates out of `DispatchFrame`, faults
the `Task.Run` task, and polling stops with **no signal**: no `Disconnected`, no log, and
`IsPolling` flips false. The exception finally re-surfaces as an `AggregateException` from
`_pollTask.Wait(…)` inside the next `StopPolling()`
([492-498](DrunkDeerSDK/DrunkDeer/KeyboardSession.cs#L492-L498)) — the least expected place.

**Fix:**
1. Wrap the dispatch call (or each event invocation) in try/catch; log via `_log.LogError`
   and either continue polling (preferred; document "handler exceptions are swallowed and
   logged") or stop-with-signal.
2. Add a `PollLoopFaulted`/extend `Disconnected` into `SessionEnded(reason)` so hosts can
   react (see roadmap C-2).
3. In `StopPolling`: treat `Wait` timeout explicitly (log + return bool), catch the
   `AggregateException`, and dispose/null `_pollCts`/`_pollTask` so Start/Stop cycles don't
   leak CTSes (see API-9).
4. Test: fake connection + `KeyDown += throw` + scripted frame → poll loop survives (or
   surfaces the configured way), `StopPolling` doesn't throw.

### TRN-1 — Device discovery matches the VID×PID cross-product
**Where:** [KeyboardDiscoverer.cs:13-15](DrunkDeerSDK/DrunkDeer/Transport/KeyboardDiscoverer.cs#L13-L15)
vs. the pairing in [models.yaml:6-15](DrunkDeerSDK/DrunkDeer/protocol/models.yaml#L6-L15).

The YAML correctly pairs `vid: 0x05AC → pids: [0x024F]`, `vid: 0x04D9 → pids: [0x2A08]`, etc.
The C# flattens this into two independent arrays and accepts any combination — e.g. Apple's
VID `0x05AC` with PID `0x2382`, or Holtek `0x04D9` (used by hundreds of keyboards) with
`0x024F`. A false positive isn't benign: `OpenFirst` picks `FindAll()[0]`, and the identity
handshake retries 20 × 500 ms ([KeyboardConnection.cs:106-135](DrunkDeerSDK/DrunkDeer/Transport/KeyboardConnection.cs#L106-L135))
— **up to ~10 s hang and then a failure**, with the real DrunkDeer board sitting unopened at
index 1.

**Fix:**
1. Match `(vid, pid)` pairs. Best: generate the pair table from the YAML `discovery` section
   into `ModelRegistry.g.cs` (the comment "Kept in sync with models.yaml" is exactly the kind
   of promise codegen should replace) and have `KeyboardDiscoverer` consume it.
2. In `OpenFirst`, on handshake failure move on to the next candidate device instead of
   giving up (and lower per-device retry budget).
3. Consider `FindAll()` returning an empty list instead of throwing (LOW-9), keeping the
   throw in `OpenFirst`.

### TRN-2 — Data-stream scan can bind another keyboard's interface
**Where:** [KeyboardConnection.cs:70-97](DrunkDeerSDK/DrunkDeer/Transport/KeyboardConnection.cs#L70-L97)

The read-only data stream is located by scanning **all** HID devices for same VID/PID with
`Out == 0 && In ≥ 64`. With two identical keyboards attached, the loop can open the *other*
unit's data interface (packets from keyboard B interleaved into a session on keyboard A).
Note the SDK currently never reads this stream (see API-12), which hides the issue — but fix
it before the stream is ever used.

**Fix:** constrain candidates to the same physical device: compare
`GetSerialNumber()` where available, else the platform device-path prefix (both HidSharp
paths embed the USB device instance; match on the path up to the interface suffix).
Fall back to "no data stream" rather than guessing.

---

## 3. Medium-severity findings

### API-7 — `EnableAutoMatch`/`DisableAutoMatch` desync the auto-match mirror
[KeyboardSession.cs:1453-1467](DrunkDeerSDK/DrunkDeer/KeyboardSession.cs#L1453-L1467) send
`0xFD 0x0C` but never touch `_rapidTriggerAutoMatch`. The next `SendCommonConfig()` (any
Enable/Disable RT/Turbo call) rebuilds the B5 packet from the stale mirror and **reverts the
auto-match state on the keyboard**. Fix: set the mirror in both methods (and decide the
sensitivity↔bool mapping); add a FeatureTest asserting B5 byte 11 after
`EnableAutoMatch(); DisableRapidTrigger();`.

### API-8 — Config mirrors mutate before the device ACKs
`SetCommonConfig`/`Enable*`/`Disable*` update `_turboEnabled` etc., then throw if the ACK
never arrives ([1422-1430](DrunkDeerSDK/DrunkDeer/KeyboardSession.cs#L1422-L1430),
[1652-1661](DrunkDeerSDK/DrunkDeer/KeyboardSession.cs#L1652-L1661)) — mirrors now disagree
with hardware, and `CaptureProfile` serialises the lie
([2594-2596](DrunkDeerSDK/DrunkDeer/KeyboardSession.cs#L2594-L2596)). Fix: set mirrors only
after a successful ACK (stage locals, commit at the end).

### API-9 — `StopPolling` contract is soft
[492-498](DrunkDeerSDK/DrunkDeer/KeyboardSession.cs#L492-L498): 2 s `Wait` result ignored
(logs "Poll loop stopped." even on timeout), CTS never disposed, `_pollTask` never cleared,
and per POLL-2 it can rethrow subscriber exceptions. The inner receive loop can legitimately
take up to 10 retries × 200 ms + dispatch, so timeouts are reachable; a caller then
immediately hits `EnsureNotPolling` throws from config methods despite having "stopped".
Fix: loop-check `IsCancellationRequested` inside the retry loop too; make `StopPolling`
return `bool`/throw on timeout explicitly; dispose CTS.

### API-10 — `Dispose` can fire `Disconnected` and races the poll thread
[2716-2735](DrunkDeerSDK/DrunkDeer/KeyboardSession.cs#L2716-L2735): if `StopPolling` times
out (API-9), `_connection.Dispose()` yanks the streams from under the still-running loop →
next `Send` throws → the loop invokes `Disconnected` on a disposed session. Fix: after a
confirmed stop, dispose; if stop timed out, set a `_disposed` gate the loop checks before
raising events. Also make `Dispose` idempotent under concurrency (it isn't locked).

### API-11 — Per-key setters clobber unknown device state with SDK defaults
All depth writes are full-profile writes; the session's shadow arrays start as guesses
(2.0 mm / API-3 zeros), not what's on the keyboard. First `SetActuationPoint(0.2f, DDKey.W)`
therefore rewrites **every other key** to 2.0 mm, destroying whatever the user configured in
the official app. Unavoidable on Standard models (no read-back) — but must be documented on
each per-key overload; on HP models seed the shadows via the existing `Read*Point()` calls at
connect.

### API-12 — The "data stream" is opened, flushed… and never read
`KeyboardSession` polls exclusively via `ReceiveCommand`
(grep: no `_connection.Receive(` call sites). The stream hunted for in
[KeyboardConnection.cs:70-97](DrunkDeerSDK/DrunkDeer/Transport/KeyboardConnection.cs#L70-L97)
only accumulates unread unsolicited reports. Either remove it (simpler) or actually use it —
reading travel from the dedicated IN endpoint while commands use the command interface is the
architecturally right split and removes the POLL-1 interleaving class entirely (roadmap E-3).

### TRN-3 — `HidTransport.Send` silently truncates oversized packets
[HidTransport.cs:35-43](DrunkDeerSDK/DrunkDeer/Transport/HidTransport.cs#L35-L43):
`Math.Min(packet.Length, reportLen - 1)` drops bytes with no error. All current packets are
64 B, but a future >64 B message or a device reporting a smaller max output length would
corrupt silently. Fix: throw `ArgumentException` when `packet.Length > reportLen - 1`.

### TRN-4 — Loose ACK matching can mistake stream packets for write-ACKs
`WriteKeyPointAcknowledgeHighPrecision.Matches` accepts **any** `0xFD` packet
([Messages.g.cs:759-765](DrunkDeerSDK/DrunkDeer/Generated/Messages.g.cs#L759-L765)) —
including `0xFD 0x06` travel frames still buffered after a polling stop. A key-point write
right after `StopPolling` can "ACK" against a stale travel packet while the real write fails.
`RgbAcknowledge`/`CommonConfigAcknowledge`/`WriteKeyPointAcknowledgeStandard` are equally
loose but their first bytes don't collide with streamed traffic today. Fix: flush before
request/ACK exchanges (cheap, in `SendAndReceive`-using paths), and tighten the HP ACK header
in YAML once a capture shows its real second byte.

### PROTO-1 — `ReadExtendedGateway` trusts any `0xAA` response
[1921-1945](DrunkDeerSDK/DrunkDeer/KeyboardSession.cs#L1921-L1945): the reply's echoed
sub-command/address/length (bytes 1-7, currently skipped as `_reserved`) are never compared
to the request, so a stale/reordered gateway response reassembles into the wrong offset of
the result buffer — subtle profile-data corruption on read. Capture the echo layout, add
fields to `ExtendedGatewayResponse` in YAML, and validate per chunk (retry once on mismatch).

### PROTO-2 — `ConfigureLastWinPairs` may only program one direction
The YAML states a user pair expands to **two** firmware entries (A→B and B→A)
([base_messages.yaml:130-142](DrunkDeerSDK/DrunkDeer/protocol/base_messages.yaml#L130-L142));
the implementation writes one entry per pair and `pair_count = pairs.Length`
([1477-1493](DrunkDeerSDK/DrunkDeer/KeyboardSession.cs#L1477-L1493)). If the comment is right,
Last Win only fires in one direction. Verify against a capture of the official app (14-pair
max would then mean 7 user pairs). Also: key indices are cast `(byte)` unvalidated — range-check
against the layout (0-125) first.

### PROTO-3 — FuncBlock light `speed` range/inversion mismatch
YAML: speed is raw 0-4, **inverted** (0 = fastest)
([profile_blocks.yaml, light_speed](DrunkDeerSDK/DrunkDeer/protocol/profile_blocks.yaml)).
`SetLightPreset`/logo/side validate 0-9 and write the value through
([1746-1756](DrunkDeerSDK/DrunkDeer/KeyboardSession.cs#L1746-L1756)). So `speed: 9` writes an
out-of-range byte and "faster" numbers run slower. Decide the public unit (keep 0-9,
`raw = 4 - (speed * 4 + 4) / 9`? simplest: expose 0-4 and invert), fix `ValidateSpeed`, and
teach the generator an `inverted-range` field annotation so this lives in YAML (GEN-2).

### PROTO-4 — `RestoreFactorySettings` sends an unverified opcode, ungated, no ACK
[2698-2702](DrunkDeerSDK/DrunkDeer/KeyboardSession.cs#L2698-L2702) sends raw
`[0x06, 0x0F, 0xFF]` — no other message in the protocol starts with `0x06`, the method is
**public** on every model (even Standard A75, while the far milder `Reset()` requires
FuncBlock), and no response is checked. Verify the byte sequence against the official app
capture before anyone runs it; until verified, mark it `internal`, and document it as
irreversible. Consider requiring a confirmation token parameter.

### PROTO-5 — `MacroAction.EncodeBlock` has no capacity guard
[MacroAction.cs:150-179](DrunkDeerSDK/DrunkDeer/Profile/MacroAction.cs#L150-L179): 32 slots ×
unbounded actions vs. a 2048-byte block. Overflow surfaces as
`ArgumentOutOfRangeException` from a span slice mid-encode (after the header was already
half-written). Fix: pre-compute `64 + 4 + Σ(len×4)` and throw a clear
`ArgumentException("macros exceed 1980 bytes …")` before writing; add a test at the boundary.

### PROTO-6 — Identity handshake can crash on short packets
`IdentityResponse.Matches` requires only 3 bytes
([Messages.g.cs:37-38](DrunkDeerSDK/DrunkDeer/Generated/Messages.g.cs#L37-L38)) but
`Open` then reads bytes 4-6, 7, 15-30
([KeyboardConnection.cs:138-155](DrunkDeerSDK/DrunkDeer/Transport/KeyboardConnection.cs#L138-L155))
— a truncated report throws `ArgumentOutOfRangeException` out of `Open` instead of retrying.
Fix: generate `Matches` length requirements from the highest payload offset (≥ 33 here), or
guard in `Open`.

---

## 4. Low severity / hygiene

- **LOW-1** — `DrunkDeer.Tests/` is a stale duplicate of `DrunkDeer.ProtocolTests` (old
  namespaces/comments), absent from `DrunkDeer.slnx`. Delete it; drop its
  `InternalsVisibleTo`.
- **LOW-2** — Dead code: `ApplyPerKeyDepth`
  ([KeyboardSession.cs:1617](DrunkDeerSDK/DrunkDeer/KeyboardSession.cs#L1617)) is never
  called; `ModelInfo.LayoutSlotOffset`/`RgbPacketCount` are populated from YAML but consumed
  nowhere (layouts hardcode the offset via empty strings; RGB packet count is derived from
  entry count). Either use them (GEN-1) or remove from `ModelInfo` to stop implying they work.
- **LOW-3** — `DDKey` has no `RightCtrl`, yet `CTRL_R` exists in the A75 and G60 layouts —
  the key is simply unaddressable by name. Add the enum member + `"CTRL_R"` mapping. (The
  numpad members are the opposite: mapped tokens `KP0…` appear in no layout — harmless,
  document as future-proofing.)
- **LOW-4** — `TravelResponse.travel` is declared `i8[59]` in
  [base_messages.yaml:49](DrunkDeerSDK/DrunkDeer/protocol/base_messages.yaml#L49) but decoded
  unsigned (`byte` → `short`), which is provably required in Kun mode (full travel = 200).
  Change the YAML to `u8[59]` and fix the `KeyEventArgs.Height` doc ("signed i8 range") in
  [KeyEventArgs.cs](DrunkDeerSDK/DrunkDeer/Events/KeyEventArgs.cs). Also fix the write-op
  docs claiming packet 2 covers "118-125" (it's 118-126, 9 values) in
  `base_operations.yaml`.
- **LOW-5** — `KeyboardProfile` XML example uses a nonexistent `ActuationMm` property
  ([KeyboardProfile.cs:27](DrunkDeerSDK/DrunkDeer/Profile/KeyboardProfile.cs#L27)) — copy-paste
  from it won't compile.
- **LOW-6** — `KeyColor.Brightness` (per-key brightness) is documented and settable via
  `KeyboardThemeBuilder.Key(…, brightness: 3)` but `ApplyTheme`
  ([1630-1650](DrunkDeerSDK/DrunkDeer/KeyboardSession.cs#L1630-L1650)) ignores it — the wire
  format has one brightness per packet, not per key. Remove the field or document it as
  unsupported; don't silently drop it.
- **LOW-7** — `[Range(0, 9)]` attributes on parameters are decoration only
  (`System.ComponentModel.DataAnnotations` is not enforced on method args); real checks are
  the manual validators. Fine, but don't let future code rely on the attribute.
- **LOW-8** — `SetKeyColor(int keyIndex, …)` validates against `_rgbProfile.Length` (128)
  rather than the model's layout ([1217-1225](DrunkDeerSDK/DrunkDeer/KeyboardSession.cs#L1217-L1225));
  accepts indices for keys that don't exist. Cosmetic; align with `TotalKeyCount`/layout.
- **LOW-9** — `KeyboardDiscoverer.FindAll()` throws when nothing is found — surprising for a
  "find all" API; return `[]` and keep the exception in `OpenFirst`.
- **LOW-10** — Threading contract undocumented: events fire on the poll thread; `_heights`/
  `_pressed` reads from other threads are benign (atomic element writes) but the RGB/depth
  shadow arrays and config methods are not thread-safe. Add a "Threading" README section;
  consider a `SynchronizationContext`/callback-dispatcher option for UI apps.
- **LOW-11** — Per-poll allocations: a fresh `byte[64]`+`buf[1..read]` copy per packet
  (≥ 4-6 per frame at ~200 Hz) plus one/two `EventArgs` per key change. Fine for now; if perf
  matters use a pooled read buffer + reusable spans in `HidTransport` and struct event args
  (roadmap E-1). Cache `GetMaxInputReportLength()`/`GetMaxOutputReportLength()` (native calls)
  in `HidTransport` fields.
- **LOW-12** — `KeyboardSessionExtensions.CaptureProfile<T>` is gated `IHasHighPrecision`
  ([KeyboardSessionExtensions.cs:272](DrunkDeerSDK/DrunkDeer/KeyboardSessionExtensions.cs#L272))
  but the implementation only needs `HasFuncBlock` — Kun-programmable G65 m1/m2/m3 and
  G60 v600 are locked out of a feature they support. Re-gate to `IHasFuncBlock` after
  verifying trigger-unit scaling there (see §5).

---

## 5. Verify on hardware / USB capture before or while fixing

These need evidence, not code reading. The repo's own ProtocolAnalyzer (once it compiles —
BLD-1) is the right tool; captures of the official web driver are the oracle.

1. **`RestoreFactorySettings` opcode** `[0x06, 0x0F, 0xFF]` (PROTO-4) — confirm bytes and
   whether an ACK exists.
2. **Last Win pair expansion** — one entry per user pair vs. mirrored pairs (PROTO-2).
3. **FuncBlock light speed** — range 0-4 + inversion on all three zones (PROTO-3).
4. **HP travel packet tail** — is a section/sequence id hiding in bytes 62-63? (POLL-1
   long-term fix.)
5. **`KeyTriggerConfig` unit scale on HP models** — `CaptureProfile` divides by 100
   ([2542-2544](DrunkDeerSDK/DrunkDeer/KeyboardSession.cs#L2542-L2544)); confirm the trigger
   region is 0.01 mm/unit on A75 Ultra/Master too, or scale by precision mode.
6. **Downstroke/upstroke (B6 0x04/0x05) vs. KeyTrigger RtPress/RtRelease** — `CaptureProfile`
   reads the latter, `ApplyProfile` writes the former; confirm they address the same firmware
   state or split the profile fields.
7. **RGB packet count** — G60/G65 metadata says 6 packets; the SDK derives 5 from 60 LEDs.
   Confirm firmware tolerates fewer packets (likely, given the 0xFF terminator) or pad to the
   model count — then delete or use `RgbPacketCount` (LOW-2).
8. **`ExtendedGatewayResponse` echo layout** (PROTO-1) — map bytes 1-7.

---

## 6. Beyond bugs — improvement roadmap

Ordered by leverage. A/B are prerequisites for trusting anything else.

### A. Repository health (do first)
1. Fix BLD-1, BLD-2; delete `DrunkDeer.Tests` (LOW-1).
2. **CI on every push/PR**: build slnx, run both test suites, codegen + `git diff
   --exit-code DrunkDeer/Generated`, `dotnet format --verify-no-changes` if adopted. Gate the
   release workflow on it (today release publishes to NuGet with zero tests).
3. Move the stray solution file: `DrunkDeer/DrunkDeer.slnx` lives inside the library project
   folder and reaches siblings via `../` — hoist to `DrunkDeerSDK/` root.

### B. Correctness hardening
1. All fixes in §1-§3; the theme is *validate at the boundary* (profile indices, key
   indices, packet sizes) and *trust nothing off the wire* (ACK headers, gateway echoes,
   response lengths).
2. Centralise the 0x55 envelope (checksum, chunking, isLast) in one generated builder so the
   checksum math exists in exactly one place (today it's duplicated between
   `ReadExtendedGateway`/`WriteExtendedGateway` and re-derived in tests/fakes).
3. Add a `ProtocolException` hierarchy (`AckTimeout`, `MalformedResponse`,
   `DeviceDisconnected`) instead of `InvalidOperationException` everywhere — callers
   currently can't distinguish "unplugged" from "firmware said no".

### C. API evolution
1. **Async surface**: `OpenFirstAsync`, `Task StopPollingAsync()`, and an
   `IAsyncEnumerable<KeyFrame>`/`Channel`-based alternative to events. All I/O is currently
   blocking with hidden 100 ms-10 s stalls; a UI thread calling `SetLightingMode` blocks on
   HID round-trips.
2. **Lifecycle events**: replace bare `Disconnected` with a `SessionEnded(reason)` (device
   gone / poll fault / disposed) and add hot-plug reconnect support (HidSharp's
   `DeviceList.Local.Changed`), e.g. a `ResilientKeyboardSession` wrapper that re-opens and
   re-applies the session's shadow state.
3. **Model safety** (API-4): marker-interface constraint + runtime slug check; plus
   `KeyboardSession.Open(device)` overloads and multi-device enumeration
   (`foreach (var kb in KeyboardDiscoverer.OpenAll())`).
4. **Batch/deferred lighting**: `SetKeyColor` sends ~7 packets + ACKs per call; a
   `session.Lighting.Begin()…Commit()` or `SetKeyColors(IEnumerable<(DDKey, RgbColor)>)`
   avoids N× full-frame pushes in animation loops. Document flash-write endurance for the
   `SaveLightingToProfile`/`WriteStoredColors` paths (don't call per-frame).
5. `KeyEventArgs`/`KeyHeightChangedEventArgs`: expose `DDKey?` and `HeightMm` (the session
   knows the scale; every consumer currently re-derives it — see the KeyboardPiano adapter).
6. Consider `IKeyboardSession` (interface) so app code can be tested without the concrete
   class, mirroring what `IKeyboardConnection` did for the SDK's own tests.

### D. Codegen consolidation (single source of truth)
1. **GEN-1**: move key layouts + RGB index tables + discovery VID/PID pairs into the YAML and
   generate `KeyLayout`/discovery tables. Unknown-model = codegen failure. This also makes
   `LayoutSlotOffset`/`RgbPacketCount` real or removable (LOW-2), and new-model support a
   pure YAML diff.
2. **GEN-2**: field annotations for inverted/offset ranges (light speed 0-4 inverted,
   `bias: 1` already exists for triggers) so scaling quirks live next to the wire docs.
3. Generate `Matches()` minimum lengths from payload extents (PROTO-6), and generate the
   response-pairing table for the ProtocolAnalyzer oracle from `operations.yaml` (it's
   currently hand-written in `ProtocolOracle.MatchOut`).
4. Generate `LightPreset`/`LightingMode` enums from YAML (values are currently duplicated in
   C# with "verify via USB capture" caveats).

### E. Performance (only after correctness)
1. Pooled receive buffers + struct event args, cached report lengths (LOW-11).
2. Skip the double copy in `WriteKeyPointStandard`/`HighPrecision` (build directly into the
   packet buffer).
3. Use the data stream for travel packets (API-12) — separates unsolicited stream from
   command/ACK traffic, removes the interleaving failure class, and enables event-driven
   reads instead of request/response polling.

### F. Documentation
1. README: document the threading model, the "per-key setters overwrite unknown state"
   caveat (API-11), `StopPolling` semantics, and that wiki links (`../../wiki`) only resolve
   on GitHub.
2. Generate the model/capability matrix in the README from `models.yaml` (it already drifted:
   README says A75 Master has HighPrecision max 3.5 mm — correct today, but only by luck).
3. Extend the "Human-Verified" table honestly: A75 is the only verified model; POLL-1/API-5
   show HP and X60 paths were never exercised on hardware.

---

## 7. Agent playbook (how to work on this repo)

**Build & test**
```sh
cd DrunkDeerSDK
dotnet build DrunkDeer/DrunkDeer.slnx                 # after BLD-1 is fixed
dotnet test DrunkDeer.ProtocolTests                    # pure packet/struct tests
dotnet test DrunkDeer.FeatureTests                     # session-level tests via fake
```

**Codegen** — never edit `DrunkDeer/Generated/*.g.cs` by hand:
```sh
dotnet run --project DrunkDeer.CodeGen                 # regenerates from protocol/*.yaml + Templates/*.sbn
git diff DrunkDeer/Generated                           # inspect; commit YAML + generated together
```

**Conventions & guardrails**
- Match file-local style: `KeyboardSession.cs` and most of the library use tabs;
  `KeyboardSessionExtensions.cs` and generated code use 4 spaces.
- Wire-format knowledge belongs in `protocol/*.yaml` comments, not C# comments.
- Anything that **writes keyboard flash** (0x55 sub-commands 0x06/0x09/0x0B/0x0D/0xA1/0xA3/
  0xA5/0xA7, `RestoreFactorySettings`, `Reset`) must validate all indices first and be tested
  against `FakeKeyboardConnection`, never speculatively against real hardware. A75 is the
  only human-verified model; treat HP models as unverified (POLL-1, API-5).
- When adding a model: today that's models.yaml + codegen + `KeyLayout` switch + README
  table + marker docs; until GEN-1 lands, grep for `ModelSlugs.` switches so no fallback
  `_ =>` swallows the new slug.
- `FakeKeyboardConnection` is a response *queue*, not a device simulator — enqueue counts
  must exactly match the chunk math (56-byte gateway chunks; 2 for FuncBlock, 19 for
  triggers, 37 for macros). Prefer adding higher-level helpers to the fake over sprinkling
  magic counts through tests.

**Suggested fix order** (each is an independent PR-sized unit):
1. BLD-1 (+ CI) → 2. BLD-2 test repair → 3. API-2, API-6 (one-liners, now provable by the
   revived tests) → 4. API-1 → 5. POLL-1 + POLL-2 → 6. API-3 → 7. TRN-1/TRN-2 → 8. API-4,
   API-5 → 9. §3 mediums → 10. roadmap items.
