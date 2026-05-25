# DrunkDeer SDK

A .NET library for communicating with DrunkDeer analog keyboards over USB HID, plus a CLI tool for validating USB captures against the protocol implementation.

## Projects

| Project | Description |
|---|---|
| `DrunkDeer` | Core SDK library |
| `DrunkDeer.ProtocolAnalyzer` | CLI tool — validates USB captures against the protocol |
| `DrunkDeer.CodeGen` | Source generator — regenerates `Generated/` from YAML + Scriban templates |
| `DrunkDeer.FeatureTests` | Integration tests against a real connected keyboard |
| `DrunkDeer.ProtocolTests` | Unit tests for generated protocol message structures |

## Quick Start

```csharp
using DrunkDeer.Protocol;

// Opens the first detected DrunkDeer keyboard, performs the identity handshake.
using var session = KeyboardSession.OpenFirst();

Console.WriteLine($"{session.Model.Name}  fw{session.FirmwareVersion}  {session.PrecisionMode}");

// React to key events from the background poll loop.
session.KeyDown    += (_, e) => Console.WriteLine($"Key Down: {e.KeyIndex}  depth={session.GetKeyHeightMm(e.KeyIndex):F2}mm");
session.KeyUp      += (_, e) => Console.WriteLine($"Key Up: {e.KeyIndex}");
session.KeyPressed += (_, e) => Console.WriteLine($"Key Press: {e.KeyIndex}");

session.StartPolling();
Console.ReadLine();
session.StopPolling();
```

## Features

### Key travel events

`KeyboardSession` runs a background poll loop and raises events based on configurable thresholds:

```csharp
session.PressThresholdMm   = 1.0f;   // depth that fires KeyDown (default 1.0mm)
session.ReleaseThresholdMm = 0.5f;   // depth that fires KeyUp   (default 0.5mm)

session.KeyHeightChanged += (_, e) => { /* fires every poll cycle on change */ };
session.KeyDown          += (_, e) => { };
session.KeyUp            += (_, e) => { };
session.KeyPressed       += (_, e) => { /* fires simultaneously with KeyUp */ };
session.Polled           += (_, e) => { /* fires once per complete poll cycle */ };

// Read current depth without waiting for an event
float mm = session.GetKeyHeightMm(DDKey.Space);
float[] allKeyHeights = session.GetAllKeyHeightsMm();
```

Configuration methods require `StopPolling()` first — they throw if called while the loop is running.

### Actuation, downstroke, and upstroke points

```csharp
// Uniform depth for all keys
session.SetActuationPoint(1.5f);

// Per-key array (length == session.TotalKeyCount)
session.SetActuationPoint(new float[session.TotalKeyCount]);

// Named keys only, all others unchanged
session.SetActuationPoint(0.2f, DDKey.W, DDKey.A, DDKey.S, DDKey.D);

// Same overloads for downstroke and upstroke:
session.SetDownstrokePoint(2.0f);
session.SetUpstrokePoint(1.8f);

// Read back (high-precision models only)
float[] ap = session.ReadActuationPoint();
```

Valid range: `session.MinDepthMm` – `session.MaxDepthMm`.

### RGB lighting

```csharp
// Uniform colour
session.SetUniformLighting(r: 255, g: 0, b: 0);

// Per-key callback
session.SetLighting(gridIdx => gridIdx % 2 == 0 ? (255, 0, 0) : (0, 0, 255));

// Single key, preserving all other colours
session.SetKeyColor(DDKey.Escape, r: 255, g: 165, b: 0);

// Built-in firmware animation preset (0 = custom RGB)
// Check HasFuncBlock first — throws NotSupportedException on unsupported models.
if (session.HasFuncBlock)
{
    session.SetLightPreset(effect: 3, brightness: 7, speed: 4);
    session.SetLightCustom();   // return to per-key RGB

    // Flash persistence
    session.SaveLightingToProfile(profileIndex: 0);
    session.LoadLightingFromProfile(profileIndex: 0);
    session.DisableLighting();
}
```

Logo and side light zones are available on supported models (`session.HasLogoLight`, `session.HasSideLight`).

### Rapid Trigger, Last Win, Turbo/Berserk

```csharp
session.EnableRapidTrigger(autoMatch: true);
session.DisableRapidTrigger();

session.EnableTurboMode();
session.DisableTurboMode();

// Berserk mode: every held key re-fires at the polling rate (auto-fire).
// Check HasBerserkMode first — throws NotSupportedException on unsupported models.
if (session.HasBerserkMode)
    session.EnableBerserkMode();

session.SetLastWinRapidTriggerMode(LastWinRapidTriggerMode.Both);
session.ConfigureLastWinPairs((DDKey.A, DDKey.D), (DDKey.Q, DDKey.E));
```

### Global settings

```csharp
session.SetReportRate(ReportRate.Hz1000);
session.SetKeyboardMode(KeyboardMode.Mac);
session.SetDebounce(level: 2);       // 0-7
session.SetStabilityMode(level: 1);  // 0-3
session.ConfigureKeyLocks(winLock: true, altTabLock: false);
```

### Profiles

```csharp
var profile = new KeyboardProfile
{
    ActuationMm  = 1.5f,
    RapidTrigger = true,
    Theme        = new KeyboardTheme { R = 0, G = 100, B = 255, Brightness = 8 },
    PerKeyActuationMm = new Dictionary<string, float> { ["W"] = 0.3f, ["Space"] = 1.0f },
};
session.ApplyProfile(profile);
```

### Testing with FakeKeyboardConnection

`IKeyboardConnection` is the seam for testing. `FakeKeyboardConnection` implements it in-process without a physical keyboard.

```csharp
using var session = new KeyboardSession(new FakeKeyboardConnection(ModelRegistry.A75));
```

## Precision modes

| Mode | Resolution | Models |
|---|---|---|
| `Standard` | 0.1 mm | A75, A75Pro, G75, G65, G60, … |
| `Kun` | 0.01 mm | G65 m1/m2/m3, G60 v600, standard models on newer firmware |
| `FD` (high-precision) | 0.005 mm | A75 Ultra, A75 Master, X60 Future |

`session.PrecisionMode` reports the active mode. Actuation read-back and high-precision key-point writes are only available in `FD` mode.

## Code generation

Protocol message code in `DrunkDeer/Generated/` is auto-generated — never edit those files directly. To add a capability or model:

1. Edit `DrunkDeer/protocol/models.yaml`
2. Edit `DrunkDeer/Templates/Models.sbn`
3. Update `DrunkDeer.CodeGen/Generator.cs`
4. Run: `cd DrunkDeer.CodeGen && dotnet run -- "../DrunkDeer/protocol" "../DrunkDeer/Generated"`
5. Build: `cd DrunkDeer && dotnet build`

---

## ProtocolAnalyzer

`DrunkDeer.ProtocolAnalyzer` is a CLI tool that validates USB HID traffic against the protocol implementation. It reads Wireshark captures or captures live from a USBPcap interface and writes an NDJSON log.

### Usage

```
ProtocolAnalyzer --pcap <capture.pcap|.pcapng> [options]   # analyze a Wireshark capture
ProtocolAnalyzer --live <interface>            [options]   # live USB capture via USBPcap
ProtocolAnalyzer --list-interfaces                         # show available USBPcap interfaces
```

**Options:**

| Flag | Description |
|---|---|
| `--pcap <file>` | Path to `.pcap` or `.pcapng` captured with Wireshark + USBPcap (link type 249) |
| `--live <interface>` | USBPcap interface, e.g. `\\.\USBPcap1` |
| `--output <file>` | NDJSON output path (default: `<pcap-stem>-analysis.ndjson` or `capture-<ts>.ndjson`) |
| `--firmware <tag>` | Label for the firmware version in the log, e.g. `fw13` |
| `--device <bus.addr>` | Filter to one USB device, e.g. `1.5` |
| `--list-interfaces` | List available USBPcap interfaces and exit |

### Live capture requirements

- [Npcap](https://npcap.com/) installed with **USBPcap support** enabled
- USBPcap: installed alongside Npcap or Wireshark
- `USBPcapCMD.exe` present at `C:\Program Files\USBPcap\USBPcapCMD.exe`
- The SDK (`TestApp` or `DrunkDeer Antler` web interface) must be running while capturing — normal key presses use the standard HID interface and are ignored by the analyzer

Find your interface:

```
ProtocolAnalyzer --list-interfaces
```

Then capture while running the SDK:

```
ProtocolAnalyzer --live \\.\USBPcap2 --firmware fw13
```

### PCAP file mode

Capture in Wireshark with USBPcap enabled (link type 249). If you don't pass `--device`, the tool shows a device inventory first so you can pick the right `bus.addr`:

```
ProtocolAnalyzer --pcap capture.pcapng
# Found 3 USB devices. Use --device bus.addr to restrict analysis:
#     2.15   4821 interrupt packets
#     ...

ProtocolAnalyzer --pcap capture.pcapng --device 2.15 --firmware fw13
```

### Output format (NDJSON)

Each line is a self-contained JSON object with a `type` discriminator:

| `type` | Description |
|---|---|
| `session` | Analysis parameters and timestamp |
| `packet` | Each HID interrupt packet: direction, hex payload, message name, `structural_ok`, `sequence_failure`, extracted fields |
| `summary` | Aggregate stats, device inventory, firmware-field snapshot |

**Quick queries with `jq`:**

```sh
# All structural failures
jq 'select(.structural_ok == false)' capture-analysis.ndjson

# Sequence mismatches (OUT→IN opcode mismatch)
jq 'select(.sequence_failure != null)' capture-analysis.ndjson

# Firmware-sensitive fields (for version comparison)
jq 'select(.type=="summary") | .firmware_fields' capture-analysis.ndjson
```

### Firmware comparison workflow

Run captures on two firmware versions with matching `--firmware` tags:

```
ProtocolAnalyzer --pcap fw12.pcap --firmware fw12 --device 2.15
ProtocolAnalyzer --pcap fw13.pcap --firmware fw13 --device 2.15
```

Then diff the `firmware_fields` entries from the two summary lines:

```sh
jq 'select(.type=="summary") | .firmware_fields' fw12-analysis.ndjson
jq 'select(.type=="summary") | .firmware_fields' fw13-analysis.ndjson
```
