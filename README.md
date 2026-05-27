# DrunkDeer SDK

A .NET library for communicating with DrunkDeer analog keyboards over USB HID.

## Quick start

```csharp
using DrunkDeer.Protocol;

using var session = KeyboardSession<A75>.OpenFirst(); // Change `A75` to the model of your keyboard.

Console.WriteLine($"{session.Model.Name}  fw{session.FirmwareVersion}  {session.PrecisionMode}");

session.KeyDown    += (_, e) => Console.WriteLine($"↓ {e.Index}  {session.GetKeyHeightMm(e.Index):F2}mm");
session.KeyUp      += (_, e) => Console.WriteLine($"↑ {e.Index}");
session.KeyPressed += (_, e) => Console.WriteLine($"  {e.Index}");

session.StartPolling();
Console.ReadLine();
```

## Model overview

DrunkDeer keyboards fall into two tiers. **Programmable models** expose the full firmware surface - key remapping, macros, lighting presets, firmware profile management, and per-key trigger configuration stored in on-board flash. All other models support actuation control, Rapid Trigger, and live RGB lighting.

| Model | Precision | Programmable | Turbo | Logo light | Side light |
|---|---|---|---|---|---|
| A75, A75 Pro, G75, G75 Jp, G65, G65 Lite, G60 | Standard / Kun | - | - | - | - |
| G65 M1/M2/M3, G60 V600 | Kun | ✓ | ✓ | - | - |
| A75 Ultra | HighPrecision | ✓ | ✓ | ✓ | - |
| A75 Master | HighPrecision | ✓ | ✓ | - | - |
| X60 Future | HighPrecision | ✓ | ✓ | - | ✓ |

**Capability interfaces** are used as compile-time type constraints on `KeyboardSession<TModel>`. A method only appears in IntelliSense when the model type implements the required interface.

| Interface | Gates |
|---|---|
| `IHasFuncBlock` | All programmable-model features |
| `IHasHighPrecision` | HighPrecision 0.005 mm depth + actuation read-back |
| `IHasTurboMode` | Turbo mode FuncBlock persistence |
| `IHasLogoLight` | Logo LED zone |
| `IHasSideLight` | Side LED strip |

```csharp
// Typed - only the methods your model supports appear in IntelliSense
using var session = KeyboardSession<A75Ultra>.OpenFirst();

// Untyped - for code that doesn't know the model at compile time
using var session = KeyboardSession.OpenFirst();
// Polling, key events, actuation, and live RGB are available on all models
```

## Key events

```csharp
session.PressThresholdMm   = 1.0f;  // depth that fires KeyDown   (default 1.0 mm)
session.ReleaseThresholdMm = 0.5f;  // depth that fires KeyUp     (default 0.5 mm)

session.KeyHeightChanged += (_, e) => { /* fires every poll cycle when depth changes */ };
session.KeyDown          += (_, e) => { };
session.KeyUp            += (_, e) => { };
session.KeyPressed       += (_, e) => { /* fires simultaneously with KeyUp */ };
session.Polled           += (_, e) => { /* fires once per complete poll cycle */ };

// Read current depth without waiting for an event
float mm      = session.GetKeyHeightMm(DDKey.Space);
float[] all   = session.GetAllKeyHeightsMm();
IReadOnlyDictionary<DDKey, float> byKey = session.GetAllKeyHeightsMmByKey();
bool pressed  = session.IsKeyPressed(DDKey.W);
```

> Configuration methods require polling to be stopped - an exception is thrown if called while actively polling.

## Actuation, downstroke, and upstroke

```csharp
// Uniform - all keys
session.SetActuationPoint(1.5f);
session.SetDownstrokePoint(0.2f);
session.SetUpstrokePoint(0.1f);

// Named keys only, all others unchanged
session.SetActuationPoint(0.2f, DDKey.W, DDKey.A, DDKey.S, DDKey.D);

// Per-key with a fluent profile
session.SetActuationPoints(new KeyDepthProfileBuilder()
    .Default(2.0f)
    .Keys([DDKey.W, DDKey.A, DDKey.S, DDKey.D], 0.2f)
    .Build());

// Read back current depths (HighPrecision models only - IHasHighPrecision)
using var session = KeyboardSession<A75Ultra>.OpenFirst();
float[] depths                          = session.ReadActuationPoint();
IReadOnlyDictionary<DDKey, float> byKey = session.ReadActuationPointByKey();
// ReadDownstrokePoint / ReadDownstrokePointByKey and ReadUpstrokePoint / ReadUpstrokePointByKey follow the same pattern.
```

Valid range: `session.MinDepthMm` – `session.MaxDepthMm`.

## RGB lighting

```csharp
// Uniform colour - raw channels or RgbColor struct
session.SetUniformLighting(r: 0, g: 100, b: 255);
session.SetUniformLighting(new RgbColor(0, 100, 255));

// Per-key via callback
session.SetLighting(idx => idx % 2 == 0 ? (255, 0, 0) : (0, 0, 255));

// Single key, all others preserved
session.SetKeyColor(DDKey.Escape, r: 255, g: 165, b: 0);
session.SetKeyColor(DDKey.Escape, new RgbColor(255, 165, 0));

session.DisableLighting();

// Built-in animation presets (all models)
session.SetLightingMode(LightingMode.CenterSurfing, brightness: 5, speed: 4);
session.SetLightingMode(LightingMode.Breath, brightness: 9, speed: 2);
```

### Firmware animation presets (programmable models)

```csharp
using var session = KeyboardSession<A75Ultra>.OpenFirst();

// Activate a named preset
session.SetLightPreset(LightPreset.CenterSurfing, brightness: 7, speed: 4);

// Optional single-colour tint for presets that support it
session.SetLightPresetColor(new RgbColor(0, 200, 255));

// Return to per-key custom RGB
session.SetLightCustom();

// Persist in-memory colours to on-board flash and restore later
session.SaveLightingToProfile(profileIndex: 0);
session.LoadLightingFromProfile(profileIndex: 0);
```

### Logo and side light zones

```csharp
using var session = KeyboardSession<A75Ultra>.OpenFirst();  // IHasLogoLight
session.SetLogoLightPreset(LightPreset.Breath, brightness: 7);
session.SetLogoLightColor(new RgbColor(255, 0, 128));
session.SetLogoLightOff();

using var session = KeyboardSession<X60Future>.OpenFirst();  // IHasSideLight
session.SetSideLightPreset(LightPreset.Spectrum);
session.SetSideLightColor(new RgbColor(255, 0, 128));
```

### RGB to a JSON file (all models)

```csharp
// Build a theme and save to file
var profile = new KeyboardProfile
{
    Theme = new KeyboardThemeBuilder()
        .Base(0, 0, 40)
        .Brightness(9)
        .Keys([DDKey.W, DDKey.A, DDKey.S, DDKey.D], 255, 140, 0)
        .Build()
};
profile.SaveToFile("my_theme.json");

// Load and apply later
var saved = KeyboardProfile.FromFile("my_theme.json");
session.ApplyProfile(saved);
```

## Rapid Trigger, Turbo, and Last Win

```csharp
session.EnableRapidTrigger(autoMatch: true);
session.DisableRapidTrigger();
session.EnableTurboMode();
session.DisableTurboMode();
session.SetLastWinRapidTriggerMode(LastWinRapidTriggerMode.Both);
session.ConfigureLastWinReplace(enabled: true);

// Last Win key pairs - whichever was pressed most recently wins
session.ConfigureLastWinPairs((DDKey.A, DDKey.D), (DDKey.Q, DDKey.E));
session.ConfigureLastWinPairs(new LastWinPair(DDKey.A, DDKey.D), new LastWinPair(DDKey.W, DDKey.S));

// Auto Match - release threshold automatically mirrors the press threshold
session.EnableAutoMatch(sensitivity: 1);
session.DisableAutoMatch();
```

### Turbo mode

Every held key re-fires at the polling rate for as long as it is held (auto-fire). Available on all models; programmable models also persist the setting to on-board flash.

```csharp
session.EnableTurboMode();
session.DisableTurboMode();
```

## Programmable models

The sections below require a programmable model (`IHasFuncBlock`): G65 M1/M2/M3, G60 V600, A75 Ultra, A75 Master, X60 Future.

### Global keyboard settings

```csharp
using var session = KeyboardSession<A75Ultra>.OpenFirst();

session.SetReportRate(ReportRate.Hz1000);
session.SetKeyboardMode(KeyboardMode.Mac);
session.SetDebounce(level: 2);       // 0–7
session.SetStabilityMode(level: 1);  // 0–3
session.ConfigureKeyLocks(winLock: true, altTabLock: false);
session.SetTickRate(rate: 8);
```

### Key remapping

```csharp
// Remap a single key (3-byte write - no read required)
session.SetKey(DDKey.CapsLock, new UserKey { Type = 0x04, Param1 = 0x29 }); // → Esc

// Read / write an entire layer
UserKey[] layer0 = session.ReadKeyMap(layerIndex: 0);
session.WriteKeyMap(layer0, layerIndex: 0);
```

### Per-key trigger configuration

```csharp
KeyTriggerConfig[] triggers = session.ReadKeyTriggers();
session.SetKeyTrigger(DDKey.Space, new KeyTriggerConfig { Actuation = 150 });
session.WriteKeyTriggers(triggers);
```

### Firmware profile slots

```csharp
FullProfileData data = session.PullFullProfile(profileIndex: 0);
session.PushFullProfile(data, profileIndex: 1);
session.CopyProfile(fromSlot: 0, toSlot: 1);
session.SwitchProfile(profileIndex: 1);
int active = session.GetCurrentProfile();
```

### Macros, Dynamic Keystroke, Multi-Tap, Toggle Keys

```csharp
// Dynamic Keystroke - different bindings at different press depths
DynamicKeystrokeEntry[] dks = session.ReadDynamicKeystrokeEntries();
session.SetDynamicKeystrokeEntry(slotIndex: 0, entry);

// Multi-Tap - different action per tap count
MultiTapEntry[] mt = session.ReadMultiTapEntries();
session.SetMultiTapEntry(slotIndex: 0, entry);

// Toggle keys and macros
session.SetToggleKeyEntry(slotIndex: 0, new UserKey { ... });
MacroAction[][] macros = session.ReadMacroSlots();
session.SetMacroSlot(slotIndex: 0, actions);
```

## Keyboard profiles

`KeyboardProfile` is a serialisable snapshot. Only non-null fields are applied - set a section to `null` to leave that part of the keyboard unchanged.

```csharp
var profile = new KeyboardProfile
{
    RapidTrigger = true,
    Actuation    = new KeyDepthProfileBuilder()
                       .Default(2.0f)
                       .Keys([DDKey.W, DDKey.A, DDKey.S, DDKey.D], 0.2f)
                       .Build(),
    Theme        = new KeyboardThemeBuilder()
                       .Base(0, 100, 255)
                       .Brightness(8)
                       .Build(),
};

session.ApplyProfile(profile);

// Save the current keyboard state to JSON
KeyboardProfile captured = session.CaptureProfile();
captured.SaveToFile("profile.json");

// load profile back from JSON
var loaded = KeyboardProfile.FromFile("profile.json");
session.ApplyProfile(loaded);
```

## Precision modes

| Mode | Resolution | Models |
|---|---|---|
| `Standard` | 0.1 mm | A75, A75 Pro, G75, G65, G60, … |
| `Kun` | 0.01 mm | G65 M1/M2/M3, G60 V600, and standard models on newer firmware |
| `HighPrecision` | 0.005 mm | A75 Ultra, A75 Master, X60 Future |

`session.PrecisionMode` reports the active mode. Actuation read-back and high-precision key-point writes are only available in `HighPrecision` mode.

# AI Disclosure

LLMs were used to assist in reverse-engineering the DrunkDeer protocol and generating test coverage. The codebase undergoes careful human review and verification by the maintainers of this repository to ensure that usage of the public API does not cause irreversible damage to DrunkDeer keyboard peripherals. See LICENSE.md for additional details.

Human cross-confirmation of the public API has been used for the following models:

| Model | Human Verified |
|---|---|
| `A75` | ✔️ |

## Additional documentation

See [GitHub Wiki](../../wiki):

- **[Testing](../../wiki/Testing)** - using `FakeKeyboardConnection` to write tests without a physical keyboard
- **[Protocol Analyzer](../../wiki/ProtocolAnalyzer)** - validating USB captures against the protocol implementation
- **[Code Generation](../../wiki/CodeGen)** - regenerating `Generated/` from YAML + Scriban templates when adding new models or capabilities

## Disclaimer
This project is an independent, open-source initiative. It is not affiliated with, funded by, endorsed by, or in any way officially connected to DrunkDeer Ltd, or any of its subsidiaries or affiliates.

The official website for DrunkDeer Ltd can be found at drunkdeer.com. The name DrunkDeer as well as related names, marks, emblems, and images are registered trademarks of their respective owners.

DrunkDeer © Copyright DrunkDeer limited（ HANG WAI IND CTR NO 6 KIN TAI ST, TUEN MUN, NT, HONGKONG).
