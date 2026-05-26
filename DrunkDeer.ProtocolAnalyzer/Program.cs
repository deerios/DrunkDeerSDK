using DrunkDeer.ProtocolAnalyzer.Analysis;
using DrunkDeer.ProtocolAnalyzer.Capture;
using DrunkDeer.ProtocolAnalyzer.Logging;

// ── Argument parsing ──────────────────────────────────────────────────────────

if (args.Length == 0 || args.Contains("--help") || args.Contains("-h"))
{
    PrintUsage();
    return 0;
}

if (args.Contains("--list-interfaces"))
{
    return ListInterfaces();
}

string? pcapFile      = null;
string? liveInterface = null;
string? outputFile    = null;
string? firmwareTag   = null;
string? deviceFilter  = null;

for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--pcap"      when i + 1 < args.Length: pcapFile      = args[++i]; break;
        case "--live"      when i + 1 < args.Length: liveInterface = args[++i]; break;
        case "--output"    when i + 1 < args.Length: outputFile    = args[++i]; break;
        case "--firmware"  when i + 1 < args.Length: firmwareTag   = args[++i]; break;
        case "--device"    when i + 1 < args.Length: deviceFilter  = args[++i]; break;
    }
}

if (pcapFile is null && liveInterface is null)
{
    Console.Error.WriteLine("error: --pcap <file> or --live <interface> is required.");
    PrintUsage();
    return 1;
}

// Parse optional bus.device filter (e.g. "1.5")
ushort? busFilter = null, devFilter = null;
if (deviceFilter is not null)
{
    var parts = deviceFilter.Split('.');
    if (parts.Length == 2
        && ushort.TryParse(parts[0], out var b)
        && ushort.TryParse(parts[1], out var d))
    {
        busFilter = b; devFilter = d;
    }
    else
    {
        Console.Error.WriteLine($"warning: could not parse --device '{deviceFilter}' (expected bus.addr, e.g. 1.5); analyzing all interrupt transfers.");
        deviceFilter = null;
    }
}

// ── PCAP file mode ────────────────────────────────────────────────────────────

if (pcapFile is not null)
{
    if (!File.Exists(pcapFile)) { Console.Error.WriteLine($"error: file not found: {pcapFile}"); return 1; }

    outputFile ??= Path.Combine(
        Path.GetDirectoryName(Path.GetFullPath(pcapFile))!,
        Path.GetFileNameWithoutExtension(pcapFile) + "-analysis.ndjson");

    Console.WriteLine($"Input:    {pcapFile}");
    if (firmwareTag  is not null) Console.WriteLine($"Firmware: {firmwareTag}");
    if (deviceFilter is not null) Console.WriteLine($"Device:   {deviceFilter}");
    Console.WriteLine($"Output:   {outputFile}");
    Console.WriteLine();

    // If no device filter, do a quick pre-scan to show the device inventory so the user
    // knows which bus.device to pass on the next run.
    if (deviceFilter is null)
    {
        var inventory = new Dictionary<string, int>();
        foreach (var (lt, ts, data) in PcapFileReader.Read(pcapFile))
        {
            var pkt = UsbPcapDecoder.TryDecode(lt, ts, data);
            if (pkt is null) continue;
            string key = $"{pkt.Bus}.{pkt.Device}";
            inventory[key] = inventory.GetValueOrDefault(key) + 1;
        }
        if (inventory.Count == 0)
        {
            Console.WriteLine("No USBPcap interrupt-transfer packets found. Verify the capture uses link type 249 (USBPcap).");
            return 1;
        }
        if (inventory.Count > 1)
        {
            Console.WriteLine($"Found {inventory.Count} USB devices in this capture. Use --device bus.addr to restrict analysis:");
            foreach (var (dev, count) in inventory.OrderByDescending(x => x.Value))
                Console.WriteLine($"  {dev,-12} {count,6} interrupt packets");
            Console.WriteLine();
        }
    }

    return RunAnalysis(PcapFileReader.Read(pcapFile), inputLabel: pcapFile, outputFile, firmwareTag, deviceFilter, busFilter, devFilter,
        progressPrefix: null, cancellation: CancellationToken.None);
}

// ── Live capture mode ─────────────────────────────────────────────────────────

{
    outputFile ??= $"capture-{DateTime.Now:yyyyMMdd-HHmmss}.ndjson";

    Console.WriteLine($"Interface: {liveInterface}");
    if (firmwareTag  is not null) Console.WriteLine($"Firmware:  {firmwareTag}");
    if (deviceFilter is not null) Console.WriteLine($"Device:    {deviceFilter}");
    Console.WriteLine($"Output:    {outputFile}");
    Console.WriteLine("Press Ctrl+C to stop.");
    Console.WriteLine();

    var cts = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) =>
    {
        e.Cancel = true;
        cts.Cancel();
        Console.WriteLine("\nStopping capture...");
    };

    IEnumerable<(uint, long, byte[])> source;
    try
    {
        source = LiveUsbCapture.Capture(liveInterface!, cts.Token);
    }
    catch (InvalidOperationException ex)
    {
        Console.Error.WriteLine($"error: {ex.Message}");
        return 1;
    }
    catch (ArgumentException ex)
    {
        Console.Error.WriteLine($"error: {ex.Message}");
        return ListInterfaces();
    }

    return RunAnalysis(source, inputLabel: $"live:{liveInterface}", outputFile, firmwareTag, deviceFilter, busFilter, devFilter,
        progressPrefix: "Captured", cancellation: cts.Token);
}

// ── Shared analysis loop ──────────────────────────────────────────────────────

int RunAnalysis(
    IEnumerable<(uint LinkType, long TimestampUs, byte[] Data)> source,
    string inputLabel, string output, string? firmware, string? deviceFilterStr,
    ushort? bus, ushort? dev,
    string? progressPrefix, CancellationToken cancellation)
{
    using var log = new NdjsonLog(output);
    log.WriteSession(inputLabel, firmware, deviceFilterStr);

    var analyzer = new SessionAnalyzer(bus, dev);
    int hidPackets = 0;

    try
    {
        foreach (var (lt, ts, data) in source)
        {
            if (cancellation.IsCancellationRequested) break;

            var pkt = UsbPcapDecoder.TryDecode(lt, ts, data);
            if (pkt is null) continue;

            hidPackets++;
            if (progressPrefix is not null && hidPackets % 100 == 0)
                Console.Write($"\r{progressPrefix}: {hidPackets} HID packets...");

            var entry = analyzer.Process(pkt);
            if (entry is not null) log.WritePacket(entry);
        }
    }
    catch (OperationCanceledException) { }
    catch (EndOfStreamException ex)
    {
        Console.Error.WriteLine($"\nwarning: capture ended unexpectedly ({ex.Message}). Results may be incomplete.");
    }
    catch (InvalidDataException ex)
    {
        Console.Error.WriteLine($"\nerror: malformed capture: {ex.Message}");
        return 1;
    }

    if (progressPrefix is not null) Console.WriteLine();

    var summary = analyzer.GetSummary();
    log.WriteSummary(summary);

    PrintSummary(summary, output);
    return 0;
}

// ── Console output helpers ────────────────────────────────────────────────────

void PrintSummary(SummaryEntry s, string outputPath)
{
    Console.WriteLine($"HID interrupt packets analyzed:");
    Console.WriteLine($"  OUT  {s.TotalOut,5}  classified {s.ClassifiedOut}");
    Console.WriteLine($"  IN   {s.TotalIn,5}  classified {s.ClassifiedIn}");

    if (s.SequenceErrors > 0)
        Console.WriteLine($"  Sequence errors:     {s.SequenceErrors}  ← OUT->IN opcode mismatch");
    if (s.StructuralFailures > 0)
        Console.WriteLine($"  Structural failures: {s.StructuralFailures}  ← packet violates protocol definition");

    if (s.FirmwareFields.Count > 0)
    {
        Console.WriteLine();
        Console.WriteLine("Firmware-sensitive fields:");
        foreach (var (name, value) in s.FirmwareFields)
            Console.WriteLine($"  {name,-24} {value}");
    }

    if (s.DeviceCounts.Count > 1)
    {
        Console.WriteLine();
        Console.WriteLine("HID interrupt packets by device (bus.addr):");
        foreach (var (device, count) in s.DeviceCounts.OrderByDescending(x => x.Value))
            Console.WriteLine($"  {device,-12} {count,6}");
    }

    Console.WriteLine();
    Console.WriteLine($"Log: {outputPath}");
    Console.WriteLine();
    Console.WriteLine("Quick queries:");
    Console.WriteLine("  jq 'select(.sequence_failure != null)'         <file>");
    Console.WriteLine("  jq 'select(.structural_ok == false)'           <file>");
    Console.WriteLine("  jq 'select(.type==\"summary\") | .firmware_fields' <file>");
}

int ListInterfaces()
{
    try
    {
        var devices = LiveUsbCapture.ListDevices();
        if (devices.Count == 0)
        {
            Console.WriteLine("Npcap returned no capture interfaces.");
            Console.WriteLine("Ensure Npcap is installed with USBPcap support: https://npcap.com/");
            return 1;
        }

        var usb   = devices.Where(d =>  d.IsUsbPcap).ToList();
        var other = devices.Where(d => !d.IsUsbPcap).ToList();

        if (usb.Count > 0)
        {
            Console.WriteLine("USB capture interfaces (likely candidates):");
            foreach (var d in usb)
                Console.WriteLine($"  {d.Name,-44}  {d.Description}");
        }
        else
        {
            Console.WriteLine("No interfaces with 'usb' in name/description found.");
            Console.WriteLine("All available interfaces are listed below - pick the one that corresponds to USBPcap.");
        }

        if (other.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("Other interfaces:");
            foreach (var d in other)
                Console.WriteLine($"  {d.Name,-44}  {d.Description}");
        }

        Console.WriteLine();
        Console.WriteLine("Usage:  ProtocolAnalyzer --live <interface-name> [--device bus.addr] [--firmware fw13]");
        return 0;
    }
    catch (InvalidOperationException ex)
    {
        Console.Error.WriteLine($"error: {ex.Message}");
        return 1;
    }
}

void PrintUsage()
{
    Console.WriteLine("""
        DrunkDeer.ProtocolAnalyzer  -  validate USB traffic against the protocol

        Usage:
          ProtocolAnalyzer --pcap <capture.pcap|.pcapng> [options]   # analyze a Wireshark capture
          ProtocolAnalyzer --live <interface>            [options]   # live USB capture via Npcap
          ProtocolAnalyzer --list-interfaces                         # show available USBPcap interfaces

        Capture requirements (--pcap):
          Capture in Wireshark with USBPcap enabled (link type 249). No keyboard connection needed.

        Capture requirements (--live):
          Npcap installed with USBPcap support (https://npcap.com/).
          The keyboard must not be exclusively claimed by another process.

        Options:
          --pcap <file>        Path to .pcap or .pcapng file
          --live <interface>   USBPcap interface name, e.g. \\Device\\USBPcap1
          --output <file>      NDJSON output path (default: <pcap-stem>-analysis.ndjson or capture-<ts>.ndjson)
          --firmware <tag>     Label for the firmware version in the log (e.g. "fw13")
          --device <bus.addr>  Filter to one USB device, e.g. "1.5"
          --list-interfaces    List available USBPcap capture interfaces and exit
          --help               Show this help

        Output (one JSON object per line):
          type=session    Analysis parameters and timestamp
          type=packet     Each HID interrupt packet: hex, message, structural_ok,
                          sequence_failure, fields, firmware_sensitive
          type=summary    Aggregate stats, device inventory, firmware-field snapshot

        Firmware comparison:
          Run captures on fw_A and fw_B with --firmware fw_A / --firmware fw_B,
          then diff the firmware_fields from the two summary entries.
        """);
}
