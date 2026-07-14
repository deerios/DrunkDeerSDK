# deerkb — DrunkDeer keyboard CLI

A git-style command-line tool for DrunkDeer analog HID keyboards: actuation points,
Rapid Trigger, Turbo, live RGB lighting, and profile import/export. Built on the
[DrunkDeer SDK](../DrunkDeer) and [System.CommandLine](https://learn.microsoft.com/dotnet/standard/commandline/).

> Only the **A75** is human-verified hardware. Other models are supported on a
> best-effort basis and print an unverified-hardware note where relevant.

## Install

### Option A — self-contained binary (no .NET required)

Download the binary for your platform from the Releases page, then:

```sh
# Linux / macOS
chmod +x deerkb-linux-x64
sudo mv deerkb-linux-x64 /usr/local/bin/deerkb
deerkb info
```

```powershell
# Windows: put deerkb-win-x64.exe somewhere on your PATH, then
deerkb.exe info
```

On Linux, HID access needs a udev rule (see [udev](#linux-udev-permissions)).

### Option B — dotnet global tool (needs the .NET 10 runtime)

```sh
dotnet tool install -g deerkb
deerkb info
```

## Try it without hardware

Every command accepts `--demo`, which runs against a simulated keyboard:

```sh
deerkb info --demo
deerkb watch --demo            # live travel display, animated
deerkb actuation set 0.2 --keys wasd --all-others 2.0 --demo --yes
```

## Global options

```
--device <serial|path>   Target a specific keyboard (default: first found)
--json                   Machine-readable output (one object/array per command)
--yes, -y                Skip confirmation prompts for flash writes
--quiet, -q              Errors only
--verbose, -v            Log SDK diagnostics to stderr (Debug)
--trace                  Log SDK diagnostics to stderr (Trace, includes TX/RX)
--timeout <ms>           HID receive timeout override
--demo                   Use a simulated keyboard (no hardware)
--demo-model <slug>      Model to simulate (default: a75_ultra)
```

### Exit codes

| Code | Meaning |
|---|---|
| 0 | Success |
| 1 | Runtime failure |
| 2 | Usage error |
| 3 | No device found |
| 4 | Capability not supported on this model |
| 5 | Aborted at confirmation |
| 6 | Device busy / handshake failed |

## Commands

```
deerkb devices [--probe]                 List connected keyboards (--probe handshakes each)
deerkb info                              Model, firmware, precision, capabilities (the bare default)
deerkb watch [--raw] [--threshold mm]    Live per-key travel display (Ctrl+C to stop)

deerkb actuation get
deerkb actuation set <mm> [--keys <spec>] [--all-others <mm>]
deerkb downstroke set <mm> [...]         Rapid Trigger downstroke
deerkb upstroke   set <mm> [...]         Rapid Trigger upstroke

deerkb rt on [--auto-match] | off
deerkb turbo on | off                    (capability-gated)

deerkb light set <color> [--keys <spec>] [--base <color>] [--brightness 0-9]
deerkb light mode <effect> [--brightness 0-9] [--speed 0-9]
deerkb light off

deerkb profile capture [-o file.json]
deerkb profile apply <file.json>
deerkb profile show  <file.json>         (no device needed)
```

### Key specs (`--keys`)

Comma-separated tokens; each is a key name, a shorthand, a named group, or a range:

- names / aliases: `Escape`, `LeftShift`, `ArrowUp`, `esc`, `space`, `caps`
- letters / digits: `w` → W, `5` → D5
- groups: `wasd`, `arrows`, `fnrow`, `numrow`, `letters`, `modifiers`
- ranges (physical key order): `F1-F12`, `1-5`

### Per-key depth safety

A per-key actuation/downstroke/upstroke write rewrites the **whole** keyboard, and the
firmware offers no depth read-back, so keys outside `--keys` would reset to the firmware
default. deerkb therefore **requires `--all-others <mm>`** for per-key depth writes, so you
state the baseline explicitly. For a single uniform value, just run `deerkb actuation set <mm>`.

## Examples

```sh
# FPS setup
deerkb rt on
deerkb actuation set 0.2 --keys wasd --all-others 2.0
deerkb light set '#0064FF'
deerkb light set orange --keys wasd

# Backup / restore lighting & trigger flags
deerkb profile capture -o backup.json
deerkb profile apply backup.json

# Diagnostics / scripting
deerkb info --json
deerkb watch --raw | jq .          # JSON-lines of pressed keys
```

## Linux udev permissions

Grant your user access to the DrunkDeer HID interfaces. Create
`/etc/udev/rules.d/99-drunkdeer.rules` (the first VID covers most models; the others
cover rebadged variants):

```
SUBSYSTEM=="hidraw", ATTRS{idVendor}=="352d", MODE="0660", TAG+="uaccess"
SUBSYSTEM=="hidraw", ATTRS{idVendor}=="04d9", MODE="0660", TAG+="uaccess"
SUBSYSTEM=="hidraw", ATTRS{idVendor}=="1a85", MODE="0660", TAG+="uaccess"
```

Then `sudo udevadm control --reload-rules && sudo udevadm trigger`, and replug the keyboard.
Run `deerkb devices` to confirm the exact VID/PID for your model.

## Building from source

```sh
dotnet build DrunkDeer.Cli/DeerKB.slnx
dotnet test  DrunkDeer.Cli/DeerKB.slnx
./DrunkDeer.Cli/publish.sh 0.1.0     # self-contained binaries + nupkg into deerkb/dist/
```
